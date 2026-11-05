using OpenSynapse.Core.Sensors;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class BatteryHealthTests
{
    [Fact]
    public void HealthPercentRoundsToOneDecimal()
    {
        var snapshot = new BatteryHealthSnapshot(80_000, 73_840, 128);

        Assert.Equal(92.3, snapshot.HealthPercent);
    }

    [Fact]
    public void HealthPercentIsUnavailableWithoutDesignCapacity()
    {
        var snapshot = new BatteryHealthSnapshot(0, 0, 0);

        Assert.Null(snapshot.HealthPercent);
    }

    [Fact]
    public void HealthPercentNeverExceedsOneHundred()
    {
        // New packs report a full-charge capacity slightly above design capacity.
        var snapshot = new BatteryHealthSnapshot(80_000, 84_100, 12);

        Assert.Equal(100.0, snapshot.HealthPercent);
    }

    [Fact]
    public void HealthPercentIsUnavailableWithoutFullChargeCapacity()
    {
        var snapshot = new BatteryHealthSnapshot(80_000, 0, 0);

        Assert.Null(snapshot.HealthPercent);
    }
}
