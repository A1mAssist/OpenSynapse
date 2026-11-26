using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using OpenSynapse.App.ViewModels;
using OpenSynapse.App.Views.DevicePanels;
using OpenSynapse.Core.Devices;
using Windows.System;
using Windows.Storage.Pickers;

namespace OpenSynapse.App.Views;

public sealed partial class DevicesPage : UserControl
{
    private void OpenBladeFanCurveWindowClick(object sender, RoutedEventArgs e) =>
        Host.OpenBladeFanCurveWindow();

    private void OpenThxAudioWindowClick(object sender, RoutedEventArgs e) =>
        Host.OpenThxAudioWindow();

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

        SelectDevice("razer-hid");
        await ViewModel.RazerHid.SelectDeviceAsync(row, Lifetime);
        await SyncChromaLightingAsync();
        UpdateDeviceSelector("razer-hid");
    }

    private async void DeviceSelectorItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DeviceSelectorItemViewModel item })
        {
            return;
        }

        await SelectDeviceSourceAsync(item.Source);
    }

    private async void PairNewDeviceClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.RazerHid.DeviceService is not { } service) return;
        var vm = new RazerPairingDialogViewModel(ViewModel.RazerHid.Devices, service);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = AppStrings.Text("RazerDualDongleTitle"),
            PrimaryButtonText = AppStrings.Text("RazerDualDongleStart"),
            CloseButtonText = AppStrings.Text("Text_949856B3"),
            DefaultButton = ContentDialogButton.Primary,
            Content = new RazerPairingDialog { DataContext = vm },
        };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            // Keep the dialog open while pairing; Hide() closes it on success.
            args.Cancel = true;
            if (!vm.CanStart) return;
            if (await vm.StartAsync(Lifetime))
            {
                dialog.Hide();
            }
        };
        await dialog.ShowAsync();
        await ViewModel.Refresh.RefreshDevicesAsync(Lifetime);
    }

    private async Task SelectDeviceSourceAsync(object? source)
    {
        if (source is RazerHidDeviceRowViewModel applicationRow)
        {
            ViewModel.DeviceRules.Select(new OpenSynapse.Core.Profiles.DeviceBindingKey(
                0x1532, applicationRow.Connection.Definition.ProductId, applicationRow.Connection.InstanceId));
            ApplicationRulesPanel.BindDeviceRules();
        }
        switch (source)
        {
            case RazerHidDeviceRowViewModel row:
                SelectDevice("razer-hid");
                ViewModel.DeviceRules.Select(new OpenSynapse.Core.Profiles.DeviceBindingKey(
                    0x1532, row.Connection.Definition.ProductId, row.Connection.InstanceId));
                await ViewModel.RazerHid.SelectDeviceAsync(row, Lifetime);
                await SyncChromaLightingAsync();
                break;
            case RazerHidKrakenDeviceRowViewModel row:
                SelectDevice("kraken");
                await ViewModel.RazerHid.SelectKrakenAsync(row);
                if (ViewModel.RazerHid.SelectedKraken is { } kraken)
                {
                    await kraken.LoadSerialAsync(Lifetime);
                    await kraken.RefreshThxAsync(Lifetime);
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
            case RazerApBleHeadsetRowViewModel row:
                SelectDevice("apbleheadset");
                await ViewModel.SelectApBleHeadsetAsync(row, Lifetime);
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
        {
            await kraken.LoadSerialAsync(Lifetime);
            await kraken.RefreshThxAsync(Lifetime);
        }
    }

    private async void BlackSharkSelectorClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: BlackSharkDeviceRowViewModel row }) return;
        SelectDevice("blackshark");
        await ViewModel.SelectBlackSharkAsync(row, Lifetime);
    }

    private async void RazerHidKrakenApplyClick(object sender, RoutedEventArgs e)
    {
        await SuspendStudioLightingAsync();
        await (ViewModel.RazerHid.SelectedKraken?.ApplyAsync(Lifetime) ?? Task.CompletedTask);
    }

    private async void RazerHidKrakenInstallThxClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidKraken?.InstallThxComponentsAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidKrakenRefreshThxClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidKraken?.RefreshThxAsync(Lifetime) ?? Task.CompletedTask);

    private RazerHidDeviceViewModel? SelectedRazerHidDevice =>
        (DataContext as MainViewModel)?.RazerHid?.SelectedDevice;
    private RazerHidKrakenViewModel? SelectedRazerHidKraken =>
        (DataContext as MainViewModel)?.RazerHid?.SelectedKraken;

    // TwoWay NumberBox bindings write their default value back when the device
    // panel first attaches, which overwrote the DPI read from the device.
    private void RazerHidDpiXChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (sender.DataContext is RazerHidDeviceViewModel device && !double.IsNaN(args.NewValue))
        {
            device.DpiX = (int)Math.Round(args.NewValue);
        }
    }

    private void RazerHidDpiYChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (sender.DataContext is RazerHidDeviceViewModel device && !double.IsNaN(args.NewValue))
        {
            device.DpiY = (int)Math.Round(args.NewValue);
        }
    }

    private async void RazerHidApplySensorRotationClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.ApplySensorRotationAsync(Lifetime) ?? Task.CompletedTask);

    private void RazerHidCalibrateSensorRotationClick(object sender, RoutedEventArgs e) =>
        SelectedRazerHidDevice?.BeginRotationCalibration();


    private async void RazerHidSwitchDpiStageClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RazerHidDpiStageRowViewModel row } &&
            SelectedRazerHidDevice is { } device)
        {
            await device.SwitchDpiStageAsync(row.Number, Lifetime);
        }
    }

    private void RazerHidIndicatorLedV2Loaded(object sender, RoutedEventArgs e) =>
        QueueIndicatorLedV2Reapply(sender);

    private void RazerHidIndicatorLedV2DataContextChanged(
        FrameworkElement sender,
        DataContextChangedEventArgs args) =>
        QueueIndicatorLedV2Reapply(sender);

    private void QueueIndicatorLedV2Reapply(object sender)
    {
        if (sender is not FrameworkElement { DataContext: RazerHidDeviceViewModel device }) return;
        // Let the card finish its first measure/bind pass before restoring the read values.
        DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            device.ReapplyIndicatorLedV2Selection);
    }

    private async void RazerHidApplyGpuCardClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.ApplyGpuCardAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidApplyForceChromaClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.ApplyForceChromaAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidApplyChromaTapClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.ApplyChromaTapAsync(Lifetime) ?? Task.CompletedTask);

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

        await SuspendStudioLightingAsync();
        await device.ApplyLightingAsync(Lifetime);
        await device.ApplyBrightnessAsync(Lifetime);
        await device.ApplyLedStateAsync(Lifetime);
        if (device.HasWristRestLighting)
        {
            await device.ApplyWristRestAsync(Lifetime);
        }
    }

    private async void RazerHidImportChromaClick(object sender, RoutedEventArgs e)
    {
        if (SelectedRazerHidDevice is not { } device)
        {
            return;
        }

        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add(".chroma");
        Host.InitializePicker(picker);
        var file = await picker.PickSingleFileAsync();
        if (file is null || string.IsNullOrWhiteSpace(file.Path))
        {
            return;
        }

        try
        {
            await SuspendStudioLightingAsync();
            await device.ImportChromaAnimationAsync(file.Path, Lifetime);
        }
        catch (OperationCanceledException) when (Lifetime.IsCancellationRequested) { }
        catch (Exception exception) when (RuntimeErrors.IsExpected(exception))
        {
            ViewModel.ReportApplicationError(exception.Message);
        }
    }

    private async void RazerHidApplyWristRestClick(object sender, RoutedEventArgs e)
    {
        if (SelectedRazerHidDevice is not { } device)
        {
            return;
        }

        await SuspendStudioLightingAsync();
        await device.ApplyWristRestAsync(Lifetime);
    }

    private async void RazerHidLightingSettingsToggled(object sender, RoutedEventArgs e)
    {
        if (SelectedRazerHidDevice is not { } device)
        {
            return;
        }

        await SuspendStudioLightingAsync();
        await device.ApplyLightingSettingsAsync(Lifetime);
    }

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
        if (Visibility != Visibility.Visible)
        {
            return;
        }

        await ReloadSelectedRazerHidLightingAsync();
    }

    private async Task ReloadSelectedRazerHidLightingAsync()
    {
        if (Host is null ||
            SelectedRazerHidDevice is not { IsLightingPowerProfileActive: true } device)
        {
            return;
        }

        try
        {
            await device.LoadLightingAsync(Lifetime);
        }
        catch (OperationCanceledException) when (Lifetime.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            // A rescan can replace the device while the combo box is updating;
            // the next visible selection reloads. Never let that race kill the UI.
        }
    }

    private async void RazerHidScrollExpanding(Expander sender, ExpanderExpandingEventArgs args) =>
        await (SelectedRazerHidDevice?.LoadScrollAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidApplyScrollWheelModeClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.ApplySelectedScrollWheelModeAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidApplyScrollModeClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.ApplyScrollModeAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidApplyOnboardProfileClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.ApplyActiveOnboardProfileAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidApplyHapticScrollClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.ApplyHapticScrollAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidCreateOnboardProfileClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.CreateOnboardProfileAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidDeleteOnboardProfileClick(object sender, RoutedEventArgs e) =>
        await ConfirmDeleteOnboardProfileAsync();

    private async void RazerHidResetOnboardProfileClick(object sender, RoutedEventArgs e) =>
        await ConfirmResetOnboardProfileAsync();

    private async Task ConfirmDeleteOnboardProfileAsync()
    {
        if (!await ConfirmOnboardProfileAsync(delete: true)) return;
        await (SelectedRazerHidDevice?.DeleteOnboardProfileAsync(Lifetime) ?? Task.CompletedTask);
    }

    private async Task ConfirmResetOnboardProfileAsync()
    {
        if (!await ConfirmOnboardProfileAsync(delete: false)) return;
        await (SelectedRazerHidDevice?.ResetOnboardProfileAsync(Lifetime) ?? Task.CompletedTask);
    }

    private async Task<bool> ConfirmOnboardProfileAsync(bool delete)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = AppStrings.Text(delete
                ? "RazerHidOnboardProfileDeleteTitle"
                : "RazerHidOnboardProfileResetTitle"),
            Content = AppStrings.Text(delete
                ? "RazerHidOnboardProfileDeleteBody"
                : "RazerHidOnboardProfileResetBody"),
            PrimaryButtonText = AppStrings.Text(delete
                ? "RazerHidOnboardProfileDeleteAction"
                : "RazerHidOnboardProfileResetAction"),
            CloseButtonText = AppStrings.Text("Text_949856B3"),
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async void RazerHidRefreshWirelessClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.LoadWirelessAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidUnpairWirelessClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.UnpairWirelessAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidApplyScrollFreeNotchedModeClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.ApplyScrollFreeNotchedModeAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidApplyScrollAccelerationLevelClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.ApplyScrollAccelerationLevelAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidApplyScrollAccelerationClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.ApplyScrollAccelerationAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidApplySmartReelThresholdClick(object sender, RoutedEventArgs e) =>
        await (SelectedRazerHidDevice?.ApplySmartReelThresholdAsync(Lifetime) ?? Task.CompletedTask);

    private async void RazerHidKeyswitchExpanding(Expander sender, ExpanderExpandingEventArgs args) =>
        await (SelectedRazerHidDevice?.LoadKeyswitchAsync(Lifetime) ?? Task.CompletedTask);

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
