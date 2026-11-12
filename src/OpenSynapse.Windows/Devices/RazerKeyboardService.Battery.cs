using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerKeyboardService
{
    /// <summary>
    /// Reads only the battery level. The taskbar polls this every ten seconds, so it must
    /// stay a single command instead of the whole device probe.
    /// </summary>
    public async Task<int?> TryReadBatteryPercentAsync(CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var battery = await ProbeAsync(
            "battery", RazerKeyboardProtocol.GetBatteryLevel, RazerKeyboardProtocol.ReadBatteryPercent,
            errors, cancellationToken).ConfigureAwait(false);
        return battery.Ok ? (int?)battery.Value : null;
    }

    /// <summary>One-shot battery read for the tray; the feature channel closes with the service.</summary>
    public static async Task<int?> TryReadBatteryPercentAsync(
        RazerKeyboardConnection connection,
        Action<string>? diagnostic,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var service = new RazerKeyboardService(
            new RazerFeatureTransport(),
            connection.DevicePath,
            TimeSpan.FromMilliseconds(20),
            diagnostic);
        try
        {
            return await service.TryReadBatteryPercentAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            diagnostic?.Invoke($"keyboard battery read failed: {exception.Message}");
            return null;
        }
    }
}
