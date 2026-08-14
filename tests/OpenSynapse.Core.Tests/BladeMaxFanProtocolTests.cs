using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Core.Tests;

public sealed class BladeMaxFanProtocolTests
{
    [Theory]
    [InlineData(0x00, BladeMaxFanMode.Disabled)]
    [InlineData(0x01, BladeMaxFanMode.Disabled)]
    [InlineData(0x02, BladeMaxFanMode.Enabled)]
    [InlineData(0x0F, BladeMaxFanMode.Enabled)]
    public void ParsesKnownStates(byte raw, BladeMaxFanMode expected)
    {
        var response = RazerFeatureReport.CreateRequest(0x1F, 0x01, 0x07, 0x8F, new byte[] { raw });
        response[1] = 0x02;
        response[3] = 0x01;
        response[89] = RazerFeatureReport.CalculateCrc(response);

        Assert.Equal(expected, BladeMaxFanProtocol.Parse(response));
    }

    [Theory]
    [InlineData(BladeMaxFanMode.Disabled, 0x0F, 0x0D)]
    [InlineData(BladeMaxFanMode.Enabled, 0x0D, 0x0F)]
    public void BuildsSetRequestWithoutChangingSiblingBits(
        BladeMaxFanMode mode,
        byte existingMask,
        byte expectedMask)
    {
        var request = BladeMaxFanProtocol.CreateSetRequest(mode, existingMask);

        Assert.Equal(0x1F, request[2]);
        Assert.Equal(0x01, request[6]);
        Assert.Equal(0x07, request[7]);
        Assert.Equal(0x0F, request[8]);
        Assert.Equal(expectedMask, request[9]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(request), request[89]);
    }
}
