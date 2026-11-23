using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerMxicHeadsetService
{
    public async Task<bool?> TryReadWirelessConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (_dialect != MxicDialect.Legacy ||
            !RazerMxicHeadsetProtocol.SupportsWirelessConnectionStatus(_productId))
        {
            return null;
        }

        try
        {
            var value = await QueryReportAsync(
                RazerMxicHeadsetProtocol.CreateRequest(
                    RazerMxicHeadsetProtocol.GetWirelessConnectionStatus, reportId: ReportId),
                RazerMxicHeadsetProtocol.WirelessConnectionStatusHeader,
                "read wireless connection",
                cancellationToken).ConfigureAwait(false);
            return RazerMxicHeadsetProtocol.ReadWirelessConnectionStatus(value);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _diagnostic?.Invoke($"mxic wireless status failed: {exception.Message}");
            return null;
        }
    }

    private async Task<(
        bool SidetoneOk,
        bool Sidetone,
        bool AncOk,
        RazerAncState AncState,
        byte AncLevel)> ReadSidetoneAndAncAsync(
        List<string> errors,
        CancellationToken cancellationToken)
    {
        var sidetone = (Ok: false, Value: false);
        if (CanUseSidetone)
        {
            sidetone = await ProbeAsync(
                "sidetone",
                RazerMxicHeadsetProtocol.CreateGetSidetone(_productId),
                SidetoneReadHeader,
                ReadSidetone,
                errors,
                cancellationToken).ConfigureAwait(false);
        }

        var anc = (Ok: false, Value: (State: RazerAncState.Off, Level: (byte)0));
        if (CanUseAnc)
        {
            anc = await ProbeAsync(
                "ANC",
                CreateAncRead(),
                RazerMxicHeadsetProtocol.AncHeader,
                ReadAnc,
                errors,
                cancellationToken).ConfigureAwait(false);
        }

        return (sidetone.Ok, sidetone.Value, anc.Ok, anc.Value.State, anc.Value.Level);
    }

    // AudioMxicDevice carries sidetone on its legacy triplet; rzDevice25ValkyrieT3 keeps
    // class 0x18 and uses source 3. ANC is only measured on 0555.
    private bool CanUseSidetone =>
        (_dialect is MxicDialect.Legacy or MxicDialect.MabelT1 or MxicDialect.ValkyrieT3) &&
        RazerMxicHeadsetProtocol.SupportsSidetone(_productId);

    private bool CanUseAnc =>
        _dialect == MxicDialect.Legacy && RazerMxicHeadsetProtocol.SupportsAnc(_productId);

    private byte[] SidetoneReadHeader => _dialect == MxicDialect.ValkyrieT3
        ? RazerMxicHeadsetProtocol.SidetoneHeaderValkyrie
        : RazerMxicHeadsetProtocol.SidetoneHeader;

    // A Valkyrie T3 setter only needs the command ACK, which the transport already checks.
    private byte[] SidetoneWriteHeader => _dialect == MxicDialect.ValkyrieT3
        ? []
        : RazerMxicHeadsetProtocol.SetSidetoneHeader;

    public async Task SetSidetoneAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        if (!CanUseSidetone)
        {
            throw new NotSupportedException(
                $"Device {_productId:X4} does not expose the MXIC sidetone command.");
        }

        await WriteAsync(
            RazerMxicHeadsetProtocol.CreateGetSidetone(_productId),
            SidetoneReadHeader,
            ReadSidetone,
            enabled,
            value => RazerMxicHeadsetProtocol.CreateSetSidetone(_productId, value),
            SidetoneWriteHeader,
            value => value == enabled,
            "sidetone",
            cancellationToken).ConfigureAwait(false);

        if (enabled && _dialect != MxicDialect.ValkyrieT3)
        {
            await EnsureSidetoneVolumeAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    // AudioMxicDevice writes the state and then the volume. With volume at 0 the
    // state bit alone stays silent, and Synapse's default micSideTone value is 70%.
    private async Task EnsureSidetoneVolumeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var percent = RazerMxicHeadsetProtocol.ReadSidetonePercent(await QueryReportAsync(
                RazerMxicHeadsetProtocol.CreateGetSidetoneVolume(_productId),
                RazerMxicHeadsetProtocol.SidetoneVolumeHeader,
                "read sidetone volume",
                cancellationToken).ConfigureAwait(false));
            if (percent > 0)
            {
                return;
            }

            await WriteReportAsync(
                RazerMxicHeadsetProtocol.CreateSetSidetoneVolume(_productId, 70),
                RazerMxicHeadsetProtocol.SetSidetoneVolumeHeader,
                "set sidetone volume",
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            // Devices without the volume command still keep the state write.
            _diagnostic?.Invoke($"mxic sidetone volume failed: {exception.Message}");
        }
    }

    public Task SetAncAsync(
        RazerAncState state,
        byte level,
        CancellationToken cancellationToken = default)
    {
        if (!CanUseAnc)
        {
            return Task.FromException(new NotSupportedException(
                $"Device {_productId:X4} does not expose the MXIC ANC command."));
        }
        if (!Enum.IsDefined(state))
        {
            return Task.FromException(new ArgumentOutOfRangeException(nameof(state)));
        }

        var requested = (State: state, Level: state switch
        {
            RazerAncState.Off => (byte)0,
            RazerAncState.Ambient => (byte)80,
            _ => (byte)Math.Clamp((int)level, 1, 10),
        });
        return WriteAsync(
            CreateAncRead(),
            RazerMxicHeadsetProtocol.AncHeader,
            ReadAnc,
            requested,
            value => RazerMxicHeadsetProtocol.CreateRequest(
                RazerMxicHeadsetProtocol.SetAncCommand,
                [(byte)value.State, value.Level]),
            RazerMxicHeadsetProtocol.SetAncHeader,
            value => value == requested,
            "ANC",
            cancellationToken);
    }

    private byte[] CreateAncRead() =>
        RazerMxicHeadsetProtocol.CreateRequest(
            RazerMxicHeadsetProtocol.GetAnc,
            reportId: ReportId);

    private static bool ReadSidetone(ReadOnlySpan<byte> value) =>
        value.Length >= 1
            ? value[0] != 0
            : throw new InvalidDataException("MXIC sidetone response is missing its state.");

    private static (RazerAncState State, byte Level) ReadAnc(ReadOnlySpan<byte> value) =>
        value.Length >= 2
            ? ((RazerAncState)value[0], value[1])
            : throw new InvalidDataException("MXIC ANC response is missing its state or level.");
}
