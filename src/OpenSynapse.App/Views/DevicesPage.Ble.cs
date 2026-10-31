using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;

namespace OpenSynapse.App.Views;

public sealed partial class DevicesPage : UserControl
{
    private async void BleSelectorClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RazerBleDeviceRowViewModel row })
        {
            return;
        }

        SelectDevice("ble");
        await ViewModel.SelectBleAsync(row, Lifetime);
        UpdateDeviceSelector("ble");
    }

    private async void BleRefreshClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedBle?.LoadAsync(Lifetime) ?? Task.CompletedTask);

    private async void BleApplyPollingClick(object sender, RoutedEventArgs e) =>
        await ViewModel.ApplyBlePollingRateAsync(Lifetime);

    private async void BleApplyDpiClick(object sender, RoutedEventArgs e) =>
        await ViewModel.ApplyBleDpiAsync(Lifetime);

    private async void BleApplyLightingClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedBle?.ApplyLightingAsync(Lifetime) ?? Task.CompletedTask);

    private async void BleApplyMappingClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedBle?.ApplyMappingAsync(Lifetime) ?? Task.CompletedTask);

    private async void BleApplyDpiStagesClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedBle?.ApplyDpiStagesAsync(Lifetime) ?? Task.CompletedTask);

    private async void BleApplyScrollClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedBle is not { } ble) return;

        await ble.ApplyScrollAsync(Lifetime);
        await ble.ApplyScrollExtrasAsync(Lifetime);
    }

    private async void BleApplyPowerClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedBle is not { } ble) return;

        await ble.ApplyPowerAsync(Lifetime);
        await ble.ApplyPowerPlanAsync(Lifetime);
    }

    private async void BleApplyProfileClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedBle?.ApplyProfileAsync(Lifetime) ?? Task.CompletedTask);

    private async void BleMappingLayerChanged(object sender, SelectionChangedEventArgs e) =>
        await (ViewModel.SelectedBle?.LoadMappingAsync(Lifetime) ?? Task.CompletedTask);

    private async void BleApplyBatteryChemistryClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedBle?.ApplyBatteryChemistryAsync(Lifetime) ?? Task.CompletedTask);

    private async void BleApplySmartIlluminationClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.SelectedBle?.ApplySmartIlluminationAsync(Lifetime) ?? Task.CompletedTask);

    private async void BleApplyKeysClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedBle is not { } ble) return;

        if (ble.SnapTapVisibility == Visibility.Visible)
        {
            await ble.ApplySnapTapAsync(Lifetime);
        }

        if (ble.DebounceVisibility == Visibility.Visible)
        {
            await ble.ApplyDebounceAsync(Lifetime);
        }
    }
}
