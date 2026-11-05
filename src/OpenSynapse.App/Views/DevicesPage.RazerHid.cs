using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Devices;
using Windows.System;

namespace OpenSynapse.App.Views;

public sealed partial class DevicesPage : UserControl
{
    private void AdditionalDevicesToggleClick(object sender, RoutedEventArgs e)
    {
        var expanded = AdditionalDevicesPanel.Visibility != Visibility.Visible;
        AdditionalDevicesPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        AdditionalDevicesExpandButton.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
        AdditionalDevicesCollapseButton.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OpenBladeFanCurveWindowClick(object sender, RoutedEventArgs e) =>
        Host.OpenBladeFanCurveWindow();

    private void OpenThxAudioWindowClick(object sender, RoutedEventArgs e) =>
        Host.OpenThxAudioWindow();

    private async void ConnectedDeviceCardClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        try
        {
            await SelectDeviceSourceAsync(button.Tag);
        }
        catch (OperationCanceledException) when (Lifetime.IsCancellationRequested) { }
        catch (Exception exception) when (RuntimeErrors.IsExpected(exception))
        {
            ViewModel.ReportApplicationError(exception.Message);
        }
    }

    private void DeviceCardClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DeviceRowViewModel row })
        {
            return;
        }

        if (DeviceProtocolFamilies.IsBlade(row.ProtocolFamily))
        {
            SelectDevice("blade");
        }
        else if (StringComparer.Ordinal.Equals(row.ProtocolFamily, DeviceProtocolFamilies.Viper))
        {
            SelectDevice("viper");
        }
    }

    private async void RazerHidDeviceSelectorClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RazerHidDeviceRowViewModel row })
        {
            return;
        }

        SelectDevice("openrazer");
        await ViewModel.RazerHid.SelectDeviceAsync(row, Lifetime);
        UpdateDeviceSelector("openrazer");
    }

    private async void DeviceSelectorItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DeviceSelectorItemViewModel item })
        {
            return;
        }

        await SelectDeviceSourceAsync(item.Source);
    }

    private async Task SelectDeviceSourceAsync(object? source)
    {
        switch (source)
        {
            case RazerHidDeviceRowViewModel row:
                SelectDevice("openrazer");
                await ViewModel.RazerHid.SelectDeviceAsync(row, Lifetime);
                break;
            case RazerHidKrakenDeviceRowViewModel row:
                SelectDevice("kraken");
                await ViewModel.RazerHid.SelectKrakenAsync(row);
                if (ViewModel.RazerHid.SelectedKraken is { } kraken)
                {
                    await kraken.LoadSerialAsync(Lifetime);
                }
                break;
            case BlackSharkDeviceRowViewModel row:
                SelectDevice("blackshark");
                await ViewModel.SelectBlackSharkAsync(row, Lifetime);
                break;
            case RazerGamepadDeviceRowViewModel row:
                SelectDevice("gamepad");
                await ViewModel.Accessories.SelectGamepadAsync(row, Lifetime);
                break;
            case RazerHeadsetAudioDeviceRowViewModel row:
                SelectDevice("headsetaudio");
                await ViewModel.Accessories.SelectHeadsetAudioAsync(row, Lifetime);
                break;
            case RazerKeyboardDeviceRowViewModel row:
                SelectDevice("keyboard");
                await ViewModel.Accessories.SelectKeyboardAsync(row, Lifetime);
                break;
            case RazerChromaDeviceRowViewModel row:
                SelectDevice("chroma");
                await ViewModel.Accessories.SelectChromaAsync(row, Lifetime);
                break;
            case RazerBleDeviceRowViewModel row:
                SelectDevice("ble");
                await ViewModel.SelectBleAsync(row, Lifetime);
                break;
            case RazerAwBleHeadsetRowViewModel row:
                SelectDevice("awbleheadset");
                await ViewModel.SelectAwBleHeadsetAsync(row, Lifetime);
                break;
            case RazerCoolingPadViewModel pad:
                SelectDevice("coolingpad");
                await pad.RefreshAsync(Lifetime);
                if (!pad.IsConnected)
                {
                    SelectDevice("none");
                }
                break;
            case RazerKiyoViewModel kiyo:
                SelectDevice("kiyo");
                await kiyo.RefreshAsync(Lifetime);
                if (!kiyo.IsConnected)
                {
                    SelectDevice("none");
                }
                break;
            case DeviceRowViewModel row
                when DeviceProtocolFamilies.IsBlade(row.ProtocolFamily):
                SelectDevice("blade");
                break;
            case DeviceRowViewModel row
                when StringComparer.Ordinal.Equals(row.ProtocolFamily, DeviceProtocolFamilies.Viper):
                SelectDevice("viper");
                break;
            case PhilipsHueViewModel:
                SelectDevice("hue");
                await ViewModel.Hue.EnsureLoadedAsync(Lifetime);
                break;
        }
        RefreshSelectorHighlight();
    }
    private async void RazerHidKrakenSelectorClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RazerHidKrakenDeviceRowViewModel row }) return;
        await ViewModel.RazerHid.SelectKrakenAsync(row);
        SelectDevice("kraken");
        if (ViewModel.RazerHid.SelectedKraken is { } kraken)
            await kraken.LoadSerialAsync(Lifetime);
    }

    private async void BlackSharkSelectorClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: BlackSharkDeviceRowViewModel row }) return;
        SelectDevice("blackshark");
        await ViewModel.SelectBlackSharkAsync(row, Lifetime);
    }

    private async void RazerHidKrakenApplyClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.RazerHid.SelectedKraken?.ApplyAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidKrakenInstallThxClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidKraken?.InstallThxComponentsAsync(Lifetime) ?? Task.CompletedTask);

    private void RazerHidKrakenRefreshThxClick(object sender, RoutedEventArgs e) =>
        SelectedRazerHidKraken?.RefreshThx();

    private async void RazerHidKrakenOpenThxGameProfilesClick(object sender, RoutedEventArgs e)
    {
        if (SelectedRazerHidKraken is { ThxGameProfilesUri: { Length: > 0 } uri })
        {
            await Launcher.LaunchUriAsync(new Uri(uri));
        }
    }

    private async void RazerHidKrakenOpenThxPatcherClick(object sender, RoutedEventArgs e)
    {
        if (SelectedRazerHidKraken is { ThxPatcherUri: { Length: > 0 } uri })
        {
            await Launcher.LaunchUriAsync(new Uri(uri));
        }
    }

    private async void RazerHidKrakenDownloadThxClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidKraken?.InstallThxNativeAsync(Lifetime) ?? Task.CompletedTask);

    private RazerHidDeviceViewModel? SelectedRazerHidDevice => ViewModel.RazerHid.SelectedDevice;
    private RazerHidKrakenViewModel? SelectedRazerHidKraken => ViewModel.RazerHid.SelectedKraken;

    private async void RazerHidApplyPollingClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.ApplyPollingAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidApplyDpiClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.ApplyDpiAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidApplyDpiStagesClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.ApplyDpiStagesAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidApplyIdleClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.ApplyIdleTimeoutAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidApplyLowBatteryClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.ApplyLowBatteryThresholdAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidApplyAutoSleepClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.ApplyAutoSleepAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidApplyChargeLimiterClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.ApplyChargeLimiterAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidApplyTimeToDimClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.ApplyTimeToDimAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidApplyIndicatorLedClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.ApplyIndicatorLedAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidApplyIndicatorLedV2Click(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.ApplyIndicatorLedV2Async(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidApplyBatteryCurveClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.ApplyBatteryCurveAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidApplyGpuCardClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.ApplyGpuCardAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidApplyForceChromaClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.ApplyForceChromaAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidApplyChromaTapClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.ApplyChromaTapAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidApplyProfileDebounceClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.ApplyProfileDebounceAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidApplyLkScrollClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.ApplyLkScrollAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidReadMappingClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.LoadMappingAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidApplyMappingClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.ApplyMappingAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidApplyBrightnessClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.ApplyBrightnessAsync(Lifetime) ?? Task.CompletedTask);

    private void RazerHidBrightnessValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (SelectedRazerHidDevice is { } device && e.NewValue is >= 0 and <= 255)
        {
            device.Brightness = (byte)Math.Round(e.NewValue);
        }
    }

    private async void RazerHidApplyLightingClick(object sender, RoutedEventArgs e)
    {
        if (SelectedRazerHidDevice is not { } device)
        {
            return;
        }

        await device.ApplyLightingAsync(Lifetime);
        await device.ApplyBrightnessAsync(Lifetime);
        await device.ApplyLedStateAsync(Lifetime);
        if (device.HasWristRestLighting)
        {
            await device.ApplyWristRestAsync(Lifetime);
        }
    }

    private async void RazerHidApplyWristRestClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.ApplyWristRestAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidLightingSettingsToggled(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.ApplyLightingSettingsAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidApplyLedStateClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.ApplyLedStateAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidReactiveTriggerClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.TriggerReactiveAsync(Lifetime) ?? Task.CompletedTask);

    private void RazerHidMatrixCellClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RazerHidMatrixCellViewModel cell } button || cell.IsPlaceholder) return;
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
            Text = AppStrings.Text("LightingQuickColors/Text"),
            FontSize = 12,
        });
        var flyout = new Flyout { Content = content, ShouldConstrainToRootBounds = false };
        content.Children.Add(CreateMatrixColorSwatches(LightingPaletteColors, picker, flyout, 8));
        if (RecentLightingColors.Count > 0)
        {
            content.Children.Add(new TextBlock
            {
                Text = AppStrings.Text("LightingRecentColors/Text"),
                FontSize = 12,
            });
            content.Children.Add(CreateMatrixColorSwatches(RecentLightingColors, picker, flyout, 6));
        }
        content.Children.Add(new Expander
        {
            Header = AppStrings.Text("LightingCustomColor/Header"),
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

    private async void RazerHidApplyMatrixClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.ApplyMatrixAsync(Lifetime) ?? Task.CompletedTask);

    private void RazerHidFillMatrixClick(object sender, RoutedEventArgs e) =>
        SelectedRazerHidDevice?.FillMatrixWithPrimaryColor();

    private async void RazerHidLightingSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var device = SelectedRazerHidDevice;
        if (device?.IsLightingPowerProfileActive != true)
        {
            return;
        }

        await device.LoadLightingAsync(Lifetime);
    }

    private async void RazerHidScrollExpanding(Expander sender, ExpanderExpandingEventArgs args) =>
        await (SelectedRazerHidDevice?.LoadScrollAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidApplyScrollModeClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.ApplyScrollModeAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidApplyOnboardProfileClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.ApplyActiveOnboardProfileAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidApplyHapticScrollClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.ApplyHapticScrollAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidCreateOnboardProfileClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.CreateOnboardProfileAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidDeleteOnboardProfileClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.DeleteOnboardProfileAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidResetOnboardProfileClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.ResetOnboardProfileAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidRefreshWirelessClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.LoadWirelessAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidUnpairWirelessClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.UnpairWirelessAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidApplyKnobClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.ApplyKnobAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidApplyAnalogKeyClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.ApplyAnalogKeyAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidApplyOledClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.ApplyOledAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidApplyScrollFreeNotchedModeClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.ApplyScrollFreeNotchedModeAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidApplyScrollAccelerationLevelClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.ApplyScrollAccelerationLevelAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidApplyScrollAccelerationClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.ApplyScrollAccelerationAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidApplySmartReelClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.ApplySmartReelAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidApplySmartReelThresholdClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.ApplySmartReelThresholdAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidKeyswitchExpanding(Expander sender, ExpanderExpandingEventArgs args) =>
        await (SelectedRazerHidDevice?.LoadKeyswitchAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidApplyKeyswitchClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.ApplyKeyswitchAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidFnPrimaryClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.SetFnPrimaryAsync(true, Lifetime) ?? Task.CompletedTask);

    private async void RazerHidMediaPrimaryClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.SetFnPrimaryAsync(false, Lifetime) ?? Task.CompletedTask);

    private async void RazerHidApplyHyperPollingIndicatorClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.SetHyperPollingIndicatorAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidPairHyperPollingClick(object sender, RoutedEventArgs e) =>
        await ConfirmHyperPollingAsync(pair: true);

    private async void RazerHidReadOnboardMacrosClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.RazerHid.SelectedDevice?.LoadOnboardMacrosAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidWriteOnboardMacroClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.RazerHid.SelectedDevice?.WriteOnboardMacroAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidUnpairHyperPollingClick(object sender, RoutedEventArgs e) =>
        await ConfirmHyperPollingAsync(pair: false);

    private async Task ConfirmHyperPollingAsync(bool pair)
    {
        if (SelectedRazerHidDevice is not { } device) return;
        var productId = new TextBox
        {
            Header = AppStrings.Text("RazerHidMouseProductIdLabel"),
            PlaceholderText = "00B8",
            MaxLength = 6,
        };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = AppStrings.Text(pair ? "RazerHidPairTitle" : "RazerHidUnpairTitle"),
            Content = productId,
            PrimaryButtonText = AppStrings.Text(pair ? "RazerHidPairAction" : "RazerHidUnpairAction"),
            CloseButtonText = AppStrings.Text("Text_949856B3"),
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var input = productId.Text.Trim();
        if (input.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) input = input[2..];
        if (input.Length != 4 || !ushort.TryParse(input, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var mouseProductId))
        {
            ViewModel.ReportApplicationError(AppStrings.Text("RazerHidInvalidProductId"));
            return;
        }
        if (pair) await device.PairHyperPollingAsync(mouseProductId, Lifetime);
        else await device.UnpairHyperPollingAsync(mouseProductId, Lifetime);
    }
}
