using System.Runtime.InteropServices;
using OpenSynapse.Windows.Lighting;

namespace OpenSynapse.Core.Tests;

public sealed class WasapiAudioMeterAdapterTests
{
    [Theory]
    [InlineData(float.NaN, 0)]
    [InlineData(float.NegativeInfinity, 0)]
    [InlineData(-0.1f, 0)]
    [InlineData(0.25f, 0.25)]
    [InlineData(2f, 1)]
    public void NormalizesPeak(float input, double expected)
    {
        Assert.Equal(expected, WasapiAudioMeterAdapter.NormalizeLevel(input), 3);
    }

    [Fact]
    public async Task ReplacesSessionWhenDefaultEndpointChanges()
    {
        var now = DateTimeOffset.UtcNow;
        var first = new Session("first", 0.2f);
        var second = new Session("second", 0.8f);
        var sessions = new Queue<IAudioMeterSession>([first, second]);
        var adapter = new WasapiAudioMeterAdapter(() => sessions.Dequeue(), () => now);

        await adapter.StartAsync(CancellationToken.None);
        Assert.Equal(0.2, adapter.ReadLevel(), 3);
        now += TimeSpan.FromSeconds(2);
        Assert.Equal(0.8, adapter.ReadLevel(), 3);

        Assert.True(first.Disposed);
        await adapter.DisposeAsync();
        Assert.True(second.Disposed);
    }

    [Fact]
    public async Task ReopensInvalidatedSession()
    {
        var now = DateTimeOffset.UtcNow;
        var invalid = new Session("same", 0, fail: true);
        var recovered = new Session("same", 0.4f);
        var sessions = new Queue<IAudioMeterSession>([invalid, recovered]);
        var adapter = new WasapiAudioMeterAdapter(() => sessions.Dequeue(), () => now);

        await adapter.StartAsync(CancellationToken.None);

        Assert.Equal(0.4, adapter.ReadLevel(), 3);
        Assert.True(invalid.Disposed);
        await adapter.DisposeAsync();
    }

    private sealed class Session(string endpointId, float peak, bool fail = false) : IAudioMeterSession
    {
        public string EndpointId { get; } = endpointId;
        public bool Disposed { get; private set; }

        public float ReadPeak() => fail
            ? throw new COMException("invalidated")
            : peak;

        public void Dispose() => Disposed = true;
    }
}
