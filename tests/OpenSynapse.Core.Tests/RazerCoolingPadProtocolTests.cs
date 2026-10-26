using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerCoolingPadProtocolTests
{
    [Fact]
    public void SetSpeed_UsesTheGenericRzDevice25ByteLayout()
    {
        var report = RazerCoolingPadProtocol.CreateSetSpeedRequest(1500);

        Assert.Equal(RazerFeatureReport.Length, report.Length);
        Assert.Equal(0x00, report[0]);
        Assert.Equal(0x1F, report[2]);
        Assert.Equal(0x03, report[6]);
        Assert.Equal(0x0D, report[7]);
        Assert.Equal(0x01, report[8]);
        Assert.Equal(new byte[] { 0x01, 0x05, 30 }, report[9..12].ToArray());
        Assert.Equal(RazerFeatureReport.CalculateCrc(report), report[89]);
        Assert.Equal(0x00, report[90]);
    }

    [Fact]
    public void GetSpeed_UsesClassAndFan1WithThreeByteDataSize()
    {
        var report = RazerCoolingPadProtocol.CreateGetSpeedRequest();

        Assert.Equal(0x03, report[6]);
        Assert.Equal(0x0D, report[7]);
        Assert.Equal(0x81, report[8]);
        Assert.Equal(new byte[] { 0x01, 0x05, 0x00 }, report[9..12].ToArray());
        Assert.Equal(RazerFeatureReport.CalculateCrc(report), report[89]);
    }

    [Fact]
    public void GetFanInformation_UsesClassWithTwelveByteDataSize()
    {
        var report = RazerCoolingPadProtocol.CreateGetFanInformationRequest();

        Assert.Equal(0x0C, report[6]);
        Assert.Equal(0x0D, report[7]);
        Assert.Equal(0x83, report[8]);
        Assert.Equal(0x01, report[9]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(report), report[89]);
    }

    [Theory]
    [InlineData(150, 3)]
    [InlineData(1500, 30)]
    [InlineData(2600, 52)]
    public void ToUnits_ConvertsRpmAtFiftyRpmPerUnit(int rpm, int expectedUnits)
    {
        Assert.Equal(expectedUnits, RazerCoolingPadProtocol.ToUnits(rpm));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(125)]
    [InlineData(2650)]
    public void ToUnits_RejectsOutOfRangeOrUnrepresentableSpeeds(int rpm)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RazerCoolingPadProtocol.ToUnits(rpm));
    }

    [Fact]
    public void ParseSpeed_ReadsTheSingleByteAtData2()
    {
        var response = Response(0x03, 0x0D, 0x81, [0x01, 0x05, 0xFF]);

        Assert.Equal(12750, RazerCoolingPadProtocol.ParseSpeedRpm(response));
        // The protocol byte allows 12750 RPM, but the pad firmware caps at 2600.
    }

    [Fact]
    public void ParseSpeed_ZeroMeansStopped()
    {
        var response = Response(0x03, 0x0D, 0x81, [0x01, 0x05, 0x00]);

        Assert.Equal(0, RazerCoolingPadProtocol.ParseSpeedRpm(response));
    }

    [Fact]
    public void ParseSpeed_RejectsTheWrongThermalObject()
    {
        var response = Response(0x03, 0x0D, 0x81, [0x01, 0x06, 30]);

        Assert.Throws<InvalidOperationException>(() => RazerCoolingPadProtocol.ParseSpeedRpm(response));
    }

    [Fact]
    public void ParseSpeed_RejectsAnOutOfOrderResponse()
    {
        var response = Response(0x03, 0x0D, 0x82, [0x01, 0x05, 30]);

        Assert.Throws<InvalidOperationException>(() => RazerCoolingPadProtocol.ParseSpeedRpm(response));
    }

    [Fact]
    public void ParseSpeed_RejectsABadCrc()
    {
        var response = Response(0x03, 0x0D, 0x81, [0x01, 0x05, 30]);
        response[89] ^= 0xFF;

        Assert.Throws<InvalidOperationException>(() => RazerCoolingPadProtocol.ParseSpeedRpm(response));
    }

    [Fact]
    public void ParseSpeed_RejectsTheOldSixByteWidth()
    {
        var response = Response(0x06, 0x0D, 0x81, [0x01, 0x05, 0x00, 30, 0x00, 0x00]);

        Assert.Throws<InvalidOperationException>(() => RazerCoolingPadProtocol.ParseSpeedRpm(response));
    }

    [Fact]
    public void ParseFanInformation_ReadsBigEndianPairsAndHysteresis()
    {
        var response = Response(
            0x0C,
            0x0D,
            0x83,
            [0x01, 0x00, 0x64, 0x07, 0x00, 0x32, 0x00, 0xC8, 0x00, 0x00, 0x00, 0x00]);

        var information = RazerCoolingPadProtocol.ParseFanInformation(response);

        Assert.Equal(100, information.ThermalSamplingInterval);
        Assert.Equal(7, information.Hysteresis);
        Assert.Equal(50, information.FanUpdateInterval);
        Assert.Equal(200, information.FanMaximumDelta);
    }

    [Fact]
    public void ParseFanInformation_RejectsAnUnexpectedThermalClass()
    {
        var response = Response(
            0x0C,
            0x0D,
            0x83,
            [0x02, 0x00, 0x64, 0x07, 0x00, 0x32, 0x00, 0xC8, 0x00, 0x00, 0x00, 0x00]);

        Assert.Throws<InvalidOperationException>(() => RazerCoolingPadProtocol.ParseFanInformation(response));
    }

    private static byte[] Response(byte dataSize, byte commandClass, byte commandId, byte[] arguments)
    {
        var response = new byte[RazerFeatureReport.Length];
        response[0] = 0x00;
        response[1] = 0x02;
        response[2] = 0x1F;
        response[6] = dataSize;
        response[7] = commandClass;
        response[8] = commandId;
        arguments.CopyTo(response, RazerFeatureReport.ArgumentsOffset);
        response[89] = RazerFeatureReport.CalculateCrc(response);
        return response;
    }
}
