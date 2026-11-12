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
    private readonly Func<IReadOnlyList<DeviceRowViewModel>> _devices;
    private readonly RazerHidWorkspace _razerHid;

    internal TrayWorkspace(
        Func<IReadOnlyList<DeviceRowViewModel>> devices,
        RazerHidWorkspace razerHid)
    {
        _devices = devices;
        _razerHid = razerHid;
    }

    /// <summary>
    /// Battery-capable peripherals. Telemetry-backed devices already carry a value;
    /// RazerHid ones are listed with no value yet, so the tray can size itself
    /// before any hardware read.
    /// </summary>
    public IReadOnlyList<PeripheralBattery> ConnectedBatteries =>
        TelemetryBatteries()
            .Concat(BatteryDevices()
                .Select(row => new PeripheralBattery(row.Name, null, row.IconGlyph, row, $"razer-hid:{row.Connection.InstanceId}")))
            .ToArray();

    /// <summary>
    /// Reads each RazerHid battery with a single command, not the whole device state.
    /// Only the tray calls this, and only while the menu is opening, so a device that
    /// is asleep costs one retry budget and then reports no percentage.
    /// </summary>
    public async Task<IReadOnlyList<PeripheralBattery>> ReadBatteriesAsync(
        CancellationToken cancellationToken = default)
    {
        // Telemetry rows keep their polled value; only the RazerHid rows are read here.
        var results = new List<PeripheralBattery>(TelemetryBatteries());
        foreach (var row in BatteryDevices().ToArray())
        {
            results.Add(new PeripheralBattery(
                row.Name,
                await _razerHid.ReadBatteryPercentAsync(row, cancellationToken).ConfigureAwait(false),
                row.IconGlyph,
                row, $"razer-hid:{row.Connection.InstanceId}"));
        }

        return results;
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
}
