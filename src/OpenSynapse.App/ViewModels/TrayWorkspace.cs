using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

/// <summary>
/// One peripheral shown in the tray battery list. Source is the row the device page
/// uses, so clicking the entry can open that exact device.
/// </summary>
public sealed record PeripheralBattery(string Name, int? Percent, string Glyph, object Source);

public sealed class TrayWorkspace
{
    private readonly Func<IReadOnlyList<DeviceRowViewModel>> _devices;
    private readonly OpenRazerWorkspace _openRazer;

    internal TrayWorkspace(
        Func<IReadOnlyList<DeviceRowViewModel>> devices,
        OpenRazerWorkspace openRazer)
    {
        _devices = devices;
        _openRazer = openRazer;
    }

    /// <summary>
    /// Battery-capable peripherals. Telemetry-backed devices already carry a value;
    /// OpenRazer ones are listed with no value yet, so the tray can size itself
    /// before any hardware read.
    /// </summary>
    public IReadOnlyList<PeripheralBattery> ConnectedBatteries =>
        TelemetryBatteries()
            .Concat(BatteryDevices()
                .Select(row => new PeripheralBattery(row.Name, null, row.IconGlyph, row)))
            .ToArray();

    /// <summary>
    /// Reads each OpenRazer battery with a single command, not the whole device state.
    /// Only the tray calls this, and only while the menu is opening, so a device that
    /// is asleep costs one retry budget and then reports no percentage.
    /// </summary>
    public async Task<IReadOnlyList<PeripheralBattery>> ReadBatteriesAsync(
        CancellationToken cancellationToken = default)
    {
        // Telemetry rows keep their polled value; only the OpenRazer rows are read here.
        var results = new List<PeripheralBattery>(TelemetryBatteries());
        foreach (var row in BatteryDevices())
        {
            results.Add(new PeripheralBattery(
                row.Name,
                await _openRazer.ReadBatteryPercentAsync(row, cancellationToken).ConfigureAwait(false),
                row.IconGlyph,
                row));
        }

        return results;
    }

    private IReadOnlyList<PeripheralBattery> TelemetryBatteries() =>
        _devices()
            .Where(row => row.BatteryPercent is not null && row.HardwareCategory != DeviceCategory.Laptop)
            .Select(row => new PeripheralBattery(row.Name, row.BatteryPercent, row.IconGlyph, row))
            .ToArray();

    private IEnumerable<OpenRazerDeviceRowViewModel> BatteryDevices() =>
        _openRazer.Devices.Where(row => row.Connection.IsReady &&
            row.HardwareCategory != DeviceCategory.Laptop &&
            row.Connection.Capabilities.Contains(OpenRazerBackendCapability.BatteryRead));
}
