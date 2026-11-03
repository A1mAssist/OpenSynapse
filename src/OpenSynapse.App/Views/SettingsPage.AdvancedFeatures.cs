using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OpenSynapse.App.Views;

public sealed partial class SettingsPage
{    private async void AdvancedFeaturesToggled(object sender, RoutedEventArgs e)
    {
        if (!_behaviorUiReady || sender is not ToggleSwitch toggle)
        {
            return;
        }

        var settings = Host.BehaviorSettings;
        var previous = settings.AdvancedFeaturesEnabled;
        if (toggle.IsOn == previous)
        {
            return;
        }

        if (toggle.IsOn)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = AppStrings.Text("AdvancedFeaturesDisclaimerTitle"),
                Content = AppStrings.Text("AdvancedFeaturesDisclaimerMessage"),
                PrimaryButtonText = AppStrings.Text("AdvancedFeaturesDisclaimerAccept"),
                CloseButtonText = AppStrings.Text("Text_949856B3"),
                DefaultButton = ContentDialogButton.Close,
            };
            if (await ShowDialogPreservingScrollAsync(dialog) != ContentDialogResult.Primary)
            {
                _behaviorUiReady = false;
                toggle.IsOn = false;
                _behaviorUiReady = true;
                return;
            }
        }

        try
        {
            settings.AdvancedFeaturesEnabled = toggle.IsOn;
            settings.Save();
            Host.RefreshAdvancedFeaturesVisibility();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                InvalidOperationException or System.Security.SecurityException)
        {
            settings.AdvancedFeaturesEnabled = previous;
            _behaviorUiReady = false;
            toggle.IsOn = previous;
            _behaviorUiReady = true;
            ViewModel.ReportApplicationError(AppStrings.FormatText("BehaviorSettingError", exception.Message));
        }
    }

    private async Task<ContentDialogResult> ShowDialogPreservingScrollAsync(ContentDialog dialog)
    {
        var offset = PageScrollViewer.VerticalOffset;
        var result = await dialog.ShowAsync();
        DispatcherQueue.TryEnqueue(() =>
            PageScrollViewer.ChangeView(null, offset, null, disableAnimation: true));
        return result;
    }

}