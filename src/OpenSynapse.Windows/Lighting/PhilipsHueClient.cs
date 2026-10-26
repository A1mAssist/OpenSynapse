using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenSynapse.Windows.Lighting;

public sealed record PhilipsHueBridgeInfo(string? Name, string? ModelId, string? SoftwareVersion);

public sealed record PhilipsHueLight(
    string Id,
    string Name,
    bool IsOn,
    double? Brightness,
    double? X,
    double? Y);

public sealed record PhilipsHueLightUpdate(
    bool? On = null,
    double? Brightness = null,
    (double X, double Y)? Color = null);

public enum PhilipsHuePairingOutcome
{
    Paired,
    LinkButtonNotPressed,
    Failed,
}

public sealed record PhilipsHuePairingResult(
    PhilipsHuePairingOutcome Outcome,
    string? AppKey,
    string? Error);

/// <summary>
/// Philips Hue CLIP v2 bridge client. The Razer module only covers discovery and pairing, so
/// light control talks to the documented bridge API: pairing through <c>/api</c>, lights
/// through <c>/clip/v2/resource/light</c>.
/// </summary>
public sealed class PhilipsHueClient : IDisposable
{
    private const string DeviceType = "opensynapse#desktop";
    private readonly HttpClient _http;
    private readonly string _host;

    /// <summary>
    /// The bridge serves a self-signed certificate, so the handler accepts it for the exact
    /// host the user entered and nothing else. Any other host must present a valid certificate.
    /// </summary>
    public PhilipsHueClient(string host, string? appKey = null, HttpMessageHandler? handler = null)
    {
        _host = NormalizeHost(host);
        _http = handler is null
            ? new HttpClient(CreateHandler(_host), disposeHandler: true)
            : new HttpClient(handler, disposeHandler: true);
        _http.BaseAddress = new Uri($"https://{_host}/");
        _http.Timeout = TimeSpan.FromSeconds(10);
        if (!string.IsNullOrWhiteSpace(appKey))
        {
            _http.DefaultRequestHeaders.Add("hue-application-key", appKey);
        }
    }

    public string Host => _host;

    public async Task<PhilipsHuePairingResult> PairAsync(CancellationToken cancellationToken = default)
    {
        var body = JsonSerializer.Serialize(new { devicetype = DeviceType, generateclientkey = true });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync("api", content, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(text);
        }
        catch (JsonException exception)
        {
            return new PhilipsHuePairingResult(PhilipsHuePairingOutcome.Failed, null, exception.Message);
        }

        if (node is not JsonArray results || results.Count == 0)
        {
            return new PhilipsHuePairingResult(PhilipsHuePairingOutcome.Failed, null, "Empty pairing reply.");
        }

        foreach (var entry in results)
        {
            if (entry?["success"]?["username"]?.GetValue<string>() is { Length: > 0 } username)
            {
                return new PhilipsHuePairingResult(PhilipsHuePairingOutcome.Paired, username, null);
            }

            if (entry?["error"] is { } error)
            {
                var type = error["type"]?.GetValue<int>() ?? 0;
                var description = error["description"]?.GetValue<string>() ?? "Pairing failed.";
                return new PhilipsHuePairingResult(
                    type == 101 ? PhilipsHuePairingOutcome.LinkButtonNotPressed : PhilipsHuePairingOutcome.Failed,
                    null,
                    description);
            }
        }

        return new PhilipsHuePairingResult(PhilipsHuePairingOutcome.Failed, null, "Unrecognised pairing reply.");
    }

    public async Task<PhilipsHueBridgeInfo> GetBridgeAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync("clip/v2/resource/bridge", cancellationToken).ConfigureAwait(false);
        var text = await ReadAsync(response, "read bridge", cancellationToken).ConfigureAwait(false);
        var root = JsonNode.Parse(text);
        var bridge = root?["data"] is JsonArray data && data.Count > 0 ? data[0] : null;
        return new PhilipsHueBridgeInfo(
            bridge?["metadata"]?["name"]?.GetValue<string>(),
            bridge?["product_data"]?["model_id"]?.GetValue<string>(),
            bridge?["software_version"]?.GetValue<string>());
    }
    public async Task<IReadOnlyList<PhilipsHueLight>> GetLightsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync("clip/v2/resource/light", cancellationToken).ConfigureAwait(false);
        var text = await ReadAsync(response, "list lights", cancellationToken).ConfigureAwait(false);
        var root = JsonNode.Parse(text);
        if (root?["data"] is not JsonArray data)
        {
            throw new InvalidOperationException("The Hue bridge reply does not contain any lights.");
        }

        return data
            .Select(ParseLight)
            .Where(light => light is not null)
            .Select(light => light!)
            .ToArray();
    }

    /// <summary>
    /// Reads the light, writes the requested fields, reads back and restores the previous
    /// values when the bridge reports a different state.
    /// </summary>
    public async Task SetLightAsync(
        string lightId,
        PhilipsHueLightUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lightId);
        var before = await GetLightAsync(lightId, cancellationToken).ConfigureAwait(false);

        var body = new JsonObject();
        if (update.On is { } on) body["on"] = new JsonObject { ["on"] = on };
        if (update.Brightness is { } brightness)
        {
            body["dimming"] = new JsonObject { ["brightness"] = Math.Round(brightness, 2) };
        }
        if (update.Color is { } color)
        {
            body["color"] = new JsonObject
            {
                ["xy"] = new JsonObject
                {
                    ["x"] = Math.Round(color.X, 4),
                    ["y"] = Math.Round(color.Y, 4),
                },
            };
        }

        if (body.Count == 0)
        {
            return;
        }

        await PutAsync(lightId, body, cancellationToken).ConfigureAwait(false);
        var after = await GetLightAsync(lightId, cancellationToken).ConfigureAwait(false);
        if (Matches(after, update))
        {
            return;
        }

        await PutAsync(
            lightId,
            BuildRestoreBody(before),
            cancellationToken).ConfigureAwait(false);
        throw new InvalidOperationException(
            "The Hue bridge kept a different light state; the previous values were restored.");
    }

    public void Dispose() => _http.Dispose();

    private async Task<PhilipsHueLight> GetLightAsync(string lightId, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(
            $"clip/v2/resource/light/{Uri.EscapeDataString(lightId)}",
            cancellationToken).ConfigureAwait(false);
        var text = await ReadAsync(response, "read light", cancellationToken).ConfigureAwait(false);
        var root = JsonNode.Parse(text);
        if (root?["data"] is not JsonArray data || data.Count == 0 || ParseLight(data[0]) is not { } light)
        {
            throw new InvalidOperationException($"The Hue bridge did not return light {lightId}.");
        }

        return light;
    }

    private async Task PutAsync(string lightId, JsonObject body, CancellationToken cancellationToken)
    {
        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _http.PutAsync(
            $"clip/v2/resource/light/{Uri.EscapeDataString(lightId)}",
            content,
            cancellationToken).ConfigureAwait(false);
        await ReadAsync(response, "write light", cancellationToken).ConfigureAwait(false);
    }

    private static JsonObject BuildRestoreBody(PhilipsHueLight light)
    {
        var body = new JsonObject { ["on"] = new JsonObject { ["on"] = light.IsOn } };
        if (light.Brightness is { } brightness)
        {
            body["dimming"] = new JsonObject { ["brightness"] = Math.Round(brightness, 2) };
        }
        if (light.X is { } x && light.Y is { } y)
        {
            body["color"] = new JsonObject
            {
                ["xy"] = new JsonObject
                {
                    ["x"] = Math.Round(x, 4),
                    ["y"] = Math.Round(y, 4),
                },
            };
        }

        return body;
    }

    private static bool Matches(PhilipsHueLight light, PhilipsHueLightUpdate update)
    {
        if (update.On is { } on && light.IsOn != on) return false;
        if (update.Brightness is { } brightness &&
            (light.Brightness is null || Math.Abs(light.Brightness.Value - brightness) > 1.0))
        {
            return false;
        }
        if (update.Color is { } color &&
            (light.X is null || light.Y is null ||
             Math.Abs(light.X.Value - color.X) > 0.01 ||
             Math.Abs(light.Y.Value - color.Y) > 0.01))
        {
            return false;
        }

        return true;
    }

    private static PhilipsHueLight? ParseLight(JsonNode? node)
    {
        if (node?["id"]?.GetValue<string>() is not { Length: > 0 } id)
        {
            return null;
        }

        var name = node["metadata"]?["name"]?.GetValue<string>() ?? id;
        var on = node["on"]?["on"]?.GetValue<bool>() ?? false;
        var brightness = ReadDouble(node["dimming"]?["brightness"]);
        var x = ReadDouble(node["color"]?["xy"]?["x"]);
        var y = ReadDouble(node["color"]?["xy"]?["y"]);
        return new PhilipsHueLight(id, name, on, brightness, x, y);
    }

    private static double? ReadDouble(JsonNode? node) =>
        node is null ? null : node.GetValue<double>();

    private static async Task<string> ReadAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Hue {operation} failed with {(int)response.StatusCode} {response.StatusCode}: {text}");
        }

        return text;
    }

    private static string NormalizeHost(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        var trimmed = host.Trim();
        foreach (var prefix in new[] { "https://", "http://" })
        {
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed[prefix.Length..];
            }
        }

        trimmed = trimmed.TrimEnd('/');
        if (trimmed.Length == 0 || trimmed.Contains('/', StringComparison.Ordinal) ||
            !IPAddress.TryParse(trimmed, out _) && !Uri.CheckHostName(trimmed).Equals(UriHostNameType.Dns))
        {
            throw new ArgumentException("A Hue bridge IP address or host name is required.", nameof(host));
        }

        return trimmed;
    }

    private static HttpClientHandler CreateHandler(string host) =>
        new()
        {
            // shortcut: the bridge certificate is self-signed, so it is trusted for this exact
            // host only; pinning would need the bridge certificate, which is unavailable offline.
            ServerCertificateCustomValidationCallback = (request, _, _, errors) =>
                request.RequestUri is { } uri &&
                string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase) &&
                (errors == SslPolicyErrors.None ||
                 errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch) ||
                 errors.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors)),
        };
}
