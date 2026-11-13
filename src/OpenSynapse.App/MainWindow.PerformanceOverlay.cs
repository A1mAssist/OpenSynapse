using Microsoft.UI.Xaml;

namespace OpenSynapse.App;

public sealed partial class MainWindow
{
    private PerformanceOverlayWindow? _performanceOverlayWindow;

    internal void RestorePerformanceOverlay()
    {
        if (_behaviorSettings.PerformanceOverlayEnabled)
        {
            SetPerformanceOverlayEnabled(true);
        }
    }

    internal void RefreshPerformanceOverlayLayout() => _performanceOverlayWindow?.RefreshLayout();

    internal void RefreshPerformanceOverlayTopmost() => _performanceOverlayWindow?.RefreshAlwaysOnTop();

    internal void SetPerformanceOverlayEnabled(bool enabled)
    {
        if (!enabled)
        {
            _performanceOverlayEnabled = false;
            UpdatePerformanceSamplingState();
            ClosePerformanceOverlayWindow();
            return;
        }

        if (_performanceOverlayWindow is null)
        {
            var window = new PerformanceOverlayWindow(_viewModel, _behaviorSettings);
            window.SettingsRequested += () =>
            {
                _settingsPage?.InitializeBehaviorSettingsUi();
                RequestNavigation("settings");
            };
            window.CloseRequested += () =>
            {
                _settingsPage?.InitializeBehaviorSettingsUi();
                SetPerformanceOverlayEnabled(false);
            };
            window.Closed += (_, _) =>
            {
                if (!ReferenceEquals(_performanceOverlayWindow, window)) return;
                _performanceOverlayWindow = null;
                if (!_performanceOverlayEnabled) return;
                _performanceOverlayEnabled = false;
                _behaviorSettings.PerformanceOverlayEnabled = false;
                try
                {
                    _behaviorSettings.Save();
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or
                    InvalidOperationException or System.Security.SecurityException)
                {
                }
                UpdatePerformanceSamplingState();
            };
            _performanceOverlayWindow = window;
            window.AppWindow.Show(false);
        }

        _performanceOverlayEnabled = true;
        UpdatePerformanceSamplingState();
    }

    private void ClosePerformanceOverlayWindow()
    {
        var window = _performanceOverlayWindow;
        _performanceOverlayWindow = null;
        window?.Close();
    }
}
