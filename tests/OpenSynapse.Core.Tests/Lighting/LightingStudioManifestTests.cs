using System.Net;
using System.Net.Http.Headers;
using System.Text;
using OpenSynapse.Core.Lighting;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class LightingStudioManifestTests : IDisposable
{
    private const ushort ProductId = 736;
    private const int EditionId = 0;
    private const string PrimaryUrl =
        "https://apps.razer.com/synapse/products/736/mw/lighting-manifests/DeviceManifest_736_0.json";
    private const string FallbackUrl =
        "https://apps.razer.com/synapse/lighting-engine/manifest/DeviceManifest_736_0.json";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "OpenSynapseLightingManifestTests",
        Guid.NewGuid().ToString("N"));

    public LightingStudioManifestTests()
    {
        Directory.CreateDirectory(_directory);
    }

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
    public async Task FreshManifestParsesAndWritesCacheAndEtag()
    {
        const string json = """
        {
          "productId": 736,
          "productName": "Razer Test Keyboard",
          "isChromaStudioSupported": true,
          "svg": {
            "svg_0_2": "second-svg",
            "svg_0_1": "first-svg"
          },
          "ledConfig": { "zones": [ { "id": 1 } ], "rows": 6 }
        }
        """;
        var handler = new FakeHandler(_ => Json(json, etag: "\"v1\""));
        using var http = new HttpClient(handler);
        var service = new StudioManifestService(http, _directory);

        var result = await service.GetAsync(ProductId, EditionId);

        Assert.Null(result.Error);
        Assert.False(result.FromCache);
        Assert.Equal(PrimaryUrl, result.SourceUrl);
        var manifest = Assert.IsType<StudioManifestDevice>(result.Manifest);
        Assert.Equal((ushort)736, manifest.ProductId);
        Assert.Equal(0, manifest.EditionId);
        Assert.Equal("Razer Test Keyboard", manifest.ProductName);
        Assert.True(manifest.IsStudioSupported);
        Assert.Equal("svg_0_1", manifest.SvgKey);
        Assert.Equal("first-svg", manifest.SvgBase64);
        Assert.Equal("{ \"zones\": [ { \"id\": 1 } ], \"rows\": 6 }", manifest.LedConfigJson);
        Assert.Equal("\"v1\"", manifest.ETag);

        var cachePath = Path.Combine(_directory, "736_0.json");
        var etagPath = Path.Combine(_directory, "736_0.etag");
        Assert.True(File.Exists(cachePath));
        Assert.True(File.Exists(etagPath));
        Assert.Equal(Encoding.UTF8.GetBytes(json), await File.ReadAllBytesAsync(cachePath));
        Assert.Equal("\"v1\"", await File.ReadAllTextAsync(etagPath));

        var request = Assert.Single(handler.Requests);
        Assert.Equal(PrimaryUrl, request.Url);
        Assert.Equal("OpenSynapse-LightingStudio/1.0", request.UserAgent);
        Assert.Null(request.IfNoneMatch);
    }

    [Fact]
    public async Task DualLinkManifestUsesTheWiredProductForCoverArt()
    {
        const string json = """
        {
          "productId": 625,
          "wiredProductId": 600,
          "dongleProductId": 625,
          "productName": "Razer BlackWidow V3 Mini Hyperspeed"
        }
        """;
        var handler = new FakeHandler(_ => Json(json));
        using var http = new HttpClient(handler);
        var service = new StudioManifestService(http, _directory);

        var manifest = Assert.IsType<StudioManifestDevice>(
            (await service.GetAsync(625, EditionId)).Manifest);

        Assert.Equal((ushort)600, manifest.WiredProductId);
        Assert.Equal((ushort)625, manifest.DongleProductId);
        Assert.Equal((ushort)600, manifest.CoverProductId);
        Assert.Equal(
            "https://app-assets.razer.com/files/synapse/devices/600/cover.png",
            StudioProductImageService.ResolveUri(manifest.CoverProductId)!.ToString());
    }

    [Fact]
    public async Task ConditionalRequestReturnsCachedManifestOnNotModified()
    {
        const string json = """
        { "productName": "Cached Device", "svg": { "svg_0_1": "cached-svg" } }
        """;
        var calls = 0;
        var handler = new FakeHandler(_ => Interlocked.Increment(ref calls) == 1
            ? Json(json, etag: "\"v1\"")
            : new HttpResponseMessage(HttpStatusCode.NotModified));
        using var http = new HttpClient(handler);
        var service = new StudioManifestService(http, _directory);

        var first = await service.GetAsync(ProductId, EditionId);
        var second = await service.GetAsync(ProductId, EditionId);

        Assert.False(first.FromCache);
        Assert.True(second.FromCache);
        Assert.Null(second.Error);
        Assert.Equal("Cached Device", second.Manifest!.ProductName);
        Assert.Equal("cached-svg", second.Manifest.SvgBase64);
        Assert.Equal(PrimaryUrl, second.SourceUrl);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("\"v1\"", handler.Requests[1].IfNoneMatch);
        Assert.Equal(Encoding.UTF8.GetBytes(json), await File.ReadAllBytesAsync(Path.Combine(_directory, "736_0.json")));
    }

    [Fact]
    public async Task MissingPrimaryFallsBackToSecondaryUrl()
    {
        const string json = """{ "productName": "Fallback Device" }""";
        var handler = new FakeHandler(request => request.RequestUri!.AbsolutePath.Contains("/products/", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : Json(json));
        using var http = new HttpClient(handler);
        var service = new StudioManifestService(http, _directory);

        var result = await service.GetAsync(ProductId, EditionId);

        Assert.Null(result.Error);
        Assert.False(result.FromCache);
        Assert.Equal(FallbackUrl, result.SourceUrl);
        Assert.Equal("Fallback Device", result.Manifest!.ProductName);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(PrimaryUrl, handler.Requests[0].Url);
        Assert.Equal(FallbackUrl, handler.Requests[1].Url);
    }

    [Fact]
    public async Task BothUrlsNotFoundReportsNotfound()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var http = new HttpClient(handler);
        var service = new StudioManifestService(http, _directory);

        var result = await service.GetAsync(ProductId, EditionId);

        Assert.Null(result.Manifest);
        Assert.Equal("not-found", result.Error);
        Assert.False(result.FromCache);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task NetworkFailureWithCacheReturnsOfflineCache()
    {
        const string json = """
        { "productName": "Offline Device", "svg": { "svg_0_1": "offline-svg" } }
        """;
        var calls = 0;
        var handler = new FakeHandler(_ =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                return Json(json, etag: "\"v1\"");
            }

            throw new HttpRequestException("offline");
        });
        using var http = new HttpClient(handler);
        var service = new StudioManifestService(http, _directory);

        var first = await service.GetAsync(ProductId, EditionId);
        var second = await service.GetAsync(ProductId, EditionId);

        Assert.False(first.FromCache);
        Assert.True(second.FromCache);
        Assert.Equal("offline-cache", second.Error);
        Assert.Equal("Offline Device", second.Manifest!.ProductName);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("\"v1\"", handler.Requests[1].IfNoneMatch);
    }

    [Fact]
    public async Task UsesExpectedUrlAndCacheFileNamesForProduct736Edition0()
    {
        var handler = new FakeHandler(_ => Json("""{ "productName": "Named Device" }""", etag: "\"named\""));
        using var http = new HttpClient(handler);
        var service = new StudioManifestService(http, _directory);

        await service.GetAsync(736, 0);

        Assert.Equal(PrimaryUrl, handler.Requests[0].Url);
        Assert.True(File.Exists(Path.Combine(_directory, "736_0.json")));
        Assert.True(File.Exists(Path.Combine(_directory, "736_0.etag")));
    }

    [Fact]
    public async Task MissingSvgStillReturnsManifest()
    {
        var handler = new FakeHandler(_ => Json("""{ "productName": "Matrix Only", "ledConfig": {} }"""));
        using var http = new HttpClient(handler);
        var service = new StudioManifestService(http, _directory);

        var result = await service.GetAsync(ProductId, EditionId);

        Assert.Null(result.Error);
        var manifest = Assert.IsType<StudioManifestDevice>(result.Manifest);
        Assert.Equal((ushort)736, manifest.ProductId);
        Assert.Equal(string.Empty, manifest.SvgKey);
        Assert.Equal(string.Empty, manifest.SvgBase64);
        Assert.Equal("{}", manifest.LedConfigJson);
        Assert.False(manifest.IsStudioSupported);
        Assert.False(File.Exists(Path.Combine(_directory, "736_0.etag")));
    }

    [Fact]
    public async Task UsesPackagedCatalogWhenTheCdnIsUnavailable()
    {
        var catalog = Path.Combine(
            RepoLayout.Root,
            "src", "OpenSynapse.App", "Assets", "LightingLayouts", "studio-manifests.zip");
        if (!File.Exists(catalog)) return;

        var handler = new FakeHandler(_ => throw new HttpRequestException("offline"));
        using var http = new HttpClient(handler);
        var service = new StudioManifestService(
            http,
            _directory,
            offlineCatalogPath: catalog);

        var result = await service.GetAsync(600, EditionId);

        Assert.Null(result.Error);
        Assert.True(result.FromCache);
        Assert.Equal(catalog, result.SourceUrl);
        var manifest = Assert.IsType<StudioManifestDevice>(result.Manifest);
        Assert.Equal("Razer BlackWidow V3 Mini Hyperspeed", manifest.ProductName);
        Assert.False(string.IsNullOrEmpty(manifest.SvgBase64));
        Assert.False(string.IsNullOrEmpty(manifest.LedConfigJson));
    }

    [Fact]
    public async Task TimeoutReportsTimeout()
    {
        var handler = new FakeHandler(_ => throw new TaskCanceledException("simulated timeout"));
        using var http = new HttpClient(handler);
        var service = new StudioManifestService(http, _directory);

        var result = await service.GetAsync(ProductId, EditionId);

        Assert.Null(result.Manifest);
        Assert.Equal("timeout", result.Error);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task OversizedContentLengthReportsTooLarge()
    {
        var handler = new FakeHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Array.Empty<byte>()),
            };
            response.Content.Headers.ContentLength = 32L * 1024 * 1024 + 1;
            return response;
        });
        using var http = new HttpClient(handler);
        var service = new StudioManifestService(http, _directory);

        var result = await service.GetAsync(ProductId, EditionId);

        Assert.Null(result.Manifest);
        Assert.Equal("too-large", result.Error);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task CreatesCacheDirectoryOnFirstWrite()
    {
        var cacheDirectory = Path.Combine(_directory, "nested", "cache");
        var handler = new FakeHandler(_ => Json("""{ "productName": "Nested Device" }"""));
        using var http = new HttpClient(handler);
        var service = new StudioManifestService(http, cacheDirectory);

        await service.GetAsync(ProductId, EditionId);

        Assert.True(File.Exists(Path.Combine(cacheDirectory, "736_0.json")));
    }

    [Fact]
    public async Task ConditionalRequestWithFreshBodyReplacesCacheAndEtag()
    {
        const string firstJson = """{ "productName": "Old Device" }""";
        const string secondJson = """{ "productName": "New Device" }""";
        var calls = 0;
        var handler = new FakeHandler(_ => Interlocked.Increment(ref calls) == 1
            ? Json(firstJson, etag: "\"v1\"")
            : Json(secondJson, etag: "\"v2\""));
        using var http = new HttpClient(handler);
        var service = new StudioManifestService(http, _directory);

        await service.GetAsync(ProductId, EditionId);
        var second = await service.GetAsync(ProductId, EditionId);

        Assert.False(second.FromCache);
        Assert.Null(second.Error);
        Assert.Equal("New Device", second.Manifest!.ProductName);
        Assert.Equal("\"v2\"", second.Manifest.ETag);
        Assert.Equal("\"v1\"", handler.Requests[1].IfNoneMatch);
        Assert.Equal(Encoding.UTF8.GetBytes(secondJson), await File.ReadAllBytesAsync(Path.Combine(_directory, "736_0.json")));
        Assert.Equal("\"v2\"", await File.ReadAllTextAsync(Path.Combine(_directory, "736_0.etag")));
    }

    private static HttpResponseMessage Json(string body, string? etag = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (etag is not null)
        {
            response.Headers.ETag = new EntityTagHeaderValue(etag);
        }

        return response;
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<RequestRecord> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var ifNoneMatch = request.Headers.TryGetValues("If-None-Match", out var values)
                ? values.FirstOrDefault()
                : null;
            Requests.Add(new RequestRecord(
                request.RequestUri!.ToString(),
                ifNoneMatch,
                request.Headers.UserAgent.ToString()));
            return Task.FromResult(respond(request));
        }
    }

    private sealed record RequestRecord(string Url, string? IfNoneMatch, string UserAgent);
}
