using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerGamepadProtocolTests
{
    [Fact]
    public void SensitivityRequestUsesReportId4()
    {
        var report = RazerGamepadProtocol.CreateGetSensitivityRequest(0x05, 0x02, 0x01);

        Assert.Equal(RazerFeatureReport.Length, report.Length);
        Assert.Equal(4, report[0]);
        Assert.Equal(0x05, report[2]);
        Assert.Equal(3, report[6]);
        Assert.Equal(0x0C, report[7]);
        Assert.Equal(0x82, report[8]);
        Assert.Equal(new byte[] { 0x02, 0x01, 0x00 }, report[9..12]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(report), report[89]);
    }

    [Fact]
    public void ZoneSetPacksBigEndianPairs()
    {
        var report = RazerGamepadProtocol.CreateSetZonesRequest(
            0x01,
            0x02,
            0x03,
            new RazerGamepadZones(0x1234, 0x0567, 0x0089, true));

        Assert.Equal(9, report[6]);
        Assert.Equal(0x05, report[8]);
        Assert.Equal(new byte[] { 0x02, 0x03, 0x12, 0x34, 0x05, 0x67, 0x00, 0x89, 0x01 }, report[9..18]);
    }

    [Fact]
    public void TriggerRequestsCarryTheirOwnSizes()
    {
        var range = RazerGamepadProtocol.CreateSetTriggerRangeRequest(0x01, 0x02, 0x04, 10, 90);
        Assert.Equal(4, range[6]);
        Assert.Equal(0x13, range[8]);
        Assert.Equal(new byte[] { 0x02, 0x04, 10, 90 }, range[9..13]);

        var mode = RazerGamepadProtocol.CreateSetTriggerModeRequest(0x01, 0x02, 0x04, 2);
        Assert.Equal(3, mode[6]);
        Assert.Equal(0x15, mode[8]);
        Assert.Equal(new byte[] { 0x02, 0x04, 2 }, mode[9..12]);
    }

    [Fact]
    public void ControlListAndAnalogControlUseTheMiddlewareLayout()
    {
        var list = RawReply(2, 1, 2);
        Assert.Equal(new byte[] { 1, 2 }, RazerGamepadProtocol.ReadControlIdList(list));

        var analog = RazerGamepadProtocol.ReadAnalogControl(Reply(0x01, 2, 0), 0x01);
        Assert.Equal(0x0C, analog.ClassId);
        Assert.Equal(0x01, analog.ControlId);
        Assert.Equal(RazerGamepadAnalogType.JoystickY, analog.Type);
        Assert.Equal(0x00, analog.Index);
    }

    [Fact]
    public void OptionAndRawReadersFollowTheJSParsers()
    {
        Assert.Equal(1, RazerGamepadProtocol.ReadIgnoreDPadDiagonals(RawReply(0x0C, 1)));
        Assert.Equal(RazerGamepadSocdMode.NoCleaning, RazerGamepadProtocol.ReadSocdMode(RawReply(0x0C, 3)));
        Assert.Equal(0x1234, RazerGamepadProtocol.ReadAdcMask(RawReply(0x12, 0x34)));

        var raw = RazerGamepadProtocol.ReadRawInput(RawReply(0, 1, 0, 2, 0, 3, 0, 4, 0, 5, 0, 6));
        Assert.Equal(new RazerGamepadRawInput(1, 2, 3, 4, 5, 6), raw);
    }

    [Fact]
    public void CalibrationAndProfileReadersUseBigEndian()
    {
        var calibration = RazerGamepadProtocol.ReadCalibration(RawReply(
            0x01, 0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE, 0xF0, 0x11, 0x22, 0x33, 0x44,
            0x55, 0x66, 0x77, 0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x00,
            0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09));
        Assert.Equal(0x01, calibration.AnalogId);
        Assert.Equal(16, calibration.Values.Count);
        Assert.Equal((ushort)0x1234, calibration.Values[0]);

        Assert.Equal(0x07, RazerGamepadProtocol.ReadActiveProfileId(RawReply(0x07)));
    }

    [Fact]
    public void OptionalCommandsKeepTheirJSSizes()
    {
        Assert.Equal(80, RazerGamepadProtocol.CreateGetControlIdListRequest(0x01)[6]);
        var adcMask = RazerGamepadProtocol.CreateGetAdcMaskRequest(0x01);
        Assert.Equal(2, adcMask[6]);
        Assert.All(adcMask[9..89], value => Assert.Equal(0, value));
        Assert.Equal(51, RazerGamepadProtocol.CreateGetCalibrationRequest(
            0x01, RazerGamepadCalibrationControl.LeftJoystick)[6]);

        var digital = RazerGamepadProtocol.CreateSetDigitalTriggerRequest(
            0x01, 0x02, 0x01, new RazerGamepadDigitalTrigger(17, true));
        Assert.Equal(new byte[] { 0x02, 0x01, 17, 1 }, digital[9..13]);

        var calibrationArguments = RazerGamepadProtocol.CreateGetRawInputRequest(0x01);
        Assert.Equal(new byte[] { 80, 12, 152 }, calibrationArguments[9..12]);
    }

    [Fact]
    public void ButtonAssignmentUsesTheResolved02Table()
    {
        var get = RazerGamepadProtocol.CreateGetButtonAssignmentRequest(
            0x01, 0x02, 0x20, RazerGamepadButtonMode.Normal);
        Assert.Equal(80, get[6]);
        Assert.Equal(0x02, get[7]);
        Assert.Equal(0x8C, get[8]);
        Assert.Equal(new byte[] { 0x02, 0x20, 0x00 }, get[9..12]);

        var set = RazerGamepadProtocol.CreateSetButtonAssignmentRequest(
            0x01, 0x02, 0x20, RazerGamepadButtonMode.Hypershift,
            RazerGamepadFunction.MacroTypeI, 3, [0x01, 0x02, 0x03]);
        Assert.Equal(80, set[6]);
        Assert.Equal(0x02, set[7]);
        Assert.Equal(0x0C, set[8]);
        Assert.Equal(new byte[] { 0x02, 0x20, 0x01, 0x03, 0x03, 0x01, 0x02, 0x03 }, set[9..17]);

        var assignment = RazerGamepadProtocol.ReadButtonAssignment(
            RawReply(0x02, 0x20, 0x01, 0x03, 0x03, 0x01, 0x02, 0x03));
        Assert.Equal(0x20, assignment.ButtonId);
        Assert.Equal(RazerGamepadButtonMode.Hypershift, assignment.Mode);
        Assert.Equal(RazerGamepadFunction.MacroTypeI, assignment.Function);
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03 }, assignment.Data.Take(assignment.DataSize).ToArray());
    }

    [Fact]
    public void ReadersParseTheValueAfterClassAndControl()
    {
        Assert.Equal(9, RazerGamepadProtocol.ReadSensitivity(Reply(0x01, 9), 0x01));
        Assert.Equal(40, RazerGamepadProtocol.ReadVibrationScale(Reply(0x05, 40), 0x05));
        Assert.Equal(2, RazerGamepadProtocol.ReadTriggerMode(Reply(0x02, 2), 0x02));

        var zones = RazerGamepadProtocol.ReadZones(
            Reply(0x01, 0x12, 0x34, 0x05, 0x67, 0x00, 0x89, 0x01),
            0x01);
        Assert.Equal(new RazerGamepadZones(0x1234, 0x0567, 0x0089, true), zones);

        Assert.Equal((byte)10, RazerGamepadProtocol.ReadTriggerRange(Reply(0x02, 10, 20), 0x02).Start);
        Assert.Equal((byte)20, RazerGamepadProtocol.ReadTriggerRange(Reply(0x02, 10, 20), 0x02).End);
    }

    [Fact]
    public void ReadersRejectAForeignControlOrShortFrame()
    {
        Assert.Throws<InvalidDataException>(
            () => RazerGamepadProtocol.ReadSensitivity(Reply(0x02, 9), 0x01));
        Assert.Throws<ArgumentException>(
            () => RazerGamepadProtocol.ReadSensitivity(new byte[90], 0x01));
        Assert.Throws<ArgumentException>(
            () => RazerGamepadProtocol.ReadZones(new byte[90], 0x01));
    }

    [Fact]
    public void PollingAndProfileFramesMatchTheBundle()
    {
        var profiles = RazerGamepadProtocol.CreateGetProfileIdListRequest(0x01);
        Assert.Equal(0x50, profiles[6]);
        Assert.Equal(0x05, profiles[7]);
        Assert.Equal(0x81, profiles[8]);

        var setProfile = RazerGamepadProtocol.CreateSetActiveProfileIdRequest(0x01, 2);
        Assert.Equal(0x05, setProfile[7]);
        Assert.Equal(0x04, setProfile[8]);
        Assert.Equal([2], setProfile[9..10]);

        var profilePolling = RazerGamepadProtocol.CreateSetProfilePollingRateRequest(0x01, 1, 0x02);
        Assert.Equal(0x00, profilePolling[7]);
        Assert.Equal(0x0E, profilePolling[8]);
        Assert.Equal([1, 0x02], profilePolling[9..11]);

        var highSpeedPolling = RazerGamepadProtocol.CreateSetHighSpeedPollingRateRequest(0x01, 1, 0x08);
        Assert.Equal(0x00, highSpeedPolling[7]);
        Assert.Equal(0x40, highSpeedPolling[8]);
        Assert.Equal([1, 0x08], highSpeedPolling[9..11]);

        Assert.Equal((byte)0x08, RazerGamepadProtocol.EncodeStandardPollingRate(125));
        Assert.Equal(8_000, RazerGamepadProtocol.DecodeHighSpeedPollingRate(0x01));
        Assert.Throws<InvalidDataException>(
            () => RazerGamepadProtocol.DecodeHighSpeedPollingRate(0x03));
    }

    [Fact]
    public void ButtonListAndVibrationUseTheBundleValues()
    {
        var list = RazerGamepadProtocol.CreateGetButtonIdListRequest(0x01);
        Assert.Equal(0x02, list[7]);
        Assert.Equal(0x84, list[8]);

        Assert.Equal((byte)0, RazerGamepadProtocol.ToVibrationScale(0));
        Assert.Equal((byte)17, RazerGamepadProtocol.ToVibrationScale(1));
        Assert.Equal((byte)50, RazerGamepadProtocol.ToVibrationScale(3));
        Assert.Equal((byte)100, RazerGamepadProtocol.ToVibrationScale(6));
        Assert.Equal((byte)5, RazerGamepadProtocol.ToVibrationIndex(83));
        Assert.Equal((byte)3, RazerGamepadProtocol.ToVibrationIndex(50));
        Assert.Equal((byte)6, RazerGamepadProtocol.ToVibrationIndex(100));
    }

    private static byte[] Reply(byte controlId, params byte[] value)
    {
        var report = new byte[RazerFeatureReport.Length];
        report[0] = RazerGamepadProtocol.ReportId;
        report[7] = RazerGamepadProtocol.CommandClass;
        report[9] = RazerGamepadProtocol.CommandClass;
        report[10] = controlId;
        value.CopyTo(report, 11);
        report[89] = RazerFeatureReport.CalculateCrc(report);
        return report;
    }

    private static byte[] RawReply(params byte[] value)
    {
        var report = new byte[RazerFeatureReport.Length];
        report[0] = RazerGamepadProtocol.ReportId;
        value.CopyTo(report, RazerFeatureReport.ArgumentsOffset);
        report[89] = RazerFeatureReport.CalculateCrc(report);
        return report;
    }
}
