using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Devices;
using static OpenSynapse.App.ViewModels.DeviceUiCatalog;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel
{
    internal void SetLegacyShortcutCycleDefaults(
        IEnumerable<BladePerformanceMode> performanceModes,
        IEnumerable<int>? refreshRates)
    {
        _legacyPerformanceCycleModes = performanceModes
            .Where(BladePerformanceModes.Contains)
            .Distinct()
            .ToArray();
        _legacyRefreshRateCycleHertz = refreshRates?
            .Where(hertz => hertz > 0)
            .Distinct()
            .Order()
            .ToArray();
    }

    internal void SetBladePerformanceCycleModes(IEnumerable<BladePerformanceMode> modes)
    {
        var selected = modes.Where(BladePerformanceModes.Contains).ToHashSet();
        if (selected.Count == 0)
        {
            throw new ArgumentException("At least one performance mode must remain in the shortcut cycle.", nameof(modes));
        }
        _bladePerformanceCycleModes = selected;
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

    internal async Task<bool> SavePerformanceCycleModesAsync(
        IEnumerable<BladePerformanceMode> modes,
        CancellationToken cancellationToken = default)
    {
        var selected = modes.Where(BladePerformanceModes.Contains).Distinct().ToArray();
        if (selected.Length == 0)
        {
            return false;
        }

        var previous = _bladePerformanceCycleModes;
        _bladePerformanceCycleModes = selected.ToHashSet();
        GetActiveProfile().Shortcuts.PerformanceCycleModes = selected.ToList();
        if (await SaveProfileAsync(cancellationToken))
        {
            OnPropertyChanged(nameof(BladePerformanceCycleModes));
            return true;
        }

        _bladePerformanceCycleModes = previous;
        GetActiveProfile().Shortcuts.PerformanceCycleModes = previous.ToList();
        return false;
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

    internal async Task SetBladeSnapTapEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var previous = _profile.Global.Blade.SnapTapEnabled;
        _profile.Global.Blade.SnapTapEnabled = enabled;
        if (!await SaveProfileAsync(cancellationToken))
        {
            _profile.Global.Blade.SnapTapEnabled = previous;
            return;
        }
        _activeSnapTapEnabled = enabled;
    }
}
