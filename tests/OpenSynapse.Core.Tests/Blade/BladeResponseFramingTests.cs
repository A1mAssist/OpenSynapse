using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

/// <summary>
/// Some Blade firmware answers a read with more payload than the request declared. The
/// transport already matched the transaction, command and checksum, so the parse gate
/// has to accept the longer reply instead of calling it out of order.
/// </summary>
public sealed class BladeResponseFramingTests
{
    [Fact]
    public void AFanSpeedReplyLongerThanTheRequestStillParses()
    {
        var request = BladeThermalProtocol.CreateGetCurrentSpeedRequest(BladeThermalProtocol.CpuFanId);
        var response = Respond(request, declaredLength: 5, [0x01, 0x01, 0x1E]);

        Assert.Equal(3000, BladeThermalProtocol.ParseCurrentSpeedRpm(response, 0x01, request));
    }

    [Fact]
    public void TheSameReplyIsRejectedWhereTheExactLengthIsStillRequired()
    {
        var request = BladeThermalProtocol.CreateGetCurrentSpeedRequest(BladeThermalProtocol.CpuFanId);
        var response = Respond(request, declaredLength: 5, [0x01, 0x01, 0x1E]);

        Assert.False(RazerFeatureReport.IsSuccessfulResponse(request, response, 3));
    }

    [Fact]
    public void AReplyShorterThanTheFieldsWeReadIsStillRejected()
    {
        var request = BladeThermalProtocol.CreateGetCurrentSpeedRequest(BladeThermalProtocol.CpuFanId);
        var response = Respond(request, declaredLength: 2, [0x01, 0x01]);

        Assert.Throws<InvalidOperationException>(
            () => BladeThermalProtocol.ParseCurrentSpeedRpm(response, 0x01, request));
    }

    private static byte[] Respond(byte[] request, byte declaredLength, byte[] arguments)
    {
        var response = request.ToArray();
        response[1] = 0x02;
        response[6] = declaredLength;
        arguments.CopyTo(response, RazerFeatureReport.ArgumentsOffset);
        response[89] = RazerFeatureReport.CalculateCrc(response);
        return response;
    }
}
