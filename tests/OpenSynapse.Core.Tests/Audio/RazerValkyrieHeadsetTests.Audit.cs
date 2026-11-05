using OpenSynapse.Windows.Devices;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed partial class RazerValkyrieHeadsetTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CustomEqSecondStepFailureOrCancellationRestoresPresetAndBands(bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        var transport = new FakeTransport
        {
            Preset = 2, Bands = Enumerable.Repeat((byte)3, 10).ToArray(),
            FailBandsOnce = true, CancelBands = cancel ? cancellation : null,
        };
        var previous = transport.Bands.ToArray();
        await using var service = new RazerValkyrieHeadsetService(transport, 0x0567);
        var error = await Record.ExceptionAsync(() => service.SetCustomEqAsync(0xFF, Enumerable.Repeat((byte)6, 10).ToArray(), cancellation.Token));
        Assert.False(transport.FailBandsOnce);
        if (cancel) Assert.Equal(cancellation.Token, Assert.IsAssignableFrom<OperationCanceledException>(error).CancellationToken);
        else Assert.Equal("injected band failure", Assert.IsType<IOException>(error).Message);
        Assert.Equal((byte)2, transport.Preset);
        Assert.Equal(previous, transport.Bands);
    }
}
