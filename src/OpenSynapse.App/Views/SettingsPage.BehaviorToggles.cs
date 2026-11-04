using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OpenSynapse.App.Views;

public sealed partial class SettingsPage
{
    private void TrayBatteryToggled(object sender, RoutedEventArgs e)
    {
        if (!_behaviorUiReady || sender is not ToggleSwitch toggle) return;
        var settings = Host.BehaviorSettings;
        var previous = settings.TrayBatteryLevelsEnabled;
        try
        {
            settings.TrayBatteryLevelsEnabled = toggle.IsOn;
            settings.Save();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            InvalidOperationException or System.Security.SecurityException)
        {
            settings.TrayBatteryLevelsEnabled = previous;
            _behaviorUiReady = false;
            toggle.IsOn = previous;
            _behaviorUiReady = true;
            ViewModel.ReportApplicationError(AppStrings.FormatText("BehaviorSettingError", exception.Message));
        }
    }

    private void PerformanceOverlayToggled(object sender, RoutedEventArgs e)
    {
        if (!_behaviorUiReady || sender is not ToggleSwitch toggle) return;
        var settings = Host.BehaviorSettings;
        var previous = settings.PerformanceOverlayEnabled;
        try
        {
            settings.PerformanceOverlayEnabled = toggle.IsOn;
            settings.Save();
            Host.SetPerformanceOverlayEnabled(toggle.IsOn);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            InvalidOperationException or System.Security.SecurityException)
        {
            settings.PerformanceOverlayEnabled = previous;
            _behaviorUiReady = false;
            toggle.IsOn = previous;
            _behaviorUiReady = true;
            ViewModel.ReportApplicationError(AppStrings.FormatText("BehaviorSettingError", exception.Message));
        }
    }

    private void PerformanceOverlayDetailedToggled(object sender, RoutedEventArgs e)
    {
        if (!_behaviorUiReady || sender is not ToggleSwitch toggle) return;
        var settings = Host.BehaviorSettings;
        var previous = settings.PerformanceOverlayDetailed;
        try
        {
            settings.PerformanceOverlayDetailed = toggle.IsOn;
            settings.Save();
            Host.RefreshPerformanceOverlayLayout();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            InvalidOperationException or System.Security.SecurityException)
        {
            settings.PerformanceOverlayDetailed = previous;
            _behaviorUiReady = false;
            toggle.IsOn = previous;
            _behaviorUiReady = true;
            ViewModel.ReportApplicationError(AppStrings.FormatText("BehaviorSettingError", exception.Message));
        }
    }
    private void TaskbarStatusToggled(object sender, RoutedEventArgs e)
    {
        if (!_behaviorUiReady || sender is not ToggleSwitch toggle) return;
        var settings = Host.BehaviorSettings;
        var previous = settings.TaskbarStatusEnabled;
        try
        {
            settings.TaskbarStatusEnabled = toggle.IsOn;
            settings.Save();
            Host.SetTaskbarStatusEnabled(toggle.IsOn);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            InvalidOperationException or System.Security.SecurityException)
        {
            settings.TaskbarStatusEnabled = previous;
            _behaviorUiReady = false;
            toggle.IsOn = previous;
            _behaviorUiReady = true;
            ViewModel.ReportApplicationError(AppStrings.FormatText("BehaviorSettingError", exception.Message));
        }
    }
}
