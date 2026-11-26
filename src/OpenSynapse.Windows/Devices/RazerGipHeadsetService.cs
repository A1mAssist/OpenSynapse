using System.ComponentModel;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// The subset of the Mxic/Protocol40 service surface shared with the native GIP
/// families, so the headset page can dispatch both through one code path.
/// </summary>
internal interface IRazerHeadsetControlService : IAsyncDisposable
{
    Task<RazerHeadsetAudioState> ReadStateAsync(CancellationToken cancellationToken);

    Task<RazerHeadsetEqualizerState> ReadEqualizerAsync(int bandCount, CancellationToken cancellationToken);

    Task SetEqPresetAsync(byte preset, CancellationToken cancellationToken);

    Task SetEqBandsAsync(IReadOnlyList<byte> bands, CancellationToken cancellationToken);

    Task SetCustomEqAsync(byte preset, IReadOnlyList<byte> bands, CancellationToken cancellationToken);

    Task SetBassAsync(byte level, CancellationToken cancellationToken);

    Task SetMicMuteAsync(bool muted, CancellationToken cancellationToken);

    Task SetSidetoneAsync(bool enabled, CancellationToken cancellationToken);

    Task SetAncAsync(RazerAncState state, byte level, CancellationToken cancellationToken);

    Task SetAudioDndAsync(bool enabled, CancellationToken cancellationToken);
}

/// <summary>
/// Controls for the nine native Xbox GIP headset identities. Commands and frame
/// layouts come from Razer.Audio.Simone.Control.dll; the service owns the DLL's
/// ten-attempt retry loop and maps replies onto the shared headset state.
/// </summary>
public sealed class RazerGipHeadsetService : IRazerHeadsetControlService
{
    private const int Attempts = 10;
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);

    private delegate T PayloadParser<T>(ReadOnlySpan<byte> payload);

    private readonly IRazerGipHeadsetTransport _transport;
    private readonly SemaphoreSlim _eqGate = new(1, 1);
    private readonly RazerGipHeadsetCommands _commands;
    private readonly RazerGipHeadsetDialect _dialect;
    private readonly ushort _productId;
    private readonly Action<string>? _diagnostic;

    public RazerGipHeadsetService(
        ushort productId,
        string devicePath,
        ushort outputReportByteLength = RazerGipHeadsetProtocol.FrameLength + 1,
        Action<string>? diagnostic = null)
        : this(productId, OpenTransport(productId, devicePath, outputReportByteLength), diagnostic)
    {
    }

    internal RazerGipHeadsetService(
        ushort productId, IRazerGipHeadsetTransport transport, Action<string>? diagnostic = null)
    {
        if (!RazerGipHeadsetProtocol.IsSupported(productId))
        {
            throw new NotSupportedException($"Product {productId:X4} is not a GIP headset.");
        }

        _productId = productId;
        _commands = RazerGipHeadsetProtocol.Commands(productId);
        _dialect = RazerGipHeadsetProtocol.ResolveDialect(productId);
        _diagnostic = diagnostic;
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    private static RazerGipHeadsetTransport OpenTransport(
        ushort productId, string devicePath, ushort outputReportByteLength)
    {
        _ = RazerGipHeadsetProtocol.ResolveDialect(productId);
        return new(devicePath, outputReportByteLength == 0
            ? RazerGipHeadsetProtocol.FrameLength + 1 : outputReportByteLength);
    }

    public static bool IsSupported(ushort productId) =>
        RazerGipHeadsetProtocol.IsSupported(productId);

    public async Task<RazerHeadsetEqualizerState> ReadEqualizerAsync(
        int bandCount, CancellationToken cancellationToken = default)
    {
        if (bandCount is not (0 or 10)) throw new ArgumentOutOfRangeException(nameof(bandCount));
        await _eqGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            byte? preset = _commands.EqPresetRead == 0 ? null : RazerGipHeadsetProtocol.ReadEqPreset(
                _productId, await ReadAsync(_commands.EqPresetRead, cancellationToken).ConfigureAwait(false));
            var bands = bandCount == 0 || !RazerGipHeadsetProtocol.SupportsEqBands(_productId) ? null
                : RazerGipHeadsetProtocol.ReadEqBands(_productId,
                    await ReadAsync(_commands.EqBandsRead, cancellationToken).ConfigureAwait(false));
            return new(preset, bands);
        }
        finally { _eqGate.Release(); }
    }

    public async Task<RazerHeadsetAudioState> ReadStateAsync(CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var firmware = await ProbeAsync(
            "firmware", _commands.Firmware,
            payload => RazerGipHeadsetProtocol.ReadFirmware(_productId, payload),
            errors, cancellationToken).ConfigureAwait(false);
        var battery = await ProbeAsync(
            "battery", _commands.Battery,
            payload => RazerGipHeadsetProtocol.ReadBatteryPercent(_productId, payload),
            errors, cancellationToken).ConfigureAwait(false);
        var charging = await ProbeAsync(
            "charging", _commands.Charging, ReadBoolean, errors, cancellationToken).ConfigureAwait(false);
        var link = await ProbeAsync(
            "link", _commands.Link, ReadBoolean, errors, cancellationToken).ConfigureAwait(false);

        var preset = _commands.EqPresetRead == 0
            ? default
            : await ProbeAsync(
                "EQ preset", _commands.EqPresetRead,
                payload => RazerGipHeadsetProtocol.ReadEqPreset(_productId, payload),
                errors, cancellationToken).ConfigureAwait(false);
        var bands = !RazerGipHeadsetProtocol.SupportsEqBands(_productId)
            ? default
            : await ProbeAsync(
                "EQ bands", _commands.EqBandsRead,
                payload => RazerGipHeadsetProtocol.ReadEqBands(_productId, payload),
                errors, cancellationToken).ConfigureAwait(false);

        byte? sidetone = null;
        if (_commands.SidetoneSwitchRead != 0)
        {
            var enabled = await ProbeAsync(
                "sidetone switch", _commands.SidetoneSwitchRead, ReadBoolean,
                errors, cancellationToken).ConfigureAwait(false);
            if (enabled.Ok)
            {
                if (!enabled.Value)
                {
                    sidetone = 0;
                }
                else
                {
                    var level = await ProbeAsync(
                        "sidetone level", _commands.SidetoneLevelRead, ReadByte,
                        errors, cancellationToken).ConfigureAwait(false);
                    if (level.Ok)
                    {
                        sidetone = (byte)Math.Round(level.Value * 100 / 255.0);
                    }
                }
            }
        }
        else if (_commands.SidetoneLevelRead != 0)
        {
            var level = await ProbeAsync(
                "sidetone", _commands.SidetoneLevelRead, ReadByte,
                errors, cancellationToken).ConfigureAwait(false);
            if (level.Ok)
            {
                sidetone = _dialect == RazerGipHeadsetDialect.Simone
                    ? level.Value == 0 ? (byte)0 : (byte)100
                    : (byte)Math.Round(level.Value * 100 / 255.0);
            }
        }

        RazerAncState? ancState = null;
        if (_commands.AncRead != 0)
        {
            var anc = await ProbeAsync(
                "ANC", _commands.AncRead, ReadByte, errors, cancellationToken).ConfigureAwait(false);
            if (anc.Ok && Enum.IsDefined(typeof(RazerAncState), anc.Value))
            {
                ancState = (RazerAncState)anc.Value;
            }
        }

        var dnd = _commands.DndRead == 0
            ? default
            : await ProbeAsync(
                "DND", _commands.DndRead, ReadBoolean, errors, cancellationToken).ConfigureAwait(false);

        return new RazerHeadsetAudioState(
            firmware.Ok ? firmware.Value : null,
            battery.Ok ? battery.Value : null,
            charging.Ok ? charging.Value : null,
            preset.Ok ? preset.Value : null,
            bands.Ok ? bands.Value : null,
            Bass: null,
            MicMuted: null,
            MicVolume: null,
            AudioSource: null,
            NoiseSuppression: null,
            AncState: ancState,
            AncLevel: null,
            Errors: errors)
        {
            Dnd = dnd.Ok ? dnd.Value : null,
            SidetonePercent = sidetone,
            MicReturnSwitch = sidetone is null ? null : sidetone > 0,
            LinkStatus = link.Ok ? link.Value ? (byte)1 : (byte)0 : null,
        };
    }

    public async Task<int?> TryReadBatteryPercentAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return RazerGipHeadsetProtocol.ReadBatteryPercent(
                _productId,
                await ReadAsync(_commands.Battery, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _diagnostic?.Invoke($"gip battery read failed: {exception.Message}");
            return null;
        }
    }

    public Task SetEqPresetAsync(byte preset, CancellationToken cancellationToken = default) =>
        SetEqualizerAsync(preset, null, cancellationToken);

    public Task SetEqBandsAsync(
        IReadOnlyList<byte> bands,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bands);
        return SetEqualizerAsync(null, bands, cancellationToken);
    }

    public Task SetCustomEqAsync(
        byte preset,
        IReadOnlyList<byte> bands,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bands);
        return SetEqualizerAsync(
            RazerGipHeadsetProtocol.SupportsCustomEqPreset(_productId) ? preset : null,
            bands, cancellationToken);
    }

    private async Task SetEqualizerAsync(
        byte? preset, IReadOnlyList<byte>? bands, CancellationToken cancellationToken)
    {
        // Copy and validate the caller's curve before any device mutation or await.
        var requestedBands = bands is null ? null
            : RazerGipHeadsetProtocol.CreateEqBandsPayload(_productId, bands);
        await _eqGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (preset.HasValue && _commands.EqPresetWrite == 0)
            {
                throw new NotSupportedException("This GIP family has no EQ preset command.");
            }

            var previousPreset = preset.HasValue
                ? await ReadEqPresetPayloadAsync(cancellationToken).ConfigureAwait(false) : null;
            var previousBands = requestedBands is not null
                ? await ReadEqBandsPayloadAsync(cancellationToken).ConfigureAwait(false) : null;
            var requestedPreset = preset.HasValue
                ? CreateEqPresetPayload(preset.Value, previousPreset!) : null;
            if (requestedBands is not null)
            {
                var prefix = requestedBands.Length - RazerGipHeadsetProtocol.EqBandCount(_productId);
                previousBands!.AsSpan(0, prefix).CopyTo(requestedBands);
            }

            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (requestedPreset is not null)
                {
                    await WriteAsync(_commands.EqPresetWrite, requestedPreset, cancellationToken)
                        .ConfigureAwait(false);
                    await VerifyEqPresetAsync(requestedPreset, cancellationToken).ConfigureAwait(false);
                }
                if (requestedBands is not null)
                {
                    await WriteAsync(_commands.EqBandsWrite, requestedBands, cancellationToken)
                        .ConfigureAwait(false);
                    await VerifyEqBandsAsync(requestedBands, cancellationToken).ConfigureAwait(false);
                    if (requestedPreset is not null)
                    {
                        await VerifyEqPresetAsync(requestedPreset, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception failure)
            {
                var rollbackFailures = new List<Exception>();
                // Restore the curve before selecting the original preset; attempt both
                // even if one restore fails, and do not reuse a cancelled caller token.
                if (previousBands is not null)
                {
                    try
                    {
                        await WriteAsync(_commands.EqBandsWrite, previousBands, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    catch (Exception exception) { rollbackFailures.Add(exception); }
                }
                if (previousPreset is not null)
                {
                    try
                    {
                        await WriteAsync(_commands.EqPresetWrite,
                            CreateEqPresetPayload(RazerGipHeadsetProtocol.ReadEqPreset(_productId, previousPreset),
                                previousPreset), CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception exception) { rollbackFailures.Add(exception); }
                }
                if (previousBands is not null)
                {
                    try { await VerifyEqBandsAsync(previousBands, CancellationToken.None).ConfigureAwait(false); }
                    catch (Exception exception) { rollbackFailures.Add(exception); }
                }
                if (previousPreset is not null)
                {
                    try { await VerifyEqPresetAsync(previousPreset, CancellationToken.None).ConfigureAwait(false); }
                    catch (Exception exception) { rollbackFailures.Add(exception); }
                }
                if (rollbackFailures.Count > 0)
                {
                    throw new IOException("GIP EQ write failed and rollback could not be confirmed; hardware EQ state is uncertain.",
                        new AggregateException(new[] { failure }.Concat(rollbackFailures)));
                }
                throw;
            }
        }
        finally { _eqGate.Release(); }
    }

    private async Task<byte[]> ReadEqPresetPayloadAsync(CancellationToken cancellationToken)
    {
        var payload = await ReadAsync(_commands.EqPresetRead, cancellationToken).ConfigureAwait(false);
        _ = RazerGipHeadsetProtocol.ReadEqPreset(_productId, payload);
        if (_dialect == RazerGipHeadsetDialect.ValkyrieT3 && payload.Length < 5)
        {
            throw new InvalidDataException("Valkyrie T3 EQ preset response is shorter than 5 bytes.");
        }
        return payload;
    }

    private async Task<byte[]> ReadEqBandsPayloadAsync(CancellationToken cancellationToken)
    {
        var payload = await ReadAsync(_commands.EqBandsRead, cancellationToken).ConfigureAwait(false);
        var bands = RazerGipHeadsetProtocol.ReadEqBands(_productId, payload);
        var length = RazerGipHeadsetProtocol.CreateEqBandsPayload(_productId, bands).Length;
        return payload[..length];
    }

    private byte[] CreateEqPresetPayload(byte preset, byte[] current)
    {
        switch (_dialect)
        {
            case RazerGipHeadsetDialect.Simone: return [0x00, preset];
            case RazerGipHeadsetDialect.SimoneX:
            case RazerGipHeadsetDialect.OdessaT1R: return [preset];
            case RazerGipHeadsetDialect.Protocol40: return [current[0], 1, 1, preset];
            case RazerGipHeadsetDialect.ValkyrieT3:
                var payload = current.ToArray();
                payload[3] = preset;
                return payload;
            default: throw new NotSupportedException("This GIP family has no EQ preset command.");
        }
    }

    private async Task VerifyEqPresetAsync(byte[] expected, CancellationToken cancellationToken)
    {
        var actual = await ReadEqPresetPayloadAsync(cancellationToken).ConfigureAwait(false);
        if (RazerGipHeadsetProtocol.ReadEqPreset(_productId, actual) !=
                RazerGipHeadsetProtocol.ReadEqPreset(_productId, expected) ||
            ((_dialect is RazerGipHeadsetDialect.Protocol40 or RazerGipHeadsetDialect.ValkyrieT3) &&
                actual[0] != expected[0]))
        {
            throw new IOException("GIP EQ preset readback did not match the requested preset/selector.");
        }
    }

    private async Task VerifyEqBandsAsync(byte[] expected, CancellationToken cancellationToken)
    {
        var actual = await ReadEqBandsPayloadAsync(cancellationToken).ConfigureAwait(false);
        if (!actual.SequenceEqual(expected))
        {
            throw new IOException("GIP EQ bands readback did not match the requested bands/selectors.");
        }
    }

    public Task SetBassAsync(byte level, CancellationToken cancellationToken = default) =>
        Task.FromException(new NotSupportedException(
            $"GIP product {_productId:X4} has no bass command."));

    public Task SetMicMuteAsync(bool muted, CancellationToken cancellationToken = default) =>
        Task.FromException(new NotSupportedException(
            $"GIP product {_productId:X4} has no microphone-mute command."));

    public async Task SetSidetoneAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        if (_commands.SidetoneLevelWrite == 0)
        {
            throw new NotSupportedException($"GIP product {_productId:X4} has no sidetone command.");
        }

        if (_commands.SidetoneSwitchWrite != 0)
        {
            await WriteAsync(
                _commands.SidetoneLevelWrite, [enabled ? (byte)255 : (byte)0], cancellationToken)
                .ConfigureAwait(false);
            await WriteAsync(
                _commands.SidetoneSwitchWrite, [enabled ? (byte)1 : (byte)0], cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        // Simone exposes monitoring as a boolean; the other families take 0..255.
        var value = _dialect == RazerGipHeadsetDialect.Simone
            ? enabled ? (byte)1 : (byte)0
            : enabled ? (byte)255 : (byte)0;
        await WriteAsync(_commands.SidetoneLevelWrite, [value], cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SetAncAsync(
        RazerAncState state,
        byte level,
        CancellationToken cancellationToken = default)
    {
        if (_commands.AncWrite == 0)
        {
            throw new NotSupportedException($"GIP product {_productId:X4} has no ANC command.");
        }
        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }

        // The GIP ANC command carries only the mode byte; the level is a UI concept
        // on the 64-byte MXIC channel.
        await WriteAsync(_commands.AncWrite, [(byte)state], cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SetAudioDndAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        if (_commands.DndWrite == 0)
        {
            throw new NotSupportedException($"GIP product {_productId:X4} has no DND command.");
        }

        byte[] payload = _dialect == RazerGipHeadsetDialect.ValkyrieT3
            ? [enabled ? (byte)1 : (byte)0, 0x00]
            : [enabled ? (byte)1 : (byte)0];
        await WriteAsync(_commands.DndWrite, payload, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync() => await _transport.DisposeAsync().ConfigureAwait(false);

    private async Task<byte[]> ReadAsync(ushort command, CancellationToken cancellationToken)
    {
        if (command == 0)
        {
            throw new NotSupportedException($"GIP product {_productId:X4} has no such read command.");
        }
        return await ExchangeAsync(command, [], cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteAsync(
        ushort command,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        if (command == 0)
        {
            throw new NotSupportedException($"GIP product {_productId:X4} has no such write command.");
        }
        await ExchangeAsync(command, payload, cancellationToken).ConfigureAwait(false);
    }

    private async Task<byte[]> ExchangeAsync(
        ushort command,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        var request = RazerGipHeadsetProtocol.CreateWrite(_productId, command, payload);
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            try
            {
                var response = await _transport.QueryAsync(
                    request,
                    frame => RazerGipHeadsetProtocol.TryReadPayload(
                        _productId, frame, command, out _, out _),
                    AttemptTimeout,
                    cancellationToken).ConfigureAwait(false);
                if (RazerGipHeadsetProtocol.TryReadPayload(
                        _productId, response, command, out var value, out _))
                {
                    return value.ToArray();
                }
            }
            catch (TimeoutException)
            {
            }

            if (attempt + 1 < Attempts)
            {
                await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new IOException(
            $"GIP command {command:X4} did not answer after {Attempts} attempts.");
    }

    private async Task<(bool Ok, T Value)> ProbeAsync<T>(
        string name,
        ushort command,
        PayloadParser<T> parse,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        if (command == 0)
        {
            return (false, default!);
        }

        try
        {
            return (true, parse(await ReadAsync(command, cancellationToken).ConfigureAwait(false)));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            errors.Add($"{name}: {exception.Message}");
            _diagnostic?.Invoke($"gip {name} read failed: {exception.Message}");
            return (false, default!);
        }
    }

    private static bool ReadBoolean(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
        {
            throw new InvalidDataException("GIP boolean response is empty.");
        }
        return payload[0] != 0;
    }

    private static byte ReadByte(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
        {
            throw new InvalidDataException("GIP byte response is empty.");
        }
        return payload[0];
    }

    private static bool IsExpected(Exception exception) =>
        exception is IOException or TimeoutException or InvalidDataException or
            NotSupportedException or Win32Exception;
}
