using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;

namespace OpenSynapse.App.Views;

public sealed partial class DevicesPage : UserControl
{
    private async void KeyboardSelectorClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RazerKeyboardDeviceRowViewModel row })
        {
            return;
        }

        SelectDevice("keyboard");
        await ViewModel.SelectKeyboardAsync(row, Lifetime);
        UpdateDeviceSelector("keyboard");
    }

    private async void KeyboardRefreshClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedKeyboard?.LoadAsync(Lifetime) ?? Task.CompletedTask);

    private async void KeyboardApplyGameModeClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedKeyboard?.ApplyGameModeAsync(Lifetime) ?? Task.CompletedTask);

    private async void KeyboardApplyBrightnessClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedKeyboard?.ApplyBrightnessAsync(Lifetime) ?? Task.CompletedTask);

    private async void KeyboardApplyLightingClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedKeyboard?.ApplyLightingAsync(Lifetime) ?? Task.CompletedTask);

    private async void KeyboardApplyPollingClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedKeyboard?.ApplyPollingAsync(Lifetime) ?? Task.CompletedTask);

    private async void KeyboardReadOnboardMacrosClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedKeyboard?.LoadOnboardMacrosAsync(Lifetime) ?? Task.CompletedTask);

    private async void KeyboardWriteOnboardMacroClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedKeyboard?.WriteOnboardMacroAsync(Lifetime) ?? Task.CompletedTask);
}


