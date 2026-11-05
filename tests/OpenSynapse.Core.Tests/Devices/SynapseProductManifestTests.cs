using System.Collections.Frozen;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class SynapseProductManifestTests : IDisposable
{
    private const ushort KeyboardPid = 0x024E;

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "OpenSynapseProductManifestTests",
        Guid.NewGuid().ToString("N"));

    public SynapseProductManifestTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public async Task ManifestSuppliesNameAndCategoryFromTheDecimalPidUrl()
    {
        var handler = new FakeHandler(_ => Json("""
            {
              "deviceName": "Razer BlackWidow V3",
              "editionId": [0, 128],
              "icons": [{ "src": "/synapse/assets/imgs/favicon/KEYBOARD.svg" }]
            }
            """));
        using var http = new HttpClient(handler);
        var service = new SynapseProductManifestService(http, _directory);

        var manifest = await service.GetAsync(KeyboardPid);

        Assert.NotNull(manifest);
        Assert.Equal("Razer BlackWidow V3", manifest.Name);
        Assert.Equal(DeviceCategory.Keyboard, manifest.Category);
        Assert.Equal(new[] { 0, 128 }, manifest.EditionIds);
        Assert.Equal(
            "https://apps.razer.com/synapse/products/590/mw/manifest.json",
            Assert.Single(handler.Requests));
    }

    [Fact]
    public void CategoryComesFromTheManifestIcon()
    {
        Assert.Equal(DeviceCategory.Mouse, SynapseProductManifestService.ParseCategory("/x/MOUSE.svg"));
        Assert.Equal(DeviceCategory.Keyboard, SynapseProductManifestService.ParseCategory("/x/KEYBOARD.svg"));
        Assert.Equal(DeviceCategory.Keyboard, SynapseProductManifestService.ParseCategory("/x/KEYPAD.svg"));
        Assert.Equal(DeviceCategory.MouseMat, SynapseProductManifestService.ParseCategory("/x/MOUSEMAT.svg"));
        Assert.Equal(DeviceCategory.Unknown, SynapseProductManifestService.ParseCategory(null));
    }

    [Fact]
    public async Task MissingManifestReturnsNullInsteadOfThrowing()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var http = new HttpClient(handler);
        var service = new SynapseProductManifestService(http, _directory);

        Assert.Null(await service.GetAsync(0x1234));
    }

    [Fact]
    public async Task CachedManifestSurvivesAnOfflineFetch()
    {
        var online = new FakeHandler(_ => Json("""{ "deviceName": "Razer Jugan", "icons": [{ "src": "MOUSE.svg" }] }"""));
        using (var http = new HttpClient(online))
        {
            var service = new SynapseProductManifestService(http, _directory);
            Assert.Equal("Razer Jugan", (await service.GetAsync(0x0063))!.Name);
        }

        var offline = new FakeHandler(_ => throw new HttpRequestException("offline"));
        using var offlineHttp = new HttpClient(offline);
        var reopened = new SynapseProductManifestService(offlineHttp, _directory);
        var manifest = await reopened.GetAsync(0x0063);

        Assert.NotNull(manifest);
        Assert.True(manifest.FromCache);
        Assert.Equal(DeviceCategory.Mouse, manifest.Category);
    }

    [Fact]
    public async Task DonglePidResolvesThroughTheCloudProductCatalog()
    {
        var handler = new FakeHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/synapse/dashboard/AvailableDevices.json" =>
                Json("""[{"productId":664,"dongleId":662,"bleId":663}]"""),
            "/synapse/products/664/mw/manifest.json" =>
                Json("""{ "deviceName": "Razer BlackWidow V4", "icons": [{ "src": "KEYBOARD.svg" }] }"""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        using var http = new HttpClient(handler);
        var service = new SynapseProductManifestService(http, _directory);

        var manifest = await service.GetAsync(662);

        Assert.NotNull(manifest);
        Assert.Equal("Razer BlackWidow V4", manifest.Name);
        Assert.Equal(DeviceCategory.Keyboard, manifest.Category);
        Assert.Contains(handler.Requests, url => url.EndsWith("AvailableDevices.json", StringComparison.Ordinal));
    }
    private static HttpResponseMessage Json(string body, string? etag = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (etag is not null) response.Headers.ETag = new EntityTagHeaderValue(etag);
        return response;
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.ToString());
            return Task.FromResult(respond(request));
        }
    }
}

public sealed class RazerHidProvisionalMappingTests
{
    [Fact]
    public void ProvisionalDefinitionCarriesOnlyTheIdentityProbe()
    {
        var definition = RazerHidDeviceCatalog.CreateProvisional(
            0x2A00, "Razer Future Mouse", DeviceCategory.Mouse);

        Assert.Equal(DeviceCategory.Mouse, definition.Category);
        Assert.Equal("Razer Future Mouse", definition.DisplayName);
        Assert.Contains("get_firmware", definition.SourceCapabilities);
        Assert.Equal((byte)0x1F, definition.GetRequiredTransaction("razer_chroma_standard_get_firmware_version"));
        Assert.Empty(definition.PollingRates);
        Assert.Null(definition.MaximumDpi);
    }

    [Fact]
    public async Task ButtonListAdmitsMappingWithoutGrantingAnotherProductsWrites()
    {
        var transport = new FakeTransport(ButtonListResponse(0x01, 0x02, 0x03));
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);
        var definition = RazerHidDeviceCatalog.CreateProvisional(
            0x2A00, "Razer Future Mouse", DeviceCategory.Mouse);

        var capabilities = await service.ProbeProvisionedMappingAsync(
            definition, "test", CancellationToken.None);

        Assert.Equal(2, capabilities.Count);
        Assert.Contains(RazerHidBackendCapability.MappingRead, capabilities);
        Assert.Contains(RazerHidBackendCapability.MappingWrite, capabilities);
    }

    [Fact]
    public async Task EmptyButtonListLeavesTheProvisionalDeviceWithoutMapping()
    {
        var transport = new FakeTransport(ButtonListResponse());
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);
        var definition = RazerHidDeviceCatalog.CreateProvisional(
            0x2A00, "Razer Future Mouse", DeviceCategory.Mouse);

        Assert.Empty(await service.ProbeProvisionedMappingAsync(
            definition, "test", CancellationToken.None));
    }

    [Fact]
    public async Task ManifestCategoryAdmitsAnUnknownMouseButNotAHeadset()
    {
        var directory = TempDirectory();
        try
        {
            using var mouseHttp = new HttpClient(new JsonHandler("""
                { "deviceName": "Razer Future Mouse", "icons": [{ "src": "MOUSE.svg" }] }
                """));
            var mouse = new RazerHidDeviceService(
                RazerHidDeviceCatalog.BuiltIn, new NoopTransport(), null,
                new SynapseProductManifestService(mouseHttp, directory));
            var definition = await mouse.ResolveDefinitionAsync(
                0x2A00, "HID Mouse", CancellationToken.None);

            Assert.NotNull(definition);
            Assert.Equal(DeviceCategory.Mouse, definition.Category);
            Assert.Equal("Razer Future Mouse", definition.DisplayName);

            using var headsetHttp = new HttpClient(new JsonHandler("""
                { "deviceName": "Razer Headset", "icons": [{ "src": "AUDIO.svg" }] }
                """));
            var headset = new RazerHidDeviceService(
                RazerHidDeviceCatalog.BuiltIn, new NoopTransport(), null,
                new SynapseProductManifestService(headsetHttp, directory));

            Assert.Null(await headset.ResolveDefinitionAsync(
                0x2A01, "HID Headset", CancellationToken.None));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task KeyboardCatalogueOwnershipWinsBeforeTheCloudLookup()
    {
        var directory = TempDirectory();
        try
        {
            var handler = new JsonHandler("""
                { "deviceName": "Owned Elsewhere", "icons": [{ "src": "KEYBOARD.svg" }] }
                """);
            using var http = new HttpClient(handler);
            var service = new RazerHidDeviceService(
                RazerHidDeviceCatalog.BuiltIn, new NoopTransport(), null,
                new SynapseProductManifestService(http, directory));

            Assert.Null(await service.ResolveDefinitionAsync(
                0x02D0, "Huntsman", CancellationToken.None));
            Assert.Empty(handler.Requests);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task OfflineManifestStillAdmitsTheProvisionalDeviceByName()
    {
        var directory = TempDirectory();
        try
        {
            using var http = new HttpClient(new JsonHandler(throwNetwork: true));
            var service = new RazerHidDeviceService(
                RazerHidDeviceCatalog.BuiltIn, new NoopTransport(), null,
                new SynapseProductManifestService(http, directory));

            var definition = await service.ResolveDefinitionAsync(
                0x2A02, "Razer Offline Mouse", CancellationToken.None);

            Assert.NotNull(definition);
            Assert.Equal(DeviceCategory.Unknown, definition.Category);
            Assert.Equal("Razer Offline Mouse", definition.DisplayName);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string TempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(), "OpenSynapseProvisionTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class JsonHandler(string? body = null, bool throwNetwork = false) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.ToString());
            if (throwNetwork) throw new HttpRequestException("offline");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body!, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class NoopTransport : IRazerFeatureTransport
    {
        public Task<byte[]> QueryAsync(
            string devicePath,
            byte transactionId,
            byte dataSize,
            byte commandClass,
            byte commandId,
            ReadOnlyMemory<byte> arguments,
            TimeSpan deviceWait,
            CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false) =>
            throw new NotSupportedException();

        public Task<byte[]> QueryPreparedAsync(
            string devicePath,
            ReadOnlyMemory<byte> request,
            TimeSpan deviceWait,
            CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false) =>
            throw new NotSupportedException();
    }
    private static byte[] ButtonListResponse(params byte[] ids)
    {
        var response = new byte[RazerFeatureReport.Length];
        response[1] = 0x02;
        response[6] = (byte)(ids.Length + 1);
        response[RazerFeatureReport.ArgumentsOffset] = (byte)ids.Length;
        ids.CopyTo(response, RazerFeatureReport.ArgumentsOffset + 1);
        response[89] = RazerFeatureReport.CalculateCrc(response);
        return response;
    }

    private sealed class FakeTransport(byte[] response) : IRazerFeatureTransport
    {
        public Task<byte[]> QueryAsync(
            string devicePath,
            byte transactionId,
            byte dataSize,
            byte commandClass,
            byte commandId,
            ReadOnlyMemory<byte> arguments,
            TimeSpan deviceWait,
            CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false) =>
            throw new NotSupportedException();

        public Task<byte[]> QueryPreparedAsync(
            string devicePath,
            ReadOnlyMemory<byte> request,
            TimeSpan deviceWait,
            CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false) =>
            Task.FromResult(response.ToArray());
    }
}