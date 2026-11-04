using System.Net.Sockets;
using System.Text;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Lighting;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class ChromaRestLifecycleTests
{
    [Theory]
    [InlineData("POST /razer/chromasdk HTTP/1.1\r\nContent-Length: 100\r\n\r\n{")]
    [InlineData("GET /razer/chromasdk HTTP/1.1\r\nX-Partial: ")]
    public async Task DisposeCancelsAndWaitsForIncompleteBodyAndHeader(string partial)
    {
        await using var host = CreateHost(TimeSpan.FromSeconds(30));
        await host.StartAsync();
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", host.ListeningPort);
        await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes(partial));
        await WaitUntilAsync(() => host.ActiveClientCount == 1);
        await host.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(host.IsRunning);
        Assert.Equal(0, host.ActiveClientCount);
        await AssertSocketClosedAsync(client);
    }

    [Fact]
    public async Task DisposeDrainsSeveralClientsInDifferentReadPhases()
    {
        await using var host = CreateHost(TimeSpan.FromSeconds(30));
        await host.StartAsync();
        var clients = new List<TcpClient>();
        try
        {
            foreach (var partial in new[] { "G", "GET / HTTP/1.1\r\nX: ", "POST / HTTP/1.1\r\nContent-Length: 40\r\n\r\n{" })
            {
                var client = new TcpClient();
                clients.Add(client);
                await client.ConnectAsync("127.0.0.1", host.ListeningPort);
                await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes(partial));
            }
            await WaitUntilAsync(() => host.ActiveClientCount == 3);
            await host.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(0, host.ActiveClientCount);
            foreach (var client in clients) await AssertSocketClosedAsync(client);
        }
        finally { foreach (var client in clients) client.Dispose(); }
    }

    [Fact]
    public async Task RequestDeadlineClosesIdleConnectionAndListenerStillServes()
    {
        await using var host = CreateHost(TimeSpan.FromMilliseconds(200));
        await host.StartAsync();
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", host.ListeningPort);
        await WaitUntilAsync(() => host.ActiveClientCount == 1);
        await WaitUntilAsync(() => host.ActiveClientCount == 0);
        await AssertSocketClosedAsync(client);
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false });
        var response = await http.GetAsync($"http://127.0.0.1:{host.ListeningPort}/razer/chromasdk");
        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("4.0.0", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ParserCountsUtf8BytesAndDoesNotConsumeTrailingData()
    {
        const string body = "{\"title\":\"中文灯效\"}";
        var bytes = Encoding.UTF8.GetBytes(body);
        var header = Encoding.ASCII.GetBytes($"POST /razer/chromasdk HTTP/1.1\r\nContent-Length: {bytes.Length}\r\n\r\n");
        using var stream = new MemoryStream([.. header, .. bytes, (byte)'Z']);
        var request = await ChromaRestRequest.ReadAsync(stream, default);
        Assert.NotNull(request);
        Assert.Equal(body, request.Body);
        Assert.Equal((int)'Z', stream.ReadByte());
    }

    [Theory]
    [InlineData("Content-Length: -1\r\n", 400)]
    [InlineData("Content-Length: 2097153\r\n", 413)]
    [InlineData("Content-Length: 0\r\nContent-Length: 0\r\n", 400)]
    [InlineData("Transfer-Encoding: chunked\r\n", 400)]
    public async Task ParserRejectsAmbiguousOrUnboundedBody(string headers, int status)
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("POST / HTTP/1.1\r\n" + headers + "\r\n"));
        var error = await Assert.ThrowsAsync<ChromaRestRequest.InvalidRequest>(() => ChromaRestRequest.ReadAsync(stream, default));
        Assert.Equal(status, error.Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ParserBoundsIndividualLinesAndTotalHeaders(bool singleLine)
    {
        var input = singleLine ? new string('A', 8194)
            : "GET / HTTP/1.1\r\n" + string.Concat(Enumerable.Range(0, 5).Select(index => $"X-{index}: {new string('a', 7000)}\r\n"));
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(input));
        Assert.Equal(413, (await Assert.ThrowsAsync<ChromaRestRequest.InvalidRequest>(() => ChromaRestRequest.ReadAsync(stream, default))).Status);
    }

    private static ChromaRestHost CreateHost(TimeSpan timeout) =>
        new(() => [], new FakeLighting(), () => Task.CompletedTask, port: 0, requestTimeout: timeout);

    private static async Task AssertSocketClosedAsync(TcpClient client)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try { Assert.Equal(0, await client.GetStream().ReadAsync(new byte[1], timeout.Token)); }
        catch (IOException) { } // Reset and orderly FIN both mean the server closed it.
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    private sealed class FakeLighting : IBladeLightingController
    {
        public event Action<string, Exception>? RestoreFailed { add { } remove { } }
        public Task RuntimeCompletion => Task.CompletedTask;
        public Task ApplyAsync(IReadOnlyList<DeviceDescriptor> devices, BladeLightingEffect effect, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public Task SetDisplayAvailableAsync(bool available) => Task.CompletedTask;
        public Task ApplyExternalAsync(IReadOnlyList<DeviceDescriptor> devices, ISoftwareLightingFrameSource source, bool restorePersistentEffect = true, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
