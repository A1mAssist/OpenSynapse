using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel
{
    internal void SetLegacyShortcutCycleDefaults(
        IEnumerable<BladePerformanceMode> performanceModes,
        IEnumerable<int>? refreshRates)
    {
        var available = Blade.PerformanceModes;
        _legacyPerformanceCycleModes = performanceModes
            .Where(available.Contains)
            .Distinct()
            .ToArray();
        _legacyRefreshRateCycleHertz = refreshRates?
            .Where(hertz => hertz > 0)
            .Distinct()
            .Order()
            .ToArray();
    }

    internal void SetInternalDisplayRefreshRateCycle(IEnumerable<int> refreshRates)
    {
        var selected = refreshRates.Where(hertz => hertz > 0).ToHashSet();
        if (selected.Count == 0)
        {
            throw new ArgumentException("At least one refresh rate must remain in the shortcut cycle.", nameof(refreshRates));
        }
        _internalDisplayRefreshRateCycleHertz = selected;
    }

    internal async Task<bool> SaveRefreshRateCycleAsync(
        IEnumerable<int> refreshRates,
        CancellationToken cancellationToken = default)
    {
        var selected = refreshRates.Where(hertz => hertz > 0).Distinct().Order().ToArray();
        if (selected.Length == 0)
        {
            return false;
        }

        var previous = _internalDisplayRefreshRateCycleHertz;
        _internalDisplayRefreshRateCycleHertz = selected.ToHashSet();
        GetActiveProfile().Shortcuts.RefreshRateCycleHertz = selected.ToList();
        if (await SaveProfileAsync(cancellationToken))
        {
            OnPropertyChanged(nameof(InternalDisplayRefreshRateCycleHertz));
            return true;
        }

        _internalDisplayRefreshRateCycleHertz = previous;
        GetActiveProfile().Shortcuts.RefreshRateCycleHertz = previous?.Order().ToList();
        return false;
    }

}
