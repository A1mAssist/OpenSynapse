namespace OpenSynapse.Core.Sensors;

public readonly record struct TaskbarBatteryStatus(
    string Key,
    int Percent,
    bool? IsCharging,
    string Icon,
    string Name);

public readonly record struct TaskbarStatusSnapshot(
    IReadOnlyList<TaskbarBatteryStatus> Batteries);
