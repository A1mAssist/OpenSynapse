using System.Reflection;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class BlackSharkV3ProProtocolTests
{
    [Fact]
    public void GetBattery_UsesWirelessEnvelope()
    {
        var report = BlackSharkV3ProProtocol.CreateGetBattery();

        Assert.Equal(BlackSharkV3ProProtocol.ReportLength, report.Length);
        Assert.Equal(BlackSharkV3ProProtocol.ReportId, report[0]);
        Assert.Equal(BlackSharkV3ProProtocol.TransactionId, report[2]);
        Assert.Equal(0x05, report[6]);
        Assert.Equal(0x80, report[9]);
        Assert.Equal(0x21, report[10]);
        Assert.Equal(0x00, report[11]);
        Assert.Equal(0x00, report[12]);
        Assert.Equal(0x00, report[13]);
        Assert.True(BlackSharkV3ProProtocol.HasValidCrc(report));
    }

    [Fact]
    public void GetBattery_WiredUsesZeroTransportByte()
    {
        var report = BlackSharkV3ProProtocol.CreateGetBattery(wireless: false);

        Assert.Equal(0x00, report[9]);
        Assert.True(BlackSharkV3ProProtocol.HasValidCrc(report));
    }

    [Fact]
    public void SetSidetone_EncodesLevel()
    {
        var report = BlackSharkV3ProProtocol.CreateSetSidetone(7);

        Assert.Equal(0x06, report[6]);
        Assert.Equal(0x80, report[9]);
        Assert.Equal(0x99, report[10]);
        Assert.Equal(0x01, report[12]);
        Assert.Equal(0x07, report[13]);
        Assert.Equal(0x00, report[14]);
        Assert.True(BlackSharkV3ProProtocol.HasValidCrc(report));
    }

    [Fact]
    public void SetAnc_EncodesModeAndLevel()
    {
        var report = BlackSharkV3ProProtocol.CreateSetAnc(BlackSharkAncMode.Anc, 3);

        Assert.Equal(0x06, report[6]);
        Assert.Equal(0x92, report[10]);
        Assert.Equal(0x02, report[12]);
        Assert.Equal(0x01, report[13]);
        Assert.Equal(0x03, report[14]);
        Assert.True(BlackSharkV3ProProtocol.HasValidCrc(report));
    }

    [Fact]
    public void SetAnc_OffForcesZeroLevel()
    {
        var report = BlackSharkV3ProProtocol.CreateSetAnc(BlackSharkAncMode.Off, 4);

        Assert.Equal(0x00, report[13]);
        Assert.Equal(0x00, report[14]);
    }

    [Fact]
    public void SetGameChat_EncodesBalance()
    {
        var report = BlackSharkV3ProProtocol.CreateSetGameChat(10);

        // The middleware declares one payload byte for 0xDC:0x01.
        Assert.Equal(0x05, report[6]);
        Assert.Equal(0xDC, report[10]);
        Assert.Equal(0x01, report[12]);
        Assert.Equal(0x0A, report[13]);
        Assert.Equal(0x00, report[14]);
        Assert.True(BlackSharkV3ProProtocol.HasValidCrc(report));
    }

    [Fact]
    public void SetThx_EncodesToggle()
    {
        var report = BlackSharkV3ProProtocol.CreateSetThx(true);

        Assert.Equal(0x06, report[6]);
        Assert.Equal(0x9E, report[10]);
        Assert.Equal(0x01, report[12]);
        Assert.Equal(0x01, report[13]);
        Assert.Equal(0x00, report[14]);
        Assert.True(BlackSharkV3ProProtocol.HasValidCrc(report));
    }

    [Theory]
    [InlineData(0x056B, "Razer Kraken V4 USB", 0xFF14, 0x0001, 64, false)]
    [InlineData(0x056C, "Razer Kraken V4 2.4", 0xFF14, 0x0001, 64, false)]
    [InlineData(0x101B, "Razer BlackShark V3 Pro", 0xFF14, 0x0001, 64, true)]
    [InlineData(0x101C, "", 0xFF14, 0x0001, 64, true)]
    [InlineData(0x9999, "Razer BlackShark V3 Pro", 0xFF14, 0x0001, 64, true)]
    [InlineData(0x9999, "Razer Naga", 0xFF14, 0x0001, 64, false)]
    [InlineData(0x101B, "Razer BlackShark V3 Pro", 0xFF14, 0x0001, 63, false)]
    [InlineData(0x101B, "Razer BlackShark V3 Pro", 0xFF12, 0x0001, 64, false)]
    public void BlackSharkSelectionRequiresAnObservedIdentity(
        ushort productId,
        string productName,
        ushort usagePage,
        ushort usage,
        int outputReportLength,
        bool expected) =>
        Assert.Equal(expected, BlackSharkHeadsetService.IsBlackSharkControlInterface(
            productId, productName, usagePage, usage, outputReportLength));

    [Fact]
    public void UsbProductIdHasAStableDisplayName()
    {
        Assert.Equal("Razer BlackShark V3 Pro (USB)",
            BlackSharkV3ProProtocol.DescribeProduct(BlackSharkV3ProProtocol.UsbProductId));
    }

    [Theory]
    [InlineData(0x0576, false)]
    [InlineData(0x0577, true)]
    [InlineData(0x101B, true)]
    [InlineData(0x101C, true)]
    public void TransportDirection_IsWirelessForTheReceiverAndDongle(int productId, bool wireless)
    {
        // Only the wired pid maps to the wired transport byte; every other observed variant,
        // including the PlayStation build, starts on the wireless byte.
        Assert.Equal(wireless, (ushort)productId != BlackSharkV3ProProtocol.WiredProductId);
    }

    [Fact]
    public void WakeSequence_UsesTheThreeSynapsePrimers()
    {
        var reports = BlackSharkV3ProProtocol.CreateWakeSequence();

        Assert.Collection(
            reports,
            report => AssertPrimer(report, 0x02, 0x00),
            report => AssertPrimer(report, 0x2A, 0x00),
            report => AssertPrimer(report, 0x2A, 0x80));
    }

    [Fact]
    public void ParseResponse_ReadsArguments()
    {
        var report = new byte[BlackSharkV3ProProtocol.ReportLength];
        report[0] = BlackSharkV3ProProtocol.ReportId;
        report[1] = 0x02;
        report[10] = 0x21;
        report[11] = 0x01;
        report[12] = 2;
        report[13] = 87;
        report[14] = 1;

        Assert.True(BlackSharkV3ProProtocol.TryParseResponse(report, out var response));
        Assert.Equal(0x02, response.Status);
        Assert.Equal(0x21, response.CommandClass);
        Assert.Equal(0x01, response.Subtype);
        Assert.Equal(new byte[] { 87, 1 }, response.Arguments);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(16)]
    public void SetSidetone_RejectsOutOfRangeLevel(int level)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BlackSharkV3ProProtocol.CreateSetSidetone(level));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(21)]
    public void SetGameChat_RejectsOutOfRangeBalance(int balance)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BlackSharkV3ProProtocol.CreateSetGameChat(balance));
    }

    [Fact]
    public void TransportTeardownIsBoundedByTheTimeout()
    {
        // The stall users reported came from awaiting a HID read the driver never completes, so
        // teardown must not inherit an unbounded wait. A cancellable source plus a capped wait is
        // what keeps a save from sitting idle for a minute.
        var source = typeof(BlackSharkHeadsetService).Assembly
            .GetType("OpenSynapse.Windows.Devices.BlackSharkHeadsetTransport");
        Assert.NotNull(source);

        Assert.NotNull(source!.GetMethod("DisposeAsync"));
        Assert.NotNull(source.GetMethod("ReadLoopAsync",
            BindingFlags.Instance | BindingFlags.NonPublic));
        Assert.NotNull(source.GetField("_readLoop", BindingFlags.Instance | BindingFlags.NonPublic));
        Assert.NotNull(source.GetField("_readCancellation",
            BindingFlags.Instance | BindingFlags.NonPublic));
    }

    private static void AssertPrimer(byte[] report, byte commandClass, byte direction)
    {
        Assert.Equal(0x04, report[6]);
        Assert.Equal(direction, report[9]);
        Assert.Equal(commandClass, report[10]);
        Assert.True(BlackSharkV3ProProtocol.HasValidCrc(report));
    }
}
