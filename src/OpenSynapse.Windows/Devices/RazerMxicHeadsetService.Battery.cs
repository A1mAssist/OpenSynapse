using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerMxicHeadsetService
{
    /// <summary>
    /// Reads only the battery level for the tray poll. The page keeps its full probe
    /// set in <see cref="ReadStateAsync"/>; this path must not issue other commands.
    /// </summary>
    public async Task<int?> TryReadBatteryPercentAsync(CancellationToken cancellationToken = default)
    {
        if (_dialect is MxicDialect.ValkyrieT3 or MxicDialect.Joliet)
        {
            return null;
        }

        try
        {
            if (_dialect == MxicDialect.Protocol40)
            {
                await _transport.SendAsync(
                    RazerProtocol40HeadsetProtocol.CreateConnect(), cancellationToken).ConfigureAwait(false);
                var protocol40 = await QueryReportAsync(
                    RazerProtocol40HeadsetProtocol.CreateGetBattery(),
                    [],
                    "read battery",
                    cancellationToken).ConfigureAwait(false);
                return RazerProtocol40HeadsetProtocol.ReadBatteryPercent(protocol40);
            }

            var value = await QueryReportAsync(
                RazerMxicHeadsetProtocol.CreateRequest(
                    RazerMxicHeadsetProtocol.GetBattery, reportId: ReportId),
                RazerMxicHeadsetProtocol.BatteryHeader,
                "read battery",
                cancellationToken).ConfigureAwait(false);
            return RazerMxicHeadsetProtocol.ReadBatteryPercent(value);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _diagnostic?.Invoke($"mxic battery read failed: {exception.Message}");
            return null;
        }
    }
}
