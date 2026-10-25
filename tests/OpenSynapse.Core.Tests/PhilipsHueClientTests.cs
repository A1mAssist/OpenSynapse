using System.Net;
using System.Net.Http;
using OpenSynapse.Windows.Lighting;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class PhilipsHueClientTests
{
    [Fact]
    public async Task PairingReturnsTheUsername()
    {
        var handler = new FakeHandler((_, _) => Json(
            """
            [{"success":{"username":"abc123","clientkey":"def456"}}]
            """));
        using var client = new PhilipsHueClient("192.168.1.2", handler: handler);

        var result = await client.PairAsync();

        Assert.Equal(PhilipsHuePairingOutcome.Paired, result.Outcome);
        Assert.Equal("abc123", result.AppKey);
        var call = Assert.Single(handler.Calls);
        Assert.Equal(HttpMethod.Post, call.Method);
        Assert.Equal("https://192.168.1.2/api", call.Uri);
        Assert.Contains("opensynapse#desktop", call.Body);
    }

    [Fact]
    public async Task PairingMapsTheLinkButtonError()
    {
        var handler = new FakeHandler((_, _) => Json(
            """
            [{"error":{"type":101,"description":"link button not pressed"}}]
            """));

        using var client = new PhilipsHueClient("192.168.1.2", handler: handler);
        var result = await client.PairAsync();

        Assert.Equal(PhilipsHuePairingOutcome.LinkButtonNotPressed, result.Outcome);
        Assert.Null(result.AppKey);
    }

    [Fact]
    public async Task BridgeIdentityIsParsed()
    {
        const string json = "{\"data\":[{\"metadata\":{\"name\":\"Hue Bridge\"},\"product_data\":{\"model_id\":\"BSB002\"},\"software_version\":\"1.60.1\"}]}";
        var handler = new FakeHandler((_, _) => Json(json));
        using var client = new PhilipsHueClient("192.168.1.2", "key-1", handler);

        var bridge = await client.GetBridgeAsync();

        Assert.Equal("Hue Bridge", bridge.Name);
        Assert.Equal("BSB002", bridge.ModelId);
        Assert.Equal("1.60.1", bridge.SoftwareVersion);
    }

    [Fact]
    public async Task LightsAreParsedWithNameStateAndColor()
    {
        var handler = new FakeHandler((_, _) => Json(LightJson(
            id: "7",
            name: "Desk lamp",
            on: true,
            brightness: 62.5,
            x: 0.31,
            y: 0.32)));
        using var client = new PhilipsHueClient("192.168.1.2", "key-1", handler);

        var lights = await client.GetLightsAsync();

        var light = Assert.Single(lights);
        Assert.Equal("7", light.Id);
        Assert.Equal("Desk lamp", light.Name);
        Assert.True(light.IsOn);
        Assert.Equal(62.5, light.Brightness);
        Assert.Equal(0.31, light.X);
        Assert.Equal("key-1", Assert.Single(handler.Calls).AppKey);
    }

    [Fact]
    public async Task SettingALightWritesAndVerifies()
    {
        var state = (On: false, Brightness: 20.0);
        var handler = new FakeHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Put)
            {
                var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                Assert.Contains("\"on\":true", body);
                Assert.Contains("\"brightness\":75", body);
                state = (true, 75);
                return Json(LightJson("7", "Desk lamp", true, 75, null, null));
            }

            return Json(LightJson("7", "Desk lamp", state.On, state.Brightness, null, null));
        });
        using var client = new PhilipsHueClient("192.168.1.2", "key-1", handler);

        await client.SetLightAsync("7", new PhilipsHueLightUpdate(On: true, Brightness: 75));

        Assert.Equal(3, handler.Calls.Count);
        Assert.Equal(HttpMethod.Get, handler.Calls[0].Method);
        Assert.Equal(HttpMethod.Put, handler.Calls[1].Method);
        Assert.Equal("https://192.168.1.2/clip/v2/resource/light/7", handler.Calls[1].Uri);
        Assert.Equal(HttpMethod.Get, handler.Calls[2].Method);
    }

    [Fact]
    public async Task SettingALightRestoresThePreviousStateWhenTheBridgeKeepsAnotherValue()
    {
        var handler = new FakeHandler((request, _) => request.Method == HttpMethod.Put
            ? Json("""{"data":[]}""")
            : Json(LightJson("7", "Desk lamp", false, 40, 0.2, 0.3)));
        using var client = new PhilipsHueClient("192.168.1.2", "key-1", handler);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.SetLightAsync("7", new PhilipsHueLightUpdate(On: true, Brightness: 75)));

        Assert.Contains("restored", error.Message);
        var puts = handler.Calls.Where(call => call.Method == HttpMethod.Put).ToArray();
        Assert.Equal(2, puts.Length);
        Assert.Contains("\"brightness\":40", puts[1].Body);
        Assert.Contains("\"on\":false", puts[1].Body);
    }

    [Theory]
    [InlineData(255, 255, 255, 0.3127, 0.3290)]
    [InlineData(255, 0, 0, 0.64, 0.33)]
    [InlineData(0, 255, 0, 0.30, 0.60)]
    [InlineData(0, 0, 255, 0.15, 0.06)]
    public void RgbMapsToTheHueChromaticity(byte r, byte g, byte b, double x, double y)
    {
        var (actualX, actualY) = PhilipsHueColor.FromRgb(r, g, b);

        Assert.InRange(actualX, x - 0.03, x + 0.03);
        Assert.InRange(actualY, y - 0.03, y + 0.03);
    }
    [Fact]
    public void HostsAreNormalisedAndValidated()
    {
        using var client = new PhilipsHueClient("https://192.168.1.2/");
        Assert.Equal("192.168.1.2", client.Host);
        Assert.Throws<ArgumentException>(() => new PhilipsHueClient("192.168.1.2/lights"));
        Assert.Throws<ArgumentException>(() => new PhilipsHueClient(" "));
    }

    private static string LightJson(
        string id,
        string name,
        bool on,
        double? brightness,
        double? x,
        double? y)
    {
        var dimming = brightness is null ? string.Empty : $",\"dimming\":{{\"brightness\":{brightness.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}";
        var color = x is null || y is null
            ? string.Empty
            : $",\"color\":{{\"xy\":{{\"x\":{x.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"y\":{y.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}}}";
        var onValue = on ? "true" : "false";
        return $"{{\"data\":[{{\"id\":\"{id}\",\"metadata\":{{\"name\":\"{name}\"}},\"on\":{{\"on\":{onValue}}}{dimming}{color}}}]}}";
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };

    private sealed class FakeHandler(
        Func<HttpRequestMessage, string?, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Uri, string? AppKey, string? Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            var appKey = request.Headers.TryGetValues("hue-application-key", out var values)
                ? values.FirstOrDefault()
                : null;
            Calls.Add((request.Method, request.RequestUri!.ToString(), appKey, body));
            return respond(request, body);
        }
    }
}
