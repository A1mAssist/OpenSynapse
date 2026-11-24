using System.Buffers.Binary;

namespace OpenSynapse.Windows.Protocols;

public enum RazerApBleStatus : byte
{
    New = 0,
    Busy = 1,
    Success = 2,
    Fail = 3,
    Timeout = 4,
    UnsupportedCommand = 5,
    UnsupportedProfile = 6,
    UnsupportedTarget = 7,
    GrabControl = 8,
    NotSupportedInCurrentMode = 9,
    Broadcast = 10,
    Unknown = 255,
}

public enum RazerApBleAudioDeviceType : byte
{
    Speaker = 1,
    Microphone = 2,
    Sidetone = 3,
}

public enum RazerApBleChromaEffect : byte
{
    Off = 0,
    Static = 1,
    Breathing = 2,
    Spectrum = 3,
    Wave = 4,
    Starlight = 7,
    Fire = 9,
    AudioMeter = 10,
    Wheel = 12,
}

public enum RazerApBleAudioSource : byte
{
    Bluetooth = 0,
    Dongle = 1,
    Usb = 2,
    Analog = 3,
}

public sealed record RazerApBleResponse(
    byte Status,
    byte Routing,
    byte TransactionId,
    ushort Command,
    byte[] Payload)
{
    public bool IsSuccess => Status == (byte)RazerApBleStatus.Success;

    public bool IsUnsupported =>
        Status is (byte)RazerApBleStatus.UnsupportedCommand or
            (byte)RazerApBleStatus.UnsupportedProfile or
            (byte)RazerApBleStatus.UnsupportedTarget or
            (byte)RazerApBleStatus.NotSupportedInCurrentMode;
}

/// <summary>
/// Razer Audio 10.35 AP-BLE: 00 | 60|profile | 00 | txn | len16BE | class | id |
/// payload | XOR(frame[2..]) | 00. Replies mirror the header with the status first.
/// </summary>
public static class RazerApBleProtocol
{
    public static readonly Guid GenericWriteUuid = new("416d0000-2d52-617a-6572-424c4501f40a");
    public static readonly Guid GenericNotifyUuid = new("416d0001-2d52-617a-6572-424c4501f40a");

    public static readonly Guid LegacyWriteUuid = new("52401524-f97c-7f90-0e7f-6c6f4e36db1c");
    public static readonly Guid LegacyNotifyUuid = new("52401525-f97c-7f90-0e7f-6c6f4e36db1c");
    public static readonly Guid LegacyExternalNotifyUuid = new("52401526-f97c-7f90-0e7f-6c6f4e36db1c");

    public static readonly Guid AirohaServiceUuid = new("e49a25e0-f69a-11e8-8eb2-f2801f1b9fd1");
    public static readonly Guid AirohaWriteUuid = new("50524000-2dab-0341-6972-6f6861424c45");
    public static readonly Guid AirohaNotifyUuid = new("43484152-2dab-3141-6972-6f6861424c45");
    public static readonly Guid AirohaNotify2Uuid = new("43484152-2dab-3241-6972-6f6861424c45");
    public static readonly Guid AirohaControlUuid = new("e49a25f8-f69a-11e8-8eb2-f2801f1b9fd1");
    public static readonly Guid AirohaExternalNotifyUuid = new("e49a28e1-f69a-11e8-8eb2-f2801f1b9fd1");

    /// <summary>Write/notify pairs seen on Razer audio GATT services.</summary>
    public static readonly (Guid Write, Guid Notify, Guid? External)[] GattLayouts =
    [
        (GenericWriteUuid, GenericNotifyUuid, null),
        (LegacyWriteUuid, LegacyNotifyUuid, LegacyExternalNotifyUuid),
        (AirohaWriteUuid, AirohaNotifyUuid, AirohaExternalNotifyUuid),
        (AirohaWriteUuid, AirohaNotify2Uuid, AirohaControlUuid),
        (AirohaWriteUuid, AirohaControlUuid, AirohaExternalNotifyUuid),
    ];

    public const int HeaderLength = 8;
    public const byte MaximumTransactionId = 31;
    public const byte DefaultProfile = 0;
    public const byte StereoState = 1;
    public const byte ThxSpatialState = 2;

    public const ushort FirmwareGet = 0x0087;
    public const ushort SerialGet = 0x0082;
    public const ushort BatteryGet = 0x0780;
    public const ushort ChargingGet = 0x0784;
    public const ushort ConnectionGet = 0x189A;

    public const ushort EqPresetGet = 0x1880;
    public const ushort EqPresetSet = 0x1800;
    public const ushort EqCustomGet = 0x1881;
    public const ushort EqCustomSet = 0x1801;
    public const ushort ThxGet = 0x1882;
    public const ushort ThxSet = 0x1802;
    public const ushort VolumeGet = 0x1883;
    public const ushort VolumeSet = 0x1803;
    public const ushort MuteGet = 0x1884;
    public const ushort MuteSet = 0x1804;
    public const ushort DndGet = 0x1885;
    public const ushort DndSet = 0x1805;
    public const ushort AncGet = 0x1886;
    public const ushort AncSet = 0x1806;
    public const ushort GameModeGet = 0x1889;
    public const ushort GameModeSet = 0x1809;
    public const ushort MultipointGet = 0x18A6;
    public const ushort MultipointSet = 0x1826;
    public const ushort MicNoiseReductionGet = 0x188F;
    public const ushort MicNoiseReductionSet = 0x180F;
    public const ushort VoicePromptGet = 0x1890;
    public const ushort VoicePromptSet = 0x1810;
    public const ushort GestureGet = 0x18A2;
    public const ushort GestureSet = 0x1822;
    public const ushort WearingGet = 0x189F;
    public const ushort FitTestGet = 0x18A0;
    public const ushort FitTestSet = 0x1820;
    public const ushort PlayPauseGet = 0x189E;
    public const ushort PlayPauseSet = 0x181E;
    public const ushort AudioSourceGet = 0x189D;
    public const ushort AudioSourceSet = 0x181D;
    public const ushort AutoPowerOffStatusGet = 0x0788;
    public const ushort AutoPowerOffStatusSet = 0x0708;
    public const ushort AutoPowerOffValueGet = 0x0783;
    public const ushort AutoPowerOffValueSet = 0x0703;
    public const ushort ChromaModeGet = 0x0F82;
    public const ushort ChromaModeSet = 0x0F02;
    public const ushort ChromaBrightnessGet = 0x0F84;
    public const ushort ChromaBrightnessSet = 0x0F04;
    public const ushort FactoryReset = 0x000B;

    public static byte ClassOf(ushort command) => (byte)(command >> 8);

    public static byte IdOf(ushort command) => (byte)command;

    public static byte[] CreateFrame(
        byte profile,
        byte transactionId,
        ushort command,
        ReadOnlySpan<byte> payload = default)
    {
        if (profile > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(profile), "AP-BLE profiles are 0..31.");
        }
        if (transactionId is 0 or > MaximumTransactionId)
        {
            throw new ArgumentOutOfRangeException(
                nameof(transactionId), "AP-BLE transactions are 1..31.");
        }
        if (payload.Length > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(payload), "AP-BLE payloads fit in 16 bits.");
        }

        var frame = new byte[HeaderLength + payload.Length + 2];
        frame[0] = 0x00;
        frame[1] = (byte)(0x60 | profile);
        frame[2] = 0x00;
        frame[3] = transactionId;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4), (ushort)payload.Length);
        frame[6] = ClassOf(command);
        frame[7] = IdOf(command);
        payload.CopyTo(frame.AsSpan(HeaderLength));

        var checksum = 0;
        for (var index = 2; index < HeaderLength + payload.Length; index++)
        {
            checksum ^= frame[index];
        }

        frame[^2] = (byte)checksum;
        frame[^1] = 0x00;
        return frame;
    }

    public static byte[] CreateRead(byte transactionId, ushort command, byte profile = DefaultProfile) =>
        CreateFrame(profile, transactionId, command);

    public static byte[] CreateSet(
        byte transactionId,
        ushort command,
        ReadOnlySpan<byte> payload,
        byte profile = DefaultProfile) =>
        CreateFrame(profile, transactionId, command, payload);

    /// <summary>Consumes one complete reply; returns false when the buffer only has a fragment.</summary>
    public static bool TryTakeResponse(List<byte> buffer, out RazerApBleResponse response)
    {
        response = default!;
        while (buffer.Count >= HeaderLength)
        {
            var length = (buffer[4] << 8) | buffer[5];
            var bodyEnd = HeaderLength + length;
            if (buffer.Count < bodyEnd)
            {
                return false;
            }

            // The iOS parser reads 8+len and ignores the tail. Accept the request-style
            // XOR+0x00 trailer when it is present, but do not require it.
            var total = bodyEnd;
            if (buffer.Count >= bodyEnd + 2 &&
                buffer[bodyEnd + 1] == 0 &&
                Checksum(buffer, 2, bodyEnd) == buffer[bodyEnd])
            {
                total = bodyEnd + 2;
            }

            var frame = buffer.GetRange(0, total).ToArray();
            buffer.RemoveRange(0, total);
            response = new RazerApBleResponse(
                frame[0],
                frame[1],
                frame[3],
                BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(6)),
                frame.AsSpan(HeaderLength, length).ToArray());
            return true;
        }

        return false;
    }

    private static byte Checksum(IReadOnlyList<byte> buffer, int start, int end)
    {
        var checksum = 0;
        for (var index = start; index < end; index++)
        {
            checksum ^= buffer[index];
        }

        return (byte)checksum;
    }

    public static int ReadBatteryPercent(ReadOnlySpan<byte> payload) =>
        payload.Length >= 2
            ? payload[1]
            : throw new InvalidDataException("AP-BLE battery reply is empty.");

    public static bool? ReadCharging(ReadOnlySpan<byte> payload) =>
        payload.Length >= 3 ? payload[2] == 1 : null;

    public static Version ReadFirmware(ReadOnlySpan<byte> payload) =>
        payload.Length >= 4
            ? new Version(payload[0], payload[1], payload[2], payload[3])
            : throw new InvalidDataException("AP-BLE firmware reply is too short.");

    public static string ReadSerial(ReadOnlySpan<byte> payload)
    {
        var chars = new List<char>(payload.Length);
        foreach (var value in payload)
        {
            if (value is >= 32 and < 127)
            {
                chars.Add((char)value);
            }
        }

        return new string(chars.ToArray());
    }

    /// <summary>Bool getters carry the value at data[1] (len &gt;= 2).</summary>
    public static bool ReadBoolean(ReadOnlySpan<byte> payload) =>
        payload.Length >= 2
            ? payload[1] != 0
            : throw new InvalidDataException("AP-BLE boolean reply is too short.");

    public static bool ReadThx(ReadOnlySpan<byte> payload) =>
        payload.Length >= 3
            ? payload[2] == 1
            : throw new InvalidDataException("AP-BLE THX reply is too short.");

    /// <summary>
    /// Returns the bitfield from the iOS parser: low byte = connected flag,
    /// high byte = ConnectionType.
    /// </summary>
    public static ushort ReadConnection(ReadOnlySpan<byte> payload) =>
        payload.Length >= 3
            ? (ushort)(payload[0] | (payload[2] << 8))
            : throw new InvalidDataException("AP-BLE connection reply is too short.");

    /// <summary>Volume getters carry the value at data[2] (len &gt;= 3).</summary>
    public static byte ReadVolume(ReadOnlySpan<byte> payload) =>
        payload.Length >= 3
            ? payload[2]
            : throw new InvalidDataException("AP-BLE volume reply is too short.");

    /// <summary>Chroma brightness carries the value at data[2] (len &gt;= 3).</summary>
    public static byte ReadChromaBrightness(ReadOnlySpan<byte> payload) =>
        payload.Length >= 3
            ? payload[2]
            : throw new InvalidDataException("AP-BLE brightness reply is too short.");

    public static (byte Mode, byte Level) ReadAnc(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 2)
        {
            throw new InvalidDataException("AP-BLE ANC reply is too short.");
        }

        // Wire layout is [profile, mode, level]; the two-byte form omits level.
        return (payload[1], payload.Length >= 3 ? payload[2] : (byte)0);
    }

    public static byte[] CreateAncPayload(byte profile, byte mode, byte level) =>
        [profile, mode, level];

    public static byte[] CreateVolumePayload(byte profile, RazerApBleAudioDeviceType type, int percent) =>
        [profile, (byte)type, (byte)Math.Clamp(percent, 0, 100)];

    public static byte[] CreateMutePayload(byte profile, RazerApBleAudioDeviceType type, bool muted) =>
        [profile, (byte)type, muted ? (byte)1 : (byte)0];

    public static byte[] CreateEqPresetPayload(RazerApBleAudioDeviceType type, byte group, byte preset) =>
        [(byte)type, group switch { 0 => (byte)0x00, 1 => (byte)0x01, 2 => (byte)0xFF, _ => group }, preset];

    public static byte[] CreateThxPayload(byte profile, bool enabled) =>
        [profile, (byte)RazerApBleAudioDeviceType.Speaker, enabled ? ThxSpatialState : StereoState];

    public static byte[] CreateChromaBrightnessPayload(byte profile, int percent) =>
        [profile, 0x00, (byte)Math.Clamp(percent, 0, 100)];

    public static byte[] CreateChromaEffectPayload(
        byte profile,
        RazerApBleChromaEffect effect,
        byte pattern,
        byte directionSpeed,
        ReadOnlySpan<byte> colors)
    {
        if (colors.Length % 3 != 0)
        {
            throw new ArgumentException("Chroma colors must be RGB triples.", nameof(colors));
        }

        var payload = new byte[6 + colors.Length];
        payload[0] = profile;
        payload[1] = 0x00;
        payload[2] = (byte)effect;
        payload[3] = pattern;
        payload[4] = directionSpeed;
        payload[5] = (byte)(colors.Length / 3);
        colors.CopyTo(payload.AsSpan(6));
        return payload;
    }

    public static byte[] CreateAutoPowerOffValuePayload(int seconds)
    {
        var clamped = Math.Clamp(seconds, 0, ushort.MaxValue);
        return [(byte)(clamped >> 8), (byte)clamped];
    }
}
