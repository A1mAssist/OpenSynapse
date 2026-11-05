using System.IO.Pipes;
using OpenSynapse.ChromaSdk.Protocol;
using OpenSynapse.Windows.Lighting;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class ChromaSdkBridgeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task HelloThenKeyboardEffectReachesSink()
    {
        var sink = new FakeSink();
        var pipeName = NewPipeName();
        await using var host = CreateHost(pipeName, sink, ChromaSdkDeviceMask.Keyboard);
        await host.StartAsync();
        using var client = await ConnectAsync(pipeName);

        var ack = await HelloAsync(client, "Keyboard Game");
        Assert.Equal(0, ack.Result);
        Assert.Equal(ChromaSdkDeviceMask.Keyboard, ack.SupportedDevices);

        var effect = Frame(0x00FF00);
        ChromaSdkPipe.Write(client, new ChromaSdkSetEffectMessage(effect));

        await WaitUntilAsync(() => host.FramesAccepted == 1);
        var recorded = Assert.Single(sink.Effects);
        Assert.Equal(ChromaSdkDeviceType.Keyboard, recorded.DeviceType);
        Assert.Equal(effect.Colors, recorded.Colors);
        Assert.Equal("Keyboard Game", host.ActiveClientTitle);
    }

    [Fact]
    public async Task ClearAfterAppliedEffectKeepsSessionAndRestoresOnDisconnect()
    {
        var sink = new FakeSink();
        var pipeName = NewPipeName();
        await using var host = CreateHost(pipeName, sink);
        await host.StartAsync();
        using var client = await ConnectAsync(pipeName);

        await HelloAsync(client);
        ChromaSdkPipe.Write(client, new ChromaSdkSetEffectMessage(Frame(0x0000FF)));
        await WaitUntilAsync(() => host.FramesAccepted == 1);

        ChromaSdkPipe.Write(client, new ChromaSdkClearMessage(ChromaSdkDeviceType.Keyboard));
        await WaitUntilAsync(() => sink.Effects.Count == 2);
        Assert.Equal(ChromaSdkEffectKind.None, sink.Effects[1].Kind);
        Assert.Equal("Test App", host.ActiveClientTitle);
        Assert.Equal(0, sink.RestoreCount);

        await host.DisposeAsync();
        Assert.Equal(1, sink.RestoreCount);
    }

    [Fact]
    public async Task SecondClientEffectIsSkippedWhileFirstClientIsActive()
    {
        var sink = new FakeSink();
        var pipeName = NewPipeName();
        await using var host = CreateHost(pipeName, sink);
        await host.StartAsync();
        using var first = await ConnectAsync(pipeName);
        using var second = await ConnectAsync(pipeName);

        await HelloAsync(first, "First");
        await HelloAsync(second, "Second");

        var firstEffect = Frame(0xFF0000);
        ChromaSdkPipe.Write(first, new ChromaSdkSetEffectMessage(firstEffect));
        await WaitUntilAsync(() => host.FramesAccepted == 1);

        ChromaSdkPipe.Write(second, new ChromaSdkSetEffectMessage(Frame(0x00FF00)));
        await WaitUntilAsync(() => host.FramesSkipped == 1);
        Assert.Single(sink.Effects);

        var nextFirstEffect = Frame(0x0000FF);
        ChromaSdkPipe.Write(first, new ChromaSdkSetEffectMessage(nextFirstEffect));
        await WaitUntilAsync(() => host.FramesAccepted == 2);

        Assert.Equal(2, sink.Effects.Count);
        Assert.Equal(firstEffect.Colors, sink.Effects[0].Colors);
        Assert.Equal(nextFirstEffect.Colors, sink.Effects[1].Colors);
        Assert.Equal("First", host.ActiveClientTitle);
    }

    [Fact]
    public async Task DisconnectAfterAppliedEffectRestoresOnce()
    {
        var sink = new FakeSink();
        var pipeName = NewPipeName();
        await using var host = CreateHost(pipeName, sink);
        await host.StartAsync();
        var client = await ConnectAsync(pipeName);

        await HelloAsync(client);
        ChromaSdkPipe.Write(client, new ChromaSdkSetEffectMessage(Frame(0xFFFFFF)));
        await WaitUntilAsync(() => host.FramesAccepted == 1);

        client.Dispose();
        await WaitUntilAsync(() => sink.RestoreCount == 1);
        await host.DisposeAsync();
        Assert.Equal(1, sink.RestoreCount);
    }

    [Fact]
    public async Task QueryReturnsSupportedDeviceMask()
    {
        var sink = new FakeSink();
        var pipeName = NewPipeName();
        const uint devices = ChromaSdkDeviceMask.Keyboard | ChromaSdkDeviceMask.Mousepad;
        await using var host = CreateHost(pipeName, sink, devices);
        await host.StartAsync();
        using var client = await ConnectAsync(pipeName);

        var helloAck = await HelloAsync(client);
        Assert.Equal(devices, helloAck.SupportedDevices);

        ChromaSdkPipe.Write(client, new ChromaSdkQueryMessage());
        var queryAck = Assert.IsType<ChromaSdkQueryAckMessage>(await ReadAsync(client));
        Assert.Equal(devices, queryAck.Value.SupportedDevices);
    }

    [Fact]
    public async Task MalformedFrameClosesOnlyThatClient()
    {
        var sink = new FakeSink();
        var pipeName = NewPipeName();
        await using var host = CreateHost(pipeName, sink);
        await host.StartAsync();
        using var malformed = await ConnectAsync(pipeName);

        await WaitUntilAsync(() => host.ActiveClientCount == 1);
        await malformed.WriteAsync(new byte[4]);
        await malformed.FlushAsync();
        await WaitUntilAsync(() => host.ActiveClientCount == 0);

        using var client = await ConnectAsync(pipeName);
        await HelloAsync(client, "After malformed");
        ChromaSdkPipe.Write(client, new ChromaSdkSetEffectMessage(Frame(0x123456)));
        await WaitUntilAsync(() => host.FramesAccepted == 1);

        Assert.True(host.IsRunning);
        Assert.Equal("After malformed", host.ActiveClientTitle);
    }

    private static ChromaSdkBridgeHost CreateHost(string pipeName, FakeSink sink, uint devices = 0) =>
        new(sink.ApplyAsync, sink.RestoreAsync, () => true, () => devices, pipeName);

    private static string NewPipeName() => Guid.NewGuid().ToString("N");

    private static ChromaSdkEffect Frame(uint color) =>
        new(ChromaSdkDeviceType.Keyboard, ChromaSdkEffectKind.Static, 1, 1, [color]);

    private static async Task<NamedPipeClientStream> ConnectAsync(string pipeName)
    {
        var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await client.ConnectAsync(2000).WaitAsync(Timeout);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static async Task<ChromaSdkHelloAck> HelloAsync(Stream stream, string title = "Test App")
    {
        ChromaSdkPipe.Write(stream, new ChromaSdkHelloMessage(new ChromaSdkHello(
            ChromaSdkPipe.ProtocolVersion, Environment.ProcessId, title, 0)));
        var reply = await ReadAsync(stream);
        return Assert.IsType<ChromaSdkHelloAckMessage>(reply).Value;
    }

    private static Task<ChromaSdkMessage> ReadAsync(Stream stream) =>
        Task.Run(() => ChromaSdkPipe.Read(stream)).WaitAsync(Timeout);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
        Assert.True(condition(), "Timed out waiting for the Chroma SDK bridge state.");
    }

    private sealed class FakeSink
    {
        private readonly object _gate = new();
        private readonly List<ChromaSdkEffect> _effects = [];
        private int _restoreCount;

        public int RestoreCount => Volatile.Read(ref _restoreCount);
        public IReadOnlyList<ChromaSdkEffect> Effects
        {
            get
            {
                lock (_gate) return _effects.ToArray();
            }
        }

        public Task<bool> ApplyAsync(ChromaSdkEffect effect, CancellationToken cancellationToken)
        {
            lock (_gate) _effects.Add(effect);
            return Task.FromResult(true);
        }

        public Task RestoreAsync()
        {
            Interlocked.Increment(ref _restoreCount);
            return Task.CompletedTask;
        }
    }
}
