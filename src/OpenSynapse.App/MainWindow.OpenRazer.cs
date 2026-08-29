using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;

namespace OpenSynapse.App;

public sealed partial class MainWindow
{
    private async void OpenRazerDeviceSelectorClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: OpenRazerDeviceRowViewModel row })
        {
            return;
        }

        SelectDevice("openrazer");
        await _viewModel.SelectOpenRazerDeviceAsync(row, _lifetime.Token);
    }

    private void OpenRazerKrakenSelectorClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: OpenRazerKrakenDeviceRowViewModel row }) return;
        _viewModel.SelectOpenRazerKraken(row);
        SelectDevice("kraken");
    }

    private async void OpenRazerKrakenApplyClick(object sender, RoutedEventArgs e) =>
        await (_viewModel.SelectedOpenRazerKraken?.ApplyAsync(_lifetime.Token) ?? Task.CompletedTask);

    private OpenRazerDeviceViewModel? SelectedOpenRazerDevice => _viewModel.SelectedOpenRazerDevice;

    private async void OpenRazerApplyPollingClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyPollingAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void OpenRazerApplyDpiClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyDpiAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void OpenRazerDpiStagesExpanding(Expander sender, ExpanderExpandingEventArgs args) =>
        await (SelectedOpenRazerDevice?.LoadDpiStagesAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void OpenRazerApplyDpiStagesClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyDpiStagesAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void OpenRazerApplyIdleClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyIdleTimeoutAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void OpenRazerApplyLowBatteryClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyLowBatteryThresholdAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void OpenRazerApplyBrightnessClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyBrightnessAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void OpenRazerApplyLightingClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyLightingAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void OpenRazerApplyLedStateClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyLedStateAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void OpenRazerReactiveTriggerClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.TriggerReactiveAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void OpenRazerMatrixCellClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: OpenRazerMatrixCellViewModel cell }) return;
        var picker = new ColorPicker
        {
            Color = cell.Color,
            IsAlphaEnabled = false,
            IsAlphaSliderVisible = false,
            IsAlphaTextInputVisible = false,
        };
        var dialog = new ContentDialog
        {
            XamlRoot = RootLayout.XamlRoot,
            Title = cell.AutomationName,
            Content = picker,
            PrimaryButtonText = AppStrings.Get("确定"),
            CloseButtonText = AppStrings.Get("取消"),
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) cell.Color = picker.Color;
    }

    private async void OpenRazerApplyMatrixClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyMatrixAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void OpenRazerLightingSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        await (SelectedOpenRazerDevice?.LoadLightingAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void OpenRazerScrollExpanding(Expander sender, ExpanderExpandingEventArgs args) =>
        await (SelectedOpenRazerDevice?.LoadScrollAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void OpenRazerApplyScrollModeClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyScrollModeAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void OpenRazerApplyScrollAccelerationClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyScrollAccelerationAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void OpenRazerApplySmartReelClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplySmartReelAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void OpenRazerKeyswitchExpanding(Expander sender, ExpanderExpandingEventArgs args) =>
        await (SelectedOpenRazerDevice?.LoadKeyswitchAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void OpenRazerApplyKeyswitchClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyKeyswitchAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void OpenRazerFnPrimaryClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.SetFnPrimaryAsync(true, _lifetime.Token) ?? Task.CompletedTask);

    private async void OpenRazerMediaPrimaryClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.SetFnPrimaryAsync(false, _lifetime.Token) ?? Task.CompletedTask);

    private async void OpenRazerApplyHyperPollingIndicatorClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.SetHyperPollingIndicatorAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void OpenRazerPairHyperPollingClick(object sender, RoutedEventArgs e) =>
        await ConfirmHyperPollingAsync(pair: true);

    private async void OpenRazerUnpairHyperPollingClick(object sender, RoutedEventArgs e) =>
        await ConfirmHyperPollingAsync(pair: false);

    private async Task ConfirmHyperPollingAsync(bool pair)
    {
        if (SelectedOpenRazerDevice is not { } device) return;
        var productId = new TextBox
        {
            Header = AppStrings.Text("OpenRazerMouseProductIdLabel"),
            PlaceholderText = "00B8",
            MaxLength = 6,
        };
        var dialog = new ContentDialog
        {
            XamlRoot = RootLayout.XamlRoot,
            Title = AppStrings.Text(pair ? "OpenRazerPairTitle" : "OpenRazerUnpairTitle"),
            Content = productId,
            PrimaryButtonText = AppStrings.Text(pair ? "OpenRazerPairAction" : "OpenRazerUnpairAction"),
            CloseButtonText = AppStrings.Get("取消"),
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var input = productId.Text.Trim();
        if (input.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) input = input[2..];
        if (input.Length != 4 || !ushort.TryParse(input, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var mouseProductId))
        {
            _viewModel.ReportApplicationError(AppStrings.Text("OpenRazerInvalidProductId"));
            return;
        }
        if (pair) await device.PairHyperPollingAsync(mouseProductId, _lifetime.Token);
        else await device.UnpairHyperPollingAsync(mouseProductId, _lifetime.Token);
    }
}
