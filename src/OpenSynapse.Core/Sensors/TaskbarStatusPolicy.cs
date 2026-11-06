namespace OpenSynapse.Core.Sensors;

public static class TaskbarStatusPolicy
{
    public static bool ShouldShow(TaskbarStatusSnapshot snapshot, bool enabled) =>
        enabled && snapshot.BatteryPercent is not null;

    public static string FormatBattery(int? percent) =>
        percent is { } value ? $"{value}%" : string.Empty;

    public static string FormatDescription(
        TaskbarStatusSnapshot snapshot,
        string chargingText)
    {
        var parts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(snapshot.BatteryName))
        {
            parts.Add(snapshot.BatteryName.Trim());
        }
        var battery = FormatBattery(snapshot.BatteryPercent);
        if (battery.Length > 0) parts.Add(battery);
        if (!string.IsNullOrWhiteSpace(chargingText)) parts.Add(chargingText.Trim());
        return string.Join(" · ", parts);
    }
}
