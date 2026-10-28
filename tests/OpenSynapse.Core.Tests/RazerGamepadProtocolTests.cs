using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerGamepadProtocolTests
{
    [Fact]
    public void SensitivityRequestUsesReportId4()
    {
        var report = RazerGamepadProtocol.CreateGetSensitivityRequest(0x05, 0x01, 0x02);

        Assert.Equal(RazerFeatureReport.Length, report.Length);
        Assert.Equal(4, report[0]);
        Assert.Equal(0x05, report[2]);
        Assert.Equal(3, report[6]);
        Assert.Equal(0x0C, report[7]);
        Assert.Equal(0x82, report[8]);
        Assert.Equal(new byte[] { 0x01, 0x02, 0x00 }, report[9..12]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(report), report[89]);
    }

    [Fact]
    public void ZoneSetPacksBigEndianPairs()
    {
        var report = RazerGamepadProtocol.CreateSetZonesRequest(
            0x01,
            0x03,
            0x00,
            new RazerGamepadZones(0x1234, 0x0567, 0x0089, true));

        Assert.Equal(9, report[6]);
        Assert.Equal(0x05, report[8]);
        Assert.Equal(new byte[] { 0x03, 0x00, 0x12, 0x34, 0x05, 0x67, 0x00, 0x89, 0x01 }, report[9..18]);
    }

    [Fact]
    public void TriggerRequestsCarryTheirOwnSizes()
    {
        var range = RazerGamepadProtocol.CreateSetTriggerRangeRequest(0x01, 0x04, 0x00, 10, 90);
        Assert.Equal(4, range[6]);
        Assert.Equal(0x13, range[8]);
        Assert.Equal(new byte[] { 0x04, 0x00, 10, 90 }, range[9..13]);

        var mode = RazerGamepadProtocol.CreateSetTriggerModeRequest(0x01, 0x04, 0x00, 2);
        Assert.Equal(3, mode[6]);
        Assert.Equal(0x15, mode[8]);
        Assert.Equal(new byte[] { 0x04, 0x00, 2 }, mode[9..12]);
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
}
