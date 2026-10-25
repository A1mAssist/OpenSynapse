namespace OpenSynapse.Windows.Protocols;

public enum RazerAudio25Status : byte
{
    Error = 0,
    Invalid = 1,
    Success = 2,
    Failure = 3,
    Busy = 4,
    Unsupported = 5,
}

public readonly record struct RazerAudio25Command(
    byte DataSize,
    byte CommandClass,
    byte CommandId,
    byte[] Arguments);

public readonly record struct RazerAudio25Response(
    byte ReportId,
    RazerAudio25Status Status,
    byte TransactionId,
    byte DataSize,
    byte CommandClass,
    byte CommandId,
    byte[] Data)
{
    public bool IsSuccess => Status == RazerAudio25Status.Success;
}

/// <summary>
/// Protocol 25 audio envelope used by the 65-byte Razer headset feature reports.
/// The request payload is 64 bytes; report id 0x07 is used on USB and 0x12 on BLE.
/// Byte 0 is status in a response, byte 1 is the transaction, byte 5 is data size,
/// bytes 6/7 are class/command, and arguments start at byte 8. Byte 62 is XOR checksum.
/// </summary>
public static class RazerAudio25Protocol
{
    public const byte UsbReportId = 0x07;
    public const byte BleReportId = 0x12;
    public const int ReportLength = 65;
    public const int PayloadLength = 64;
    public const int ArgumentsOffset = 8;
    public const int ChecksumOffset = 62;
    public const int MaxDataSize = ChecksumOffset - ArgumentsOffset;

    public const byte ReadProfile = 0x00;
    public const byte WriteProfile = 0x00;

    public static RazerAudio25Command GetFirmware() => new(2, 0x00, 0x81, []);
    public static RazerAudio25Command GetSerialNumber() => new(22, 0x00, 0x82, []);
    public static RazerAudio25Command GetDeviceMode() => new(2, 0x00, 0x84, []);
    public static RazerAudio25Command GetBatteryLevel() => new(2, 0x07, 0x80, []);
    public static RazerAudio25Command GetChargingStatus() => new(2, 0x07, 0x84, []);

    public static RazerAudio25Command GetEqPreset(byte profile = ReadProfile) =>
        new(2, 0x08, 0x82, [profile]);

    public static RazerAudio25Command SetEqPreset(byte preset, byte profile = WriteProfile) =>
        new(2, 0x08, 0x02, [profile, preset]);

    public static RazerAudio25Command GetEqBands(byte profile = ReadProfile, bool tenBand = true) =>
        new(tenBand ? (byte)11 : (byte)9, 0x08, 0x84, [profile]);

    public static RazerAudio25Command SetEqBands(
        ReadOnlySpan<byte> bands,
        byte profile = WriteProfile,
        bool tenBand = true)
    {
        var expected = tenBand ? 10 : 8;
        if (bands.Length != expected)
        {
            throw new ArgumentException($"Equalizer data must contain {expected} bands.", nameof(bands));
        }

        var arguments = new byte[expected + 1];
        arguments[0] = profile;
        bands.CopyTo(arguments.AsSpan(1));
        return new RazerAudio25Command((byte)arguments.Length, 0x08, 0x04, arguments);
    }

    public static RazerAudio25Command GetBass(byte profile = ReadProfile) =>
        new(2, 0x08, 0x87, [profile]);

    public static RazerAudio25Command SetBass(byte level, byte profile = WriteProfile) =>
        new(2, 0x08, 0x07, [profile, level]);

    public static RazerAudio25Command GetMicMute(byte profile = ReadProfile) =>
        new(2, 0x08, 0x88, [profile]);

    public static RazerAudio25Command SetMicMute(bool mute, byte profile = WriteProfile) =>
        new(2, 0x08, 0x08, [profile, mute ? (byte)1 : (byte)0]);

    public static RazerAudio25Command GetMicVolume(byte profile = ReadProfile) =>
        new(2, 0x08, 0x97, [profile]);

    public static RazerAudio25Command SetMicVolume(byte volume, byte profile = WriteProfile) =>
        new(2, 0x08, 0x17, [profile, volume]);

    public static RazerAudio25Command GetAudioSource(byte profile = ReadProfile) =>
        new(2, 0x08, 0x8A, [profile]);

    public static RazerAudio25Command SetAudioSource(byte source, byte profile = WriteProfile) =>
        new(2, 0x08, 0x0A, [profile, source]);

    public static RazerAudio25Command GetNoiseSuppression(byte profile = ReadProfile) =>
        new(2, 0x08, 0x9C, [profile]);

    public static RazerAudio25Command SetNoiseSuppression(bool enabled, byte profile = WriteProfile) =>
        new(2, 0x08, 0x1C, [profile, enabled ? (byte)1 : (byte)0]);

    public static RazerAudio25Command GetAnc() => new(1, 0x18, 0x86, []);

    public static RazerAudio25Command SetAnc(byte state, byte level, byte profile = WriteProfile) =>
        new(3, 0x18, 0x06, [profile, state, level]);

    public static byte[] CreateRequest(
        byte reportId,
        byte transactionId,
        RazerAudio25Command command)
    {
        if (command.DataSize > MaxDataSize)
        {
            throw new ArgumentOutOfRangeException(nameof(command), "The command does not fit the 64-byte payload.");
        }
        if (command.Arguments.Length < command.DataSize)
        {
            throw new ArgumentException("The command arguments are shorter than its declared data size.", nameof(command));
        }

        var payload = new byte[PayloadLength];
        payload[0] = 0x00;
        payload[1] = transactionId;
        payload[5] = command.DataSize;
        payload[6] = command.CommandClass;
        payload[7] = command.CommandId;
        command.Arguments.AsSpan(0, command.DataSize).CopyTo(payload.AsSpan(ArgumentsOffset));
        payload[ChecksumOffset] = CalculateChecksum(payload);

        var report = new byte[ReportLength];
        report[0] = reportId;
        payload.CopyTo(report.AsSpan(1));
        return report;
    }

    public static RazerAudio25Response ParseResponse(ReadOnlySpan<byte> report)
    {
        if (report.Length != ReportLength)
        {
            throw new InvalidDataException($"Protocol 25 response must be {ReportLength} bytes, got {report.Length}.");
        }

        var payload = report[1..];
        var dataSize = payload[5];
        if (dataSize > MaxDataSize)
        {
            throw new InvalidDataException($"Protocol 25 response declares {dataSize} data bytes.");
        }

        return new RazerAudio25Response(
            report[0],
            (RazerAudio25Status)payload[0],
            payload[1],
            dataSize,
            payload[6],
            payload[7],
            payload.Slice(ArgumentsOffset, dataSize).ToArray());
    }

    public static byte CalculateChecksum(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < ChecksumOffset)
        {
            throw new ArgumentException("The payload is too short for a protocol 25 checksum.", nameof(payload));
        }

        byte checksum = 0;
        for (var index = 0; index < ChecksumOffset; index++)
        {
            checksum ^= payload[index];
        }
        return checksum;
    }

    public static Version ReadFirmware(ReadOnlySpan<byte> data)
    {
        Require(data, 2);
        return new Version(data[0], data[1]);
    }

    public static byte ReadBatteryLevel(ReadOnlySpan<byte> data)
    {
        Require(data, 1);
        return data[0];
    }

    public static bool ReadChargingStatus(ReadOnlySpan<byte> data)
    {
        Require(data, 1);
        return data[0] != 0;
    }

    public static byte ReadEqPreset(ReadOnlySpan<byte> data)
    {
        Require(data, 2);
        return data[1];
    }

    public static byte[] ReadEqBands(ReadOnlySpan<byte> data, bool tenBand = true)
    {
        var expected = tenBand ? 10 : 8;
        Require(data, expected + 1);
        return data.Slice(1, expected).ToArray();
    }

    public static byte ReadBass(ReadOnlySpan<byte> data)
    {
        Require(data, 2);
        return data[1];
    }

    public static bool ReadMicMute(ReadOnlySpan<byte> data)
    {
        Require(data, 2);
        return data[1] != 0;
    }

    public static byte ReadMicVolume(ReadOnlySpan<byte> data)
    {
        Require(data, 2);
        return data[1];
    }

    public static byte ReadAudioSource(ReadOnlySpan<byte> data)
    {
        Require(data, 2);
        return data[1];
    }

    public static bool ReadNoiseSuppression(ReadOnlySpan<byte> data)
    {
        Require(data, 2);
        return data[1] != 0;
    }

    public static (byte? State, byte? Level) ReadAnc(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 3)
        {
            return (data[1], data[2]);
        }
        if (data.Length >= 2)
        {
            return (data[1], null);
        }
        if (data.Length >= 1)
        {
            return (data[0], null);
        }
        return (null, null);
    }

    private static void Require(ReadOnlySpan<byte> data, int length)
    {
        if (data.Length < length)
        {
            throw new InvalidDataException($"Protocol 25 response has {data.Length} data bytes; {length} required.");
        }
    }
}
