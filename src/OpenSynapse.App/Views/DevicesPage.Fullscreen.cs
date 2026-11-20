using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OpenSynapse.App.Views;

public sealed partial class DevicesPage
{
    private async void RazerHidFullscreenPollingToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle &&
            SelectedRazerHidDevice is { } device &&
            toggle.IsOn != device.FullscreenPollingEnabled)
        {
            await device.ApplyFullscreenPollingAsync(
                toggle.IsOn,
                device.SelectedFullscreenPollingRateIndex,
                Lifetime);
            toggle.IsOn = device.FullscreenPollingEnabled;
        }
    }

    private async void RazerHidFullscreenPollingRateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox combo &&
            SelectedRazerHidDevice is { } device &&
            combo.SelectedIndex >= 0 &&
            combo.SelectedIndex != device.SelectedFullscreenPollingRateIndex)
        {
            await device.ApplyFullscreenPollingAsync(
                device.FullscreenPollingEnabled,
                combo.SelectedIndex,
                Lifetime);
            combo.SelectedIndex = device.SelectedFullscreenPollingRateIndex;
        }
    }

    private async void KeyboardFullscreenPollingToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle &&
            ViewModel.Accessories.SelectedKeyboard is { } keyboard &&
            toggle.IsOn != keyboard.FullscreenPollingEnabled)
        {
            await keyboard.SetFullscreenPollingAsync(
                toggle.IsOn,
                keyboard.SelectedFullscreenPollingIndex,
                Lifetime);
            toggle.IsOn = keyboard.FullscreenPollingEnabled;
        }
    }

    private async void KeyboardFullscreenPollingRateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox combo &&
            ViewModel.Accessories.SelectedKeyboard is { } keyboard &&
            combo.SelectedIndex >= 0 &&
            combo.SelectedIndex != keyboard.SelectedFullscreenPollingIndex)
        {
            await keyboard.SetFullscreenPollingAsync(
                keyboard.FullscreenPollingEnabled,
                combo.SelectedIndex,
                Lifetime);
            combo.SelectedIndex = keyboard.SelectedFullscreenPollingIndex;
        }
    }
}
