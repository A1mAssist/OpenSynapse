using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Core.Tests;

public sealed class BladeProduct710ProtocolTests
{
    [Fact]
    public void BuildsProduct710BatteryAndSleepGets()
    {
        Assert.Equal(new byte[] { 0x00 },
            BladeProduct710Protocol.CreateGetBatteryLevelRequest()[9..10]);
        Assert.Equal(new byte[] { 0x00 },
            BladeProduct710Protocol.CreateGetChargingStatusRequest()[9..10]);
        Assert.Equal(new byte[] { 0x00 },
            BladeProduct710Protocol.CreateGetAutoSleepRequest()[9..10]);
        Assert.Equal((byte)0x88, BladeProduct710Protocol.CreateGetAutoSleepRequest()[8]);
        Assert.Equal((byte)0x83, BladeProduct710Protocol.CreateGetTimeToSleepRequest()[8]);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(128, 50)]
    [InlineData(255, 100)]
    public void ParsesBatteryPercent(byte raw, int expected)
    {
        var response = RazerFeatureReport.CreateRequest(0x1F, 0x02, 0x07, 0x80, new byte[] { 0x00, raw });
        response[1] = 0x02;
        response[89] = RazerFeatureReport.CalculateCrc(response);

        Assert.Equal(expected, BladeProduct710Protocol.ParseBatteryPercent(response));
    }

    [Fact]
    public void ParsesReadOnlyPowerStates()
    {
        Assert.Equal((byte)2, BladeProduct710Protocol.ParseChargingStatusRaw(CreateResponse(0x84, 0x00, 0x02)));
        Assert.Equal((byte)1, BladeProduct710Protocol.ParseAutoSleepRaw(CreateResponse(0x88, 0x00, 0x01)));
        Assert.Equal(300, BladeProduct710Protocol.ParseTimeToSleepSeconds(CreateResponse(0x83, 0x01, 0x2C)));
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0xFF)]
    public void ParsesPowerStateRawValues(byte raw)
    {
        Assert.Equal(raw, BladeProduct710Protocol.ParseChargingStatusRaw(CreateResponse(0x84, 0x00, raw)));
        Assert.Equal(raw, BladeProduct710Protocol.ParseAutoSleepRaw(CreateResponse(0x88, 0x00, raw)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(180)]
    [InlineData(65535)]
    public void ParsesTimeToSleepAsBigEndianSeconds(int seconds)
    {
        Assert.Equal(seconds, BladeProduct710Protocol.ParseTimeToSleepSeconds(
            CreateResponse(0x83, (byte)(seconds >> 8), (byte)seconds)));
    }

    [Fact]
    public void RejectsWrongObjectCommandLengthAndCrc()
    {
        Assert.Throws<InvalidOperationException>(() =>
            BladeProduct710Protocol.ParseChargingStatusRaw(CreateResponse(0x84, 0x01, 0x00)));

        Assert.Throws<InvalidOperationException>(() =>
            BladeProduct710Protocol.ParseChargingStatusRaw(CreateResponse(0x88, 0x00, 0x00)));

        var wrongLength = CreateResponse(0x84, 0x00, 0x00);
        wrongLength[6] = 0x03;
        wrongLength[89] = RazerFeatureReport.CalculateCrc(wrongLength);
        Assert.Throws<InvalidOperationException>(() =>
            BladeProduct710Protocol.ParseChargingStatusRaw(wrongLength));

        var corrupt = CreateResponse(0x88, 0x00, 0x00);
        corrupt[89] ^= 0xFF;
        Assert.Throws<InvalidOperationException>(() =>
            BladeProduct710Protocol.ParseAutoSleepRaw(corrupt));
    }

    [Fact]
    public void RejectsWrongTransaction()
    {
        var response = CreateResponse(0x84, 0x00, 0x00);
        response[2] = 0x20;
        response[89] = RazerFeatureReport.CalculateCrc(response);

        Assert.Throws<InvalidOperationException>(() =>
            BladeProduct710Protocol.ParseChargingStatusRaw(response));
    }

    private static byte[] CreateResponse(byte commandId, byte first, byte second)
    {
        var response = RazerFeatureReport.CreateRequest(0x1F, 0x02, 0x07, commandId, new[] { first, second });
        response[1] = 0x02;
        response[89] = RazerFeatureReport.CalculateCrc(response);
        return response;
    }
}
