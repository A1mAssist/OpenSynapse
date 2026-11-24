using Microsoft.UI.Xaml;
using OpenSynapse.App.ViewModels;

namespace OpenSynapse.App.Views;

public sealed partial class DevicesPage
{
    private async void ApBleHeadsetSelectorClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Microsoft.UI.Xaml.Controls.Button { Tag: RazerApBleHeadsetRowViewModel row })
        {
            return;
        }

        await ViewModel.SelectApBleHeadsetAsync(row, Lifetime);
    }

    private async void ApBleHeadsetRefreshClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedApBleHeadset?.LoadAsync(Lifetime) ?? Task.CompletedTask);

    private async void ApBleHeadsetApplyAncClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedApBleHeadset?.ApplyAncAsync(Lifetime) ?? Task.CompletedTask);

    private async void ApBleHeadsetApplyVolumeClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedApBleHeadset?.ApplyVolumeAsync(Lifetime) ?? Task.CompletedTask);

    private async void ApBleHeadsetApplySidetoneClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedApBleHeadset?.ApplySidetoneAsync(Lifetime) ?? Task.CompletedTask);

    private async void ApBleHeadsetApplyEqClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedApBleHeadset?.ApplyEqPresetAsync(Lifetime) ?? Task.CompletedTask);

    private async void ApBleHeadsetApplyGameModeClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedApBleHeadset?.ApplyGameModeAsync(Lifetime) ?? Task.CompletedTask);

    private async void ApBleHeadsetApplyThxClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedApBleHeadset?.ApplyThxAsync(Lifetime) ?? Task.CompletedTask);

    private async void ApBleHeadsetApplyMultipointClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedApBleHeadset?.ApplyMultipointAsync(Lifetime) ?? Task.CompletedTask);

    private async void ApBleHeadsetApplyDndClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedApBleHeadset?.ApplyDndAsync(Lifetime) ?? Task.CompletedTask);

    private async void ApBleHeadsetApplyAutoPowerOffClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedApBleHeadset?.ApplyAutoPowerOffAsync(Lifetime) ?? Task.CompletedTask);

    private async void ApBleHeadsetApplyChromaBrightnessClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedApBleHeadset?.ApplyChromaBrightnessAsync(Lifetime) ?? Task.CompletedTask);

    private async void ApBleHeadsetApplyChromaEffectClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedApBleHeadset?.ApplyChromaEffectAsync(Lifetime) ?? Task.CompletedTask);
}
