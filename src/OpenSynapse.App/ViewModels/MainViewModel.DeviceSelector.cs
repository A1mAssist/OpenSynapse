using OpenSynapse.Core.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel
{
    private void RefreshDeviceSelectorItemsCore()
    {
        DeviceSelectorItems.Clear();
        if (!IsLiteMode &&
            _deviceDescriptors.Any(device => DeviceProtocolFamilies.IsBlade(device.ProtocolFamily)))
        {
            DeviceSelectorItems.Add(new DeviceSelectorItemViewModel(
                DeviceSelectorItemKind.Blade,
                Blade.DeviceName,
                "\uE7F8"));
        }
        if (Viper.DeviceVisibility == Microsoft.UI.Xaml.Visibility.Visible)
        {
            DeviceSelectorItems.Add(new DeviceSelectorItemViewModel(
                DeviceSelectorItemKind.Viper,
                Viper.DeviceName,
                "\uE962"));
        }

        foreach (var row in RazerHid.Devices.Where(row =>
                     row.Connection.IsReady && row.Connection.Capabilities.Count > 0))
        {
            DeviceSelectorItems.Add(new DeviceSelectorItemViewModel(
                DeviceSelectorItemKind.RazerHid,
                row.Name,
                row.IconGlyph,
                row));
        }

        foreach (var row in RazerHid.KrakenDevices.Where(row => row.Connection.IsReady))
        {
            DeviceSelectorItems.Add(new DeviceSelectorItemViewModel(
                DeviceSelectorItemKind.Kraken,
                row.Name,
                row.IconGlyph,
                row));
        }

        foreach (var row in BlackSharkDevices)
        {
            DeviceSelectorItems.Add(new DeviceSelectorItemViewModel(
                DeviceSelectorItemKind.BlackShark,
                row.Name,
                row.IconGlyph,
                row));
        }

        foreach (var row in Accessories.HeadsetAudioDevices)
        {
            DeviceSelectorItems.Add(new DeviceSelectorItemViewModel(
                DeviceSelectorItemKind.HeadsetAudio,
                row.Name,
                row.IconGlyph,
                row));
        }

        foreach (var row in Accessories.KeyboardDevices)
        {
            DeviceSelectorItems.Add(new DeviceSelectorItemViewModel(
                DeviceSelectorItemKind.Keyboard,
                row.Name,
                row.IconGlyph,
                row));
        }

        foreach (var row in Accessories.GamepadDevices)
        {
            DeviceSelectorItems.Add(new DeviceSelectorItemViewModel(
                DeviceSelectorItemKind.Gamepad,
                row.Name,
                row.IconGlyph,
                row));
        }

        if (CoolingPad.IsConnected)
        {
            DeviceSelectorItems.Add(new DeviceSelectorItemViewModel(
                DeviceSelectorItemKind.CoolingPad,
                CoolingPad.Name,
                CoolingPad.IconGlyph,
                CoolingPad));
        }

        if (Kiyo.IsConnected)
        {
            DeviceSelectorItems.Add(new DeviceSelectorItemViewModel(
                DeviceSelectorItemKind.Kiyo,
                Kiyo.Name,
                Kiyo.IconGlyph,
                Kiyo));
        }

        foreach (var row in Accessories.ChromaDevices.Where(row =>
                     !RazerHid.Devices.Any(hid =>
                         hid.Connection.Definition.ProductId == row.Connection.ProductId)))
        {
            DeviceSelectorItems.Add(new DeviceSelectorItemViewModel(
                DeviceSelectorItemKind.Chroma,
                row.Name,
                row.IconGlyph,
                row));
        }

        foreach (var row in BleDevices)
        {
            DeviceSelectorItems.Add(new DeviceSelectorItemViewModel(
                DeviceSelectorItemKind.Ble,
                row.Name,
                row.IconGlyph,
                row));
        }

        foreach (var row in AwBleHeadsetDevices)
        {
            DeviceSelectorItems.Add(new DeviceSelectorItemViewModel(
                DeviceSelectorItemKind.AwBleHeadset,
                row.Name,
                row.IconGlyph,
                row));
        }

        foreach (var row in ApBleHeadsetDevices)
        {
            DeviceSelectorItems.Add(new DeviceSelectorItemViewModel(
                DeviceSelectorItemKind.ApBleHeadset,
                row.Name,
                row.IconGlyph,
                row));
        }

        OnPropertyChanged(nameof(DeviceSelectorItems));
    }
}
