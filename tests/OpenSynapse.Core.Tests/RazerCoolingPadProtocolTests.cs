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

    [Fact]
    public void GetCoolingMode_UsesClassZeroDAndThreeByteDataSize()
    {
        var report = RazerCoolingPadProtocol.CreateGetCoolingModeRequest();

        Assert.Equal(0x03, report[6]);
        Assert.Equal(0x0D, report[7]);
        Assert.Equal(0x90, report[8]);
        Assert.Equal(0x01, report[9]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(report), report[89]);
    }

    [Theory]
    [InlineData(RazerCoolingPadFixedMode.Low, 1600, 32)]
    [InlineData(RazerCoolingPadFixedMode.Mid, 2200, 44)]
    [InlineData(RazerCoolingPadFixedMode.High, 2700, 54)]
    public void SetCoolingMode_WritesSourceFlagModeAndFiftyRpmUnits(
        RazerCoolingPadFixedMode mode,
        int expectedRpm,
        byte expectedUnits)
    {
        var report = RazerCoolingPadProtocol.CreateSetCoolingModeRequest(mode);

        Assert.Equal(0x03, report[6]);
        Assert.Equal(0x0D, report[7]);
        Assert.Equal(0x10, report[8]);
        Assert.Equal(new byte[] { 0x01, (byte)mode, expectedUnits }, report[9..12].ToArray());
        Assert.Equal(expectedRpm, RazerCoolingPadProtocol.RpmFor(mode));
        Assert.Equal(RazerFeatureReport.CalculateCrc(report), report[89]);
    }

    [Fact]
    public void SetCoolingMode_OffUsesTheFirmwareSourceAndZeroSpeed()
    {
        var report = RazerCoolingPadProtocol.CreateSetCoolingModeRequest(RazerCoolingPadFixedMode.Off);

        Assert.Equal(new byte[] { 0x00, 0x06, 0x00 }, report[9..12].ToArray());
        Assert.Equal(0, RazerCoolingPadProtocol.RpmFor(RazerCoolingPadFixedMode.Off));
        Assert.Equal(RazerFeatureReport.CalculateCrc(report), report[89]);
    }

    [Theory]
    [InlineData(RazerCoolingPadSmartMode.Quiet, 3)]
    [InlineData(RazerCoolingPadSmartMode.Balanced, 4)]
    [InlineData(RazerCoolingPadSmartMode.Performance, 5)]
    public void SetSmartMode_WritesTheBundleModeWithoutSpeed(
        RazerCoolingPadSmartMode mode,
        byte expectedMode)
    {
        var report = RazerCoolingPadProtocol.CreateSetSmartModeRequest(mode);

        Assert.Equal(0x03, report[6]);
        Assert.Equal(0x0D, report[7]);
        Assert.Equal(0x10, report[8]);
        Assert.Equal(new byte[] { 0x00, expectedMode, 0x00 }, report[9..12].ToArray());
        Assert.Equal(RazerFeatureReport.CalculateCrc(report), report[89]);
    }

    [Fact]
    public void ParseCoolingMode_ReadsSourceModeAndRawUnits()
    {
        var response = Response(3, 0x0D, 0x90, [0x01, 0x02, 54]);

        var state = RazerCoolingPadProtocol.ParseCoolingMode(response);

        Assert.Equal(0x01, state.Source);
        Assert.Equal(0x02, state.ModeId);
        Assert.Equal(54, state.SpeedUnits);
        Assert.Equal(2700, state.Rpm);
    }

    [Fact]
    public void ParseCoolingMode_RejectsAWrongDataSize()
    {
        var response = Response(2, 0x0D, 0x90, [0x01, 0x02]);

        Assert.Throws<InvalidOperationException>(
            () => RazerCoolingPadProtocol.ParseCoolingMode(response));
    }

    [Theory]
    [InlineData(500, 10)]
    [InlineData(2700, 54)]
    public void SetCurveSpeed_AllowsTheBundleTableRange(int rpm, byte expectedUnits)
    {
        var report = RazerCoolingPadProtocol.CreateSetCurveSpeedRequest(rpm);

        Assert.Equal(0x03, report[6]);
        Assert.Equal(0x0D, report[7]);
        Assert.Equal(0x01, report[8]);
        Assert.Equal(new byte[] { 0x01, 0x05, expectedUnits }, report[9..12].ToArray());
        Assert.Equal(RazerFeatureReport.CalculateCrc(report), report[89]);
    }

    [Theory]
    [InlineData(450)]
    [InlineData(1525)]
    [InlineData(2750)]
    public void SetCurveSpeed_RejectsTargetsOutsideTheTableRange(int rpm)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RazerCoolingPadProtocol.CreateSetCurveSpeedRequest(rpm));
    }

    [Fact]
    public void GetLightingRegions_UsesChromaClassWithFiftyByteDataSize()
    {
        var report = RazerCoolingPadProtocol.CreateGetLightingRegionsRequest();

        Assert.Equal(0x50, report[6]);
        Assert.Equal(0x0F, report[7]);
        Assert.Equal(0x80, report[8]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(report), report[89]);
        Assert.Equal(0x00, report[90]);
    }

    [Fact]
    public void ParseLightingRegions_ReadsFiveByteRecordsAndCapsAtEighty()
    {
        var response = Response(
            10,
            0x0F,
            0x80,
            [0x07, 0x01, 0x01, 0x03, 0x01, 0x01, 0x00, 0x00, 0x01, 0x01]);

        var regions = RazerCoolingPadProtocol.ParseLightingRegions(response);

        Assert.Equal(2, regions.Count);
        Assert.Equal(new RazerCoolingPadLightingRegion(0x07, 0x01, 0x01, 0x03, 0x01), regions[0]);
        Assert.Equal(new RazerCoolingPadLightingRegion(0x01, 0x00, 0x00, 0x01, 0x01), regions[1]);

        var longResponse = Response(254, 0x0F, 0x80, new byte[80]);
        Assert.Equal(16, RazerCoolingPadProtocol.ParseLightingRegions(longResponse).Count);
    }

    [Fact]
    public void ParseLightingEffectIds_SkipsZeroEntries()
    {
        var response = Response(6, 0x0F, 0x81, [0x00, 0x01, 0x02, 0x00, 0x03, 0x04]);

        Assert.Equal(new byte[] { 1, 2, 3, 4 }, RazerCoolingPadProtocol.ParseLightingEffectIds(response).ToArray());
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 2)]
    [InlineData(50, 127)]
    [InlineData(100, 255)]
    public void SetLightingBrightness_ScalesPercentToTheFirmwareByte(int percent, int expected)
    {
        var report = RazerCoolingPadProtocol.CreateSetLightingBrightnessRequest(0x07, percent);

        Assert.Equal(0x03, report[6]);
        Assert.Equal(0x0F, report[7]);
        Assert.Equal(0x04, report[8]);
        Assert.Equal(new byte[] { 0x00, 0x07, (byte)expected }, report[9..12].ToArray());
        Assert.Equal(RazerFeatureReport.CalculateCrc(report), report[89]);
    }

    [Fact]
    public void SetLightingBrightness_RejectsPercentOutsideZeroToHundred()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RazerCoolingPadProtocol.CreateSetLightingBrightnessRequest(1, 101));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RazerCoolingPadProtocol.CreateSetLightingBrightnessRequest(1, -1));
    }

    [Fact]
    public void ParseLightingBrightness_RoundsUpToPercent()
    {
        var half = Response(3, 0x0F, 0x84, [0x00, 0x07, 0x80]);
        var full = Response(3, 0x0F, 0x84, [0x00, 0x07, 0xFF]);

        Assert.Equal(51, RazerCoolingPadProtocol.ParseLightingBrightness(half, 0x07));
        Assert.Equal(100, RazerCoolingPadProtocol.ParseLightingBrightness(full, 0x07));
    }

    [Fact]
    public void ParseLightingBrightness_RejectsAReplyForAnotherRegion()
    {
        var response = Response(3, 0x0F, 0x84, [0x00, 0x06, 0x80]);

        Assert.Throws<InvalidDataException>(
            () => RazerCoolingPadProtocol.ParseLightingBrightness(response, 0x07));
    }

    [Fact]
    public void SetLightingEffect_SpectrumDeclaresSixBytesAndStaticNine()
    {
        var spectrum = RazerCoolingPadProtocol.CreateSetLightingEffectRequest(0x07, 0x03, 0x00, 0x10, []);
        var solid = RazerCoolingPadProtocol.CreateSetLightingEffectRequest(0x07, 0x01, 0x00, 0x10, [0x00, 0xFF, 0x00]);

        Assert.Equal(6, spectrum[6]);
        Assert.Equal(new byte[] { 0x00, 0x07, 0x03, 0x00, 0x10, 0x00 }, spectrum[9..15].ToArray());
        Assert.Equal(9, solid[6]);
        Assert.Equal(new byte[] { 0x00, 0x07, 0x01, 0x00, 0x10, 0x01, 0x00, 0xFF, 0x00 }, solid[9..18].ToArray());
        Assert.Equal(RazerFeatureReport.CalculateCrc(solid), solid[89]);
    }

    [Fact]
    public void SetLightingEffect_RejectsPartialOrTooManyColors()
    {
        Assert.Throws<ArgumentException>(
            () => RazerCoolingPadProtocol.CreateSetLightingEffectRequest(1, 1, 0, 0, [0x00, 0xFF]));
        Assert.Throws<ArgumentException>(
            () => RazerCoolingPadProtocol.CreateSetLightingEffectRequest(1, 1, 0, 0, new byte[75]));
    }

    [Fact]
    public void ParseLightingEffect_ReadsEffectAndColors()
    {
        var response = Response(
            12,
            0x0F,
            0x82,
            [0x00, 0x07, 0x01, 0x00, 0x10, 0x02, 0x00, 0xFF, 0x00, 0xFF, 0x00, 0x00]);

        var effect = RazerCoolingPadProtocol.ParseLightingEffect(response, 0x07);

        Assert.Equal(0x01, effect.EffectId);
        Assert.Equal(0x00, effect.Flags);
        Assert.Equal(0x10, effect.Rate);
        Assert.Equal(new byte[] { 0x00, 0xFF, 0x00, 0xFF, 0x00, 0x00 }, effect.Colors.ToArray());
    }

    [Fact]
    public void ParseLightingEffect_RejectsATruncatedColorList()
    {
        var response = Response(9, 0x0F, 0x82, [0x00, 0x07, 0x01, 0x00, 0x10, 0x02, 0x00, 0xFF, 0x00]);

        Assert.Throws<InvalidDataException>(
            () => RazerCoolingPadProtocol.ParseLightingEffect(response, 0x07));
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
