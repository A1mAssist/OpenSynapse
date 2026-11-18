using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OpenSynapse.App.Views;

public sealed partial class DevicesPage : UserControl
{
    private async void RazerHidCheckFirmwareClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.RazerHid.SelectedDevice?.CheckFirmwareUpdateAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidInstallFirmwareClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.RazerHid.SelectedDevice?.InstallFirmwareUpdateAsync(Lifetime) ?? Task.CompletedTask);

    private async void KeyboardCheckFirmwareClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.Accessories.SelectedKeyboard?.CheckFirmwareUpdateAsync(Lifetime) ?? Task.CompletedTask);

    private async void KeyboardInstallFirmwareClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.Accessories.SelectedKeyboard?.InstallFirmwareUpdateAsync(Lifetime) ?? Task.CompletedTask);
}
