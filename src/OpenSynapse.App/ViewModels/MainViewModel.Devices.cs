using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel
{
    private readonly Dictionary<string, string> _deviceSerials = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<ConnectedDeviceRowViewModel>? _connectedDevicesCache;

    public IReadOnlyList<ConnectedDeviceRowViewModel> ConnectedDevices =>
        _connectedDevicesCache ??= BuildConnectedDevices();

    private IReadOnlyList<ConnectedDeviceRowViewModel> BuildConnectedDevices() =>
        Devices.Select(device => new ConnectedDeviceRowViewModel(device))
            .Concat(RazerHid.Devices.Select(device => new ConnectedDeviceRowViewModel(device)))
            .Concat(RazerHid.KrakenDevices.Select(device => new ConnectedDeviceRowViewModel(device)))
            .Concat(BlackSharkDevices.Select(device => new ConnectedDeviceRowViewModel(device)))
            .Concat(Accessories.HeadsetAudioDevices.Select(device => new ConnectedDeviceRowViewModel(device)))
            .Concat(Accessories.KeyboardDevices.Select(device => new ConnectedDeviceRowViewModel(device)))
            .Concat(Accessories.GamepadDevices.Select(device => new ConnectedDeviceRowViewModel(device)))
            .Concat(CoolingPad.IsConnected
                ? [new ConnectedDeviceRowViewModel(CoolingPad)]
                : [])
            .Concat(Kiyo.IsConnected
                ? [new ConnectedDeviceRowViewModel(Kiyo)]
                : [])
            // The Viper owns a native page and still answers Chroma discovery; its lighting page
            // opens from there, so list the device once.
            .Concat(Accessories.ChromaDevices
                .Where(chroma => !Devices.Any(native => native.ProductId == chroma.Connection.ProductId))
                .Select(device => new ConnectedDeviceRowViewModel(device)))
            .Concat(BleDevices.Select(device => new ConnectedDeviceRowViewModel(device)))
            .Concat(AwBleHeadsetDevices.Select(device => new ConnectedDeviceRowViewModel(device)))
            .Concat([new ConnectedDeviceRowViewModel(Hue)])
            .Where(device => device.IsVisible)
            // The laptop owns the Blade page and must stay in the first two cards even
            // when its 91-byte control channel is temporarily unavailable.
            .OrderBy(device => device.Category == DeviceCategory.Laptop ? 0 : device.IsReady ? 1 : 2)
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
        OnPropertyChanged(nameof(EmptyStateText));
        OnPropertyChanged(nameof(EmptyStateVisibility));
        Blade.RefreshDeviceOverview();
        Viper.RefreshDeviceRow();
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

        foreach (var row in Accessories.ChromaDevices)
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

        OnPropertyChanged(nameof(DeviceSelectorItems));
    }

    private async Task<string?> GetDeviceSerialAsync(DeviceDescriptor? device, CancellationToken cancellationToken)
    {
        if (device is not { Access: DeviceAccessState.Available }) return null;
        if (_deviceSerials.TryGetValue(device.Id, out var serial)) return serial;
        serial = await WindowsHidDiscovery.TryReadSerialNumberAsync(device.Id, cancellationToken);
        if (!string.IsNullOrWhiteSpace(serial)) _deviceSerials[device.Id] = serial;
        return serial;
    }

    private async Task RefreshAttachedDevicesAsync(CancellationToken cancellationToken)
    {
        Diagnostics.CaptureAccessorySnapshot();
        // One unplugged or unresponsive accessory must not abort the whole refresh:
        // that used to surface as a generic probe failure and tear down unrelated device pages.
        await RefreshAccessoryAsync("blackshark", RefreshBlackSharkAsync, cancellationToken);
        await RefreshAccessoryAsync("headset-audio", Accessories.RefreshHeadsetAudioAsync, cancellationToken);
        await RefreshAccessoryAsync("keyboard", Accessories.RefreshKeyboardsAsync, cancellationToken);
        await RefreshAccessoryAsync("gamepad", Accessories.RefreshGamepadsAsync, cancellationToken);
        await RefreshAccessoryAsync("cooling-pad", CoolingPad.RefreshAsync, cancellationToken);
        await RefreshAccessoryAsync("kiyo", Kiyo.RefreshAsync, cancellationToken);
        await RefreshAccessoryAsync("chroma", Accessories.RefreshChromaAsync, cancellationToken);
        await RefreshAccessoryAsync("ble", RefreshBleAsync, cancellationToken);
        await RefreshAccessoryAsync("aw-ble", RefreshAwBleAsync, cancellationToken);
    }

    private async Task RefreshAccessoryAsync(
        string label,
        Func<CancellationToken, Task> refresh,
        CancellationToken cancellationToken)
    {
        try
        {
            await refresh(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _diagnosticLog.TryWrite("devices", $"{label} refresh failed: {exception}");
        }
    }

    private IEnumerable<AccessoryDiagnostic> ReadAccessoryDiagnostics()
    {
        static string? Error(string path, string? selectedPath, string? error) =>
            StringComparer.OrdinalIgnoreCase.Equals(path, selectedPath) ? error : null;
        foreach (var row in BlackSharkDevices)
            yield return new("blackshark:" + row.Connection.DevicePath, row.Name, "BlackShark",
                row.Identity + " | " + row.Connection.DevicePath, row.Status,
                Error(row.Connection.DevicePath, SelectedBlackShark?.DevicePath, SelectedBlackShark?.ErrorText));
        foreach (var row in Accessories.HeadsetAudioDevices)
            yield return new("headset-audio:" + row.Connection.DevicePath, row.Name, "HeadsetAudio",
                row.Identity + " | " + row.Connection.DevicePath, row.Status,
                Error(row.Connection.DevicePath, Accessories.SelectedHeadsetAudio?.DevicePath,
                    string.Join(Environment.NewLine, new[] { Accessories.SelectedHeadsetAudio?.ErrorText,
                        Accessories.SelectedHeadsetAudio?.Mixer053E?.ErrorText }.Where(error => !string.IsNullOrWhiteSpace(error)))));
        foreach (var row in Accessories.KeyboardDevices)
            yield return new("keyboard:" + row.Connection.DevicePath, row.Name, "Keyboard",
                row.Identity + " | " + row.Connection.DevicePath, row.Status,
                Error(row.Connection.DevicePath, Accessories.SelectedKeyboard?.DevicePath, Accessories.SelectedKeyboard?.ErrorText));
        foreach (var row in Accessories.GamepadDevices)
            yield return new("gamepad:" + row.Connection.DevicePath, row.Name, "Gamepad",
                row.Identity + " | " + row.Connection.DevicePath, row.Status,
                Error(row.Connection.DevicePath, Accessories.SelectedGamepad?.DevicePath, Accessories.SelectedGamepad?.ErrorText));
        foreach (var row in Accessories.ChromaDevices)
            yield return new("chroma:" + row.Connection.DevicePath, row.Name, "Chroma",
                row.Identity + " | " + row.Connection.DevicePath, row.Status,
                Error(row.Connection.DevicePath, Accessories.SelectedChroma?.DevicePath, Accessories.SelectedChroma?.ErrorText));
        foreach (var row in BleDevices)
            yield return new("ble:" + row.Device.Id, row.Name, "BLE", row.Identity + " | " + row.Device.Id, row.Status,
                Error(row.Device.Id, SelectedBle?.DeviceId, SelectedBle?.ErrorText));
        foreach (var row in AwBleHeadsetDevices)
            yield return new("aw-ble:" + row.Device.Id, row.Name, "AudioAWKittyBLE", row.Identity + " | " + row.Device.Id, row.Status,
                Error(row.Device.Id, SelectedAwBleHeadset?.Device.Id, SelectedAwBleHeadset?.ErrorText));
        if (CoolingPad.IsConnected || CoolingPad.HasError)
            yield return new("cooling-pad", CoolingPad.Name, "CoolingPad", CoolingPad.Identity, CoolingPad.StatusText, CoolingPad.ErrorText);
        if (Kiyo.IsConnected || Kiyo.HasError)
            yield return new("kiyo", Kiyo.Name, "Kiyo", Kiyo.Identity, Kiyo.StatusText, Kiyo.ErrorText);
        if (Hue.IsConfigured)
            yield return new("hue:" + Hue.BridgeHost, "Philips Hue", "Hue", Hue.DeviceIdentity, Hue.DeviceStatusText, Hue.ErrorText);
    }
}
