namespace OpenSynapse.Core.Devices;

/// <summary>
/// One battery-optimizer reading. <paramref name="ThresholdPercent"/> is the
/// seven-bit firmware threshold; when the optimizer is off it keeps the value
/// the firmware reported and the UI should present the switch as off.
/// </summary>
public readonly record struct BladeBatteryOptimizerState(
    bool IsEnabled,
    int ThresholdPercent,
    bool OneTimeOverrideEnabled);
