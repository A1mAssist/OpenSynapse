using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Core.Tests;

public sealed class BladeLogoProtocolTests
{
    [Fact]
    public void BuildsExactLogoGetAndSetRequests()
    {
        AssertRequest(BladeLogoProtocol.CreateGetPowerRequest(), 0x80, 0x00);
        AssertRequest(BladeLogoProtocol.CreateGetModeRequest(), 0x82, 0x00);
        AssertRequest(BladeLogoProtocol.CreateSetPowerRequest(false), 0x00, 0x00);
        AssertRequest(BladeLogoProtocol.CreateSetPowerRequest(true), 0x00, 0x01);
        AssertRequest(BladeLogoProtocol.CreateSetModeRequest(BladeLogoMode.Static), 0x02, 0x00);
        AssertRequest(BladeLogoProtocol.CreateSetModeRequest(BladeLogoMode.Breathing), 0x02, 0x02);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BladeLogoProtocol.CreateSetModeRequest(BladeLogoMode.Off));
    }

    [Fact]
    public void UsesOpenRazerBladeTransactionIdForEveryRequest()
    {
        var requests = new[]
        {
            BladeLogoProtocol.CreateGetPowerRequest(),
            BladeLogoProtocol.CreateGetModeRequest(),
            BladeLogoProtocol.CreateSetPowerRequest(true),
            BladeLogoProtocol.CreateSetModeRequest(BladeLogoMode.Breathing),
        };

        Assert.All(requests, request => Assert.Equal(0xFF, request[2]));
    }

    [Theory]
    [InlineData(0x00, false)]
    [InlineData(0x01, true)]
    public void ParsesLogoPower(byte raw, bool expected)
    {
        var response = CreateResponse(0x80, raw);

        Assert.Equal(expected, BladeLogoProtocol.ParsePower(response));
    }

    [Theory]
    [InlineData(0x00, BladeLogoMode.Static)]
    [InlineData(0x02, BladeLogoMode.Breathing)]
    public void ParsesLogoMode(byte raw, BladeLogoMode expected)
    {
        var response = CreateResponse(0x82, raw);

        Assert.Equal(expected, BladeLogoProtocol.ParseMode(response));
    }

    [Fact]
    public void CombinesPowerAndModeWithoutTreatingDeviceModeAsLighting()
    {
        Assert.Equal(BladeLogoMode.Off, BladeLogoProtocol.Combine(false, BladeLogoMode.Breathing));
        Assert.Equal(BladeLogoMode.Static, BladeLogoProtocol.Combine(true, BladeLogoMode.Static));
        Assert.Throws<ArgumentOutOfRangeException>(() => BladeLogoProtocol.Combine(true, BladeLogoMode.Off));
    }

    [Theory]
    [InlineData(0x03)]
    [InlineData(0xFF)]
    public void RejectsUnknownLogoPower(byte raw)
    {
        Assert.Throws<InvalidOperationException>(() => BladeLogoProtocol.ParsePower(CreateResponse(0x80, raw)));
    }

    [Theory]
    [InlineData(0x01)]
    [InlineData(0xFF)]
    public void RejectsUnknownLogoMode(byte raw)
    {
        Assert.Throws<InvalidOperationException>(() => BladeLogoProtocol.ParseMode(CreateResponse(0x82, raw)));
    }

    [Fact]
    public void RejectsWrongObject()
    {
        var response = CreateResponse(0x80, 0x01);
        response[RazerFeatureReport.ArgumentsOffset] = 0x02;
        response[89] = RazerFeatureReport.CalculateCrc(response);

        Assert.Throws<InvalidOperationException>(() => BladeLogoProtocol.ParsePower(response));
    }

    [Fact]
    public void RejectsCorruptCrc()
    {
        var response = CreateResponse(0x80, 0x01);
        response[89] ^= 0xFF;

        Assert.Throws<InvalidOperationException>(() => BladeLogoProtocol.ParsePower(response));
    }

    [Theory]
    [InlineData(0x01)]
    [InlineData(0x1F)]
    public void RejectsNonOpenRazerTransaction(byte transactionId)
    {
        var response = CreateResponse(0x80, 0x01);
        response[2] = transactionId;
        response[89] = RazerFeatureReport.CalculateCrc(response);

        Assert.Throws<InvalidOperationException>(() => BladeLogoProtocol.ParsePower(response));
    }

    [Theory]
    [InlineData(1, 0x04)]
    [InlineData(6, 0x02)]
    [InlineData(8, 0x81)]
    public void RejectsInvalidResponseEnvelope(int index, byte value)
    {
        var response = CreateResponse(0x80, 0x01);
        response[index] = value;
        response[89] = RazerFeatureReport.CalculateCrc(response);

        Assert.Throws<InvalidOperationException>(() => BladeLogoProtocol.ParsePower(response));
    }

    private static byte[] CreateResponse(byte commandId, byte raw)
    {
        var response = RazerFeatureReport.CreateRequest(
            0xFF, 0x03, 0x03, commandId, new byte[] { 0x01, 0x04, raw });
        response[1] = 0x02;
        return response;
    }

    private static void AssertRequest(byte[] request, byte commandId, byte value)
    {
        Assert.Equal(0xFF, request[2]);
        Assert.Equal(0x03, request[6]);
        Assert.Equal(0x03, request[7]);
        Assert.Equal(commandId, request[8]);
        Assert.Equal(new byte[] { 0x01, 0x04, value }, request[9..12]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(request), request[89]);
    }
}
