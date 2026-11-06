namespace OpenSynapse.Core.Sensors;

public readonly record struct TaskbarStatusSnapshot(
    int? BatteryPercent,
    bool? IsCharging,
    string BatteryIcon = "",
    string BatteryName = "");
