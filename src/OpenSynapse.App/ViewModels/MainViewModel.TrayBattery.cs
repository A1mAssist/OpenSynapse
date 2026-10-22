using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

/// <summary>One peripheral shown in the tray battery list.</summary>
public sealed record PeripheralBattery(string Name, int? Percent);

public sealed partial class MainViewModel
{
    /// <summary>
    /// Battery-capable peripherals. The names come from discovery, so the tray can
    /// size itself before any percentage has been read.
    /// </summary>
    public IReadOnlyList<PeripheralBattery> ConnectedBatteries =>
        BatteryDevices()
            .Select(connection => new PeripheralBattery(connection.Definition.DisplayName, null))
            .ToArray();

    /// <summary>
    /// Reads each battery with a single command, not the whole device state. Only the
    /// tray calls this, and only while the menu is opening, so a device that is asleep
    /// costs one retry budget and then reports no percentage.
    /// </summary>
    public async Task<IReadOnlyList<PeripheralBattery>> ReadBatteriesAsync(
        CancellationToken cancellationToken = default)
    {
        if (_openRazerDeviceService is null)
        {
            return [];
        }

        var results = new List<PeripheralBattery>();
        foreach (var connection in BatteryDevices())
        {
            int? percent = null;
            try
            {
                percent = await _openRazerDeviceService
                    .GetBatteryPercentAsync(connection, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // No answer means no percentage for this row; the menu still lists it.
            }

            results.Add(new PeripheralBattery(connection.Definition.DisplayName, percent));
        }

        return results;
    }

    private IEnumerable<OpenRazerDeviceConnection> BatteryDevices() =>
        OpenRazerDevices
            .Select(row => row.Connection)
            .Where(connection => connection.IsReady &&
                connection.Capabilities.Contains(OpenRazerBackendCapability.BatteryRead));
}