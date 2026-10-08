using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Devices;

namespace OpenSynapse.App;

public sealed partial class MainWindow
{
    private void AdditionalDevicesToggleClick(object sender, RoutedEventArgs e)
    {
        var expanded = AdditionalDevicesPanel.Visibility != Visibility.Visible;
        AdditionalDevicesPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        AdditionalDevicesExpandButton.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
        AdditionalDevicesCollapseButton.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ConnectedDeviceCardClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        RootNavigationView.SelectedItem = DevicesNavigationItem;
        switch (button.Tag)
        {
            case DeviceRowViewModel:
                DeviceCardClick(sender, e);
                break;
            case OpenRazerDeviceRowViewModel:
                OpenRazerDeviceSelectorClick(sender, e);
                break;
            case OpenRazerKrakenDeviceRowViewModel:
                OpenRazerKrakenSelectorClick(sender, e);
                break;
        }
    }

    private void DeviceCardClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DeviceRowViewModel row })
        {
            return;
        }

        if (StringComparer.Ordinal.Equals(row.ProtocolFamily, DeviceProtocolFamilies.Blade))
        {
            SelectDevice("blade");
        }
        else if (StringComparer.Ordinal.Equals(row.ProtocolFamily, DeviceProtocolFamilies.Viper))
        {
            SelectDevice("viper");
        }
    }

    private async void OpenRazerDeviceSelectorClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: OpenRazerDeviceRowViewModel row })
        {
            return;
        }

        SelectDevice("openrazer");
        await _viewModel.SelectOpenRazerDeviceAsync(row, _lifetime.Token);
        UpdateDeviceSelector("openrazer");
    }

    private async void DeviceSelectorItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DeviceSelectorItemViewModel item })
        {
            return;
        }

        switch (item.Kind)
        {
            case DeviceSelectorItemKind.Blade:
                SelectDevice("blade");
                break;
            case DeviceSelectorItemKind.Viper:
                SelectDevice("viper");
                break;
            case DeviceSelectorItemKind.OpenRazer when item.Source is OpenRazerDeviceRowViewModel row:
                SelectDevice("openrazer");
                await _viewModel.SelectOpenRazerDeviceAsync(row, _lifetime.Token);
                break;
            case DeviceSelectorItemKind.Kraken when item.Source is OpenRazerKrakenDeviceRowViewModel row:
                await _viewModel.SelectOpenRazerKrakenAsync(row);
                SelectDevice("kraken");
                if (_viewModel.SelectedOpenRazerKraken is { } kraken)
                    await kraken.LoadSerialAsync(_lifetime.Token);
                break;
        }

        UpdateDeviceSelector(item.Kind switch
        {
            DeviceSelectorItemKind.Blade => "blade",
            DeviceSelectorItemKind.Viper => "viper",
            DeviceSelectorItemKind.OpenRazer => "openrazer",
            DeviceSelectorItemKind.Kraken => "kraken",
            _ => string.Empty,
        });
    }

    private async void OpenRazerKrakenSelectorClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: OpenRazerKrakenDeviceRowViewModel row }) return;
        await _viewModel.SelectOpenRazerKrakenAsync(row);
        SelectDevice("kraken");
        if (_viewModel.SelectedOpenRazerKraken is { } kraken)
            await kraken.LoadSerialAsync(_lifetime.Token);
    }

    private async void OpenRazerKrakenApplyClick(object sender, RoutedEventArgs e) =>
        await (_viewModel.SelectedOpenRazerKraken?.ApplyAsync(_lifetime.Token) ?? Task.CompletedTask);

    private OpenRazerDeviceViewModel? SelectedOpenRazerDevice => _viewModel.SelectedOpenRazerDevice;
    private OpenRazerKrakenViewModel? SelectedOpenRazerKraken => _viewModel.SelectedOpenRazerKraken;

    private async void OpenRazerApplyPollingClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyPollingAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void OpenRazerApplyDpiClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyDpiAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void OpenRazerApplyDpiStagesClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyDpiStagesAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void OpenRazerApplyIdleClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyIdleTimeoutAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void OpenRazerApplyLowBatteryClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyLowBatteryThresholdAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void OpenRazerApplyBrightnessClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyBrightnessAsync(_lifetime.Token) ?? Task.CompletedTask);

    private void OpenRazerBrightnessValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (SelectedOpenRazerDevice is { } device && e.NewValue is >= 0 and <= 255)
        {
            device.Brightness = (byte)Math.Round(e.NewValue);
        }
    }

    private async void OpenRazerApplyLightingClick(object sender, RoutedEventArgs e)
    {
        if (SelectedOpenRazerDevice is not { } device)
        {
            return;
        }

        await device.ApplyLightingAsync(_lifetime.Token);
        await device.ApplyBrightnessAsync(_lifetime.Token);
        await device.ApplyLedStateAsync(_lifetime.Token);
    }

    private async void OpenRazerLightingSettingsToggled(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyLightingSettingsAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void OpenRazerApplyLedStateClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyLedStateAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void OpenRazerReactiveTriggerClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.TriggerReactiveAsync(_lifetime.Token) ?? Task.CompletedTask);

    private void OpenRazerMatrixCellClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: OpenRazerMatrixCellViewModel cell } button) return;
        var picker = new ColorPicker
        {
            Color = cell.Color,
            Width = 260,
            IsAlphaEnabled = false,
            IsColorChannelTextInputVisible = false,
            IsHexInputVisible = true,
            IsMoreButtonVisible = false,
        };
        var content = new StackPanel { Width = 280, Spacing = 10 };
        content.Children.Add(new TextBlock
        {
            Text = AppStrings.Text("LightingQuickColors.Text"),
            FontSize = 12,
        });
        var flyout = new Flyout { Content = content, ShouldConstrainToRootBounds = false };
        content.Children.Add(CreateMatrixColorSwatches(LightingPaletteColors, picker, flyout, 8));
        if (RecentLightingColors.Count > 0)
        {
            content.Children.Add(new TextBlock
            {
                Text = AppStrings.Text("LightingRecentColors.Text"),
                FontSize = 12,
            });
            content.Children.Add(CreateMatrixColorSwatches(RecentLightingColors, picker, flyout, 6));
        }
        content.Children.Add(new Expander
        {
            Header = AppStrings.Text("LightingCustomColor.Header"),
            Content = picker,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        });
        flyout.Closed += (_, _) =>
        {
            if (cell.Color == picker.Color) return;
            cell.Color = picker.Color;
            AddRecentLightingColor(picker.Color);
        };
        flyout.ShowAt(button);
    }

    private static Grid CreateMatrixColorSwatches(
        IEnumerable<LightingColorOption> colors, ColorPicker picker, Flyout flyout, int columns)
    {
        var grid = new Grid();
        for (var index = 0; index < columns; index++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        var options = colors.ToArray();
        for (var index = 0; index < options.Length; index++)
        {
            if (index % columns == 0) grid.RowDefinitions.Add(new RowDefinition());
            var option = options[index];
            var button = new Button
            {
                Width = 28,
                Height = 28,
                MinWidth = 28,
                MinHeight = 28,
                Margin = new Thickness(3),
                Padding = new Thickness(0),
                Background = option.Brush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
            };
            ToolTipService.SetToolTip(button, option.Hex);
            button.Click += (_, _) =>
            {
                picker.Color = option.Brush.Color;
                flyout.Hide();
            };
            Grid.SetRow(button, index / columns);
            Grid.SetColumn(button, index % columns);
            grid.Children.Add(button);
        }
        return grid;
    }

    private async void OpenRazerApplyMatrixClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyMatrixAsync(_lifetime.Token) ?? Task.CompletedTask);

    private async void OpenRazerLightingSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var device = SelectedOpenRazerDevice;
        if (device?.IsLightingPowerProfileActive != true)
        {
            return;
        }

        await device.LoadLightingAsync(_lifetime.Token);
    }

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
            CloseButtonText = AppStrings.Text("Text_949856B3"),
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
