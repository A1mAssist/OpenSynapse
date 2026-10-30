using System.ComponentModel;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Client for the AudioAWKittyBLE headsets. Battery and the controls below are
/// implemented from the commands in the Razer bundle; unsupported commands are expected
/// to time out or reject and are hidden by the page.
/// </summary>
public sealed class RazerAwBleHeadsetService : IDisposable
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(500);

    private readonly IRazerBleTransport _transport;
    private readonly TimeSpan _timeout;
    private readonly Action<string>? _diagnostic;

    internal RazerAwBleHeadsetService(
        IRazerBleTransport transport,
        string deviceId,
        TimeSpan timeout,
        Action<string>? diagnostic = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        DeviceId = string.IsNullOrWhiteSpace(deviceId)
            ? throw new ArgumentException("A BLE device id is required.", nameof(deviceId))
            : deviceId;
        _timeout = timeout;
        _diagnostic = diagnostic;
    }

    public string DeviceId { get; }

    public static async Task<RazerAwBleHeadsetService> ConnectAsync(
        string deviceId,
        Action<string>? diagnostic = null,
        CancellationToken cancellationToken = default)
    {
        var transport = await WindowsRazerAwBleTransport.ConnectAsync(deviceId, cancellationToken)
            .ConfigureAwait(false);
        return new RazerAwBleHeadsetService(transport, deviceId, DefaultTimeout, diagnostic);
    }

    public async Task<RazerAwBatteryState> ReadBatteryAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _transport.ExchangeAsync(
                RazerAwBleProtocol.CreateBatteryRequest(),
                _timeout,
                cancellationToken).ConfigureAwait(false);
            if (response is null)
            {
                throw new InvalidOperationException("The headset did not answer the battery request.");
            }

            if (!RazerAwBleProtocol.TryParseBattery(response, out var state))
            {
                throw new InvalidDataException("The headset returned an invalid battery reply.");
            }

            _diagnostic?.Invoke(
                $"aw ble battery: headset={state.HeadsetPercent?.ToString() ?? "--"} " +
                $"right={state.RightEarbudPercent?.ToString() ?? "--"} " +
                $"left={state.LeftEarbudPercent?.ToString() ?? "--"}");
            return state;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            throw new InvalidOperationException($"Headset battery read failed: {exception.Message}", exception);
        }
    }

    public async Task<RazerAwFirmwareVersion> ReadFirmwareAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await ReadFrameAsync(
            RazerAwBleProtocol.FirmwareGetCommand, "firmware", cancellationToken, ProbeTimeout)
            .ConfigureAwait(false);
        if (!RazerAwBleProtocol.TryParseFirmware(response, out var version))
        {
            throw new InvalidDataException("The headset returned an invalid firmware reply.");
        }

        return version;
    }

    public async Task<string> ReadSerialAsync(CancellationToken cancellationToken = default)
    {
        var response = await ReadFrameAsync(
            RazerAwBleProtocol.SerialGetCommand, "serial number", cancellationToken, ProbeTimeout)
            .ConfigureAwait(false);
        if (!RazerAwBleProtocol.TryParseSerial(response, out var serial))
        {
            throw new InvalidDataException("The headset returned an invalid serial number reply.");
        }

        return serial;
    }

    public async Task<RazerAwConnectionState> ReadConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await ReadFrameAsync(
            RazerAwBleProtocol.ConnectionGetCommand, "connection", cancellationToken, ProbeTimeout)
            .ConfigureAwait(false);
        if (!RazerAwBleProtocol.TryParseConnection(response, out var state))
        {
            throw new InvalidDataException("The headset returned an invalid connection reply.");
        }

        return state;
    }

    public async Task<RazerAwChargingStatus> ReadChargingAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await ReadFrameAsync(
            RazerAwBleProtocol.ChargingGetCommand, "charging", cancellationToken, ProbeTimeout)
            .ConfigureAwait(false);
        if (!RazerAwBleProtocol.TryParseCharging(response, out var status))
        {
            throw new InvalidDataException("The headset returned an invalid charging reply.");
        }

        return status;
    }

    public async Task<byte> ReadVolumeAsync(CancellationToken cancellationToken = default)
    {
        var value = await ReadControlAsync(
            RazerAwBleProtocol.VolumeGetCommand, "volume", cancellationToken, ProbeTimeout)
            .ConfigureAwait(false);
        return value <= 100
            ? value
            : throw new InvalidDataException($"The headset returned an invalid volume level {value}.");
    }

    public Task<byte> SetVolumeAsync(int percent, CancellationToken cancellationToken = default) =>
        WriteControlAsync(
            RazerAwBleProtocol.VolumeGetCommand,
            RazerAwBleProtocol.VolumeSetCommand,
            (byte)Math.Clamp(percent, 0, 100),
            "volume",
            verifyStatus: false,
            cancellationToken);

    public async Task<byte> ReadSidetoneAsync(CancellationToken cancellationToken = default)
    {
        var raw = await ReadControlAsync(
            RazerAwBleProtocol.SidetoneGetCommand, "sidetone", cancellationToken, ProbeTimeout)
            .ConfigureAwait(false);
        return raw <= 15
            ? RazerAwBleProtocol.SidetoneRawToPercent(raw)
            : throw new InvalidDataException($"The headset returned an invalid sidetone level {raw}.");
    }

    public async Task<byte> SetSidetoneAsync(int percent, CancellationToken cancellationToken = default)
    {
        var applied = await WriteControlAsync(
            RazerAwBleProtocol.SidetoneGetCommand,
            RazerAwBleProtocol.SidetoneSetCommand,
            RazerAwBleProtocol.SidetonePercentToRaw(percent),
            "sidetone",
            verifyStatus: false,
            cancellationToken).ConfigureAwait(false);
        return RazerAwBleProtocol.SidetoneRawToPercent(applied);
    }

    public async Task<byte> ReadBassAsync(CancellationToken cancellationToken = default)
    {
        var value = await ReadControlAsync(
            RazerAwBleProtocol.BassGetCommand, "bass", cancellationToken, ProbeTimeout)
            .ConfigureAwait(false);
        return value <= 100
            ? value
            : throw new InvalidDataException($"The headset returned an invalid bass level {value}.");
    }

    public Task<byte> SetBassAsync(int percent, CancellationToken cancellationToken = default) =>
        WriteControlAsync(
            RazerAwBleProtocol.BassGetCommand,
            RazerAwBleProtocol.BassSetCommand,
            (byte)Math.Clamp(percent, 0, 100),
            "bass",
            verifyStatus: false,
            cancellationToken);

    public async Task<byte> ReadHapticsAsync(CancellationToken cancellationToken = default)
    {
        var value = await ReadControlAsync(
            RazerAwBleProtocol.HapticsGetCommand, "haptics", cancellationToken, ProbeTimeout)
            .ConfigureAwait(false);
        return value <= RazerAwBleProtocol.HapticsMaxLevel
            ? value
            : throw new InvalidDataException($"The headset returned an invalid haptic level {value}.");
    }

    public Task<byte> SetHapticsAsync(int level, CancellationToken cancellationToken = default) =>
        WriteControlAsync(
            RazerAwBleProtocol.HapticsGetCommand,
            RazerAwBleProtocol.HapticsSetCommand,
            (byte)Math.Clamp(level, RazerAwBleProtocol.HapticsMinLevel, RazerAwBleProtocol.HapticsMaxLevel),
            "haptics",
            verifyStatus: false,
            cancellationToken);

    public async Task<byte> ReadMicEqPresetAsync(CancellationToken cancellationToken = default)
    {
        var preset = await ReadControlAsync(
            RazerAwBleProtocol.MicEqPresetGetCommand,
            "mic EQ preset",
            cancellationToken,
            ProbeTimeout).ConfigureAwait(false);
        return RazerAwBleProtocol.IsKnownMicEqPreset(preset)
            ? preset
            : throw new InvalidDataException(
                $"The headset returned an unknown mic EQ preset {preset}.");
    }

    public Task<byte> SetMicEqPresetAsync(byte preset, CancellationToken cancellationToken = default)
    {
        if (!RazerAwBleProtocol.IsKnownMicEqPreset(preset))
        {
            return Task.FromException<byte>(
                new ArgumentOutOfRangeException(nameof(preset), preset, "Unknown mic EQ preset."));
        }

        return WriteControlAsync(
            RazerAwBleProtocol.MicEqPresetGetCommand,
            RazerAwBleProtocol.MicEqPresetSetCommand,
            preset,
            "mic EQ preset",
            verifyStatus: false,
            cancellationToken);
    }

    public async Task<bool> ReadGameModeAsync(CancellationToken cancellationToken = default) =>
        await ReadControlAsync(
            RazerAwBleProtocol.GameModeGetCommand, "game mode", cancellationToken, ProbeTimeout)
            .ConfigureAwait(false) != 0;

    public async Task<bool> SetGameModeAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        var applied = await WriteControlAsync(
            RazerAwBleProtocol.GameModeGetCommand,
            RazerAwBleProtocol.GameModeSetCommand,
            enabled ? (byte)1 : (byte)0,
            "game mode",
            verifyStatus: true,
            cancellationToken).ConfigureAwait(false);
        return applied != 0;
    }

    public Task<byte> ReadEqPresetAsync(CancellationToken cancellationToken = default) =>
        ReadControlAsync(
            RazerAwBleProtocol.EqPresetGetCommand, "EQ preset", cancellationToken, ProbeTimeout);

    public Task<byte> SetEqPresetAsync(byte preset, CancellationToken cancellationToken = default)
    {
        if (!RazerAwBleProtocol.IsKnownEqPreset(preset))
        {
            return Task.FromException<byte>(
                new ArgumentOutOfRangeException(nameof(preset), preset, "Unknown EQ preset."));
        }

        return WriteControlAsync(
            RazerAwBleProtocol.EqPresetGetCommand,
            RazerAwBleProtocol.EqPresetSetCommand,
            preset,
            "EQ preset",
            verifyStatus: true,
            cancellationToken);
    }

    public Task<IReadOnlyList<sbyte>> ReadCustomEqBandsAsync(
        CancellationToken cancellationToken = default) =>
        ReadCustomEqBandsCoreAsync(ProbeTimeout, cancellationToken);

    public async Task<IReadOnlyList<sbyte>> SetCustomEqBandsAsync(
        IReadOnlyList<sbyte> levels,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(levels);
        if (levels.Count != RazerAwBleProtocol.CustomEqBandCount)
        {
            throw new ArgumentException(
                $"Exactly {RazerAwBleProtocol.CustomEqBandCount} EQ bands are required.",
                nameof(levels));
        }

        var previous = await ReadCustomEqBandsCoreAsync(_timeout, cancellationToken)
            .ConfigureAwait(false);
        if (previous.SequenceEqual(levels))
        {
            return previous;
        }

        try
        {
            var acknowledgement = await _transport.ExchangeAsync(
                RazerAwBleProtocol.CreateEqBandsRequest(levels),
                _timeout,
                cancellationToken).ConfigureAwait(false);
            if (acknowledgement is null)
            {
                throw new InvalidOperationException(
                    "The headset did not acknowledge the custom EQ write.");
            }

            if (!RazerAwBleProtocol.IsReplyForCommand(
                    acknowledgement,
                    RazerAwBleProtocol.CustomEqBandsSetCommand))
            {
                throw new InvalidDataException(
                    "The headset returned an invalid custom EQ acknowledgement.");
            }

            var actual = await ReadCustomEqBandsCoreAsync(_timeout, cancellationToken)
                .ConfigureAwait(false);
            if (!actual.SequenceEqual(levels))
            {
                throw new InvalidOperationException(
                    "Custom EQ readback did not match the requested values.");
            }

            return actual;
        }
        catch
        {
            await TryRestoreEqBandsAsync(previous).ConfigureAwait(false);
            throw;
        }
    }

    public Task<byte> ReadAutoPowerOffMinutesAsync(CancellationToken cancellationToken = default) =>
        ReadControlAsync(
            RazerAwBleProtocol.AutoPowerOffGetCommand,
            "auto power-off",
            cancellationToken,
            ProbeTimeout);

    public Task<byte> SetAutoPowerOffMinutesAsync(
        byte minutes,
        CancellationToken cancellationToken = default) =>
        WriteControlAsync(
            RazerAwBleProtocol.AutoPowerOffGetCommand,
            RazerAwBleProtocol.AutoPowerOffSetCommand,
            minutes,
            "auto power-off",
            verifyStatus: true,
            cancellationToken);

    public async Task<byte> ReadChromaBrightnessAsync(CancellationToken cancellationToken = default)
    {
        var raw = await ReadControlAsync(
            RazerAwBleProtocol.ChromaBrightnessGetCommand,
            "Chroma brightness",
            cancellationToken,
            ProbeTimeout).ConfigureAwait(false);
        return RazerAwBleProtocol.ChromaRawToPercent(raw);
    }

    public async Task<byte> SetChromaBrightnessAsync(
        int percent,
        CancellationToken cancellationToken = default)
    {
        var applied = await WriteControlAsync(
            RazerAwBleProtocol.ChromaBrightnessGetCommand,
            RazerAwBleProtocol.ChromaBrightnessSetCommand,
            RazerAwBleProtocol.ChromaPercentToRaw(percent),
            "Chroma brightness",
            verifyStatus: false,
            cancellationToken).ConfigureAwait(false);
        return RazerAwBleProtocol.ChromaRawToPercent(applied);
    }

    public void Dispose() => _transport.Dispose();

    private async Task<byte[]> ReadFrameAsync(
        byte command,
        string label,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        try
        {
            var response = await _transport.ExchangeAsync(
                RazerAwBleProtocol.CreateReadRequest(command),
                timeout ?? _timeout,
                cancellationToken).ConfigureAwait(false);
            if (response is null)
            {
                throw new InvalidOperationException($"The headset did not answer the {label} request.");
            }

            return response;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            throw new InvalidOperationException(
                $"Headset {label} read failed: {exception.Message}", exception);
        }
    }

    private async Task<IReadOnlyList<sbyte>> ReadCustomEqBandsCoreAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var response = await ReadFrameAsync(
            RazerAwBleProtocol.CustomEqBandsGetCommand,
            "custom EQ bands",
            cancellationToken,
            timeout).ConfigureAwait(false);
        if (!RazerAwBleProtocol.TryParseEqBands(response, out var levels))
        {
            throw new InvalidDataException(
                "The headset returned an invalid custom EQ reply.");
        }

        return levels;
    }

    private async Task TryRestoreEqBandsAsync(IReadOnlyList<sbyte> levels)
    {
        try
        {
            var acknowledgement = await _transport.ExchangeAsync(
                RazerAwBleProtocol.CreateEqBandsRequest(levels),
                _timeout,
                CancellationToken.None).ConfigureAwait(false);
            if (acknowledgement is null)
            {
                _diagnostic?.Invoke("aw ble restore custom EQ bands: no acknowledgement");
            }
            else if (!RazerAwBleProtocol.IsReplyForCommand(
                         acknowledgement,
                         RazerAwBleProtocol.CustomEqBandsSetCommand))
            {
                _diagnostic?.Invoke("aw ble restore custom EQ bands: invalid acknowledgement");
            }
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _diagnostic?.Invoke($"aw ble restore custom EQ bands failed: {exception.Message}");
        }
    }

    private async Task<byte> ReadControlAsync(
        byte command,
        string label,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        try
        {
            var response = await _transport.ExchangeAsync(
                RazerAwBleProtocol.CreateReadRequest(command),
                timeout ?? _timeout,
                cancellationToken).ConfigureAwait(false);
            if (response is null)
            {
                throw new InvalidOperationException($"The headset did not answer the {label} request.");
            }

            if (!RazerAwBleProtocol.TryParseByteReply(response, command, out var value))
            {
                throw new InvalidDataException($"The headset returned an invalid {label} reply.");
            }

            return value;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            throw new InvalidOperationException($"Headset {label} read failed: {exception.Message}", exception);
        }
    }

    private async Task<byte> WriteControlAsync(
        byte getCommand,
        byte setCommand,
        byte value,
        string label,
        bool verifyStatus,
        CancellationToken cancellationToken)
    {
        var previous = await ReadControlAsync(getCommand, label, cancellationToken).ConfigureAwait(false);
        if (previous == value)
        {
            return previous;
        }

        try
        {
            var acknowledgement = await _transport.ExchangeAsync(
                RazerAwBleProtocol.CreateWriteRequest(setCommand, value),
                _timeout,
                cancellationToken).ConfigureAwait(false);
            if (acknowledgement is null)
            {
                throw new InvalidOperationException($"The headset did not acknowledge the {label} write.");
            }

            if (verifyStatus)
            {
                if (!RazerAwBleProtocol.TryParseWriteAck(acknowledgement, setCommand, out var status))
                {
                    throw new InvalidDataException(
                        $"The headset returned an invalid {label} acknowledgement.");
                }

                if (status != 0)
                {
                    throw new InvalidOperationException(
                        $"The headset rejected the {label} write (status {status}).");
                }
            }
            else if (!RazerAwBleProtocol.IsReplyForCommand(acknowledgement, setCommand))
            {
                throw new InvalidDataException(
                    $"The headset returned an invalid {label} acknowledgement.");
            }

            var actual = await ReadControlAsync(getCommand, label, cancellationToken).ConfigureAwait(false);
            if (actual != value)
            {
                throw new InvalidOperationException(
                    $"{label} readback did not match the requested value.");
            }

            return actual;
        }
        catch
        {
            await TryRestoreAsync(setCommand, previous, label).ConfigureAwait(false);
            throw;
        }
    }

    private async Task TryRestoreAsync(byte setCommand, byte value, string label)
    {
        try
        {
            var acknowledgement = await _transport.ExchangeAsync(
                RazerAwBleProtocol.CreateWriteRequest(setCommand, value),
                _timeout,
                CancellationToken.None).ConfigureAwait(false);
            if (acknowledgement is null)
            {
                _diagnostic?.Invoke($"aw ble restore {label}: no acknowledgement");
            }
            else if (!RazerAwBleProtocol.IsReplyForCommand(acknowledgement, setCommand))
            {
                _diagnostic?.Invoke($"aw ble restore {label}: invalid acknowledgement");
            }
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _diagnostic?.Invoke($"aw ble restore {label} failed: {exception.Message}");
        }
    }

    private static bool IsExpected(Exception exception) =>
        exception is IOException or InvalidDataException or InvalidOperationException or
            NotSupportedException or UnauthorizedAccessException or ArgumentException;
}
