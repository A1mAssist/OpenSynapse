using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Core.Tests;

public sealed class BladeDeviceModeProtocolTests
{
    [Fact]
    public void BuildsProduct710NormalModeRequest()
    {
        var request = BladeDeviceModeProtocol.CreateSetNormalRequest();

        Assert.Equal((byte)0x02, request[6]);
        Assert.Equal((byte)0x00, request[7]);
        Assert.Equal((byte)0x04, request[8]);
        Assert.Equal(new byte[] { 0x00, 0x00 }, request[9..11]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(request), request[89]);
    }

    [Fact]
    public void BuildsProduct710SoftwareModeRequest()
    {
        var request = BladeDeviceModeProtocol.CreateSetSoftwareRequest();

        Assert.Equal((byte)0x02, request[6]);
        Assert.Equal((byte)0x00, request[7]);
        Assert.Equal((byte)0x04, request[8]);
        Assert.Equal(new byte[] { 0x03, 0x00 }, request[9..11]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(request), request[89]);
    }
}
