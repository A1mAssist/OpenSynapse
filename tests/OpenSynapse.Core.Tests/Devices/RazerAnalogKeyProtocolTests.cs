using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerAnalogKeyProtocolTests
{
    [Fact]
    public void MultiActuationUsesTheFiveByteRecord()
    {
        var get = RazerAnalogKeyProtocol.CreateGetMultiAnalogKeyActuationPointRequest(
            0x11,
            0x00,
            0x03,
            [0x01, 0x02]);
        var set = RazerAnalogKeyProtocol.CreateSetMultiAnalogKeyActuationPointRequest(
            0x12,
            0x00,
            0x03,
            [
                new RazerAnalogActuationPoint(0x01, 0x1234, 0x5678),
                new RazerAnalogActuationPoint(0x02, 0x0102, 0x0304),
            ]);

        Assert.Equal((12, 0x02, 0x99), Command(get));
        Assert.Equal(new byte[] { 0x00, 0x03, 0x01, 0, 0, 0, 0, 0x02, 0, 0, 0, 0 }, get[9..21]);
        Assert.Equal((12, 0x02, 0x19), Command(set));
        Assert.Equal(
            new byte[]
            {
                0x00, 0x03,
                0x01, 0x12, 0x34, 0x56, 0x78,
                0x02, 0x01, 0x02, 0x03, 0x04,
            },
            set[9..21]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(set), set[89]);

        var table = RazerAnalogKeyProtocol.ParseMultiAnalogKeyActuationPoints(
            Response(0x12, 12, set[9..21]));

        Assert.Equal(0x00, table.DataType);
        Assert.Equal(0x03, table.ClassId);
        Assert.Equal(2, table.Points.Count);
        Assert.Equal(new RazerAnalogActuationPoint(0x01, 0x1234, 0x5678), table.Points[0]);
        Assert.Equal(new RazerAnalogActuationPoint(0x02, 0x0102, 0x0304), table.Points[1]);
    }

    [Fact]
    public void MultiRapidTriggerUsesTheSixByteRecord()
    {
        var get = RazerAnalogKeyProtocol.CreateGetMultiAnalogKeyRapidTriggerRequest(
            0x21,
            0x00,
            0x03,
            [0x01, 0x02]);
        var set = RazerAnalogKeyProtocol.CreateSetMultiAnalogKeyRapidTriggerRequest(
            0x22,
            0x00,
            0x03,
            [new RazerAnalogRapidTrigger(0x01, true, 0x1234, 0x5678)]);

        Assert.Equal((14, 0x02, 0x9A), Command(get));
        Assert.Equal(
            new byte[] { 0x00, 0x03, 0x01, 0, 0, 0, 0, 0, 0x02, 0, 0, 0, 0, 0 },
            get[9..23]);
        Assert.Equal((8, 0x02, 0x1A), Command(set));
        Assert.Equal(new byte[] { 0x00, 0x03, 0x01, 0x01, 0x12, 0x34, 0x56, 0x78 }, set[9..17]);

        var table = RazerAnalogKeyProtocol.ParseMultiAnalogKeyRapidTriggers(
            Response(0x22, 8, set[9..17]));

        Assert.Equal(0x00, table.DataType);
        Assert.Equal(0x03, table.ClassId);
        Assert.Single(table.Entries);
        Assert.Equal(new RazerAnalogRapidTrigger(0x01, true, 0x1234, 0x5678), table.Entries[0]);
    }

    [Fact]
    public void ContinuousRapidTriggerCarriesTheClassAndEnabledByte()
    {
        var get = RazerAnalogKeyProtocol.CreateGetAnalogKeyContinuousRapidTriggerRequest(0x31, 0x01);
        var set = RazerAnalogKeyProtocol.CreateSetAnalogKeyContinuousRapidTriggerRequest(0x32, 0x01, true);

        Assert.Equal((2, 0x02, 0xA0), Command(get));
        Assert.Equal(new byte[] { 0x01, 0x00 }, get[9..11]);
        Assert.Equal((2, 0x02, 0x20), Command(set));
        Assert.Equal(new byte[] { 0x01, 0x01 }, set[9..11]);

        var state = RazerAnalogKeyProtocol.ParseAnalogKeyContinuousRapidTrigger(
            Response(0x32, 2, 0x01, 0x01));

        Assert.Equal(0x01, state.ClassId);
        Assert.True(state.Enabled);
    }

    [Fact]
    public void MultiModTapUsesTheFourByteRecord()
    {
        var get = RazerAnalogKeyProtocol.CreateGetMultiModTapKeyAssignmentRequest(
            0x41,
            0x03,
            [0x01, 0x02]);
        var set = RazerAnalogKeyProtocol.CreateSetMultiModTapKeyAssignmentRequest(
            0x42,
            0x03,
            [
                new RazerAnalogModTapAssignment(0x01, true, 0x04, 0x05),
                new RazerAnalogModTapAssignment(0x02, false, 0x06, 0x07),
            ]);

        Assert.Equal((9, 0x02, 0x9B), Command(get));
        Assert.Equal(new byte[] { 0x03, 0x01, 0, 0, 0, 0x02, 0, 0, 0 }, get[9..18]);
        Assert.Equal((9, 0x02, 0x1B), Command(set));
        Assert.Equal(new byte[] { 0x03, 0x01, 0x01, 0x04, 0x05, 0x02, 0x00, 0x06, 0x07 }, set[9..18]);

        var table = RazerAnalogKeyProtocol.ParseMultiModTapKeyAssignments(
            Response(0x42, 9, set[9..18]));

        Assert.Equal(0x03, table.ClassId);
        Assert.Equal(2, table.Entries.Count);
        Assert.Equal(new RazerAnalogModTapAssignment(0x01, true, 0x04, 0x05), table.Entries[0]);
        Assert.Equal(new RazerAnalogModTapAssignment(0x02, false, 0x06, 0x07), table.Entries[1]);
    }

    [Fact]
    public void MultiKeyAssignmentUsesTheNineByteRecord()
    {
        var get = RazerAnalogKeyProtocol.CreateGetMultiKeyAssignmentRequest(
            0x51,
            0x03,
            [
                new RazerAnalogKeyAssignmentSelector(0x01, RazerButtonAssignmentMode.NormalMode),
                new RazerAnalogKeyAssignmentSelector(0x02, RazerButtonAssignmentMode.HypershiftMode),
            ]);
        var set = RazerAnalogKeyProtocol.CreateSetMultiKeyAssignmentRequest(
            0x52,
            0x03,
            [
                new RazerAnalogKeyAssignment(
                    0x01,
                    RazerButtonAssignmentMode.NormalMode,
                    RazerButtonFunctionType.KeyCode,
                    2,
                    [0xAA, 0xBB]),
                new RazerAnalogKeyAssignment(
                    0x02,
                    RazerButtonAssignmentMode.HypershiftMode,
                    RazerButtonFunctionType.MacroTypeI,
                    1,
                    [0xCC]),
            ]);

        Assert.Equal((19, 0x02, 0x9E), Command(get));
        Assert.Equal(
            new byte[]
            {
                0x03,
                0x01, 0x00, 0, 0, 0, 0, 0, 0, 0,
                0x02, 0x01, 0, 0, 0, 0, 0, 0, 0,
            },
            get[9..28]);
        Assert.Equal((19, 0x02, 0x1E), Command(set));
        Assert.Equal(
            new byte[]
            {
                0x03,
                0x01, 0x00, 0x02, 0x02, 0xAA, 0xBB, 0x00, 0x00, 0x00,
                0x02, 0x01, 0x03, 0x01, 0xCC, 0x00, 0x00, 0x00, 0x00,
            },
            set[9..28]);

        var table = RazerAnalogKeyProtocol.ParseMultiKeyAssignments(
            Response(0x52, 19, set[9..28]));

        Assert.Equal(0x03, table.ClassId);
        Assert.Equal(2, table.Entries.Count);
        Assert.Equal(0x01, table.Entries[0].KeyId);
        Assert.Equal(RazerButtonAssignmentMode.NormalMode, table.Entries[0].Mode);
        Assert.Equal(RazerButtonFunctionType.KeyCode, table.Entries[0].FunctionId);
        Assert.Equal(2, table.Entries[0].DataSize);
        Assert.Equal(new byte[] { 0xAA, 0xBB, 0, 0, 0 }, table.Entries[0].Data);
    }

    [Fact]
    public void BottomDeadZoneReportModeAndKillSwitchUseTheirTuples()
    {
        var getDeadZone = RazerAnalogKeyProtocol.CreateGetAnalogKeyBottomDeadZoneRequest(0x61, 0x01);
        var setDeadZone = RazerAnalogKeyProtocol.CreateSetAnalogKeyBottomDeadZoneRequest(0x62, 0x01, 0x40);
        var getReportMode = RazerAnalogKeyProtocol.CreateGetAnalogReportModeRequest(0x63);
        var setReportMode = RazerAnalogKeyProtocol.CreateSetAnalogReportModeRequest(0x64, 0x02);
        var getKillSwitch = RazerAnalogKeyProtocol.CreateGetKeyboardKillSwitchRequest(0x65, 0x01);
        var setKillSwitch = RazerAnalogKeyProtocol.CreateSetKeyboardKillSwitchRequest(
            0x66,
            0x01,
            true,
            0x02,
            0x03);

        Assert.Equal((2, 0x02, 0xA8), Command(getDeadZone));
        Assert.Equal(new byte[] { 0x01, 0x00 }, getDeadZone[9..11]);
        Assert.Equal((2, 0x02, 0x28), Command(setDeadZone));
        Assert.Equal(new byte[] { 0x01, 0x40 }, setDeadZone[9..11]);
        Assert.Equal((1, 0x02, 0xAA), Command(getReportMode));
        Assert.Equal(new byte[] { 0x00 }, getReportMode[9..10]);
        Assert.Equal((1, 0x02, 0x2A), Command(setReportMode));
        Assert.Equal(new byte[] { 0x02 }, setReportMode[9..10]);
        Assert.Equal((4, 0x02, 0xA1), Command(getKillSwitch));
        Assert.Equal(new byte[] { 0x01, 0x00, 0x00, 0x00 }, getKillSwitch[9..13]);
        Assert.Equal((4, 0x02, 0x21), Command(setKillSwitch));
        Assert.Equal(new byte[] { 0x01, 0x01, 0x02, 0x03 }, setKillSwitch[9..13]);

        var deadZone = RazerAnalogKeyProtocol.ParseAnalogKeyBottomDeadZone(
            Response(0x62, 2, 0x01, 0x40));
        var reportMode = RazerAnalogKeyProtocol.ParseAnalogReportMode(Response(0x64, 1, 0x02));
        var killSwitch = RazerAnalogKeyProtocol.ParseKeyboardKillSwitch(
            Response(0x66, 4, 0x01, 0x01, 0x02, 0x03));

        Assert.Equal(new RazerAnalogKeyBottomDeadZone(0x01, 0x40), deadZone);
        Assert.Equal(0x02, reportMode);
        Assert.Equal(new RazerAnalogKeyKillSwitch(0x01, true, 0x02, 0x03), killSwitch);
    }

    [Fact]
    public void CountsAndPartialRecordsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RazerAnalogKeyProtocol.CreateGetMultiAnalogKeyActuationPointRequest(
                0,
                0,
                0,
                new byte[RazerAnalogKeyProtocol.MaxActuationPoints + 1]));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RazerAnalogKeyProtocol.CreateSetMultiAnalogKeyRapidTriggerRequest(
                0,
                0,
                0,
                new RazerAnalogRapidTrigger[RazerAnalogKeyProtocol.MaxRapidTriggerKeys + 1]));
        Assert.Throws<InvalidDataException>(
            () => RazerAnalogKeyProtocol.ParseMultiAnalogKeyActuationPoints(
                Response(0, 4, 0x00, 0x03, 0x01, 0x00)));
        Assert.Throws<InvalidDataException>(
            () => RazerAnalogKeyProtocol.ParseMultiKeyAssignments(
                Response(0, 3, 0x03, 0x01, 0x00)));
    }

    private static (byte Size, byte Class, byte Command) Command(byte[] request) =>
        (request[6], request[7], request[8]);

    private static byte[] Response(byte transaction, byte dataSize, params byte[] arguments)
    {
        var response = new byte[RazerFeatureReport.Length];
        response[1] = 0x02;
        response[2] = transaction;
        response[6] = dataSize;
        arguments.CopyTo(response.AsSpan(RazerFeatureReport.ArgumentsOffset));
        response[89] = RazerFeatureReport.CalculateCrc(response);
        return response;
    }
}
