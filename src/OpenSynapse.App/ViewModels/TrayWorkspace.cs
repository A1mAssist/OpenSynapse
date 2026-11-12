using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

/// <summary>
/// One peripheral shown in the tray battery list. Source is the row the device page
/// uses, so clicking the entry can open that exact device.
/// </summary>
public sealed record PeripheralBattery(string Name, int? Percent, string Glyph, object Source, string Key);

public sealed class TrayWorkspace
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(1);

    private readonly Func<IReadOnlyList<DeviceRowViewModel>> _devices;
    private readonly RazerHidWorkspace _razerHid;
    private readonly UsbAccessoriesWorkspace _accessories;
    private readonly Func<IReadOnlyList<BlackSharkDeviceRowViewModel>> _blackSharkDevices;
    private readonly Func<IReadOnlyList<RazerBleDeviceRowViewModel>> _bleDevices;
    private readonly Func<IReadOnlyList<RazerAwBleHeadsetRowViewModel>> _awBleDevices;

    internal TrayWorkspace(
        Func<IReadOnlyList<DeviceRowViewModel>> devices,
        RazerHidWorkspace razerHid,
        UsbAccessoriesWorkspace accessories,
        Func<IReadOnlyList<BlackSharkDeviceRowViewModel>> blackSharkDevices,
        Func<IReadOnlyList<RazerBleDeviceRowViewModel>> bleDevices,
        Func<IReadOnlyList<RazerAwBleHeadsetRowViewModel>> awBleDevices)
    {
        _devices = devices;
        _razerHid = razerHid;
        _accessories = accessories;
        _blackSharkDevices = blackSharkDevices;
        _bleDevices = bleDevices;
        _awBleDevices = awBleDevices;
    }

    /// <summary>
    /// Every peripheral the app can report a percentage for. Telemetry and the cached
    /// BLE values already carry a number; the HID sources are filled by
    /// <see cref="ReadBatteriesAsync"/>, so the tray can size itself before a read.
    /// </summary>
    public IReadOnlyList<PeripheralBattery> ConnectedBatteries
    {
        get
        {
            var claimed = ClaimedDevicePaths();
            return TelemetryBatteries()
                .Concat(BatteryDevices().Select(row =>
                    new PeripheralBattery(row.Name, null, row.IconGlyph, row, HidKey(row))))
                .Concat(HeadsetRows(claimed).Select(row =>
                    new PeripheralBattery(row.Name, null, row.IconGlyph, row, HeadsetKey(row))))
                .Concat(BlackSharkRows(claimed).Select(row =>
                    new PeripheralBattery(row.Name, null, row.IconGlyph, row, BlackSharkKey(row))))
                .Concat(KeyboardRows(claimed).Select(row =>
                    new PeripheralBattery(row.Name, null, row.IconGlyph, row, KeyboardKey(row))))
                .Concat(_bleDevices().Select(row =>
                    new PeripheralBattery(row.Name, row.BatteryPercent, row.IconGlyph, row, $"ble:{row.Device.Id}")))
                .Concat(_awBleDevices().Select(row =>
                    new PeripheralBattery(row.Name, row.BatteryPercent, row.IconGlyph, row, $"awble:{row.Device.Id}")))
                .ToArray();
        }
    }

    /// <summary>
    /// Reads one command per HID source. BLE never opens a session here: its page caches
    /// the percentage, and reconnecting every ten seconds would fight that session.
    /// </summary>
    public async Task<IReadOnlyList<PeripheralBattery>> ReadBatteriesAsync(
        CancellationToken cancellationToken = default)
    {
        var results = new List<PeripheralBattery>(TelemetryBatteries());
        foreach (var row in BatteryDevices().ToArray())
        {
            results.Add(new PeripheralBattery(
                row.Name,
                await _razerHid.ReadBatteryPercentAsync(row, cancellationToken).ConfigureAwait(false),
                row.IconGlyph,
                row, HidKey(row)));
        }

        var claimed = ClaimedDevicePaths();
        var headsetBusy = _accessories.SelectedHeadsetAudio?.IsBusy == true;
        foreach (var row in HeadsetRows(claimed))
        {
            results.Add(new PeripheralBattery(
                row.Name,
                headsetBusy
                    ? null
                    : await ReadWithTimeoutAsync(
                        token => RazerHeadsetAudioService.TryReadBatteryPercentAsync(
                            row.Connection, null, token),
                        cancellationToken).ConfigureAwait(false),
                row.IconGlyph, row, HeadsetKey(row)));
        }

        foreach (var row in BlackSharkRows(claimed))
        {
            results.Add(new PeripheralBattery(
                row.Name,
                await ReadWithTimeoutAsync(
                    token => ReadBlackSharkBatteryAsync(row.Connection, token),
                    cancellationToken).ConfigureAwait(false),
                row.IconGlyph, row, BlackSharkKey(row)));
        }

        foreach (var row in KeyboardRows(claimed))
        {
            results.Add(new PeripheralBattery(
                row.Name,
                await ReadWithTimeoutAsync(
                    token => RazerKeyboardService.TryReadBatteryPercentAsync(
                        row.Connection, null, token),
                    cancellationToken).ConfigureAwait(false),
                row.IconGlyph, row, KeyboardKey(row)));
        }

        results.AddRange(_bleDevices().Select(row =>
            new PeripheralBattery(row.Name, row.BatteryPercent, row.IconGlyph, row, $"ble:{row.Device.Id}")));
        results.AddRange(_awBleDevices().Select(row =>
            new PeripheralBattery(row.Name, row.BatteryPercent, row.IconGlyph, row, $"awble:{row.Device.Id}")));
        return results;
    }

    private static async Task<int?> ReadBlackSharkBatteryAsync(
        BlackSharkHeadsetConnection connection,
        CancellationToken cancellationToken)
    {
        await using var service = await BlackSharkHeadsetService
            .OpenAsync(connection, null, cancellationToken).ConfigureAwait(false);
        return await service.TryReadBatteryPercentAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// A sleeping device must not hold the strip hostage, so each source gets one second
    /// and then reports nothing. A cancelled tray read still propagates.
    /// </summary>
    private static async Task<int?> ReadWithTimeoutAsync(
        Func<CancellationToken, Task<int?>> read,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ReadTimeout);
        try
        {
            return await read(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException or
            System.ComponentModel.Win32Exception or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    private IReadOnlyList<PeripheralBattery> TelemetryBatteries() =>
        _devices()
            .Where(row => row.BatteryPercent is not null && row.HardwareCategory != DeviceCategory.Laptop)
            .Select(row => new PeripheralBattery(row.Name, row.BatteryPercent, row.IconGlyph, row, $"manifest:{row.Id}"))
            .ToArray();

    private IEnumerable<RazerHidDeviceRowViewModel> BatteryDevices() =>
        _razerHid.Devices.Where(row => row.Connection.IsReady &&
            row.HardwareCategory != DeviceCategory.Laptop &&
            row.Connection.Capabilities.Contains(RazerHidBackendCapability.BatteryRead));

    /// <summary>Paths the RazerHid rows already cover, so nothing is listed twice.</summary>
    private HashSet<string> ClaimedDevicePaths() =>
        _razerHid.Devices
            .Select(row => row.Connection.DevicePath)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private IEnumerable<RazerHeadsetAudioDeviceRowViewModel> HeadsetRows(HashSet<string> claimed) =>
        _accessories.HeadsetAudioDevices.Where(row => !claimed.Contains(row.Connection.DevicePath));

    private IEnumerable<BlackSharkDeviceRowViewModel> BlackSharkRows(HashSet<string> claimed) =>
        _blackSharkDevices().Where(row => !claimed.Contains(row.Connection.DevicePath));

    private IEnumerable<RazerKeyboardDeviceRowViewModel> KeyboardRows(HashSet<string> claimed) =>
        _accessories.KeyboardDevices.Where(row => !claimed.Contains(row.Connection.DevicePath));

    private static string HidKey(RazerHidDeviceRowViewModel row) =>
        $"razer-hid:{row.Connection.InstanceId}";

    private static string HeadsetKey(RazerHeadsetAudioDeviceRowViewModel row) =>
        $"headset-audio:{row.Connection.DevicePath}";

    private static string BlackSharkKey(BlackSharkDeviceRowViewModel row) =>
        $"blackshark:{row.Connection.DevicePath}";

    private static string KeyboardKey(RazerKeyboardDeviceRowViewModel row) =>
        $"keyboard:{row.Connection.DevicePath}";
}
