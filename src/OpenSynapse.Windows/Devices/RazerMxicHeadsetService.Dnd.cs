using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerMxicHeadsetService
{
    private byte[] CreateAudioDndRead() =>
        _dialect == MxicDialect.Protocol40
            ? RazerProtocol40HeadsetProtocol.CreateRequest([1, 0x18, 0x85], [0x00])
            : RazerMxicHeadsetProtocol.CreateGetAudioDnd(_productId);

    private byte[] AudioDndReadHeader => _dialect switch
    {
        MxicDialect.Protocol40 => [],
        MxicDialect.Class18 => RazerMxicHeadsetProtocol.AudioDndHeaderClass18,
        MxicDialect.ValkyrieT3 => [],
        _ => RazerMxicHeadsetProtocol.AudioDndHeader,
    };

    private byte[] SetAudioDndHeader => _dialect switch
    {
        MxicDialect.Class18 => RazerMxicHeadsetProtocol.AudioDndHeaderClass18,
        MxicDialect.ValkyrieT3 => [],
        _ => RazerMxicHeadsetProtocol.SetAudioDndHeader,
    };

    private bool ReadAudioDndValue(ReadOnlySpan<byte> value) =>
        _dialect == MxicDialect.Protocol40
            ? value.Length >= 2
                ? value[1] == 1
                : throw new InvalidDataException("Missing DND state.")
            : RazerMxicHeadsetProtocol.ReadAudioDnd(_productId, value);

    private async Task<bool?> ReadAudioDndAsync(
        List<string> errors,
        CancellationToken cancellationToken)
    {
        var dnd = await ProbeAsync(
            "audio DND",
            CreateAudioDndRead(),
            AudioDndReadHeader,
            ReadAudioDndValue,
            errors,
            cancellationToken).ConfigureAwait(false);
        return dnd.Ok ? dnd.Value : null;
    }

    /// <summary>
    /// Protocol40 only probes DND, so its write path refuses instead of guessing a setter.
    /// Every other dialect reads before writing and must read back the requested value.
    /// </summary>
    public async Task SetAudioDndAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        if (_dialect is MxicDialect.Protocol40 or MxicDialect.Joliet)
        {
            throw new NotSupportedException(
                _dialect == MxicDialect.Protocol40
                    ? "The protocol40 headset bundle does not expose a DND setter."
                    : "The Joliet bundle does not expose a DND setter.");
        }

        var previous = await QueryReportAsync(
            CreateAudioDndRead(), AudioDndReadHeader, "read audio DND", cancellationToken)
            .ConfigureAwait(false);
        if (ReadAudioDndValue(previous) == enabled)
        {
            return;
        }

        await WriteReportAsync(
            RazerMxicHeadsetProtocol.CreateSetAudioDnd(_productId, enabled),
            SetAudioDndHeader,
            "audio DND",
            cancellationToken).ConfigureAwait(false);

        byte[] readback;
        try
        {
            readback = await QueryReportAsync(
                CreateAudioDndRead(), AudioDndReadHeader, "read back audio DND", cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            throw new InvalidOperationException(
                "DND was written, but the current state could not be read back.", exception);
        }

        if (ReadAudioDndValue(readback) != enabled)
        {
            throw new InvalidOperationException(
                "DND was written, but the readback did not match the requested value.");
        }
    }
}
