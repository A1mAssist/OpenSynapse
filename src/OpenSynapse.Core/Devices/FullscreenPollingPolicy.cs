namespace OpenSynapse.Core.Devices;

public readonly record struct FullscreenPollingDecision(
    int? WriteRate,
    bool CaptureCurrent,
    bool Applied)
{
    public static FullscreenPollingDecision None => new(null, false, false);
}

/// <summary>
/// Decides what one fullscreen sample does to one device. The runtime owns the
/// captured rate and the applied flag; this policy only answers "write what, and
/// what should the state become".
/// </summary>
public static class FullscreenPollingPolicy
{
    public static FullscreenPollingDecision Decide(
        bool fullscreen,
        bool applied,
        bool configChanged,
        int? fullscreenRate,
        int? captured,
        int? normalRate,
        int? current)
    {
        if (fullscreen && fullscreenRate is int target)
        {
            if (!applied)
            {
                return current == target
                    ? new FullscreenPollingDecision(null, true, true)
                    : new FullscreenPollingDecision(target, true, true);
            }

            return configChanged && current != target
                ? new FullscreenPollingDecision(target, false, true)
                : new FullscreenPollingDecision(null, false, true);
        }

        if (!applied || (captured ?? normalRate) is not int restore)
        {
            return FullscreenPollingDecision.None;
        }

        return current == restore
            ? FullscreenPollingDecision.None
            : new FullscreenPollingDecision(restore, false, false);
    }
}
