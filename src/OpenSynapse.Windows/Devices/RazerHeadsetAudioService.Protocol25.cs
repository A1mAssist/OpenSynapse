using System.Text;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHeadsetAudioService
{
    private byte _protocol25TransactionId;

    private async Task<RazerHeadsetAudioState> ReadProtocol25StateAsync(
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var firmware = await ProbeProtocol25Async(
            "firmware",
            txn => RazerHeadsetAudioProtocol.GetFirmware(txn),
            RazerHeadsetAudioProtocol.ReadFirmware,
            errors,
            cancellationToken).ConfigureAwait(false);
        var serial = await ProbeProtocol25Async(
            "serial number",
            txn => RazerHeadsetAudioProtocol.GetSerialNumber(txn),
            ReadProtocol25SerialNumber,
            errors,
            cancellationToken).ConfigureAwait(false);
        var edition = await ProbeProtocol25Async(
            "edition",
            txn => RazerHeadsetAudioProtocol.GetEdition(txn),
            ReadProtocol25Edition,
            errors,
            cancellationToken,
            optional: true).ConfigureAwait(false);

        var tenBand = RazerProtocol25FeatureProtocol.UsesTenBandEqualizer(_productId);
        var preset = RazerProtocol25FeatureProtocol.SupportsHidEqualizer(_productId)
            ? await ProbeProtocol25Async(
                "EQ preset",
                txn => RazerHeadsetAudioProtocol.GetEqPreset(txn),
                RazerHeadsetAudioProtocol.ReadEqPreset,
                errors,
                cancellationToken).ConfigureAwait(false)
            : (Ok: false, Value: default(byte));
        var bands = RazerProtocol25FeatureProtocol.SupportsHidEqualizer(_productId)
            ? await ProbeProtocol25Async(
                "EQ bands",
                txn => RazerHeadsetAudioProtocol.GetEqBands(txn, tenBand),
                response => RazerHeadsetAudioProtocol.ReadEqBands(response, tenBand),
                errors,
                cancellationToken).ConfigureAwait(false)
            : (Ok: false, Value: default(IReadOnlyList<byte>)!);
        var bass = RazerProtocol25FeatureProtocol.SupportsHidBass(_productId)
            ? await ProbeProtocol25Async(
                "bass",
                txn => RazerHeadsetAudioProtocol.GetBass(txn),
                RazerHeadsetAudioProtocol.ReadBass,
                errors,
                cancellationToken).ConfigureAwait(false)
            : (Ok: false, Value: default(byte));
        var micMute = RazerProtocol25FeatureProtocol.SupportsHidMicrophone(_productId)
            ? await ProbeProtocol25Async(
                "microphone mute",
                txn => RazerHeadsetAudioProtocol.GetMicMute(txn),
                RazerHeadsetAudioProtocol.ReadMicMute,
                errors,
                cancellationToken).ConfigureAwait(false)
            : (Ok: false, Value: default(bool));
        var micVolume = RazerProtocol25FeatureProtocol.SupportsHidMicVolume(_productId)
            ? await ProbeProtocol25Async(
                "microphone volume",
                txn => RazerHeadsetAudioProtocol.GetMicVolume(txn),
                RazerHeadsetAudioProtocol.ReadMicVolume,
                errors,
                cancellationToken).ConfigureAwait(false)
            : (Ok: false, Value: default(byte));
        var noiseSuppression = RazerProtocol25FeatureProtocol.SupportsHidAudioInput(_productId)
            ? await ProbeProtocol25Async(
                "noise suppression",
                txn => RazerHeadsetAudioProtocol.GetAudioInputControlState(txn),
                RazerHeadsetAudioProtocol.ReadAudioInputControlState,
                errors,
                cancellationToken).ConfigureAwait(false)
            : (Ok: false, Value: default(bool));

        return new RazerHeadsetAudioState(
            Firmware: firmware.Ok ? firmware.Value : null,
            BatteryPercent: null,
            Charging: null,
            EqPreset: preset.Ok ? preset.Value : null,
            EqBands: bands.Ok ? bands.Value : null,
            Bass: bass.Ok ? bass.Value : null,
            MicMuted: micMute.Ok ? micMute.Value : null,
            MicVolume: micVolume.Ok ? micVolume.Value : null,
            AudioSource: null,
            NoiseSuppression: noiseSuppression.Ok ? noiseSuppression.Value : null,
            AncState: null,
            AncLevel: null,
            Errors: errors)
        {
            SerialNumber = serial.Ok ? serial.Value : null,
            Edition = edition.Ok ? edition.Value : null,
        };
    }

    private async Task<(bool Ok, T Value)> ProbeProtocol25Async<T>(
        string label,
        Func<byte, byte[]> request,
        Func<ReadOnlySpan<byte>, T> parse,
        List<string> errors,
        CancellationToken cancellationToken,
        bool optional = false)
    {
        try
        {
            var response = await QueryProtocol25Async(request, cancellationToken).ConfigureAwait(false);
            return (true, parse(response));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            if (!optional)
            {
                errors.Add($"{label}: {exception.Message}");
            }

            return (false, default!);
        }
    }

    private Task<byte[]> QueryProtocol25Async(
        Func<byte, byte[]> request,
        CancellationToken cancellationToken) =>
        _protocol25Transport!.QueryAsync(
            RazerProtocol25FeatureProtocol.CreateRequest(
                _productId,
                request(NextProtocol25TransactionId())),
            cancellationToken);

    private byte NextProtocol25TransactionId()
    {
        var current = _protocol25TransactionId;
        _protocol25TransactionId = current == 31 ? (byte)0 : (byte)(current + 1);
        return current;
    }

    private void RequireProtocol25Support(bool supported, string feature)
    {
        if (!supported)
        {
            throw new NotSupportedException(
                $"Razer product 0x{_productId:X4} has no protocol-25 {feature} control.");
        }
    }

    private static string ReadProtocol25SerialNumber(ReadOnlySpan<byte> response)
    {
        var data = RazerHeadsetAudioProtocol.GetArguments(response);
        var text = new StringBuilder();
        foreach (var value in data)
        {
            if (value is >= 0x20 and < 0x7F)
            {
                text.Append((char)value);
            }
            else if (text.Length > 0)
            {
                break;
            }
        }

        return text.ToString();
    }

    private static byte ReadProtocol25Edition(ReadOnlySpan<byte> response)
    {
        var data = RazerHeadsetAudioProtocol.GetArguments(response);
        if (data.Length < 2)
        {
            throw new InvalidDataException("Protocol-25 audio edition response is short.");
        }

        return data[1];
    }

    private Task SetProtocol25EqPresetAsync(byte preset, CancellationToken cancellationToken) =>
        WriteProtocol25Async(
            txn => RazerHeadsetAudioProtocol.GetEqPreset(txn),
            RazerHeadsetAudioProtocol.ReadEqPreset,
            preset,
            (value, txn) => RazerHeadsetAudioProtocol.SetEqPreset(txn, value),
            value => value == preset,
            "EQ preset",
            cancellationToken);

    private async Task SetProtocol25EqBandsAsync(
        IReadOnlyList<byte> bands,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bands);
        var tenBand = bands.Count == RazerHeadsetAudioProtocol.TenBandCount;
        if (!tenBand && bands.Count != RazerHeadsetAudioProtocol.EightBandCount)
        {
            throw new ArgumentException("Equalizer data must contain 10 or 8 bands.", nameof(bands));
        }

        var requested = bands.ToArray();
        var previous = RazerHeadsetAudioProtocol.ReadEqBands(
            await QueryProtocol25Async(
                txn => RazerHeadsetAudioProtocol.GetEqBands(txn, tenBand),
                cancellationToken).ConfigureAwait(false),
            tenBand);
        if (previous.SequenceEqual(requested))
        {
            return;
        }

        try
        {
            await QueryProtocol25Async(
                txn => RazerHeadsetAudioProtocol.SetEqBands(txn, requested),
                cancellationToken).ConfigureAwait(false);
            var actual = RazerHeadsetAudioProtocol.ReadEqBands(
                await QueryProtocol25Async(
                    txn => RazerHeadsetAudioProtocol.GetEqBands(txn, tenBand),
                    cancellationToken).ConfigureAwait(false),
                tenBand);
            if (!actual.SequenceEqual(requested))
            {
                throw new InvalidOperationException("EQ band readback did not match the requested values.");
            }
        }
        catch
        {
            try
            {
                await QueryProtocol25Async(
                    txn => RazerHeadsetAudioProtocol.SetEqBands(txn, previous),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                _diagnostic?.Invoke($"restore protocol-25 EQ bands failed: {exception.Message}");
            }

            throw;
        }
    }

    private async Task SetProtocol25CustomEqAsync(
        byte preset,
        IReadOnlyList<byte> bands,
        CancellationToken cancellationToken)
    {
        var tenBand = bands.Count == RazerHeadsetAudioProtocol.TenBandCount;
        var previousPreset = RazerHeadsetAudioProtocol.ReadEqPreset(
            await QueryProtocol25Async(
                txn => RazerHeadsetAudioProtocol.GetEqPreset(txn),
                cancellationToken).ConfigureAwait(false));
        var previousBands = RazerHeadsetAudioProtocol.ReadEqBands(
            await QueryProtocol25Async(
                txn => RazerHeadsetAudioProtocol.GetEqBands(txn, tenBand),
                cancellationToken).ConfigureAwait(false),
            tenBand);
        try
        {
            await SetProtocol25EqPresetAsync(preset, cancellationToken).ConfigureAwait(false);
            await SetProtocol25EqBandsAsync(bands, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await TryRestoreProtocol25EqAsync(previousPreset, previousBands).ConfigureAwait(false);
            throw;
        }
    }

    private async Task TryRestoreProtocol25EqAsync(byte preset, byte[] bands)
    {
        try
        {
            await SetProtocol25EqPresetAsync(preset, CancellationToken.None).ConfigureAwait(false);
            await SetProtocol25EqBandsAsync(bands, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _diagnostic?.Invoke($"restore protocol-25 EQ failed: {exception.Message}");
        }
    }

    private Task SetProtocol25BassAsync(byte level, CancellationToken cancellationToken) =>
        WriteProtocol25Async(
            txn => RazerHeadsetAudioProtocol.GetBass(txn),
            RazerHeadsetAudioProtocol.ReadBass,
            level,
            (value, txn) => RazerHeadsetAudioProtocol.SetBass(txn, value),
            value => value == level,
            "bass",
            cancellationToken);

    private Task SetProtocol25MicMuteAsync(bool muted, CancellationToken cancellationToken) =>
        WriteProtocol25Async(
            txn => RazerHeadsetAudioProtocol.GetMicMute(txn),
            RazerHeadsetAudioProtocol.ReadMicMute,
            muted,
            (value, txn) => RazerHeadsetAudioProtocol.SetMicMute(txn, value),
            value => value == muted,
            "microphone mute",
            cancellationToken);

    private Task SetProtocol25MicVolumeAsync(byte volume, CancellationToken cancellationToken) =>
        WriteProtocol25Async(
            txn => RazerHeadsetAudioProtocol.GetMicVolume(txn),
            RazerHeadsetAudioProtocol.ReadMicVolume,
            volume,
            (value, txn) => RazerHeadsetAudioProtocol.SetMicVolume(txn, value),
            value => value == volume,
            "microphone volume",
            cancellationToken);

    private Task SetProtocol25NoiseSuppressionAsync(bool enabled, CancellationToken cancellationToken) =>
        WriteProtocol25Async(
            txn => RazerHeadsetAudioProtocol.GetAudioInputControlState(txn),
            RazerHeadsetAudioProtocol.ReadAudioInputControlState,
            enabled,
            (value, txn) => RazerHeadsetAudioProtocol.SetAudioInputControlState(txn, value),
            value => value == enabled,
            "noise suppression",
            cancellationToken);

    private async Task WriteProtocol25Async<T>(
        Func<byte, byte[]> readRequest,
        Func<ReadOnlySpan<byte>, T> parse,
        T expected,
        Func<T, byte, byte[]> writeRequest,
        Func<T, bool> matches,
        string label,
        CancellationToken cancellationToken)
    {
        var previous = parse(await QueryProtocol25Async(readRequest, cancellationToken).ConfigureAwait(false));
        try
        {
            await QueryProtocol25Async(
                txn => writeRequest(expected, txn),
                cancellationToken).ConfigureAwait(false);
            var actual = parse(await QueryProtocol25Async(readRequest, cancellationToken).ConfigureAwait(false));
            if (!matches(actual))
            {
                throw new InvalidOperationException($"{label} readback did not match the requested value.");
            }
        }
        catch
        {
            try
            {
                await QueryProtocol25Async(
                    txn => writeRequest(previous, txn),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                _diagnostic?.Invoke($"restore protocol-25 {label} failed: {exception.Message}");
            }

            throw;
        }
    }
}
