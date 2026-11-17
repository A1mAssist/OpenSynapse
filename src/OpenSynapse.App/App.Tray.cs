using Microsoft.UI.Xaml;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using OpenSynapse.App.Runtime;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Diagnostics;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Storage;
using OpenSynapse.Windows.Audio;
using OpenSynapse.Windows.Displays;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Input;
using OpenSynapse.Windows.Lifecycle;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;
using OpenSynapse.Windows.Sensors;
using Velopack;
using Velopack.Locators;
namespace OpenSynapse.App;

public partial class App
{
    private void RegisterAppNotifications()
    {
        try
        {
            AppNotificationManager.Default.Register();
            _appNotificationsRegistered = true;
        }
        catch (Exception exception)
        {
            _diagnosticLog.TryWrite("notifications", $"registration failed: {exception}");
        }
    }

    private void OnBladePerformanceModeChangedByUser(OpenSynapse.Core.Devices.BladePerformanceMode mode)
    {
        var modeText = mode switch
        {
            OpenSynapse.Core.Devices.BladePerformanceMode.Balanced => AppStrings.Text("Text_9753B259"),
            OpenSynapse.Core.Devices.BladePerformanceMode.Gaming => AppStrings.Text("Text_GamingPerformanceMode"),
            OpenSynapse.Core.Devices.BladePerformanceMode.Performance => AppStrings.Text("Text_C1DB7AE1"),
            OpenSynapse.Core.Devices.BladePerformanceMode.Custom => AppStrings.Text("Text_598C5804"),
            OpenSynapse.Core.Devices.BladePerformanceMode.Silent => AppStrings.Text("Text_60E54E25"),
            OpenSynapse.Core.Devices.BladePerformanceMode.BatterySaver => AppStrings.Text("Text_EE43F0B1"),
            OpenSynapse.Core.Devices.BladePerformanceMode.Hyperboost => "HyperBoost",
            _ => mode.ToString(),
        };
        ShowModeNotification(
            AppStrings.Text("Text_B5DCD004"),
            AppStrings.FormatText("PerformanceModeNotification", modeText));
    }

    private void OnBladeGamingModeChangedByUser(bool enabled) => ShowModeNotification(
        AppStrings.Text("Text_F5627AEF"),
        enabled
            ? AppStrings.Text("Text_4D59EFD5")
            : AppStrings.Text("Text_7D9D5842"));

    private void OnBladeGamingModeStateChanged(bool enabled) =>
        BladeGameModeRuntime.Apply(_bladeFnRuntime, enabled, _diagnosticLog);

    private void OnBladeTouchpadChangedByUser(bool enabled) => ShowModeNotification(
        AppStrings.Text("Text_0A982CB6"),
        enabled ? AppStrings.Text("Text_B56E2FE0") : AppStrings.Text("Text_F21C10DE"));

    private void OnBladeOneTimeFullChargeChangedByUser(bool enabled) => ShowModeNotification(
        AppStrings.Text("Text_5BE3EA9C"),
        enabled ? AppStrings.Text("Text_0B549ED2") : AppStrings.Text("Text_E0468C2D"));

    private void OnInternalDisplayRefreshRateChangedByUser(int hertz) => ShowModeNotification(
        AppStrings.Text("Text_8A7CE3A6"),
        AppStrings.FormatText("RefreshRateNotification", hertz));

    private void OnMacroContinuousPlaybackStarted(string title, string body)
    {
        if (_behaviorSettings.MacroPlaybackNotificationsEnabled)
        {
            ShowNotification(title, body, "macro-playback");
        }
    }

    private void ShowModeNotification(string title, string body)
    {
        if (_behaviorSettings.ModeChangeNotificationsEnabled)
        {
            ShowNotification(title, body, "blade-mode");
        }
    }

    private bool ShowNotification(string title, string body, string tag)
    {
        if (!_appNotificationsRegistered || Volatile.Read(ref _closing) != 0) return false;

        try
        {
            var notification = new AppNotificationBuilder()
                .AddText(title)
                .AddText(body)
                .BuildNotification();
            notification.Tag = tag;
            AppNotificationManager.Default.Show(notification);
            return true;
        }
        catch (Exception exception)
        {
            _diagnosticLog.TryWrite("notifications", $"show failed: {exception}");
            return false;
        }
    }

    private void InitializeTray(MainWindow window, MainViewModel viewModel)
    {
        _trayIcon?.Dispose();
        _trayIcon = null;
        _trayMenuWindow?.CloseMenuHost();
        _trayMenuWindow = null;
        try
        {
            var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "OpenSynapse.ico");
            _trayIcon = new WindowsTrayIcon(windowHandle, "OpenSynapse", iconPath);
            _trayMenuWindow = new TrayMenuWindow();
            _trayIcon.ShowRequested += window.RequestActivation;
            _trayIcon.MenuRequested += _trayMenuWindow.ShowAt;
            _trayMenuWindow.BatterySnapshot = () => _behaviorSettings.TrayBatteryLevelsEnabled
                ? viewModel.Tray.ConnectedBatteries
                : Array.Empty<PeripheralBattery>();
            _trayMenuWindow.BatteryReader = viewModel.Tray.ReadBatteriesAsync;
            _trayMenuWindow.PerformanceSnapshot = () =>
                viewModel.Blade.CanWritePerformanceMode && viewModel.Blade.PerformanceModes.Count > 0
                    ? viewModel.Blade.PerformanceModeCards
                        .Where(card => viewModel.Blade.CanApplyPerformanceModeFromTray(card.Mode))
                        .Select(static card => new TrayPerformanceMode(
                            card.Index, card.Name, card.IconGlyph, card.IsSelected))
                        .ToArray()
                    : Array.Empty<TrayPerformanceMode>();
            _trayMenuWindow.PerformanceApplier = async (index, cancellationToken) =>
            {
                var blade = viewModel.Blade;
                if (!blade.CanWritePerformanceMode ||
                    index < 0 || index >= blade.PerformanceModes.Count)
                {
                    return false;
                }

                blade.PerformanceModeIndex = index;
                await blade.ApplyPerformanceModeAsync(cancellationToken);
                return blade.PerformanceModeIndex == index;
            };
            _trayMenuWindow.ShowRequested += window.RequestActivation;
            _trayMenuWindow.NavigationRequested += window.RequestNavigation;
            _trayMenuWindow.DeviceRequested += window.RequestDeviceNavigation;
            _trayMenuWindow.ExitRequested += window.RequestExit;
            _trayIcon.Unavailable += () =>
            {
                viewModel.ReportApplicationError(AppStrings.Text("Text_57D50118"));
                window.DisableTrayLifecycle();
            };
            window.EnableTrayLifecycle();
        }
        catch (Exception exception)
        {
            _trayIcon?.Dispose();
            _trayIcon = null;
            _trayMenuWindow?.CloseMenuHost();
            _trayMenuWindow = null;
            viewModel.ReportApplicationError(AppStrings.FormatText("TrayInitializationError", exception.Message));
        }
    }

    internal void RefreshTrayLocalization() => _trayMenuWindow?.RefreshLocalization();

    private void SetTaskbarStatusEnabled(bool enabled) =>
        _taskbarStatusPresenter?.SetEnabled(enabled);

    private void RefreshTaskbarStatus() => _taskbarStatusPresenter?.RefreshNow();
}
