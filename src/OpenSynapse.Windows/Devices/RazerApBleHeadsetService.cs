using System.Buffers.Binary;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed record RazerApBleState(
    int? BatteryPercent,
    bool? Charging,
    string? Firmware,
    string? Serial,
    ushort? ConnectionStatus,
    byte? AncMode,
    byte? AncLevel,
    byte? Volume,
    byte? Sidetone,
    byte? EqPreset,
    bool? GameMode,
    bool? ThxSpatial,
    bool? Multipoint,
    bool? Dnd,
    byte? ChromaBrightness,
    RazerApBleChromaEffect? ChromaEffect,
    ushort? AutoPowerOffSeconds,
    IReadOnlyList<string> Errors);

/// <summary>
/// AP-BLE client used by the Razer Audio 10.35 families (Valkyrie/Bastet/HTW/Noa/
/// Mabel/Elaine/...). Commands and payloads follow the AP-BLE report; unsupported
/// getters are reported as null so the page can hide that card.
/// </summary>
public sealed class RazerApBleHeadsetService : IDisposable
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(400);
    private static readonly IReadOnlySet<ushort> MainsPoweredProductIds = new HashSet<ushort>
    {
        0x0558, // Leviathan V2 Pro BLE alias
        0x0559, // Leviathan V2 X BLE alias
    };
    private static readonly string[] MainsPoweredNames =
    [
        "Leviathan", "Nommo", "Mako", "Seiren", "Soma",
    ];

    private readonly IRazerApBleTransport _transport;
    private readonly SemaphoreSlim _eqGate = new(1, 1);
    private readonly TimeSpan _timeout;
    private readonly Action<string>? _diagnostic;
    private byte _transaction;
    private byte _profile;

    internal RazerApBleHeadsetService(
        IRazerApBleTransport transport,
        TimeSpan timeout,
        Action<string>? diagnostic = null,
        byte profile = RazerApBleProtocol.DefaultProfile)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _timeout = timeout;
        _diagnostic = diagnostic;
        _profile = profile;
    }

    public static bool IsSupported(ushort productId) =>
        RazerHeadsetChannelCatalog.Resolve(productId) == RazerHeadsetChannel.ApBle;

    public static bool SupportsBattery(RazerBleDevice device) =>
        !MainsPoweredProductIds.Contains(device.ProductId) &&
        !MainsPoweredNames.Any(name => device.Name.Contains(name, StringComparison.OrdinalIgnoreCase));

    public static async Task<RazerApBleHeadsetService> ConnectAsync(
        string deviceId,
        Action<string>? diagnostic = null,
        CancellationToken cancellationToken = default)
    {
        var transport = await WindowsRazerApBleTransport.ConnectAsync(deviceId, cancellationToken)
            .ConfigureAwait(false);
        return new RazerApBleHeadsetService(transport, DefaultTimeout, diagnostic);
    }

    public async Task<RazerApBleState> ReadStateAsync(CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var battery = await ProbeAsync(
            "battery",
            async token =>
            {
                var payload = await ReadAsync(
                    RazerApBleProtocol.BatteryGet, [], "battery", token, ProbeTimeout)
                    .ConfigureAwait(false);
                return (Percent: RazerApBleProtocol.ReadBatteryPercent(payload),
                    Charging: RazerApBleProtocol.ReadCharging(payload));
            },
            errors, cancellationToken).ConfigureAwait(false);

        var firmware = await ProbeAsync(
            "firmware",
            async token => RazerApBleProtocol.ReadFirmware(
                await ReadAsync(
                    RazerApBleProtocol.FirmwareGet, [], "firmware", token, ProbeTimeout)
                    .ConfigureAwait(false)).ToString(),
            errors, cancellationToken).ConfigureAwait(false);

        var serial = await ProbeAsync(
            "serial",
            async token => RazerApBleProtocol.ReadSerial(
                await ReadAsync(
                    RazerApBleProtocol.SerialGet, [], "serial", token, ProbeTimeout)
                    .ConfigureAwait(false)),
            errors, cancellationToken).ConfigureAwait(false);

        var connection = await ProbeAsync(
            "connection",
            async token => RazerApBleProtocol.ReadConnection(
                await ReadAsync(
                    RazerApBleProtocol.ConnectionGet, [_profile], "connection", token, ProbeTimeout)
                    .ConfigureAwait(false)),
            errors, cancellationToken).ConfigureAwait(false);

        var anc = await ProbeAsync(
            "ANC",
            async token => RazerApBleProtocol.ReadAnc(
                await ReadAsync(
                    RazerApBleProtocol.AncGet, [_profile], "ANC", token, ProbeTimeout)
                    .ConfigureAwait(false)),
            errors, cancellationToken).ConfigureAwait(false);

        var volume = await ProbeAsync(
            "volume",
            async token => RazerApBleProtocol.ReadVolume(
                await ReadAsync(
                    RazerApBleProtocol.VolumeGet,
                    [_profile, (byte)RazerApBleAudioDeviceType.Speaker],
                    "volume",
                    token,
                    ProbeTimeout).ConfigureAwait(false)),
            errors, cancellationToken).ConfigureAwait(false);

        var sidetone = await ProbeAsync(
            "sidetone",
            async token => RazerApBleProtocol.ReadVolume(
                await ReadAsync(
                    RazerApBleProtocol.VolumeGet,
                    [_profile, (byte)RazerApBleAudioDeviceType.Sidetone],
                    "sidetone",
                    token,
                    ProbeTimeout).ConfigureAwait(false)),
            errors, cancellationToken).ConfigureAwait(false);

        var eq = await ProbeAsync(
            "EQ preset",
            token => ReadEqPresetAsync(
                RazerApBleAudioDeviceType.Speaker, token),
            errors, cancellationToken).ConfigureAwait(false);

        var gameMode = await ProbeAsync(
            "game mode",
            token => ReadBoolAsync(RazerApBleProtocol.GameModeGet, "game mode", token),
            errors, cancellationToken).ConfigureAwait(false);

        var thx = await ProbeAsync(
            "THX spatial",
            async token => RazerApBleProtocol.ReadThx(
                await ReadAsync(
                    RazerApBleProtocol.ThxGet, [_profile, (byte)RazerApBleAudioDeviceType.Speaker],
                    "THX spatial", token, ProbeTimeout).ConfigureAwait(false)),
            errors, cancellationToken).ConfigureAwait(false);

        var multipoint = await ProbeAsync(
            "multipoint",
            token => ReadBoolAsync(RazerApBleProtocol.MultipointGet, "multipoint", token),
            errors, cancellationToken).ConfigureAwait(false);

        var dnd = await ProbeAsync(
            "DND",
            token => ReadBoolAsync(RazerApBleProtocol.DndGet, "DND", token),
            errors, cancellationToken).ConfigureAwait(false);

        var brightness = await ProbeAsync(
            "Chroma brightness",
            async token => RazerApBleProtocol.ReadChromaBrightness(
                await ReadAsync(
                    RazerApBleProtocol.ChromaBrightnessGet, [], "Chroma brightness",
                    token, ProbeTimeout).ConfigureAwait(false)),
            errors, cancellationToken).ConfigureAwait(false);

        var effect = await ProbeAsync(
            "Chroma effect",
            async token =>
            {
                var payload = await ReadAsync(
                    RazerApBleProtocol.ChromaModeGet, [], "Chroma effect",
                    token, ProbeTimeout).ConfigureAwait(false);
                return payload.Length >= 1 && Enum.IsDefined(typeof(RazerApBleChromaEffect), payload[0])
                    ? (RazerApBleChromaEffect?)payload[0]
                    : null;
            },
            errors, cancellationToken).ConfigureAwait(false);

        var autoPowerOff = await ProbeAsync(
            "auto power off",
            async token =>
            {
                var payload = await ReadAsync(
                    RazerApBleProtocol.AutoPowerOffValueGet, [_profile], "auto power off",
                    token, ProbeTimeout).ConfigureAwait(false);
                return payload.Length >= 2 ? (ushort?)((payload[0] << 8) | payload[1]) : null;
            },
            errors, cancellationToken).ConfigureAwait(false);

        return new RazerApBleState(
            battery.Ok ? battery.Value.Percent : null,
            battery.Ok ? battery.Value.Charging : null,
            firmware.Ok ? firmware.Value : null,
            serial.Ok ? serial.Value : null,
            connection.Ok ? connection.Value : null,
            anc.Ok ? anc.Value.Mode : null,
            anc.Ok ? anc.Value.Level : null,
            volume.Ok ? volume.Value : null,
            sidetone.Ok ? sidetone.Value : null,
            eq.Ok ? eq.Value : null,
            gameMode.Ok ? gameMode.Value : null,
            thx.Ok ? thx.Value : null,
            multipoint.Ok ? multipoint.Value : null,
            dnd.Ok ? dnd.Value : null,
            brightness.Ok ? brightness.Value : null,
            effect.Ok ? effect.Value : null,
            autoPowerOff.Ok ? autoPowerOff.Value : null,
            errors);
    }

    public async Task<(int Percent, bool? Charging)> ReadBatteryAsync(
        CancellationToken cancellationToken = default)
    {
        var payload = await ReadAsync(
            RazerApBleProtocol.BatteryGet, [], "battery", cancellationToken, _timeout)
            .ConfigureAwait(false);
        return (RazerApBleProtocol.ReadBatteryPercent(payload),
            RazerApBleProtocol.ReadCharging(payload));
    }

    public Task SetAncAsync(byte mode, byte level, CancellationToken cancellationToken = default) =>
        WriteAsync(
            RazerApBleProtocol.AncSet,
            RazerApBleProtocol.CreateAncPayload(_profile, mode, level),
            "ANC",
            cancellationToken);

    public Task SetVolumeAsync(int percent, CancellationToken cancellationToken = default) =>
        WriteAsync(
            RazerApBleProtocol.VolumeSet,
            RazerApBleProtocol.CreateVolumePayload(
                _profile, RazerApBleAudioDeviceType.Speaker, percent),
            "volume",
            cancellationToken);

    public Task SetSidetoneAsync(int percent, CancellationToken cancellationToken = default) =>
        WriteAsync(
            RazerApBleProtocol.VolumeSet,
            RazerApBleProtocol.CreateVolumePayload(
                _profile, RazerApBleAudioDeviceType.Sidetone, percent),
            "sidetone",
            cancellationToken);

    public Task SetMuteAsync(bool muted, CancellationToken cancellationToken = default) =>
        WriteAsync(
            RazerApBleProtocol.MuteSet,
            RazerApBleProtocol.CreateMutePayload(
                _profile, RazerApBleAudioDeviceType.Speaker, muted),
            "mute",
            cancellationToken);

    public async Task SetEqPresetAsync(
        byte preset,
        RazerApBleAudioDeviceType type = RazerApBleAudioDeviceType.Speaker,
        byte group = 0,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(type)) throw new ArgumentOutOfRangeException(nameof(type));
        var requested = RazerApBleProtocol.CreateEqPresetPayload(type, group, preset);
        await _eqGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var previous = await ReadEqPresetPayloadAsync(type, requested[1], _timeout, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await WriteAndVerifyEqPresetAsync(requested, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                try
                {
                    // GET includes a leading reserved/profile byte; SET does not.
                    await WriteAndVerifyEqPresetAsync(previous[1..4], CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception rollbackFailure)
                {
                    throw new IOException("AP-BLE EQ write failed and rollback could not be confirmed; hardware EQ state is uncertain.",
                        new AggregateException(failure, rollbackFailure));
                }
                throw;
            }
        }
        finally { _eqGate.Release(); }
    }

    private async Task WriteAndVerifyEqPresetAsync(byte[] requested, CancellationToken cancellationToken)
    {
        await WriteAsync(RazerApBleProtocol.EqPresetSet, requested, "EQ preset", cancellationToken)
            .ConfigureAwait(false);
        var actual = await ReadEqPresetPayloadAsync(
            (RazerApBleAudioDeviceType)requested[0], requested[1], _timeout, cancellationToken)
            .ConfigureAwait(false);
        if (actual[3] != requested[2])
        {
            throw new IOException("AP-BLE EQ preset readback did not match the requested preset.");
        }
    }

    public Task SetGameModeAsync(bool enabled, CancellationToken cancellationToken = default) =>
        WriteAsync(
            RazerApBleProtocol.GameModeSet,
            [_profile, enabled ? (byte)1 : (byte)0],
            "game mode",
            cancellationToken);

    public Task SetThxAsync(bool enabled, CancellationToken cancellationToken = default) =>
        WriteAsync(
            RazerApBleProtocol.ThxSet,
            RazerApBleProtocol.CreateThxPayload(_profile, enabled),
            "THX spatial",
            cancellationToken);

    public Task SetMultipointAsync(bool enabled, CancellationToken cancellationToken = default) =>
        WriteAsync(
            RazerApBleProtocol.MultipointSet,
            [_profile, enabled ? (byte)1 : (byte)0],
            "multipoint",
            cancellationToken);

    public Task SetDndAsync(bool enabled, CancellationToken cancellationToken = default) =>
        WriteAsync(
            RazerApBleProtocol.DndSet,
            [_profile, enabled ? (byte)1 : (byte)0],
            "DND",
            cancellationToken);

    public Task SetMicNoiseReductionAsync(
        bool enabled,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            RazerApBleProtocol.MicNoiseReductionSet,
            [_profile, enabled ? (byte)1 : (byte)0],
            "mic noise reduction",
            cancellationToken);

    public Task SetVoicePromptAsync(bool enabled, CancellationToken cancellationToken = default) =>
        WriteAsync(
            RazerApBleProtocol.VoicePromptSet,
            [_profile, enabled ? (byte)1 : (byte)0],
            "voice prompt",
            cancellationToken);

    public Task SetGestureAsync(bool enabled, CancellationToken cancellationToken = default) =>
        WriteAsync(
            RazerApBleProtocol.GestureSet,
            [_profile, enabled ? (byte)1 : (byte)0],
            "gesture",
            cancellationToken);

    public Task SetFitTestAsync(bool enabled, CancellationToken cancellationToken = default) =>
        WriteAsync(
            RazerApBleProtocol.FitTestSet,
            [_profile, enabled ? (byte)1 : (byte)0],
            "fit test",
            cancellationToken);

    public Task SetPlayPauseAsync(bool playing, CancellationToken cancellationToken = default) =>
        WriteAsync(
            RazerApBleProtocol.PlayPauseSet,
            [_profile, playing ? (byte)1 : (byte)0],
            "play/pause",
            cancellationToken);

    public Task SetAudioSourceAsync(
        RazerApBleAudioSource source,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            RazerApBleProtocol.AudioSourceSet,
            [_profile, (byte)source],
            "audio source",
            cancellationToken);

    public Task SetChromaBrightnessAsync(
        int percent,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            RazerApBleProtocol.ChromaBrightnessSet,
            RazerApBleProtocol.CreateChromaBrightnessPayload(_profile, percent),
            "Chroma brightness",
            cancellationToken);

    public Task SetChromaEffectAsync(
        RazerApBleEffectRequest effect,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            RazerApBleProtocol.ChromaModeSet,
            RazerApBleProtocol.CreateChromaEffectPayload(
                _profile, effect.Effect, effect.Pattern, effect.DirectionSpeed, effect.Colors),
            "Chroma effect",
            cancellationToken);

    public Task SetAutoPowerOffAsync(
        int seconds,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            RazerApBleProtocol.AutoPowerOffValueSet,
            RazerApBleProtocol.CreateAutoPowerOffValuePayload(seconds),
            "auto power off",
            cancellationToken);

    public Task FactoryResetAsync(CancellationToken cancellationToken = default) =>
        WriteAsync(RazerApBleProtocol.FactoryReset, [], "factory reset", cancellationToken);

    public Task<byte> ReadSpeakerEqPresetAsync(CancellationToken cancellationToken = default) =>
        ReadEqPresetAsync(RazerApBleAudioDeviceType.Speaker, cancellationToken);

    private async Task<byte> ReadEqPresetAsync(
        RazerApBleAudioDeviceType type,
        CancellationToken cancellationToken)
    {
        await _eqGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return (await ReadEqPresetPayloadAsync(type, null, ProbeTimeout, cancellationToken)
                .ConfigureAwait(false))[3];
        }
        finally { _eqGate.Release(); }
    }

    private async Task<byte[]> ReadEqPresetPayloadAsync(
        RazerApBleAudioDeviceType type, byte? group, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var payload = await ReadAsync(
            RazerApBleProtocol.EqPresetGet,
            group.HasValue ? [_profile, (byte)type, group.Value] : [_profile, (byte)type],
            "EQ preset",
            cancellationToken,
            timeout).ConfigureAwait(false);
        if (payload.Length < 4)
        {
            throw new InvalidDataException("AP-BLE EQ preset reply is too short.");
        }
        if (payload[1] != (byte)type || (group.HasValue && payload[2] != group.Value))
        {
            throw new InvalidDataException("AP-BLE EQ preset reply targets a different device type/group.");
        }
        return payload;
    }

    private async Task<bool> ReadBoolAsync(
        ushort command,
        string label,
        CancellationToken cancellationToken)
    {
        var payload = await ReadAsync(
            command, [_profile], label, cancellationToken, ProbeTimeout).ConfigureAwait(false);
        return RazerApBleProtocol.ReadBoolean(payload);
    }

    private async Task<byte[]> ReadAsync(
        ushort command,
        byte[] payload,
        string label,
        CancellationToken cancellationToken,
        TimeSpan timeout)
    {
        var reply = await SendAsync(command, payload, label, cancellationToken, timeout)
            .ConfigureAwait(false);
        return reply.Payload;
    }

    private async Task WriteAsync(
        ushort command,
        byte[] payload,
        string label,
        CancellationToken cancellationToken)
    {
        _diagnostic?.Invoke(
            $"ap-ble set {label}: command=0x{command:X4} payload={Convert.ToHexString(payload)}");
        await SendAsync(command, payload, label, cancellationToken, _timeout)
            .ConfigureAwait(false);
    }

    private async Task<RazerApBleResponse> SendAsync(
        ushort command,
        byte[] payload,
        string label,
        CancellationToken cancellationToken,
        TimeSpan timeout)
    {
        var transaction = NextTransactionId();
        var frame = RazerApBleProtocol.CreateFrame(_profile, transaction, command, payload);
        var reply = await _transport.ExchangeAsync(frame, timeout, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"AP-BLE {label} did not answer transaction {transaction}.");
        if (reply.IsUnsupported)
        {
            throw new NotSupportedException(
                $"AP-BLE {label} is not supported (status 0x{reply.Status:X2}).");
        }
        if (!reply.IsSuccess)
        {
            throw new InvalidOperationException(
                $"AP-BLE {label} failed with status 0x{reply.Status:X2}.");
        }

        return reply;
    }

    private byte NextTransactionId()
    {
        _transaction = _transaction >= RazerApBleProtocol.MaximumTransactionId
            ? (byte)1
            : (byte)(_transaction + 1);
        return _transaction;
    }

    private async Task<(bool Ok, T Value)> ProbeAsync<T>(
        string label,
        Func<CancellationToken, Task<T>> read,
        List<string> errors,
        CancellationToken cancellationToken,
        bool required = false)
    {
        try
        {
            return (true, await read(cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (required)
            {
                errors.Add($"{label}: {exception.Message}");
            }
            _diagnostic?.Invoke($"ap-ble read {label} failed: {exception.Message}");
            return (false, default!);
        }
    }

    public void Dispose() => _transport.Dispose();
}

public readonly record struct RazerApBleEffectRequest(
    RazerApBleChromaEffect Effect,
    byte Pattern,
    byte DirectionSpeed,
    byte[] Colors);
