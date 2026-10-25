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
}
