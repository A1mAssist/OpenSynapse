using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OpenSynapse.App.Views;

public sealed partial class DevicesPage
{
    private async void RazerHidFullscreenPollingToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch { IsLoaded: true, DataContext: ViewModels.RazerHidDeviceViewModel device } toggle &&
            !device.IsBasicBusy && !device.IsPollingBusy &&
            device.HasFullscreenPollingChange(toggle.IsOn, device.SelectedFullscreenPollingRateIndex))
        {
            await device.ApplyFullscreenPollingAsync(
                toggle.IsOn,
                device.SelectedFullscreenPollingRateIndex,
                Host?.LifetimeToken ?? default);
        }
    }

    private async void RazerHidFullscreenPollingRateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { IsLoaded: true, DataContext: ViewModels.RazerHidDeviceViewModel device } combo &&
            !device.IsBasicBusy && !device.IsPollingBusy &&
            combo.SelectedIndex >= 0 &&
            device.HasFullscreenPollingChange(device.FullscreenPollingEnabled, combo.SelectedIndex))
        {
            await device.ApplyFullscreenPollingAsync(
                device.FullscreenPollingEnabled,
                combo.SelectedIndex,
                Host?.LifetimeToken ?? default);
        }
    }

    private async void KeyboardFullscreenPollingToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch { IsLoaded: true, DataContext: ViewModels.RazerKeyboardViewModel keyboard } toggle &&
            !keyboard.IsBusy &&
            keyboard.HasFullscreenPollingChange(toggle.IsOn, keyboard.SelectedFullscreenPollingIndex))
        {
            await keyboard.SetFullscreenPollingAsync(
                toggle.IsOn,
                keyboard.SelectedFullscreenPollingIndex,
                Host?.LifetimeToken ?? default);
        }
    }

    private async void KeyboardFullscreenPollingRateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { IsLoaded: true, DataContext: ViewModels.RazerKeyboardViewModel keyboard } combo &&
            !keyboard.IsBusy && combo.SelectedIndex >= 0 &&
            keyboard.HasFullscreenPollingChange(keyboard.FullscreenPollingEnabled, combo.SelectedIndex))
        {
            await keyboard.SetFullscreenPollingAsync(
                keyboard.FullscreenPollingEnabled,
                combo.SelectedIndex,
                Host?.LifetimeToken ?? default);
        }
    }
}
