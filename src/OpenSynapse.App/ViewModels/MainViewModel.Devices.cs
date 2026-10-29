using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel
{
    private IReadOnlyList<ConnectedDeviceRowViewModel>? _connectedDevicesCache;

    public IReadOnlyList<ConnectedDeviceRowViewModel> ConnectedDevices =>
        _connectedDevicesCache ??= BuildConnectedDevices();

    private IReadOnlyList<ConnectedDeviceRowViewModel> BuildConnectedDevices() =>
        Devices.Select(device => new ConnectedDeviceRowViewModel(device))
            .Concat(OpenRazer.Devices.Select(device => new ConnectedDeviceRowViewModel(device)))
            .Concat(OpenRazer.KrakenDevices.Select(device => new ConnectedDeviceRowViewModel(device)))
            .Concat(BlackSharkDevices.Select(device => new ConnectedDeviceRowViewModel(device)))
            .Concat(HeadsetAudioDevices.Select(device => new ConnectedDeviceRowViewModel(device)))
            .Concat(KeyboardDevices.Select(device => new ConnectedDeviceRowViewModel(device)))
            .Concat(GamepadDevices.Select(device => new ConnectedDeviceRowViewModel(device)))
            // The Viper owns a native page and still answers Chroma discovery; its lighting page
            // opens from there, so list the device once.
            .Concat(ChromaDevices
                .Where(chroma => !Devices.Any(native => native.ProductId == chroma.Connection.ProductId))
                .Select(device => new ConnectedDeviceRowViewModel(device)))
            .Concat(BleDevices.Select(device => new ConnectedDeviceRowViewModel(device)))
            .Concat([new ConnectedDeviceRowViewModel(Hue)])
            .Where(device => device.IsVisible)
            .OrderByDescending(device => device.IsReady)
            .ThenBy(device => device.SortOrder)
            .ThenBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public IReadOnlyList<ConnectedDeviceRowViewModel> PrimaryDevices => ConnectedDevices.Take(2).ToArray();

    public IReadOnlyList<ConnectedDeviceRowViewModel> AdditionalDevices => ConnectedDevices.Skip(2).ToArray();

    public Visibility AdditionalDevicesVisibility =>
        AdditionalDevices.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

    private void RefreshDeviceOverviewVisibility()
    {
        _connectedDevicesCache = null;
        OnPropertyChanged(nameof(PrimaryDevices));
        OnPropertyChanged(nameof(AdditionalDevices));
        OnPropertyChanged(nameof(ConnectedDevices));
        Blade.RefreshDeviceOverview();
        OnPropertyChanged(nameof(ViperProtocolText));
        OnPropertyChanged(nameof(ViperIdentityText));
        OnPropertyChanged(nameof(AdditionalDevicesVisibility));
    }

    private void RefreshDeviceSelectorItems()
    {
        DeviceSelectorItems.Clear();
        if (_deviceDescriptors.Any(device => DeviceProtocolFamilies.IsBlade(device.ProtocolFamily)))
        {
            DeviceSelectorItems.Add(new DeviceSelectorItemViewModel(
                DeviceSelectorItemKind.Blade,
                Blade.DeviceName,
                "\uE7F8"));
        }
        if (ViperDeviceVisibility == Microsoft.UI.Xaml.Visibility.Visible)
        {
            DeviceSelectorItems.Add(new DeviceSelectorItemViewModel(
                DeviceSelectorItemKind.Viper,
                ViperDeviceName,
                "\uE962"));
        }

        foreach (var row in OpenRazer.Devices)
        {
            DeviceSelectorItems.Add(new DeviceSelectorItemViewModel(
                DeviceSelectorItemKind.OpenRazer,
                row.Name,
                row.IconGlyph,
                row));
        }

        foreach (var row in OpenRazer.KrakenDevices)
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

        foreach (var row in HeadsetAudioDevices)
        {
            DeviceSelectorItems.Add(new DeviceSelectorItemViewModel(
                DeviceSelectorItemKind.HeadsetAudio,
                row.Name,
                row.IconGlyph,
                row));
        }

        foreach (var row in KeyboardDevices)
        {
            DeviceSelectorItems.Add(new DeviceSelectorItemViewModel(
                DeviceSelectorItemKind.Keyboard,
                row.Name,
                row.IconGlyph,
                row));
        }

        foreach (var row in GamepadDevices)
        {
            DeviceSelectorItems.Add(new DeviceSelectorItemViewModel(
                DeviceSelectorItemKind.Gamepad,
                row.Name,
                row.IconGlyph,
                row));
        }

        foreach (var row in ChromaDevices)
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

        OnPropertyChanged(nameof(DeviceSelectorItems));
    }
}
