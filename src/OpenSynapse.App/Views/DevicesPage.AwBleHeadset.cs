using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;

namespace OpenSynapse.App.Views;

public sealed partial class DevicesPage : UserControl
{
    private async void AwBleHeadsetSelectorClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Microsoft.UI.Xaml.Controls.Button { Tag: RazerAwBleHeadsetRowViewModel row })
        {
            return;
        }

        SelectDevice("awbleheadset");
        await ViewModel.SelectAwBleHeadsetAsync(row, Lifetime);
        UpdateDeviceSelector("awbleheadset");
    }

    private async void AwBleHeadsetRefreshClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedAwBleHeadset?.LoadAsync(Lifetime) ?? Task.CompletedTask);

    private async void AwBleHeadsetApplyVolumeClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedAwBleHeadset?.ApplyVolumeAsync(Lifetime) ?? Task.CompletedTask);

    private async void AwBleHeadsetApplySidetoneClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedAwBleHeadset?.ApplySidetoneAsync(Lifetime) ?? Task.CompletedTask);

    private async void AwBleHeadsetApplyGameModeClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedAwBleHeadset?.ApplyGameModeAsync(Lifetime) ?? Task.CompletedTask);

    private async void AwBleHeadsetApplyEqPresetClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedAwBleHeadset?.ApplyEqPresetAsync(Lifetime) ?? Task.CompletedTask);

    private async void AwBleHeadsetApplyAutoPowerOffClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedAwBleHeadset?.ApplyAutoPowerOffAsync(Lifetime) ?? Task.CompletedTask);

    private async void AwBleHeadsetApplyChromaBrightnessClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedAwBleHeadset?.ApplyChromaBrightnessAsync(Lifetime) ?? Task.CompletedTask);

    private async void AwBleHeadsetApplyBassClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedAwBleHeadset?.ApplyBassAsync(Lifetime) ?? Task.CompletedTask);

    private async void AwBleHeadsetApplyHapticsClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedAwBleHeadset?.ApplyHapticsAsync(Lifetime) ?? Task.CompletedTask);

    private async void AwBleHeadsetApplyMicEqClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedAwBleHeadset?.ApplyMicEqPresetAsync(Lifetime) ?? Task.CompletedTask);

    private async void AwBleHeadsetApplyCustomEqClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedAwBleHeadset?.ApplyCustomEqAsync(Lifetime) ?? Task.CompletedTask);
}
