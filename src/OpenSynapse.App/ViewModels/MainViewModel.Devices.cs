using System.Collections.Concurrent;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel
{
    private readonly Dictionary<string, string> _deviceSerials = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<ushort, string> _deviceImagePaths = new();
    private readonly ConcurrentDictionary<ushort, bool> _deviceImageRequests = new();
    private IReadOnlyList<ConnectedDeviceRowViewModel>? _connectedDevicesCache;
    private StudioDeviceVisualLoader? _deviceImages;

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
            // The laptop owns the Blade page and must stay in the first cards even
            // when its 91-byte control channel is temporarily unavailable.
            .OrderBy(device => device.Category == DeviceCategory.Laptop ? 0 : device.IsReady ? 1 : 2)
            .ThenBy(device => device.SortOrder)
            .ThenBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    // Four cards fit a 1080p window without hiding the page controls behind "more".
    public IReadOnlyList<ConnectedDeviceRowViewModel> PrimaryDevices => ConnectedDevices.Take(4).ToArray();

    public IReadOnlyList<ConnectedDeviceRowViewModel> AdditionalDevices => ConnectedDevices.Skip(4).ToArray();

    public bool CanPairNewDevice =>
        !IsLiteMode && RazerHid.Devices.Any(device => device.CanPairNewDevice);

    public Visibility AdditionalDevicesVisibility =>
        AdditionalDevices.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

    private bool _batteryPrimeRunning;
    private bool _batteryPrimeRequested;

    /// <summary>
    /// The cards read every source that already has no percentage: RazerHid, BLE,
    /// AudioAW BLE, headsets, BlackShark and standalone keyboards. The page calls this
    /// again when it is shown or a device collection changes, so discovery races cannot
    /// leave a card permanently batteryless. Failures keep the last value.
    /// </summary>
    internal async Task PrimeDeviceBatteriesAsync(CancellationToken cancellationToken)
    {
        if (_batteryPrimeRunning)
        {
            _batteryPrimeRequested = true;
            return;
        }

        _batteryPrimeRunning = true;
        try
        {
            do
            {
                _batteryPrimeRequested = false;
                await PrimeRazerHidBatteriesAsync(cancellationToken);
                await PrimeBleBatteriesAsync(cancellationToken);
                await PrimeAwBleBatteriesAsync(cancellationToken);
                await PrimeAccessoryBatteriesAsync(cancellationToken);
            }
            while (_batteryPrimeRequested && !cancellationToken.IsCancellationRequested);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            _batteryPrimeRunning = false;
        }
    }

    private async Task PrimeRazerHidBatteriesAsync(CancellationToken cancellationToken)
    {
        foreach (var row in RazerHid.Devices.Where(row =>
                     row.BatteryPercent is null &&
                     row.Connection.Capabilities.Contains(RazerHidBackendCapability.BatteryRead)).ToArray())
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(1));
                await RazerHid.ReadBatteryPercentAsync(row, timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // A silent device keeps the card without a battery number.
            }
        }
    }

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
        OnPropertyChanged(nameof(CanPairNewDevice));
        // Card art comes from the same product-image cache the lighting studio fills.
        ApplyDeviceImages();
    }

    private void ApplyDeviceImages()
    {
        foreach (var row in ConnectedDevices)
        {
            var productId = row.ProductId;
            if (productId == 0)
            {
                continue;
            }

            if (_deviceImagePaths.TryGetValue(productId, out var path))
            {
                row.SetProductImage(new BitmapImage(new Uri(path)));
            }
            else if (_deviceImageRequests.TryAdd(productId, true))
            {
                _ = LoadDeviceImageAsync(productId);
            }
        }
    }

    private async Task LoadDeviceImageAsync(ushort productId)
    {
        _deviceImages ??= new StudioDeviceVisualLoader();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var image = await _deviceImages
                .LoadProductImageAsync(productId, name: null, timeout.Token)
                .ConfigureAwait(false);
            if (image.State != StudioProductImageState.Loaded || image.Path is not { } path)
            {
                return;
            }

            // BitmapImage must be created on the UI thread. Reuse the loaded path so a
            // later card rebuild paints immediately instead of downloading again.
            _dispatcher?.TryEnqueue(() =>
            {
                _deviceImagePaths[productId] = path;
                foreach (var row in ConnectedDevices.Where(row => row.ProductId == productId))
                {
                    row.SetProductImage(new BitmapImage(new Uri(path)));
                }
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (
            exception is IOException or HttpRequestException or
                InvalidOperationException or UriFormatException)
        {
        }
        finally
        {
            _deviceImageRequests.TryRemove(productId, out _);
        }
    }

    private void RefreshDeviceSelectorItems()
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
            yield return new("blackshark:" + row.Connection.DevicePath, row.Name,
                AppStrings.Text("BlackSharkHeadsetCategory"),
                row.Identity + " | " + row.Connection.DevicePath, row.Status,
                Error(row.Connection.DevicePath, SelectedBlackShark?.DevicePath, SelectedBlackShark?.ErrorText));
        foreach (var row in Accessories.HeadsetAudioDevices)
            yield return new("headset-audio:" + row.Connection.DevicePath, row.Name,
                AppStrings.Text("RazerHidHeadsetCategory"),
                row.Identity + " | " + row.Connection.DevicePath, row.Status,
                Error(row.Connection.DevicePath, Accessories.SelectedHeadsetAudio?.DevicePath,
                    string.Join(Environment.NewLine, new[] { Accessories.SelectedHeadsetAudio?.ErrorText,
                        Accessories.SelectedHeadsetAudio?.Mixer053E?.ErrorText }.Where(error => !string.IsNullOrWhiteSpace(error)))));
        foreach (var row in Accessories.KeyboardDevices)
            yield return new("keyboard:" + row.Connection.DevicePath, row.Name,
                AppStrings.Text("RazerKeyboardCategory"),
                row.Identity + " | " + row.Connection.DevicePath, row.Status,
                Error(row.Connection.DevicePath, Accessories.SelectedKeyboard?.DevicePath, Accessories.SelectedKeyboard?.ErrorText));
        foreach (var row in Accessories.GamepadDevices)
            yield return new("gamepad:" + row.Connection.DevicePath, row.Name,
                AppStrings.Text("RazerGamepadCategory"),
                row.Identity + " | " + row.Connection.DevicePath, row.Status,
                Error(row.Connection.DevicePath, Accessories.SelectedGamepad?.DevicePath, Accessories.SelectedGamepad?.ErrorText));
        foreach (var row in Accessories.ChromaDevices)
            yield return new("chroma:" + row.Connection.DevicePath, row.Name, "Chroma",
                row.Identity + " | " + row.Connection.DevicePath, row.Status,
                Error(row.Connection.DevicePath, Accessories.SelectedChroma?.DevicePath, Accessories.SelectedChroma?.ErrorText));
        foreach (var row in BleDevices)
            yield return new("ble:" + row.Device.Id, row.Name,
                AppStrings.Text("LeviathanBluetooth"), row.Identity + " | " + row.Device.Id, row.Status,
                Error(row.Device.Id, SelectedBle?.DeviceId, SelectedBle?.ErrorText));
        foreach (var row in AwBleHeadsetDevices)
            yield return new("aw-ble:" + row.Device.Id, row.Name,
                AppStrings.Text("RazerHidHeadsetCategory"), row.Identity + " | " + row.Device.Id, row.Status,
                Error(row.Device.Id, SelectedAwBleHeadset?.Device.Id, SelectedAwBleHeadset?.ErrorText));
        if (CoolingPad.IsConnected || CoolingPad.HasError)
            yield return new("cooling-pad", CoolingPad.Name,
                AppStrings.Text("CoolingPadCategory"), CoolingPad.Identity, CoolingPad.StatusText, CoolingPad.ErrorText);
        if (Kiyo.IsConnected || Kiyo.HasError)
            yield return new("kiyo", Kiyo.Name,
                AppStrings.Text("KiyoCategory"), Kiyo.Identity, Kiyo.StatusText, Kiyo.ErrorText);
        // Unpaired bridges are onboarding candidates, not devices yet.
        if (Hue.IsConfigured)
            yield return new("hue:" + Hue.BridgeHost, AppStrings.Text("HueTitle"),
                AppStrings.Text("HueTitle"), Hue.DeviceIdentity, Hue.DeviceStatusText, Hue.ErrorText);
    }
}
