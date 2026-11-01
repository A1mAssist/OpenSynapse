namespace OpenSynapse.Windows.Devices;

public sealed partial class OpenRazerDeviceService
{
    /// <summary>
    /// A wireless keyboard can be asleep before either live read. Keeping both timeouts in
    /// Errors leaves the battery card visible and lets the 10-second refresh retry; when one
    /// value answers, the silent sibling stays cached as unsupported as before.
    /// </summary>
    private static void KeepBatteryRetryableWhenBothUnanswered(
        int? battery,
        bool? charging,
        IReadOnlySet<OpenRazerBackendCapability> timeouts,
        HashSet<OpenRazerBackendCapability> unsupported,
        IDictionary<string, string> errors)
    {
        if (battery is not null || charging is not null ||
            !timeouts.Contains(OpenRazerBackendCapability.BatteryRead) ||
            !timeouts.Contains(OpenRazerBackendCapability.ChargingRead))
        {
            return;
        }

        unsupported.ExceptWith(timeouts);
        foreach (var capability in timeouts)
        {
            errors[capability.ToString()] = "The device did not answer this command.";
        }
    }
}
