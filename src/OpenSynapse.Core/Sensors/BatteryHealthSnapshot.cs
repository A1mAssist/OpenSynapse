namespace OpenSynapse.Core.Sensors;

public sealed record BatteryHealthSnapshot(
    uint DesignCapacityMilliwattHours,
    uint FullChargeCapacityMilliwattHours,
    uint CycleCount)
{
    /// <summary>
    /// A new pack routinely reports a full-charge capacity above its design capacity, so the
    /// raw ratio can exceed 100. Windows and Synapse present that as a healthy battery, and
    /// the detail line still shows both capacities for anyone who wants the raw numbers.
    /// </summary>
    public double? HealthPercent => DesignCapacityMilliwattHours == 0 || FullChargeCapacityMilliwattHours == 0
        ? null
        : Math.Round(
            Math.Min(100.0, 100.0 * FullChargeCapacityMilliwattHours / DesignCapacityMilliwattHours),
            1,
            MidpointRounding.AwayFromZero);
}
