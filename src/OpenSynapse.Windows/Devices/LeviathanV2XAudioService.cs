using System.ComponentModel;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed record LeviathanV2XAudioState(
    LeviathanV2XEqPreset? EqPreset,
    IReadOnlyList<byte>? EqBands,
    int? BassLevel,
    LeviathanV2XInputSource? InputSource,
    LeviathanV2XAudioIoStatus? IoStatus,
    IReadOnlyList<string> Errors);

/// <summary>
/// Audio-only controls for the 1354 Leviathan V2 X family. Lighting and identity stay on the
/// OpenRazer page; this service owns the EQ, bass and input-source commands that OpenRazer
/// does not model.
/// </summary>
public sealed class LeviathanV2XAudioService
{
    private readonly IRazerFeatureTransport _transport;
    private readonly string _devicePath;
    private readonly TimeSpan _deviceWait;
    private readonly Action<string>? _diagnostic;

    internal LeviathanV2XAudioService(
        IRazerFeatureTransport transport,
        string devicePath,
        TimeSpan deviceWait,
        Action<string>? diagnostic = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _devicePath = string.IsNullOrWhiteSpace(devicePath)
            ? throw new ArgumentException("A HID device path is required.", nameof(devicePath))
            : devicePath;
        _deviceWait = deviceWait;
        _diagnostic = diagnostic;
    }

    public async Task<LeviathanV2XAudioState> ReadStateAsync(CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var preset = await TryReadAsync(
            "EQ preset",
            LeviathanV2XProtocol.CreateGetEqPreset(),
            response => (LeviathanV2XEqPreset)LeviathanV2XProtocol.ReadEqPreset(response, out _),
            errors,
            cancellationToken).ConfigureAwait(false);

        IReadOnlyList<byte>? bands = null;
        try
        {
            bands = await ReadEqBandsRequiredAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            errors.Add($"EQ bands: {exception.Message}");
        }

        var bass = await TryReadAsync(
            "bass level",
            LeviathanV2XProtocol.CreateGetBass(),
            response => (int)LeviathanV2XProtocol.ReadBass(response, out _),
            errors,
            cancellationToken).ConfigureAwait(false);

        var source = await TryReadAsync(
            "input source",
            LeviathanV2XProtocol.CreateGetInputSource(),
            response => LeviathanV2XProtocol.ReadInputSource(response, out _),
            errors,
            cancellationToken).ConfigureAwait(false);

        var io = await TryReadAsync(
            "audio I/O status",
            LeviathanV2XProtocol.CreateGetAudioIoStatus(),
            response => LeviathanV2XProtocol.ReadAudioIoStatus(response),
            errors,
            cancellationToken).ConfigureAwait(false);

        return new LeviathanV2XAudioState(preset, bands, bass, source, io, errors);
    }

    public async Task SetEqPresetAsync(
        LeviathanV2XEqPreset preset,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(preset))
        {
            throw new ArgumentOutOfRangeException(nameof(preset));
        }

        var previousResponse = await QueryAsync(
            LeviathanV2XProtocol.CreateGetEqPreset(), "read EQ preset", cancellationToken).ConfigureAwait(false);
        var previous = (LeviathanV2XEqPreset)LeviathanV2XProtocol.ReadEqPreset(previousResponse, out _);
        if (previous == preset)
        {
            return;
        }

        try
        {
            await QueryAsync(
                LeviathanV2XProtocol.CreateSetEqPreset(preset),
                "set EQ preset",
                cancellationToken).ConfigureAwait(false);
            var actual = (LeviathanV2XEqPreset)LeviathanV2XProtocol.ReadEqPreset(
                await QueryAsync(
                    LeviathanV2XProtocol.CreateGetEqPreset(),
                    "read back EQ preset",
                    cancellationToken).ConfigureAwait(false),
                out _);
            if (actual != preset)
            {
                throw new InvalidOperationException(
                    $"Equalizer preset readback was {actual}, expected {preset}.");
            }
        }
        catch
        {
            await TryRestoreAsync(
                LeviathanV2XProtocol.CreateSetEqPreset(previous),
                "restore EQ preset",
                CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task SetBassAsync(int level, CancellationToken cancellationToken = default)
    {
        if (level is < 1 or > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(level), "Bass level must be between 1 and 7.");
        }

        var previousResponse = await QueryAsync(
            LeviathanV2XProtocol.CreateGetBass(), "read bass level", cancellationToken).ConfigureAwait(false);
        var previous = LeviathanV2XProtocol.ReadBass(previousResponse, out _);
        if (previous == level)
        {
            return;
        }

        try
        {
            await QueryAsync(
                LeviathanV2XProtocol.CreateSetBass((byte)level),
                "set bass level",
                cancellationToken).ConfigureAwait(false);
            var actual = LeviathanV2XProtocol.ReadBass(
                await QueryAsync(
                    LeviathanV2XProtocol.CreateGetBass(),
                    "read back bass level",
                    cancellationToken).ConfigureAwait(false),
                out _);
            if (actual != level)
            {
                throw new InvalidOperationException(
                    $"Bass level readback was {actual}, expected {level}.");
            }
        }
        catch
        {
            await TryRestoreAsync(
                LeviathanV2XProtocol.CreateSetBass(previous),
                "restore bass level",
                cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public async Task SetEqBandsAsync(
        IReadOnlyList<byte> bands,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bands);
        if (bands.Count != LeviathanV2XProtocol.EqBandCount ||
            bands.Any(level => level > 100))
        {
            throw new ArgumentOutOfRangeException(nameof(bands));
        }

        var previousPresetResponse = await QueryAsync(
            LeviathanV2XProtocol.CreateGetEqPreset(), "read EQ preset", cancellationToken).ConfigureAwait(false);
        var previousPreset = (LeviathanV2XEqPreset)LeviathanV2XProtocol.ReadEqPreset(
            previousPresetResponse, out _);
        var previous = await ReadEqBandsRequiredAsync(cancellationToken).ConfigureAwait(false);
        if (previousPreset == LeviathanV2XEqPreset.Custom && previous.SequenceEqual(bands))
        {
            return;
        }

        try
        {
            if (previousPreset != LeviathanV2XEqPreset.Custom)
            {
                await QueryAsync(
                    LeviathanV2XProtocol.CreateSetEqPreset(LeviathanV2XEqPreset.Custom),
                    "select custom EQ preset",
                    cancellationToken).ConfigureAwait(false);
            }

            await QueryAsync(
                LeviathanV2XProtocol.CreateSetEqBands(bands.ToArray()),
                "set EQ bands",
                cancellationToken).ConfigureAwait(false);
            var actual = await ReadEqBandsRequiredAsync(cancellationToken).ConfigureAwait(false);
            if (!actual.SequenceEqual(bands))
            {
                throw new InvalidOperationException("Equalizer readback did not match the requested bands.");
            }

            var presetAfter = (LeviathanV2XEqPreset)LeviathanV2XProtocol.ReadEqPreset(
                await QueryAsync(
                    LeviathanV2XProtocol.CreateGetEqPreset(),
                    "read back EQ preset",
                    cancellationToken).ConfigureAwait(false),
                out _);
            if (presetAfter != LeviathanV2XEqPreset.Custom)
            {
                throw new InvalidOperationException(
                    $"Equalizer preset readback was {presetAfter}, expected Custom.");
            }
        }
        catch
        {
            if (previousPreset == LeviathanV2XEqPreset.Custom)
            {
                await TryRestoreAsync(
                    LeviathanV2XProtocol.CreateSetEqBands(previous),
                    "restore EQ bands",
                    CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                // A non-custom preset does not expose the stored custom curve, so writing
                // the active curve into profile 1 would corrupt it rather than restore it.
                _diagnostic?.Invoke("leviathan restore EQ bands skipped: previous preset was not custom");
            }

            await TryRestoreAsync(
                LeviathanV2XProtocol.CreateSetEqPreset(previousPreset),
                "restore EQ preset",
                CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<byte[]> ReadEqBandsRequiredAsync(CancellationToken cancellationToken)
    {
        var response = await QueryAsync(
            LeviathanV2XProtocol.CreateGetEqBands(),
            "read EQ bands",
            cancellationToken).ConfigureAwait(false);
        return LeviathanV2XProtocol.ReadEqBands(response, out _);
    }

    private async Task<T?> TryReadAsync<T>(
        string label,
        byte[] request,
        Func<byte[], T> parse,
        List<string> errors,
        CancellationToken cancellationToken)
        where T : struct
    {
        try
        {
            return parse(await QueryAsync(request, $"read {label}", cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            errors.Add($"{label}: {exception.Message}");
            return null;
        }
    }

    private async Task TryRestoreAsync(
        byte[] request,
        string label,
        CancellationToken cancellationToken)
    {
        try
        {
            await QueryAsync(request, label, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            _diagnostic?.Invoke($"leviathan {label} failed: {exception.Message}");
        }
    }

    private async Task<byte[]> QueryAsync(
        byte[] request,
        string label,
        CancellationToken cancellationToken)
    {
        _diagnostic?.Invoke(
            $"leviathan {label}: size={request[6]}, class=0x{request[7]:X2}, command=0x{request[8]:X2}");
        return await _transport.QueryPreparedAsync(
            _devicePath, request, _deviceWait, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsExpectedException(Exception exception) =>
        exception is IOException or InvalidDataException or InvalidOperationException or
            NotSupportedException or Win32Exception or UnauthorizedAccessException;
}
