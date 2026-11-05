using OpenSynapse.Core.Devices;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class CpuPowerLimitTests
{
    private const ulong ShippedRegister = 0x00008578002384B0;
    private const double PowerUnit = 0.125;
    private const double TimeUnit = 1.0 / 1024;

    [Fact]
    public void TheShippedRegisterDecodesToTheWallsTheFirmwareAdvertises()
    {
        var limit = CpuPowerLimit.Decode(ShippedRegister, PowerUnit, TimeUnit);

        Assert.Equal(150, limit.Pl1Watts);
        Assert.Equal(175, limit.Pl2Watts);
        Assert.Equal(35 * TimeUnit, limit.Pl1TimeSeconds);
        Assert.Equal(0, limit.Pl2TimeSeconds);
        Assert.False(CpuPowerLimit.IsLocked(ShippedRegister));
    }

    [Fact]
    public void ThePowerRegisterExposesUnitsForEachQuantity()
    {
        const ulong unitRegister = 0x00000000000A0E03;

        Assert.Equal(0.125, CpuPowerLimit.PowerUnitFromRegister(unitRegister));
        Assert.Equal(1.0 / 16384, CpuPowerLimit.EnergyUnitFromRegister(unitRegister));
        Assert.Equal(1.0 / 1024, CpuPowerLimit.TimeUnitFromRegister(unitRegister));
    }

    [Fact]
    public void EncodingWattsOnlyLeavesEveryOtherFieldAlone()
    {
        var encoded = new CpuPowerLimit(45, 60, 0.034, 0.002)
            .EncodeWattsOnly(ShippedRegister, PowerUnit);
        const ulong wattMask = 0x7FFFUL | (0x7FFFUL << 32);

        Assert.Equal(ShippedRegister & ~wattMask, encoded & ~wattMask);
        var decoded = CpuPowerLimit.Decode(encoded, PowerUnit, TimeUnit);
        Assert.Equal(45, decoded.Pl1Watts);
        Assert.Equal(60, decoded.Pl2Watts);
        Assert.Equal(35 * TimeUnit, decoded.Pl1TimeSeconds);
    }

    [Theory]
    [InlineData(15)]
    [InlineData(25)]
    [InlineData(45)]
    [InlineData(95)]
    public void TheLowPresetsEncodeAndDecodeCleanly(double watts)
    {
        var original = CpuPowerLimit.Decode(ShippedRegister, PowerUnit, TimeUnit);
        var request = new CpuPowerLimit(watts, watts + 15, 0.034, 0)
            .EncodeWattsOnly(ShippedRegister, PowerUnit);
        var decoded = CpuPowerLimit.Decode(request, PowerUnit, TimeUnit);

        Assert.Equal(watts, decoded.Pl1Watts);
        Assert.Equal(watts + 15, decoded.Pl2Watts);
        Assert.Equal(original.Pl1TimeSeconds, decoded.Pl1TimeSeconds);
        Assert.Equal(original.Pl2TimeSeconds, decoded.Pl2TimeSeconds);
    }

    [Fact]
    public void ClampingKeepsPl2AtOrAbovePl1AndInsideTheOfferedRange()
    {
        var high = new CpuPowerLimit(120, 5, 0, 0).Clamp(CpuPowerLimit.MaximumWatts);
        var crossed = new CpuPowerLimit(45, 30, 0, 0).Clamp(CpuPowerLimit.MaximumWatts);
        var low = new CpuPowerLimit(0, 0, 0, 0).Clamp(CpuPowerLimit.MaximumWatts);

        Assert.Equal(100, high.Pl1Watts);
        Assert.Equal(100, high.Pl2Watts);
        Assert.Equal(45, crossed.Pl1Watts);
        Assert.Equal(45, crossed.Pl2Watts);
        Assert.Equal(CpuPowerLimit.MinimumWatts, low.Pl1Watts);
        Assert.Equal(CpuPowerLimit.MinimumWatts, low.Pl2Watts);
    }

    [Fact]
    public void AClampedRequestAlwaysStaysWritable()
    {
        var clamped = new CpuPowerLimit(120, 5, 0, 0)
            .Clamp(CpuPowerLimit.MaximumWatts)
            .EncodeWattsOnly(ShippedRegister, PowerUnit);
        var decoded = CpuPowerLimit.Decode(clamped, PowerUnit, TimeUnit);

        Assert.InRange(decoded.Pl1Watts, CpuPowerLimit.MinimumWatts, CpuPowerLimit.MaximumWatts);
        Assert.InRange(decoded.Pl2Watts, decoded.Pl1Watts, CpuPowerLimit.MaximumWatts);
        Assert.True(CpuPowerLimit.WattsToUnits(decoded.Pl2Watts, PowerUnit) <= 0x7FFF);
    }

    [Fact]
    public void ARestoreIsAllowedBackAboveTheUserCeiling()
    {
        var restored = new CpuPowerLimit(150, 175, 0, 0)
            .Clamp(CpuPowerLimit.MaximumRestoreWatts);

        Assert.Equal(150, restored.Pl1Watts);
        Assert.Equal(175, restored.Pl2Watts);
    }

    [Fact]
    public void TheLockBitIsReadFromTheTopBit()
    {
        Assert.True(CpuPowerLimit.IsLocked(1UL << 63));
        Assert.False(CpuPowerLimit.IsLocked((1UL << 63) - 1));
    }

    [Fact]
    public void TimeWindowsRoundTripThroughTheExplicitEncoder()
    {
        var encoded = new CpuPowerLimit(
                CpuPowerLimit.MinimumWatts,
                CpuPowerLimit.MinimumWatts,
                CpuPowerLimit.MinimumTimeSeconds,
                CpuPowerLimit.MaximumTimeSeconds)
            .Encode(ShippedRegister, PowerUnit, TimeUnit);
        var decoded = CpuPowerLimit.Decode(encoded, PowerUnit, TimeUnit);

        Assert.Equal(CpuPowerLimit.MinimumWatts, decoded.Pl1Watts);
        Assert.Equal(TimeUnit, decoded.Pl1TimeSeconds, 10);
        Assert.Equal(255 * TimeUnit, decoded.Pl2TimeSeconds, 10);
    }

    [Fact]
    public void AnInvalidPowerUnitIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CpuPowerLimit.UnitsToWatts(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CpuPowerLimit.WattsToUnits(45, double.NaN));
    }
}
