namespace OpenSynapse.Core.Sensors;

public readonly record struct TaskbarStatusSnapshot(
    int? BatteryPercent,
    bool? IsCharging,
    string PerformanceMode,
    string BatteryIcon = "",
    string BatteryName = "");
