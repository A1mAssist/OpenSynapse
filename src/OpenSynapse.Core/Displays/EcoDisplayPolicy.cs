using OpenSynapse.Core.Sensors;

namespace OpenSynapse.Core.Displays;

/// <summary>
/// Battery-only display policy for integrated mode. The policy only borrows the
/// refresh rate for the current run; it never persists the temporary 60 Hz value.
/// </summary>
public sealed class EcoDisplayPolicy
{
    public const int PreferredHertz = 60;
    private bool _active;
    private int? _restoreHertz;

    public bool SetActive(bool active)
    {
        var changed = _active != active;
        _active = active;
        return active || changed || _restoreHertz is not null;
    }

    public int? Resolve(IReadOnlyList<int> supportedRates, int currentHertz, int? configuredHertz)
    {
        if (configuredHertz is not null) return configuredHertz;
        if (!_active) return _restoreHertz is int restore && supportedRates.Contains(restore) ? restore : null;
        var preferred = Select(supportedRates, null);
        if (preferred is int hertz && hertz != currentHertz) _restoreHertz ??= currentHertz;
        return preferred;
    }

    public void ConfirmApplied(int appliedHertz, int? configuredHertz)
    {
        if (configuredHertz == appliedHertz || (!_active && _restoreHertz == appliedHertz))
        {
            ClearRestore();
        }
    }

    public void ClearRestore() => _restoreHertz = null;

    public static int? Select(IReadOnlyList<int> supportedRates, int? configuredHertz)
    {
        ArgumentNullException.ThrowIfNull(supportedRates);
        if (configuredHertz is int configured)
        {
            return configured;
        }

        if (supportedRates.Count == 0)
        {
            return null;
        }

        return supportedRates.Contains(PreferredHertz)
            ? PreferredHertz
            : supportedRates.Min();
    }

    public static bool IsPowerSaving(bool discreteGpuDisabled, PowerSourceKind powerSource) =>
        discreteGpuDisabled && powerSource == PowerSourceKind.Battery;
}
