using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Core.Tests;

public sealed class BladeThermalProtocolTests
{
    [Fact]
    public void BuildsProduct710CurrentSpeedRequest()
    {
        var request = BladeThermalProtocol.CreateGetCurrentSpeedRequest(BladeThermalProtocol.CpuFanId);
        Assert.Equal(new byte[] { 0x01, 0x01 }, request[9..11]);
        Assert.Equal((byte)0x88, request[8]);
    }

    [Fact]
    public void ParsesCurrentSpeedWithProductScale()
    {
        var response = RazerFeatureReport.CreateRequest(0x1F, 0x03, 0x0D, 0x88, new byte[] { 0x01, 0x02, 0x2D });
        response[1] = 0x02;
        response[89] = RazerFeatureReport.CalculateCrc(response);

        Assert.Equal(4500, BladeThermalProtocol.ParseCurrentSpeedRpm(response, 0x02));
    }

    [Fact]
    public void ParsesFanIdList()
    {
        var response = RazerFeatureReport.CreateRequest(0x1F, 0x50, 0x0D, 0x80, new byte[] { 0x02, 0x01, 0x02 });
        response[1] = 0x02;
        response[89] = RazerFeatureReport.CalculateCrc(response);

        Assert.Equal(new byte[] { 0x01, 0x02 }, BladeThermalProtocol.ParseFanIdList(response));
    }

    [Fact]
    public void ParsesAdvancedFanMode()
    {
        var response = RazerFeatureReport.CreateRequest(
            0x1F, 0x03, 0x0D, 0x87, new byte[] { 0x01, 0x02, 0x05 });
        response[1] = 0x02;
        response[89] = RazerFeatureReport.CalculateCrc(response);

        Assert.Equal((byte)0x05, BladeThermalProtocol.ParseAdvancedFanMode(response, 0x02));
    }
}
