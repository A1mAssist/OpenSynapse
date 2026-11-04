using System.Net;
using System.Net.Http.Headers;
using System.Text;
using OpenSynapse.Core.Lighting;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class LightingStudioManifestCacheTests : IDisposable
{
    private const ushort ProductId = 736;
    private const int EditionId = 0;

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "OpenSynapseLightingManifestCacheTests",
        Guid.NewGuid().ToString("N"));

    public LightingStudioManifestCacheTests() => Directory.CreateDirectory(_directory);

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
    public async Task MemoryCacheSkipsConditionalRequestWithinLifetime()
    {
        var handler = new FakeHandler(_ => Json("""{ "productName": "Cached Device" }""", "\"v1\""));
        using var http = new HttpClient(handler);
        var service = new StudioManifestService(http, _directory, TimeSpan.FromMinutes(5));

        var first = await service.GetAsync(ProductId, EditionId);
        var second = await service.GetAsync(ProductId, EditionId);

        Assert.False(first.FromCache);
        Assert.True(second.FromCache);
        Assert.Null(second.Error);
        Assert.Equal("Cached Device", second.Manifest!.ProductName);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task MemoryCacheIsSharedAcrossServiceInstances()
    {
        var handler = new FakeHandler(_ => Json("""{ "productName": "Reopen Device" }""", "\"v1\""));
        using var http = new HttpClient(handler);
        var lifetime = TimeSpan.FromMinutes(5);

        var first = new StudioManifestService(http, _directory, lifetime);
        await first.GetAsync(ProductId, EditionId);
        var reopened = new StudioManifestService(http, _directory, lifetime);
        var second = await reopened.GetAsync(ProductId, EditionId);

        Assert.True(second.FromCache);
        Assert.Equal("Reopen Device", second.Manifest!.ProductName);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task MemoryCacheSeparatesEditions()
    {
        var handler = new FakeHandler(request => request.RequestUri!.AbsolutePath.Contains("_128.", StringComparison.Ordinal)
            ? Json("""{ "productName": "White Edition" }""")
            : Json("""{ "productName": "Standard Edition" }"""));
        using var http = new HttpClient(handler);
        var service = new StudioManifestService(http, _directory, TimeSpan.FromMinutes(5));

        var standard = await service.GetAsync(ProductId, EditionId);
        var white = await service.GetAsync(ProductId, 128);

        Assert.Equal("Standard Edition", standard.Manifest!.ProductName);
        Assert.Equal("White Edition", white.Manifest!.ProductName);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task FailedFetchIsNotRetriedImmediately()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("offline"));
        using var http = new HttpClient(handler);
        var service = new StudioManifestService(http, _directory, TimeSpan.FromMinutes(5));

        var first = await service.GetAsync(ProductId, EditionId);
        var requestsAfterFirst = handler.Requests.Count;
        var second = await service.GetAsync(ProductId, EditionId);

        Assert.Null(first.Manifest);
        Assert.Equal("network", first.Error);
        Assert.True(second.FromCache);
        Assert.Equal("network", second.Error);
        Assert.Equal(requestsAfterFirst, handler.Requests.Count);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task DisabledMemoryCacheKeepsConditionalRefresh()
    {
        var calls = 0;
        var handler = new FakeHandler(_ => Interlocked.Increment(ref calls) == 1
            ? Json("""{ "productName": "Refresh Device" }""", "\"v1\"")
            : new HttpResponseMessage(HttpStatusCode.NotModified));
        using var http = new HttpClient(handler);
        var service = new StudioManifestService(http, _directory);

        await service.GetAsync(ProductId, EditionId);
        var second = await service.GetAsync(ProductId, EditionId);

        Assert.True(second.FromCache);
        Assert.Equal("Refresh Device", second.Manifest!.ProductName);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("\"v1\"", handler.Requests[1].IfNoneMatch);
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
        public List<RequestRecord> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var ifNoneMatch = request.Headers.TryGetValues("If-None-Match", out var values)
                ? values.FirstOrDefault()
                : null;
            Requests.Add(new RequestRecord(request.RequestUri!.ToString(), ifNoneMatch));
            return Task.FromResult(respond(request));
        }
    }

    private sealed record RequestRecord(string Url, string? IfNoneMatch);
}