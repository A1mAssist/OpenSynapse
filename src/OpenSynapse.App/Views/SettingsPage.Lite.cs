using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.Windows.Input;

namespace OpenSynapse.App.Views;

public sealed partial class SettingsPage
{
    internal void SetLiteMode(bool liteMode)
    {
        var visibility = liteMode ? Visibility.Collapsed : Visibility.Visible;
        AdvancedFeaturesCard.Visibility = visibility;
        ModeNotificationCard.Visibility = visibility;
        TaskbarStatusCard.Visibility = visibility;
        PerformanceOverlayCard.Visibility = visibility;
        ChromaRestCard.Visibility = visibility;
    }

    private async void LiteModeToggled(object sender, RoutedEventArgs e)
    {
        if (!_behaviorUiReady || sender is not ToggleSwitch toggle)
        {
            return;
        }

        var settings = Host.BehaviorSettings;
        var previous = settings.LiteModeEnabled;
        if (toggle.IsOn == previous)
        {
            return;
        }

        _behaviorUiReady = false;
        try
        {
            if (XamlRoot is null)
            {
                toggle.IsOn = previous;
                return;
            }

            var prompt = AppStrings.Text("LiteModeRestartPrompt");
            if (ProcessIntegrityLevel.IsElevated(ProcessIntegrityLevel.Current()))
            {
                prompt += Environment.NewLine + AppStrings.Text("LiteModeRestartElevated");
            }
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = AppStrings.Text("LiteModeRestartTitle"),
                Content = prompt,
                PrimaryButtonText = AppStrings.Text("LiteModeRestartConfirm"),
                CloseButtonText = AppStrings.Text("LiteModeRestartCancel"),
                DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                toggle.IsOn = previous;
                return;
            }

            try
            {
                settings.LiteModeEnabled = toggle.IsOn;
                settings.Save();
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or
                    InvalidOperationException or System.Security.SecurityException)
            {
                settings.LiteModeEnabled = previous;
                toggle.IsOn = previous;
                ViewModel.ReportApplicationError(AppStrings.FormatText(
                    "BehaviorSettingError", exception.Message));
                return;
            }

            ((App)Application.Current).RequestRestart(previous);
        }
        finally
        {
            _behaviorUiReady = true;
        }
    }
}
