using OpenSynapse.Windows.Devices;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed partial class RazerHeadsetAudioServiceTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CustomEqSecondStepFailureOrCancellationRestoresPresetAndBands(bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        var simulator = new HeadsetSimulator { Preset = 2 };
        var previous = simulator.Bands.ToArray();
        var failed = false;
        var service = new RazerHeadsetAudioService(new FakeTransport(request =>
        {
            var reply = simulator.Handle(request);
            if (!failed && request[7] == 8 && request[8] == 4)
            {
                failed = true;
                if (cancel) { cancellation.Cancel(); throw new OperationCanceledException(cancellation.Token); }
                throw new IOException("injected band failure");
            }
            return reply;
        }), "fake", TimeSpan.Zero);
        var error = await Record.ExceptionAsync(() => service.SetCustomEqAsync(16, Enumerable.Repeat((byte)50, 10).ToArray(), cancellation.Token));
        Assert.True(failed);
        if (cancel) Assert.Equal(cancellation.Token, Assert.IsAssignableFrom<OperationCanceledException>(error).CancellationToken);
        else Assert.Equal("injected band failure", Assert.IsType<IOException>(error).Message);
        Assert.Equal((byte)2, simulator.Preset);
        Assert.Equal(previous, simulator.Bands);
    }

    [Fact]
    public async Task FailedBandRestorationStillRestoresPresetAndReportsUnknownCurve()
    {
        var simulator = new HeadsetSimulator { Preset = 2 };
        var writes = 0;
        var service = new RazerHeadsetAudioService(new FakeTransport(request =>
        {
            if (request[7] == 8 && request[8] == 4)
            {
                writes++;
                if (writes == 1) { simulator.Handle(request); throw new IOException("original band failure"); }
                throw new IOException("restore band unavailable");
            }
            return simulator.Handle(request);
        }), "fake", TimeSpan.Zero);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SetCustomEqAsync(16, Enumerable.Repeat((byte)50, 10).ToArray()));
        Assert.Equal((byte)2, simulator.Preset);
        Assert.Contains("original band failure", error.Message);
        Assert.Contains("could not be fully restored", error.Message);
        Assert.IsType<IOException>(error.InnerException);
    }
}
