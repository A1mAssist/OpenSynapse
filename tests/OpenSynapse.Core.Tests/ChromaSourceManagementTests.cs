using System.Text;
using System.Text.Json;
using Xunit;
using OpenSynapse.App.Runtime;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Lighting;

namespace OpenSynapse.Core.Tests;

public sealed class ChromaSourceManagementTests
{
    [Fact]
    public async Task DefaultAllowsSourcesAndBlockingActiveSourceRestoresOnceAndRejectsNewFrames()
    {
        var lighting = new FakeLighting();
        var restores = 0;
        var outputs = 0;
        await using var host = new ChromaRestHost(() => [], lighting,
            () => { restores++; return Task.CompletedTask; },
            additionalFrameSink: (_, _) => { outputs++; return Task.FromResult(true); }, port: 0);
        await host.StartAsync();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var uri = await ConnectAsync(client, host, " Example game ");
        Assert.Equal(0, await FrameAsync(client, uri));
        Assert.Equal("Example game", host.Snapshot.ActiveSessionTitle);
        Assert.True(Assert.Single(host.Snapshot.Sources!).Active);

        await host.SetSourceAllowedAsync("example GAME", false);
        Assert.Null(host.Snapshot.ActiveSessionTitle);
        Assert.False(Assert.Single(host.Snapshot.Sources!).Allowed);
        Assert.Equal(1, restores);
        Assert.Equal(1, lighting.Stops);
        Assert.Equal(5, await FrameAsync(client, uri));
        Assert.Equal(1, outputs);
        await host.SetSourceAllowedAsync("Example game", false);
        Assert.Equal(1, restores);

        await host.SetSourceAllowedAsync("Example game", true);
        Assert.Equal(0, await FrameAsync(client, uri));
        Assert.Equal(2, outputs);
        await client.DeleteAsync(uri);
        Assert.Null(host.Snapshot.ActiveSessionTitle);
    }

    [Fact]
    public async Task PersistedBlockAppliesToReconnectButDoesNotBlockOtherSources()
    {
        var outputs = 0;
        await using var host = new ChromaRestHost(() => [], new FakeLighting(), () => Task.CompletedTask,
            additionalFrameSink: (_, _) => { outputs++; return Task.FromResult(true); },
            blockedSources: ["Blocked game"], port: 0);
        await host.StartAsync();
        using var client = new HttpClient();
        var uri = await ConnectAsync(client, host, "blocked GAME");
        Assert.Equal(5, await FrameAsync(client, uri));
        await client.DeleteAsync(uri);
        var reconnect = await ConnectAsync(client, host, "Blocked game");
        Assert.Equal(5, await FrameAsync(client, reconnect));
        var other = await ConnectAsync(client, host, "Other game");
        Assert.Equal(0, await FrameAsync(client, other));
        Assert.Equal(1, outputs);
        Assert.Equal("Other game", host.Snapshot.ActiveSessionTitle);
        await host.SetSourceAllowedAsync("Blocked game", false);
        Assert.Equal("Other game", host.Snapshot.ActiveSessionTitle);
    }

    [Fact]
    public async Task InFlightFrameAndRestoreAreSerializedBeforeAllowedSourceTakesOver()
    {
        var frameEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFrame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var restoreEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRestore = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var outputs = 0;
        await using var host = new ChromaRestHost(() => [], new FakeLighting(), async () =>
        {
            restoreEntered.TrySetResult();
            await releaseRestore.Task;
        }, additionalFrameSink: async (_, _) =>
        {
            if (Interlocked.Increment(ref outputs) == 1)
            {
                frameEntered.TrySetResult();
                await releaseFrame.Task;
            }
            return true;
        }, port: 0);
        await host.StartAsync();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            var first = await ConnectAsync(client, host, "First game");
            var second = await ConnectAsync(client, host, "Second game");
            var frame = FrameAsync(client, first);
            await frameEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var block = host.SetSourceAllowedAsync("First game", false);
            Assert.False(block.IsCompleted);
            releaseFrame.TrySetResult();
            Assert.Equal(0, await frame);
            await restoreEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var nextFrame = FrameAsync(client, second);
            Assert.Equal(1, Volatile.Read(ref outputs));
            releaseRestore.TrySetResult();
            await block;
            Assert.Equal(0, await nextFrame);
            Assert.Equal("Second game", host.Snapshot.ActiveSessionTitle);
            Assert.Equal(5, await FrameAsync(client, first));
        }
        finally
        {
            releaseFrame.TrySetResult();
            releaseRestore.TrySetResult();
        }
    }

    [Fact]
    public async Task RestorationFailureKeepsTheSourceBlocked()
    {
        var outputs = 0;
        await using var host = new ChromaRestHost(() => [], new FakeLighting(),
            () => throw new IOException("Device unavailable during restore"),
            additionalFrameSink: (_, _) => { outputs++; return Task.FromResult(true); }, port: 0);
        await host.StartAsync();
        using var client = new HttpClient();
        var uri = await ConnectAsync(client, host, "Game");
        Assert.Equal(0, await FrameAsync(client, uri));
        await Assert.ThrowsAsync<IOException>(() => host.SetSourceAllowedAsync("Game", false));
        Assert.False(Assert.Single(host.Snapshot.Sources!).Allowed);
        Assert.Null(host.Snapshot.ActiveSessionTitle);
        Assert.Equal(5, await FrameAsync(client, uri));
        Assert.Equal(1, outputs);
    }

    private static async Task<string> ConnectAsync(HttpClient client, ChromaRestHost host, string title)
    {
        using var body = new StringContent(JsonSerializer.Serialize(new
        {
            title, description = "Source management test", author = new { name = "Test", contact = "" },
            device_supported = new[] { "keyboard" }, category = "application",
        }), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync($"http://127.0.0.1:{host.ListeningPort}/razer/chromasdk", body);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("uri").GetString()!;
    }

    private static async Task<int> FrameAsync(HttpClient client, string uri)
    {
        using var body = new StringContent(JsonSerializer.Serialize(
            new { effect = "CHROMA_STATIC", param = new { color = 255 } }), Encoding.UTF8, "application/json");
        using var response = await client.PutAsync(uri + "/keyboard", body);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("result").GetInt32();
    }

    private sealed class FakeLighting : IBladeLightingController
    {
        internal int Stops;
        public Task RuntimeCompletion => Task.CompletedTask;
        public Task ApplyAsync(IReadOnlyList<DeviceDescriptor> devices, BladeLightingEffect effect,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync() { Stops++; return Task.CompletedTask; }
        public Task SetDisplayAvailableAsync(bool available) => Task.CompletedTask;
        public Task ApplyExternalAsync(IReadOnlyList<DeviceDescriptor> devices, ChromaExternalFrameSource source,
            bool restorePersistentEffect = true, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
