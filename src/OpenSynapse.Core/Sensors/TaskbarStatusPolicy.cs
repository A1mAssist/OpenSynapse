namespace OpenSynapse.Core.Sensors;

public static class TaskbarStatusPolicy
{
    public const int MaximumBatteryCount = 3;

    public static bool ShouldShow(TaskbarStatusSnapshot snapshot, bool enabled) =>
        enabled && snapshot.Batteries.Count > 0;

    public static string FormatBattery(int? percent) =>
        percent is { } value ? $"{value}%" : string.Empty;

    public static string FormatDescription(
        TaskbarStatusSnapshot snapshot,
        string chargingText)
    {
        var parts = new List<string>(snapshot.Batteries.Count);
        foreach (var battery in snapshot.Batteries)
        {
            var entry = new List<string>(3);
            if (!string.IsNullOrWhiteSpace(battery.Name))
            {
                entry.Add(battery.Name.Trim());
            }
            var percent = FormatBattery(battery.Percent);
            if (percent.Length > 0) entry.Add(percent);
            if (!string.IsNullOrWhiteSpace(chargingText)) entry.Add(chargingText.Trim());
            parts.Add(string.Join(" · ", entry));
        }
        return string.Join("; ", parts);
    }

    public static IReadOnlyList<TaskbarBatteryStatus> SelectBatteries(
        IEnumerable<TaskbarBatteryStatus> available,
        IReadOnlyList<TaskbarBatteryPreference> preferences,
        int maximum = MaximumBatteryCount)
    {
        var batteries = available.ToArray();
        if (preferences.Count == 0)
        {
            var lowest = batteries
                .OrderBy(item => item.Percent)
                .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .FirstOrDefault();
            return lowest.Key is null ? [] : [lowest];
        }

        var byKey = new Dictionary<string, TaskbarBatteryStatus>(StringComparer.OrdinalIgnoreCase);
        foreach (var battery in batteries)
        {
            byKey.TryAdd(battery.Key, battery);
        }

        var selected = new List<TaskbarBatteryStatus>(Math.Min(maximum, preferences.Count));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var preference in preferences)
        {
            if (selected.Count >= maximum) break;
            var key = preference.Key?.Trim();
            if (string.IsNullOrEmpty(key) || !seen.Add(key)) continue;
            if (byKey.TryGetValue(key, out var battery)) selected.Add(battery);
        }
        return selected;
    }

    public static bool SameContent(TaskbarStatusSnapshot left, TaskbarStatusSnapshot right)
    {
        if (left.Batteries.Count != right.Batteries.Count) return false;
        for (var index = 0; index < left.Batteries.Count; index++)
        {
            if (left.Batteries[index] != right.Batteries[index]) return false;
        }
        return true;
    }
}
