using System.Text;

namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Battery levels reported by the AudioAWKittyBLE family. Headset models return one
/// level; earbud models return right and left separately.
/// </summary>
public readonly record struct RazerAwBatteryState(
    byte? HeadsetPercent,
    byte? RightEarbudPercent,
    byte? LeftEarbudPercent);

public enum RazerAwHeadsetConnectionStatus : byte
{
    NoConnection = 0,
    BleConnected = 1,
    DongleConnected = 2,
}

public enum RazerAwEarbudConnectionStatus : byte
{
    ConnectedAsMaster = 0,
    ConnectedAsSlave = 1,
    NoConnection = 2,
}

public enum RazerAwChargingStatus : byte
{
    NotCharging = 0,
    Charging = 1,
    ChargingFailed = 2,
}

public enum RazerAwChromaEffect : byte
{
    Off = 0,
    Spectrum = 1,
    Breathing = 2,
    Static = 3,
    AudioMeter = 4,
    Streaming = 5,
    On = 255,
}

public readonly record struct RazerAwChromaState(
    RazerAwChromaEffect Effect,
    byte[] PrimaryColor,
    byte[] SecondaryColor);

public readonly record struct RazerAwConnectionState(
    RazerAwHeadsetConnectionStatus? Headset,
    RazerAwEarbudConnectionStatus? Right,
    RazerAwEarbudConnectionStatus? Left);

public readonly record struct RazerAwFirmwareVersion(
    byte Major,
    byte Minor,
    byte Internal,
    byte Reserved)
{
    public override string ToString() => $"{Major}.{Minor}.{Internal}.{Reserved}";
}

/// <summary>
/// Razer audio BLE protocol used by the AudioAWKittyBLE family (0309 / 0530 / 0534 /
/// 0562). Frames are tiny: a 3-byte header [command, 0x00 or 0x02, payloadLength]
/// followed by the payload. Battery is command 0x21; the reply is [0x21, 0x02, len,
/// payload...] on the notify characteristic. Control getters use [command, 0x00, 0x00]
/// and setters use [command, 0x00, 0x01, value].
/// </summary>
public static class RazerAwBleProtocol
{
    public const int CustomEqBandCount = 10;
    public const int HapticsMinLevel = 0;
    public const int HapticsMaxLevel = 6;

    public static readonly Guid ServiceUuid = new("416d656c-2d52-617a-6572-424c4501f40a");
    public static readonly Guid WriteCharacteristicUuid = new("416d0000-2d52-617a-6572-424c4501f40a");
    public static readonly Guid NotifyCharacteristicUuid = new("416d0001-2d52-617a-6572-424c4501f40a");

    public const byte SerialGetCommand = 0x00;
    public const byte FirmwareGetCommand = 0x02;
    public const byte VolumeGetCommand = 0x11;
    public const byte VolumeSetCommand = 0x91;
    public const byte SidetoneGetCommand = 0x19;
    public const byte SidetoneSetCommand = 0x99;
    public const byte GameModeGetCommand = 0x14;
    public const byte GameModeSetCommand = 0x94;
    public const byte EqPresetGetCommand = 0x13;
    public const byte EqPresetSetCommand = 0x93;
    public const byte AutoPowerOffGetCommand = 0x27;
    public const byte AutoPowerOffSetCommand = 0xA7;
    public const byte ChromaEffectGetCommand = 0x40;
    public const byte ChromaEffectSetCommand = 0xC0;
    public const byte ChromaBrightnessGetCommand = 0x41;
    public const byte ChromaBrightnessSetCommand = 0xC1;
    public const byte ChromaBreathingColorGetCommand = 0x42;
    public const byte ChromaBreathingColorSetCommand = 0xC2;
    public const byte ChromaStaticColorGetCommand = 0x43;
    public const byte ChromaStaticColorSetCommand = 0xC3;
    public const byte ChromaAudioMeterColorGetCommand = 0x45;
    public const byte ChromaAudioMeterColorSetCommand = 0xC5;
    public const byte BassGetCommand = 0x1B;
    public const byte BassSetCommand = 0x9B;
    public const byte HapticsGetCommand = 0x54;
    public const byte HapticsSetCommand = 0xD4;
    public const byte MicEqPresetGetCommand = 0x16;
    public const byte MicEqPresetSetCommand = 0x96;
    public const byte MicCustomEqBandsGetCommand = 0x17;
    public const byte MicCustomEqBandsSetCommand = 0x97;
    public const byte ConnectionGetCommand = 0x25;
    public const byte ChargingGetCommand = 0x29;
    public const byte CustomEqBandsGetCommand = 0x15;
    public const byte CustomEqBandsSetCommand = 0x95;

    public const byte DefaultEqPreset = 0;
    public const byte CustomEqPreset = 255;

    public const byte ResponseTransferType = 0x02;
    public const int ResponseHeaderLength = 3;

    public static byte[] CreateBatteryRequest() => [BatteryCommand, 0x00, 0x00];

    public const byte BatteryCommand = 0x21;

    public static byte[] CreateReadRequest(byte command) => [command, 0x00, 0x00];

    public static byte[] CreateWriteRequest(byte command, byte value) =>
        [command, 0x00, 0x01, value];

    public static byte[] CreateWriteRequest(byte command, ReadOnlySpan<byte> payload)
    {
        var frame = new byte[ResponseHeaderLength + payload.Length];
        frame[0] = command;
        frame[1] = 0x00;
        frame[2] = (byte)payload.Length;
        payload.CopyTo(frame.AsSpan(ResponseHeaderLength));
        return frame;
    }

    public static byte[] CreateEqBandsRequest(IReadOnlyList<sbyte> levels) =>
        CreateEqBandsRequest(CustomEqBandsSetCommand, levels);

    public static byte[] CreateMicEqBandsRequest(IReadOnlyList<sbyte> levels) =>
        CreateEqBandsRequest(MicCustomEqBandsSetCommand, levels);

    private static byte[] CreateEqBandsRequest(byte command, IReadOnlyList<sbyte> levels)
    {
        ArgumentNullException.ThrowIfNull(levels);
        if (levels.Count != CustomEqBandCount)
        {
            throw new ArgumentException(
                $"Exactly {CustomEqBandCount} EQ bands are required.", nameof(levels));
        }

        var payload = new byte[CustomEqBandCount];
        for (var index = 0; index < CustomEqBandCount; index++)
        {
            var level = levels[index];
            if (level is < -5 or > 5)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(levels), level, "EQ levels must be between -5 and +5 dB.");
            }

            payload[index] = (byte)(level + 5);
        }

        return CreateWriteRequest(command, payload);
    }

    /// <summary>
    /// Parses one notification. Payload length 1 is a headset level, length 2 is
    /// right/left earbud levels; anything else is not a battery reply.
    /// </summary>
    public static bool TryParseBattery(ReadOnlySpan<byte> notification, out RazerAwBatteryState state)
    {
        state = default;
        if (!TryGetPayload(notification, BatteryCommand, out var payload))
        {
            return false;
        }

        switch (payload.Length)
        {
            case 1:
                state = new RazerAwBatteryState(payload[0], null, null);
                return true;
            case 2:
                state = new RazerAwBatteryState(null, payload[0], payload[1]);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Parses a one-byte control reply such as the master-volume level or the
    /// auto-power-off minutes. The bundle reads <c>data[0]</c> for all of these.
    /// </summary>
    public static bool TryParseByteReply(
        ReadOnlySpan<byte> notification,
        byte command,
        out byte value)
    {
        value = 0;
        if (!TryGetPayload(notification, command, out var payload) || payload.Length != 1)
        {
            return false;
        }

        value = payload[0];
        return true;
    }

    public static readonly int[] AutoPowerOffMinuteOptions = [0, 5, 15, 30, 45, 60];

    /// <summary>
    /// The Kitty BLE bundle stores auto power-off as a five-step index, not a raw
    /// minute byte: 0 off, then 5/15/30/45/60 minutes.
    /// </summary>
    public static bool TryParseAutoPowerOff(
        ReadOnlySpan<byte> notification,
        out int minutes)
    {
        minutes = 0;
        if (!TryParseByteReply(notification, AutoPowerOffGetCommand, out var index) ||
            index >= AutoPowerOffMinuteOptions.Length)
        {
            return false;
        }

        minutes = AutoPowerOffMinuteOptions[index];
        return true;
    }

    public static byte AutoPowerOffIndexFromMinutes(int minutes)
    {
        if (minutes <= 0)
        {
            return 0;
        }

        if (minutes <= 10)
        {
            return 1;
        }

        if (minutes <= 20)
        {
            return 2;
        }

        if (minutes <= 40)
        {
            return 3;
        }

        return minutes <= 50 ? (byte)4 : (byte)5;
    }

    /// <summary>
    /// Setter replies carry the result code in <c>data[0]</c>; 0 is successful.
    /// </summary>
    public static bool TryParseWriteAck(
        ReadOnlySpan<byte> notification,
        byte command,
        out byte status) =>
        TryParseByteReply(notification, command, out status);

    /// <summary>
    /// Some setters return the applied value instead of a status byte; only the
    /// frame header is common to every reply.
    /// </summary>
    public static bool IsReplyForCommand(ReadOnlySpan<byte> notification, byte command) =>
        notification.Length >= ResponseHeaderLength &&
        notification[0] == command &&
        notification[1] == ResponseTransferType &&
        notification.Length >= ResponseHeaderLength + notification[2];

    public static bool TryParseFirmware(
        ReadOnlySpan<byte> notification,
        out RazerAwFirmwareVersion version)
    {
        version = default;
        if (!TryGetPayload(notification, FirmwareGetCommand, out var payload) ||
            payload.Length != 4)
        {
            return false;
        }

        version = new RazerAwFirmwareVersion(payload[0], payload[1], payload[2], payload[3]);
        return true;
    }

    public static bool TryParseSerial(ReadOnlySpan<byte> notification, out string serial)
    {
        serial = string.Empty;
        if (!TryGetPayload(notification, SerialGetCommand, out var payload) ||
            payload.Length == 0)
        {
            return false;
        }

        var builder = new StringBuilder(payload.Length);
        foreach (var value in payload)
        {
            if (value is >= 32 and < 127)
            {
                builder.Append((char)value);
            }
        }

        if (builder.Length == 0)
        {
            return false;
        }

        serial = builder.ToString();
        return true;
    }

    public static bool TryParseConnection(
        ReadOnlySpan<byte> notification,
        out RazerAwConnectionState state)
    {
        state = default;
        if (!TryGetPayload(notification, ConnectionGetCommand, out var payload))
        {
            return false;
        }

        if (payload.Length == 1)
        {
            if (payload[0] > (byte)RazerAwHeadsetConnectionStatus.DongleConnected)
            {
                return false;
            }

            state = new RazerAwConnectionState(
                (RazerAwHeadsetConnectionStatus)payload[0], null, null);
            return true;
        }

        if (payload.Length == 2)
        {
            if (payload[0] > (byte)RazerAwEarbudConnectionStatus.NoConnection ||
                payload[1] > (byte)RazerAwEarbudConnectionStatus.NoConnection)
            {
                return false;
            }

            state = new RazerAwConnectionState(
                null,
                (RazerAwEarbudConnectionStatus)payload[0],
                (RazerAwEarbudConnectionStatus)payload[1]);
            return true;
        }

        return false;
    }

    public static bool TryParseCharging(
        ReadOnlySpan<byte> notification,
        out RazerAwChargingStatus status)
    {
        status = default;
        if (!TryParseByteReply(notification, ChargingGetCommand, out var value) ||
            value > (byte)RazerAwChargingStatus.ChargingFailed)
        {
            return false;
        }

        status = (RazerAwChargingStatus)value;
        return true;
    }

    public static bool TryParseEqBands(
        ReadOnlySpan<byte> notification,
        out IReadOnlyList<sbyte> levels) =>
        TryParseEqBands(notification, CustomEqBandsGetCommand, out levels);

    public static bool TryParseMicEqBands(
        ReadOnlySpan<byte> notification,
        out IReadOnlyList<sbyte> levels) =>
        TryParseEqBands(notification, MicCustomEqBandsGetCommand, out levels);

    private static bool TryParseEqBands(
        ReadOnlySpan<byte> notification,
        byte command,
        out IReadOnlyList<sbyte> levels)
    {
        levels = [];
        if (!TryGetPayload(notification, command, out var payload) ||
            payload.Length != CustomEqBandCount)
        {
            return false;
        }

        var result = new sbyte[CustomEqBandCount];
        for (var index = 0; index < CustomEqBandCount; index++)
        {
            if (payload[index] > 10)
            {
                return false;
            }

            result[index] = (sbyte)(payload[index] - 5);
        }

        levels = result;
        return true;
    }

    public static byte SidetonePercentToRaw(int percent) =>
        (byte)(Math.Clamp(percent, 0, 100) * 15 / 100);

    public static byte SidetoneRawToPercent(byte raw) =>
        (byte)Math.Round(raw * 100d / 15d);

    public static byte ChromaPercentToRaw(int percent) =>
        (byte)(Math.Clamp(percent, 0, 100) * 255 / 100);

    public static byte ChromaRawToPercent(byte raw) =>
        (byte)Math.Round(raw * 100d / 255d);

    public static byte[] CreateChromaEffectRequest(RazerAwChromaEffect effect)
    {
        if (effect is RazerAwChromaEffect.Static or RazerAwChromaEffect.Breathing or
            RazerAwChromaEffect.AudioMeter)
        {
            throw new ArgumentException(
                "Static, breathing and audio-meter effects use their color command.",
                nameof(effect));
        }

        return CreateWriteRequest(ChromaEffectSetCommand, (byte)effect);
    }

    public static byte[] CreateChromaStaticColorRequest(ReadOnlySpan<byte> color)
    {
        RequireRgb(color, nameof(color));
        return CreateWriteRequest(ChromaStaticColorSetCommand, color);
    }

    public static byte[] CreateChromaBreathingColorRequest(
        ReadOnlySpan<byte> primary,
        ReadOnlySpan<byte> secondary)
    {
        RequireRgb(primary, nameof(primary));
        RequireRgb(secondary, nameof(secondary));
        var payload = new byte[6];
        primary.CopyTo(payload);
        secondary.CopyTo(payload.AsSpan(3));
        return CreateWriteRequest(ChromaBreathingColorSetCommand, payload);
    }

    public static byte[] CreateChromaAudioMeterColorRequest(ReadOnlySpan<byte> color)
    {
        RequireRgb(color, nameof(color));
        return CreateWriteRequest(ChromaAudioMeterColorSetCommand, color);
    }

    public static bool TryParseChromaEffect(
        ReadOnlySpan<byte> notification,
        out RazerAwChromaEffect effect,
        out byte[] inlineColor)
    {
        effect = default;
        inlineColor = [];
        if (!TryGetPayload(notification, ChromaEffectGetCommand, out var payload) ||
            payload.Length == 0)
        {
            return false;
        }

        var value = payload[0];
        if (value is not (0 or 1 or 2 or 3 or 4 or 5 or 255))
        {
            return false;
        }

        effect = (RazerAwChromaEffect)value;
        inlineColor = payload[1..].ToArray();
        return true;
    }

    public static bool TryParseChromaColor(
        ReadOnlySpan<byte> notification,
        byte command,
        int expectedLength,
        out byte[] color)
    {
        color = [];
        if (expectedLength is not (3 or 6) ||
            !TryGetPayload(notification, command, out var payload) ||
            payload.Length != expectedLength)
        {
            return false;
        }

        color = payload.ToArray();
        return true;
    }

    public static bool IsCustomEqPreset(byte preset) => preset is 254 or 255;

    public static bool IsKnownEqPreset(byte preset) =>
        preset <= 13 || preset is 254 or 255;

    public static bool IsKnownMicEqPreset(byte preset) =>
        preset is 0 or 1 or 2 or 3 or 255;

    private static void RequireRgb(ReadOnlySpan<byte> color, string parameterName)
    {
        if (color.Length != 3)
        {
            throw new ArgumentException("An RGB color needs exactly three bytes.", parameterName);
        }
    }

    private static bool TryGetPayload(
        ReadOnlySpan<byte> notification,
        byte command,
        out ReadOnlySpan<byte> payload)
    {
        payload = default;
        if (notification.Length < ResponseHeaderLength ||
            notification[0] != command ||
            notification[1] != ResponseTransferType)
        {
            return false;
        }

        var length = notification[2];
        if (notification.Length < ResponseHeaderLength + length)
        {
            return false;
        }

        payload = notification.Slice(ResponseHeaderLength, length);
        return true;
    }
}
