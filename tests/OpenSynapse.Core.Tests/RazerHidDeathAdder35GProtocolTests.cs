using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerHidDeathAdder35GProtocolTests
{
    [Fact]
    public void DefaultStateMatchesUpstreamFixedReport()
    {
        var state = RazerHidDeathAdder35GState.Default;

        Assert.Equal(new byte[] { 1, 1, 1, 3 }, RazerHidDeathAdder35GProtocol.Encode(state));
        Assert.Equal(1000, state.PollingRate);
        Assert.Equal(3500, state.DpiValue);
        Assert.Equal(0x21, RazerHidDeathAdder35GProtocol.RequestType);
        Assert.Equal(0x09, RazerHidDeathAdder35GProtocol.Request);
        Assert.Equal(0x0010, RazerHidDeathAdder35GProtocol.Value);
    }

    [Fact]
    public void UpdatesPreserveTheOtherStateBytes()
    {
        var state = RazerHidDeathAdder35GState.Default;
        state = RazerHidDeathAdder35GProtocol.WithPollingRate(state, 125);
        state = RazerHidDeathAdder35GProtocol.WithDpi(state, 900);
        state = RazerHidDeathAdder35GProtocol.WithLed(state, RazerHidDeathAdder35GLeds.Logo, false);

        Assert.Equal(new byte[] { 3, 3, 1, 2 }, RazerHidDeathAdder35GProtocol.Encode(state));
        Assert.Equal(state, RazerHidDeathAdder35GProtocol.Decode([3, 3, 1, 2]));
    }

    [Fact]
    public void UpstreamFallbackMappingsArePreserved()
    {
        var state = RazerHidDeathAdder35GProtocol.WithPollingRate(RazerHidDeathAdder35GState.Default, 250);
        state = RazerHidDeathAdder35GProtocol.WithDpi(state, 1600);

        Assert.Equal(new byte[] { 2, 1, 1, 3 }, RazerHidDeathAdder35GProtocol.Encode(state));
    }
}
