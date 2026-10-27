using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;

namespace OpenSynapse.App;

public sealed partial class MainWindow
{
    private async void BleSelectorClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RazerBleDeviceRowViewModel row })
        {
            return;
        }

        SelectDevice("ble");
        await _viewModel.SelectBleAsync(row, _lifetime.Token);
        UpdateDeviceSelector("ble");
    }

    private async void BleRefreshClick(object sender, RoutedEventArgs e) =>
        await (_viewModel.SelectedBle?.LoadAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void BleApplyPollingClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyBlePollingRateAsync(_lifetime.Token);

    private async void BleApplyDpiClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyBleDpiAsync(_lifetime.Token);

    private async void BleApplyLightingClick(object sender, RoutedEventArgs e) =>
        await (_viewModel.SelectedBle?.ApplyLightingAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void BleApplyMappingClick(object sender, RoutedEventArgs e) =>
        await (_viewModel.SelectedBle?.ApplyMappingAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void BleApplyDpiStagesClick(object sender, RoutedEventArgs e) =>
        await (_viewModel.SelectedBle?.ApplyDpiStagesAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void BleApplyScrollClick(object sender, RoutedEventArgs e) =>
        await (_viewModel.SelectedBle?.ApplyScrollAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void BleApplyPowerClick(object sender, RoutedEventArgs e) =>
        await (_viewModel.SelectedBle?.ApplyPowerAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void BleApplyProfileClick(object sender, RoutedEventArgs e) =>
        await (_viewModel.SelectedBle?.ApplyProfileAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void BleMappingLayerChanged(object sender, SelectionChangedEventArgs e) =>
        await (_viewModel.SelectedBle?.LoadMappingAsync(_lifetime.Token) ?? Task.CompletedTask);
}
