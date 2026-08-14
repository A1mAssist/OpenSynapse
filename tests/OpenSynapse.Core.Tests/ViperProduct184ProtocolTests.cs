using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Core.Tests;

public sealed class ViperProduct184ProtocolTests
{
    [Fact]
    public void BuildsProduct184ReadHeaders()
    {
        AssertHeader(ViperProduct184Protocol.CreateGetBatteryRequest(), 0x02, 0x07, 0x80, 0);
        AssertHeader(ViperProduct184Protocol.CreateGetPollingRateRequest(), 0x01, 0x00, 0x85, 0);
        AssertHeader(ViperProduct184Protocol.CreateGetDpiRequest(), 0x07, 0x04, 0x85, 1);
        AssertHeader(ViperProduct184Protocol.CreateGetIdleTimeoutRequest(), 0x02, 0x07, 0x83, 0);
        AssertHeader(ViperProduct184Protocol.CreateGetLowBatteryThresholdRequest(), 0x01, 0x07, 0x81, 0);
        AssertHeader(ViperProduct184Protocol.CreateGetDpiStagesRequest(), 0x26, 0x04, 0x86, 1);
    }

    [Theory]
    [InlineData(5, 0x0D)]
    [InlineData(30, 0x4D)]
    [InlineData(100, 0xFF)]
    public void BuildsLowBatteryThresholdValue(int percent, byte raw)
    {
        var report = ViperProduct184Protocol.CreateSetLowBatteryThresholdRequest(percent);

        AssertHeader(report, 0x01, 0x07, 0x01, 1);
        Assert.Equal(raw, report[RazerFeatureReport.ArgumentsOffset]);
    }

    [Theory]
    [InlineData(125, 0x08)]
    [InlineData(500, 0x02)]
    [InlineData(1000, 0x01)]
    public void BuildsPollingRateValue(int hertz, byte raw)
    {
        var report = ViperProduct184Protocol.CreateSetPollingRateRequest(hertz);

        AssertHeader(report, 0x01, 0x00, 0x05, 1);
        Assert.Equal(raw, report[RazerFeatureReport.ArgumentsOffset]);
    }

    [Fact]
    public void BuildsBigEndianDpiAndIdleValues()
    {
        var dpi = ViperProduct184Protocol.CreateSetDpiRequest(1600, 3200);
        AssertHeader(dpi, 0x07, 0x04, 0x05, 5);
        Assert.Equal(new byte[] { 0, 0x06, 0x40, 0x0C, 0x80 },
            dpi[RazerFeatureReport.ArgumentsOffset..(RazerFeatureReport.ArgumentsOffset + 5)]);

        var idle = ViperProduct184Protocol.CreateSetIdleTimeoutRequest(900);
        AssertHeader(idle, 0x02, 0x07, 0x03, 2);
        Assert.Equal(new byte[] { 0x03, 0x84 },
            idle[RazerFeatureReport.ArgumentsOffset..(RazerFeatureReport.ArgumentsOffset + 2)]);
    }

    [Fact]
    public void BuildsPersistentDpiStagesWithZeroBasedSetIds()
    {
        var state = new ViperDpiStagesState(3,
        [
            new(1, 400, 400),
            new(2, 800, 800),
            new(3, 1600, 1600),
            new(4, 3200, 3200),
            new(5, 6400, 6400),
        ]);

        var report = ViperProduct184Protocol.CreateSetDpiStagesRequest(state);

        AssertHeader(report, 0x26, 0x04, 0x06, 0x26);
        Assert.Equal(new byte[]
        {
            0x01, 0x03, 0x05,
            0x00, 0x01, 0x90, 0x01, 0x90, 0x00, 0x00,
            0x01, 0x03, 0x20, 0x03, 0x20, 0x00, 0x00,
            0x02, 0x06, 0x40, 0x06, 0x40, 0x00, 0x00,
            0x03, 0x0C, 0x80, 0x0C, 0x80, 0x00, 0x00,
            0x04, 0x19, 0x00, 0x19, 0x00, 0x00, 0x00,
        }, report[RazerFeatureReport.ArgumentsOffset..(RazerFeatureReport.ArgumentsOffset + 0x26)]);
    }

    [Fact]
    public void RejectsInvalidPersistentDpiStageState()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ViperProduct184Protocol.CreateSetDpiStagesRequest(new(1, [])));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ViperProduct184Protocol.CreateSetDpiStagesRequest(new(2, [new(1, 800, 800)])));
        Assert.Throws<ArgumentException>(() =>
            ViperProduct184Protocol.CreateSetDpiStagesRequest(new(1, [new(2, 800, 800)])));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ViperProduct184Protocol.CreateSetDpiStagesRequest(new(1, [new(1, 125, 800)])));
    }

    [Theory]
    [InlineData(59)]
    [InlineData(901)]
    public void RejectsIdleOutsideProductRange(int seconds) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ViperProduct184Protocol.CreateSetIdleTimeoutRequest(seconds));

    [Fact]
    public void BuildsBatteryChemistryValues()
    {
        var report = ViperProduct184Protocol.CreateSetBatteryChemistryRequest(ViperBatteryChemistry.Lithium);

        AssertHeader(report, 0x01, 0x07, 0x14, 1);
        Assert.Equal((byte)ViperBatteryChemistry.Lithium, report[RazerFeatureReport.ArgumentsOffset]);
    }

    [Fact]
    public void ParsesStrictReadResponses()
    {
        var battery = Response(0x02, 0x07, 0x80, 0x00, 128);
        Assert.Equal(50, ViperProduct184Protocol.ParseBatteryPercent(battery));

        Assert.Equal(500, ViperProduct184Protocol.ParsePollingRateHertz(
            Response(0x01, 0x00, 0x85, 0x02)));

        var dpi = Response(0x07, 0x04, 0x85, 0x00, 0x06, 0x40, 0x0C, 0x80);
        Assert.Equal((1600, 3200), ViperProduct184Protocol.ParseDpi(dpi));

        Assert.Equal(300, ViperProduct184Protocol.ParseIdleSeconds(
            Response(0x02, 0x07, 0x83, 0x01, 0x2C)));
    }

    [Fact]
    public void RejectsWrongTransactionCommandCrcAndValues()
    {
        var response = Response(0x01, 0x00, 0x85, 0x02);
        response[2] = 0x20;
        response[89] = RazerFeatureReport.CalculateCrc(response);
        Assert.Throws<InvalidOperationException>(() => ViperProduct184Protocol.ParsePollingRateHertz(response));

        response = Response(0x01, 0x00, 0x85, 0x04);
        Assert.Throws<InvalidOperationException>(() => ViperProduct184Protocol.ParsePollingRateHertz(response));

        response = Response(0x02, 0x07, 0x83, 0x00, 0x01);
        Assert.Throws<InvalidOperationException>(() => ViperProduct184Protocol.ParseIdleSeconds(response));

        response = Response(0x07, 0x04, 0x85, 0x00, 0x00, 0x32, 0x0C, 0x80);
        Assert.Throws<InvalidOperationException>(() => ViperProduct184Protocol.ParseDpi(response));

        response = Response(0x02, 0x07, 0x80, 0x00, 128);
        response[89] ^= 0xFF;
        Assert.Throws<InvalidOperationException>(() => ViperProduct184Protocol.ParseBatteryPercent(response));
    }

    private static byte[] Response(byte dataSize, byte commandClass, byte commandId, params byte[] arguments)
    {
        var response = RazerFeatureReport.CreateRequest(0x1F, dataSize, commandClass, commandId, arguments);
        response[1] = 0x02;
        return response;
    }

    private static void AssertHeader(byte[] report, byte dataSize, byte commandClass, byte commandId, int argumentCount)
    {
        Assert.Equal(RazerFeatureReport.Length, report.Length);
        Assert.Equal(ViperProduct184Protocol.TransactionId, report[2]);
        Assert.Equal(dataSize, report[6]);
        Assert.Equal(commandClass, report[7]);
        Assert.Equal(commandId, report[8]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(report), report[89]);
        Assert.All(report[(RazerFeatureReport.ArgumentsOffset + argumentCount)..89], value => Assert.Equal((byte)0, value));
    }
}
