using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;

namespace OpenSynapse.App;

public sealed partial class MainWindow
{
    private async void GamepadSelectorClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RazerGamepadDeviceRowViewModel row }) return;
        SelectDevice("gamepad");
        await _viewModel.SelectGamepadAsync(row, _lifetime.Token);
        UpdateDeviceSelector("gamepad");
    }

    private async void GamepadRefreshClick(object sender, RoutedEventArgs e)
    {
        await (_viewModel.SelectedGamepad?.LoadAsync(_lifetime.Token) ?? Task.CompletedTask);
    }

    private async void GamepadApplyStickClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RazerGamepadStickRowViewModel row })
        {
            await (_viewModel.SelectedGamepad?.ApplyStickAsync(row, _lifetime.Token) ?? Task.CompletedTask);
        }
    }

    private async void GamepadApplyTriggerClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RazerGamepadTriggerRowViewModel row })
        {
            await (_viewModel.SelectedGamepad?.ApplyTriggerAsync(row, _lifetime.Token) ?? Task.CompletedTask);
        }
    }

    private async void GamepadApplyDpadClick(object sender, RoutedEventArgs e)
    {
        await (_viewModel.SelectedGamepad?.ApplyDpadAsync(_lifetime.Token) ?? Task.CompletedTask);
    }

    private async void GamepadApplyVibrationClick(object sender, RoutedEventArgs e)
    {
        await (_viewModel.SelectedGamepad?.ApplyVibrationAsync(_lifetime.Token) ?? Task.CompletedTask);
    }
}
