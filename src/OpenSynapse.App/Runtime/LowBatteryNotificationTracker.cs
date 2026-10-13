using OpenSynapse.Core.Devices;

namespace OpenSynapse.App.Runtime;

internal sealed class LowBatteryNotificationTracker(Dictionary<string, int> notifiedLevels)
{
    internal int? Observe(DeviceBatteryReading reading, bool enabled, out bool changed)
    {
        changed = false;
        // Zero is ambiguous on receivers whose peripheral is asleep or switched off.
        if (string.IsNullOrWhiteSpace(reading.DeviceId) || reading.Percent is not (>= 1 and <= 100))
            return null;

        if (reading.Percent >= 25)
        {
            changed = notifiedLevels.Remove(reading.DeviceId);
            return null;
        }
        if (!enabled || reading.IsCharging == true || reading.Percent > 20)
            return null;

        var threshold = reading.Percent <= 10 ? 10 : 20;
        if (notifiedLevels.TryGetValue(reading.DeviceId, out var previous) && previous <= threshold)
            return null;

        notifiedLevels[reading.DeviceId] = threshold;
        changed = true;
        return threshold;
    }
}
