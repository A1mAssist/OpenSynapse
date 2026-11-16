using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerMxicHeadsetService
{
    // Barracuda V2 X is the only T3 identity whose bundle proves Get Battery Level.
    internal const ushort BarracudaV2XProductId = 0x05B3;

    /// <summary>
    /// Reads only the battery level for the tray poll. The page keeps its full probe
    /// set in <see cref="ReadStateAsync"/>; this path must not issue other commands.
    /// </summary>
    public async Task<int?> TryReadBatteryPercentAsync(CancellationToken cancellationToken = default)
    {
        if (_dialect == MxicDialect.ValkyrieT3)
        {
            return _productId == BarracudaV2XProductId
                ? await TryReadValkyrieT3BatteryAsync(cancellationToken).ConfigureAwait(false)
                : null;
        }

        if (_dialect == MxicDialect.Joliet)
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
            return ReadBatteryWithLog(value);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _diagnostic?.Invoke($"mxic battery read failed: {exception.Message}");
            return null;
        }
    }

    internal static byte[] CreateValkyrieT3BatteryRead() =>
        RazerValkyrieT3Protocol.Create(0x07, 0x80, [0x02]);

    internal static byte ReadValkyrieT3BatteryPercent(ReadOnlySpan<byte> value) =>
        value.Length >= 1
            ? value[0]
            : throw new InvalidDataException("Valkyrie T3 battery response is short.");

    private async Task<int?> TryReadValkyrieT3BatteryAsync(CancellationToken cancellationToken)
    {
        try
        {
            var value = await QueryReportAsync(
                CreateValkyrieT3BatteryRead(),
                [0x02],
                "read battery",
                cancellationToken).ConfigureAwait(false);
            return ReadValkyrieT3BatteryPercent(value);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _diagnostic?.Invoke($"valkyrie T3 battery read failed: {exception.Message}");
            return null;
        }
    }

    private byte ReadBatteryWithLog(ReadOnlySpan<byte> value)
    {
        _diagnostic?.Invoke(value.Length > 0
            ? $"mxic battery raw=0x{value[0]:X2}"
            : "mxic battery raw=<empty>");
        return RazerMxicHeadsetProtocol.ReadBatteryPercent(value);
    }
}
