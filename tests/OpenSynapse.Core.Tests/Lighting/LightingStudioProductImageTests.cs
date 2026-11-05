using System.Net;
using System.Net.Http.Headers;
using OpenSynapse.Core.Lighting;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class LightingStudioProductImageTests : IDisposable
{
    private const ushort ProductId = 100;
    private static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "OpenSynapseProductImageTests",
        Guid.NewGuid().ToString("N"));

    public LightingStudioProductImageTests() => Directory.CreateDirectory(_directory);

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
    public void ResolveUriUsesVerifiedSynapseCoverPattern()
    {
        Assert.Equal(
            "https://app-assets.razer.com/files/synapse/devices/100/cover.png",
            StudioProductImageService.ResolveUri(ProductId)!.ToString());
        Assert.Null(StudioProductImageService.ResolveUri(0));
    }

    [Fact]
    public void WirelessBlackWidowV3MiniUsesTheSharedWiredCover()
    {
        Assert.Equal(
            "https://app-assets.razer.com/files/synapse/devices/600/cover.png",
            StudioProductImageService.ResolveUri(0x0271)!.ToString());
    }

    [Fact]
    public async Task CoverDownloadsOnceAndIsReusedByMemoryAndDiskCache()
    {
        var handler = new FakeHandler(_ => Png());
        using var http = new HttpClient(handler);
        var service = new StudioProductImageService(http, _directory);

        var first = await service.GetAsync(ProductId, "Razer Basilisk");
        var second = await service.GetAsync(ProductId, "Razer Basilisk");
        var reopened = new StudioProductImageService(http, _directory);
        var third = await reopened.GetAsync(ProductId, "Razer Basilisk");

        Assert.Equal(StudioProductImageState.Loaded, first.State);
        Assert.Equal(first.Path, second.Path);
        Assert.Equal(first.Path, third.Path);
        Assert.NotNull(first.Path);
        Assert.True(File.Exists(first.Path));
        Assert.Equal(PngBytes, await File.ReadAllBytesAsync(first.Path));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task OfflineWithoutCacheReturnsUnavailableWithInitial()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("offline"));
        using var http = new HttpClient(handler);
        var service = new StudioProductImageService(http, _directory);

        var result = await service.GetAsync(ProductId, "Razer Basilisk");

        Assert.Equal(StudioProductImageState.Unavailable, result.State);
        Assert.Null(result.Path);
        Assert.Equal("network", result.Error);
        Assert.Equal("R", result.Initial);
    }

    [Fact]
    public async Task HttpForbiddenIsReportedAsUnavailable()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        using var http = new HttpClient(handler);
        var service = new StudioProductImageService(http, _directory);

        var result = await service.GetAsync(ProductId, "Razer Basilisk");

        Assert.Equal(StudioProductImageState.Unavailable, result.State);
        Assert.Equal("http-403", result.Error);
        Assert.Equal("R", result.Initial);
    }

    [Fact]
    public async Task OversizedCoverIsRejected()
    {
        var handler = new FakeHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Array.Empty<byte>()),
            };
            response.Content.Headers.ContentLength = 4L * 1024 * 1024 + 1;
            return response;
        });
        using var http = new HttpClient(handler);
        var service = new StudioProductImageService(http, _directory);

        var result = await service.GetAsync(ProductId, "Razer Basilisk");

        Assert.Equal(StudioProductImageState.Unavailable, result.State);
        Assert.Equal("too-large", result.Error);
    }

    [Fact]
    public async Task NonImageResponseIsRejected()
    {
        var handler = new FakeHandler(_ =>
        {
            var content = new StringContent("<html></html>");
            content.Headers.ContentType = new MediaTypeHeaderValue("text/html");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
        using var http = new HttpClient(handler);
        var service = new StudioProductImageService(http, _directory);

        var result = await service.GetAsync(ProductId, "Razer Basilisk");

        Assert.Equal(StudioProductImageState.Unavailable, result.State);
        Assert.Equal("not-image", result.Error);
    }

    [Fact]
    public void ProductImageStatesKeepNameFallback()
    {
        var pending = StudioProductImage.ForName("Razer Basilisk");
        var unavailable = StudioProductImage.Unavailable("Razer Basilisk", "network");
        var loaded = StudioProductImage.Loaded("Razer Basilisk", @"C:\cache\100.png");

        Assert.Equal(StudioProductImageState.None, pending.State);
        Assert.Equal("R", pending.Initial);
        Assert.Equal(StudioProductImageState.Unavailable, unavailable.State);
        Assert.Equal("R", unavailable.Initial);
        Assert.Equal(StudioProductImageState.Loaded, loaded.State);
        Assert.Equal("R", loaded.Initial);
        Assert.Equal("?", StudioProductImage.ForName("   ").Initial);
    }

    private static HttpResponseMessage Png()
    {
        var content = new ByteArrayContent(PngBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
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