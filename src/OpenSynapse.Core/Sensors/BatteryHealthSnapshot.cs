namespace OpenSynapse.Core.Sensors;

public sealed record BatteryHealthSnapshot(
    uint DesignCapacityMilliwattHours,
    uint FullChargeCapacityMilliwattHours,
    uint CycleCount)
{
    public double? HealthPercent => DesignCapacityMilliwattHours == 0
        ? null
        : Math.Round(
            100.0 * FullChargeCapacityMilliwattHours / DesignCapacityMilliwattHours,
            1,
            MidpointRounding.AwayFromZero);
}
