using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

/// <summary>
/// One peripheral shown in the tray battery list. Source is the row the device page
/// uses, so clicking the entry can open that exact device.
/// </summary>
public sealed record PeripheralBattery(string Name, int? Percent, string Glyph, object Source);

public sealed partial class MainViewModel
{
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
        if (_openRazerDeviceService is null)
        {
            return TelemetryBatteries();
        }

        // Telemetry rows keep their polled value; only the OpenRazer rows are read here.
        var results = new List<PeripheralBattery>(TelemetryBatteries());
        foreach (var row in BatteryDevices())
        {
            int? percent = null;
            try
            {
                percent = await _openRazerDeviceService
                    .GetBatteryPercentAsync(row.Connection, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // No answer means no percentage for this row; the menu still lists it.
            }

            results.Add(new PeripheralBattery(row.Name, percent, row.IconGlyph, row));
        }

        return results;
    }

    private IReadOnlyList<PeripheralBattery> TelemetryBatteries() =>
        Devices
            .Where(row => row.BatteryPercent is not null)
            .Select(row => new PeripheralBattery(row.Name, row.BatteryPercent, row.IconGlyph, row))
            .ToArray();

    private IEnumerable<OpenRazerDeviceRowViewModel> BatteryDevices() =>
        OpenRazerDevices.Where(row => row.Connection.IsReady &&
            row.Connection.Capabilities.Contains(OpenRazerBackendCapability.BatteryRead));
}