using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class OpenRazerDeathAdder35GProtocolTests
{
    [Fact]
    public void DefaultStateMatchesUpstreamFixedReport()
    {
        var state = OpenRazerDeathAdder35GState.Default;

        Assert.Equal(new byte[] { 1, 1, 1, 3 }, OpenRazerDeathAdder35GProtocol.Encode(state));
        Assert.Equal(1000, state.PollingRate);
        Assert.Equal(3500, state.DpiValue);
        Assert.Equal(0x21, OpenRazerDeathAdder35GProtocol.RequestType);
        Assert.Equal(0x09, OpenRazerDeathAdder35GProtocol.Request);
        Assert.Equal(0x0010, OpenRazerDeathAdder35GProtocol.Value);
    }

    [Fact]
    public void UpdatesPreserveTheOtherStateBytes()
    {
        var state = OpenRazerDeathAdder35GState.Default;
        state = OpenRazerDeathAdder35GProtocol.WithPollingRate(state, 125);
        state = OpenRazerDeathAdder35GProtocol.WithDpi(state, 900);
        state = OpenRazerDeathAdder35GProtocol.WithLed(state, OpenRazerDeathAdder35GLeds.Logo, false);

        Assert.Equal(new byte[] { 3, 3, 1, 2 }, OpenRazerDeathAdder35GProtocol.Encode(state));
        Assert.Equal(state, OpenRazerDeathAdder35GProtocol.Decode([3, 3, 1, 2]));
    }

    [Fact]
    public void UpstreamFallbackMappingsArePreserved()
    {
        var state = OpenRazerDeathAdder35GProtocol.WithPollingRate(OpenRazerDeathAdder35GState.Default, 250);
        state = OpenRazerDeathAdder35GProtocol.WithDpi(state, 1600);

        Assert.Equal(new byte[] { 2, 1, 1, 3 }, OpenRazerDeathAdder35GProtocol.Encode(state));
    }
}
