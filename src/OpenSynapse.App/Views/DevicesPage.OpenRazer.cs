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

    private void ConnectedDeviceCardClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
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
            case BlackSharkDeviceRowViewModel:
                BlackSharkSelectorClick(sender, e);
                break;
            case RazerHeadsetAudioDeviceRowViewModel:
                HeadsetAudioSelectorClick(sender, e);
                break;
            case RazerKeyboardDeviceRowViewModel:
                KeyboardSelectorClick(sender, e);
                break;
            case RazerGamepadDeviceRowViewModel:
                GamepadSelectorClick(sender, e);
                break;
            case RazerChromaDeviceRowViewModel:
                ChromaSelectorClick(sender, e);
                break;
            case RazerBleDeviceRowViewModel:
                BleSelectorClick(sender, e);
                break;
            case PhilipsHueViewModel:
                HueSelectorClick(sender, e);
                break;
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

    private async void OpenRazerDeviceSelectorClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: OpenRazerDeviceRowViewModel row })
        {
            return;
        }

        SelectDevice("openrazer");
        await ViewModel.OpenRazer.SelectDeviceAsync(row, Lifetime);
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
            case OpenRazerDeviceRowViewModel row:
                SelectDevice("openrazer");
                await ViewModel.OpenRazer.SelectDeviceAsync(row, Lifetime);
                break;
            case OpenRazerKrakenDeviceRowViewModel row:
                SelectDevice("kraken");
                await ViewModel.OpenRazer.SelectKrakenAsync(row);
                if (ViewModel.OpenRazer.SelectedKraken is { } kraken)
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
                await ViewModel.SelectGamepadAsync(row, Lifetime);
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
    }
    private async void OpenRazerKrakenSelectorClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: OpenRazerKrakenDeviceRowViewModel row }) return;
        await ViewModel.OpenRazer.SelectKrakenAsync(row);
        SelectDevice("kraken");
        if (ViewModel.OpenRazer.SelectedKraken is { } kraken)
            await kraken.LoadSerialAsync(Lifetime);
    }

    private async void BlackSharkSelectorClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: BlackSharkDeviceRowViewModel row }) return;
        SelectDevice("blackshark");
        await ViewModel.SelectBlackSharkAsync(row, Lifetime);
    }

    private async void OpenRazerKrakenApplyClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.OpenRazer.SelectedKraken?.ApplyAsync(Lifetime) ?? Task.CompletedTask);

    private async void OpenRazerKrakenInstallThxClick(object sender, RoutedEventArgs e)
    {
        if (SelectedOpenRazerKraken is { ThxInstallerUri: { Length: > 0 } uri })
        {
            await Launcher.LaunchUriAsync(new Uri(uri));
        }
    }

    private void OpenRazerKrakenRefreshThxClick(object sender, RoutedEventArgs e) =>
        SelectedOpenRazerKraken?.RefreshThx();

    private async void OpenRazerKrakenOpenThxGameProfilesClick(object sender, RoutedEventArgs e)
    {
        if (SelectedOpenRazerKraken is { ThxGameProfilesUri: { Length: > 0 } uri })
        {
            await Launcher.LaunchUriAsync(new Uri(uri));
        }
    }

    private async void OpenRazerKrakenOpenThxPatcherClick(object sender, RoutedEventArgs e)
    {
        if (SelectedOpenRazerKraken is { ThxPatcherUri: { Length: > 0 } uri })
        {
            await Launcher.LaunchUriAsync(new Uri(uri));
        }
    }

    private async void OpenRazerKrakenDownloadThxClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerKraken?.InstallThxNativeAsync(Lifetime) ?? Task.CompletedTask);

    private OpenRazerDeviceViewModel? SelectedOpenRazerDevice => ViewModel.OpenRazer.SelectedDevice;
    private OpenRazerKrakenViewModel? SelectedOpenRazerKraken => ViewModel.OpenRazer.SelectedKraken;

    private async void OpenRazerApplyPollingClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyPollingAsync(Lifetime) ?? Task.CompletedTask);

    private async void OpenRazerApplyDpiClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyDpiAsync(Lifetime) ?? Task.CompletedTask);

    private async void OpenRazerApplyDpiStagesClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyDpiStagesAsync(Lifetime) ?? Task.CompletedTask);

    private async void OpenRazerApplyIdleClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyIdleTimeoutAsync(Lifetime) ?? Task.CompletedTask);

    private async void OpenRazerApplyLowBatteryClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyLowBatteryThresholdAsync(Lifetime) ?? Task.CompletedTask);

    private async void OpenRazerApplyAutoSleepClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyAutoSleepAsync(Lifetime) ?? Task.CompletedTask);

    private async void OpenRazerApplyChargeLimiterClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyChargeLimiterAsync(Lifetime) ?? Task.CompletedTask);

    private async void OpenRazerApplyTimeToDimClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyTimeToDimAsync(Lifetime) ?? Task.CompletedTask);

    private async void OpenRazerApplyIndicatorLedClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyIndicatorLedAsync(Lifetime) ?? Task.CompletedTask);

    private async void OpenRazerApplyBatteryCurveClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyBatteryCurveAsync(Lifetime) ?? Task.CompletedTask);

    private async void OpenRazerApplyGpuCardClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyGpuCardAsync(Lifetime) ?? Task.CompletedTask);

    private async void OpenRazerApplyForceChromaClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyForceChromaAsync(Lifetime) ?? Task.CompletedTask);

    private async void OpenRazerApplyChromaTapClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyChromaTapAsync(Lifetime) ?? Task.CompletedTask);

    private async void OpenRazerApplyProfileDebounceClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyProfileDebounceAsync(Lifetime) ?? Task.CompletedTask);

    private async void OpenRazerApplyLkScrollClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyLkScrollAsync(Lifetime) ?? Task.CompletedTask);

    private async void OpenRazerReadMappingClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.LoadMappingAsync(Lifetime) ?? Task.CompletedTask);

    private async void OpenRazerApplyMappingClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyMappingAsync(Lifetime) ?? Task.CompletedTask);

    private async void OpenRazerApplyBrightnessClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyBrightnessAsync(Lifetime) ?? Task.CompletedTask);

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

        await device.ApplyLightingAsync(Lifetime);
        await device.ApplyBrightnessAsync(Lifetime);
        await device.ApplyLedStateAsync(Lifetime);
    }

    private async void OpenRazerLightingSettingsToggled(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyLightingSettingsAsync(Lifetime) ?? Task.CompletedTask);

    private async void OpenRazerApplyLedStateClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyLedStateAsync(Lifetime) ?? Task.CompletedTask);

    private async void OpenRazerReactiveTriggerClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.TriggerReactiveAsync(Lifetime) ?? Task.CompletedTask);

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

    private async void OpenRazerApplyMatrixClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyMatrixAsync(Lifetime) ?? Task.CompletedTask);

    private async void OpenRazerLightingSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var device = SelectedOpenRazerDevice;
        if (device?.IsLightingPowerProfileActive != true)
        {
            return;
        }

        await device.LoadLightingAsync(Lifetime);
    }

    private async void OpenRazerScrollExpanding(Expander sender, ExpanderExpandingEventArgs args) =>
        await (SelectedOpenRazerDevice?.LoadScrollAsync(Lifetime) ?? Task.CompletedTask);

    private async void OpenRazerApplyScrollModeClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyScrollModeAsync(Lifetime) ?? Task.CompletedTask);

    private async void OpenRazerApplyOnboardProfileClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyActiveOnboardProfileAsync(Lifetime) ?? Task.CompletedTask);

    private async void OpenRazerApplyHapticScrollClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyHapticScrollAsync(Lifetime) ?? Task.CompletedTask);

    private async void OpenRazerCreateOnboardProfileClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.CreateOnboardProfileAsync(Lifetime) ?? Task.CompletedTask);

    private async void OpenRazerDeleteOnboardProfileClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.DeleteOnboardProfileAsync(Lifetime) ?? Task.CompletedTask);

    private async void OpenRazerResetOnboardProfileClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ResetOnboardProfileAsync(Lifetime) ?? Task.CompletedTask);

    private async void OpenRazerRefreshWirelessClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.LoadWirelessAsync(Lifetime) ?? Task.CompletedTask);

    private async void OpenRazerUnpairWirelessClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.UnpairWirelessAsync(Lifetime) ?? Task.CompletedTask);

    private async void OpenRazerApplyKnobClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyKnobAsync(Lifetime) ?? Task.CompletedTask);

    private async void OpenRazerApplyAnalogKeyClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyAnalogKeyAsync(Lifetime) ?? Task.CompletedTask);

    private async void OpenRazerApplyOledClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyOledAsync(Lifetime) ?? Task.CompletedTask);

    private async void OpenRazerApplyScrollFreeNotchedModeClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyScrollFreeNotchedModeAsync(Lifetime) ?? Task.CompletedTask);

    private async void OpenRazerApplyScrollAccelerationLevelClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyScrollAccelerationLevelAsync(Lifetime) ?? Task.CompletedTask);

    private async void OpenRazerApplyScrollAccelerationClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyScrollAccelerationAsync(Lifetime) ?? Task.CompletedTask);

    private async void OpenRazerApplySmartReelClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplySmartReelAsync(Lifetime) ?? Task.CompletedTask);

    private async void OpenRazerApplySmartReelThresholdClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplySmartReelThresholdAsync(Lifetime) ?? Task.CompletedTask);

    private async void OpenRazerKeyswitchExpanding(Expander sender, ExpanderExpandingEventArgs args) =>
        await (SelectedOpenRazerDevice?.LoadKeyswitchAsync(Lifetime) ?? Task.CompletedTask);

    private async void OpenRazerApplyKeyswitchClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.ApplyKeyswitchAsync(Lifetime) ?? Task.CompletedTask);

    private async void OpenRazerFnPrimaryClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.SetFnPrimaryAsync(true, Lifetime) ?? Task.CompletedTask);

    private async void OpenRazerMediaPrimaryClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.SetFnPrimaryAsync(false, Lifetime) ?? Task.CompletedTask);

    private async void OpenRazerApplyHyperPollingIndicatorClick(object sender, RoutedEventArgs e) =>
        await (SelectedOpenRazerDevice?.SetHyperPollingIndicatorAsync(Lifetime) ?? Task.CompletedTask);

    private async void OpenRazerPairHyperPollingClick(object sender, RoutedEventArgs e) =>
        await ConfirmHyperPollingAsync(pair: true);

    private async void OpenRazerReadOnboardMacrosClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.OpenRazer.SelectedDevice?.LoadOnboardMacrosAsync(Lifetime) ?? Task.CompletedTask);

    private async void OpenRazerWriteOnboardMacroClick(object sender, RoutedEventArgs e) =>
        await (ViewModel.OpenRazer.SelectedDevice?.WriteOnboardMacroAsync(Lifetime) ?? Task.CompletedTask);

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
            XamlRoot = XamlRoot,
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
            ViewModel.ReportApplicationError(AppStrings.Text("OpenRazerInvalidProductId"));
            return;
        }
        if (pair) await device.PairHyperPollingAsync(mouseProductId, Lifetime);
        else await device.UnpairHyperPollingAsync(mouseProductId, Lifetime);
    }
}
