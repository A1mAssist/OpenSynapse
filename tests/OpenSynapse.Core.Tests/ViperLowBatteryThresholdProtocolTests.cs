using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Core.Tests;

public sealed class ViperLowBatteryThresholdProtocolTests
{
    [Theory]
    [InlineData(0x00, 0)]
    [InlineData(0x0D, 5)]
    [InlineData(0x27, 15)]
    [InlineData(0x40, 25)]
    [InlineData(0x4D, 30)]
    [InlineData(0xFF, 100)]
    public void ParsesOfficialRawPercentageEncoding(byte raw, int percent)
    {
        var response = RazerFeatureReport.CreateRequest(0x1F, 0x01, 0x07, 0x81, new[] { raw });
        response[1] = 0x02;

        var parsed = ViperLowBatteryThresholdProtocol.ParseRaw(response);

        Assert.Equal(raw, parsed);
        Assert.Equal(percent, ViperLowBatteryThresholdProtocol.ToPercent(parsed));
        Assert.Equal($"{percent}% (raw 0x{raw:X2})", ViperLowBatteryThresholdProtocol.Format(parsed));
    }

    [Theory]
    [InlineData(5, 0x0D)]
    [InlineData(10, 0x1A)]
    [InlineData(15, 0x27)]
    [InlineData(25, 0x40)]
    [InlineData(30, 0x4D)]
    [InlineData(100, 0xFF)]
    public void EncodesOfficialSetPercentage(int percent, byte raw) =>
        Assert.Equal(raw, ViperLowBatteryThresholdProtocol.ToRaw(percent));

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(99)]
    [InlineData(105)]
    public void RejectsSetPercentageOutsideOfficialUiValues(int percent) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ViperLowBatteryThresholdProtocol.ToRaw(percent));

    [Fact]
    public void EveryOfficialSetValueRoundTripsThroughOfficialGetConversion()
    {
        for (var percent = ViperLowBatteryThresholdProtocol.MinimumPercent;
             percent <= ViperLowBatteryThresholdProtocol.MaximumPercent;
             percent += ViperLowBatteryThresholdProtocol.PercentStep)
        {
            Assert.Equal(percent, ViperLowBatteryThresholdProtocol.ToPercent(
                ViperLowBatteryThresholdProtocol.ToRaw(percent)));
        }
    }

    [Theory]
    [InlineData(1, 0x04)]
    [InlineData(2, 0x20)]
    [InlineData(6, 0x02)]
    [InlineData(7, 0x08)]
    [InlineData(8, 0x80)]
    public void RejectsInvalidResponseEnvelope(int index, byte value)
    {
        var response = RazerFeatureReport.CreateRequest(0x1F, 0x01, 0x07, 0x81, new byte[] { 0x26 });
        response[1] = 0x02;
        response[index] = value;
        response[89] = RazerFeatureReport.CalculateCrc(response);

        Assert.Throws<InvalidOperationException>(() => ViperLowBatteryThresholdProtocol.ParseRaw(response));
    }

    [Fact]
    public void RejectsCorruptCrc()
    {
        var response = RazerFeatureReport.CreateRequest(0x1F, 0x01, 0x07, 0x81, new byte[] { 0x26 });
        response[1] = 0x02;
        response[89] ^= 0xFF;

        Assert.Throws<InvalidOperationException>(() => ViperLowBatteryThresholdProtocol.ParseRaw(response));
    }
}
