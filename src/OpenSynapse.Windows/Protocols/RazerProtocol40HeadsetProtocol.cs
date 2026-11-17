namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// protocol40 headset channel selected by rzDevice40 / rzDevice40Headset /
/// rzDevice40HeadsetAiroha. The bundle builds 64-byte report id 2 with the
/// 0x60 transaction at byte 2, class/id/size at 5..8, payload at 9 and XOR of
/// bytes 3..61 at 62. The Airoha 0592/0593 pair only exposes battery and charging;
/// rzDevice40 (05B9 and the Hammerhead V3 HyperSpeed family) adds class 0x18 EQ,
/// microphone, sidetone and DND reads.
/// </summary>
public static class RazerProtocol40HeadsetProtocol
{
    public const byte ReportId = 2;
    public const int ReportLength = 64;
    public const byte TransactionId = 0x60;
    public const byte StatusOk = 2;

    public static readonly byte[] GetBattery = [0x00, 0x07, 0x80];
    public static readonly byte[] GetCharging = [0x02, 0x07, 0x84];
    public static readonly byte[] GetConnect = [0x00, 0x00, 0x87];

    public static readonly byte[] GetEqPreset = [0x02, 0x18, 0x80];
    public static readonly byte[] GetEqBands = [0x02, 0x18, 0x81];
    public static readonly byte[] GetMicState = [0x03, 0x18, 0x84];
    public static readonly byte[] GetDnd = [0x02, 0x18, 0x85];

    private const byte LiveProfile = 0x00;
    private const byte SpeakerSource = 0x01;
    private const byte MicrophoneSource = 0x02;
    private const byte SidetoneSource = 0x03;

    public static bool IsSupported(ushort productId) =>
        productId is 0x0592 or 0x0593 or 0x05B9 or
            0x0A61 or 0x0A64 or 0x0A68 or 0x0A6C or 0x0A81;

    // 05B9's bundle proves EQ, microphone mute, sidetone and DND. The HyperSpeed
    // family shares the frame and EQ/DND read commands; its ANC and sidetone
    // flags need that PID's own bundle before they are exposed.
    public static bool SupportsEq(ushort productId) =>
        productId is 0x05B9 or 0x0A61 or 0x0A64 or 0x0A68 or 0x0A6C or 0x0A81;

    public static bool SupportsDnd(ushort productId) =>
        SupportsEq(productId);

    public static bool SupportsMic(ushort productId) => productId == 0x05B9;

    public static bool SupportsSidetone(ushort productId) => productId == 0x05B9;

    // rzDevice40Headset.connectDevice writes [0,0,135] once to wake the HID channel.
    public static byte[] CreateConnect() => CreateRequest(GetConnect);

    public static byte[] CreateGetBattery() => CreateRequest(GetBattery);

    public static byte[] CreateGetCharging() => CreateRequest(GetCharging, [0x00, 0x00]);

    public static byte[] CreateGetEqPreset() =>
        CreateRequest(GetEqPreset, [LiveProfile, SpeakerSource]);

    public static byte[] CreateGetEqBands() =>
        CreateRequest(GetEqBands, [LiveProfile, SpeakerSource]);

    public static byte[] CreateGetMicMute() =>
        CreateRequest(GetMicState, [LiveProfile, MicrophoneSource, 0x00]);

    public static byte[] CreateGetSidetone() =>
        CreateRequest(GetMicState, [LiveProfile, SidetoneSource, 0x00]);

    public static byte[] CreateGetDnd() =>
        CreateRequest(GetDnd, [LiveProfile]);

    // The Chroma engine ignores the 0x18 audio class. The bundle's
    // _createDataSend writes both the data size (3..4) and the packet size
    // (5..6) for an effect, while set-brightness leaves the data size at zero
    // and only announces the three-byte packet.
    public static byte[] CreateSetChromaEffect(
        byte effect,
        byte rate,
        ReadOnlySpan<byte> rgb)
    {
        if (rgb.Length % 3 != 0)
        {
            throw new ArgumentException("A Chroma colour array must be RGB triplets.", nameof(rgb));
        }

        Span<byte> data = stackalloc byte[6 + rgb.Length];
        data[0] = 0x00; // live profile
        data[1] = 0x00; // all-region
        data[2] = effect;
        data[3] = 0x00; // flags
        data[4] = rate;
        data[5] = (byte)(rgb.Length / 3);
        rgb.CopyTo(data[6..]);
        return CreateChromaWrite(0x02, data, declareSize: true);
    }

    public static byte[] CreateSetChromaBrightness(byte percent)
    {
        Span<byte> data = [0x00, 0x00, (byte)Math.Clamp(percent * 255 / 100, 0, 255)];
        return CreateChromaWrite(0x04, data, declareSize: false);
    }

    private static byte[] CreateChromaWrite(byte command, ReadOnlySpan<byte> data, bool declareSize)
    {
        var report = new byte[ReportLength];
        report[0] = ReportId;
        // deviceType 0 (Mouse): the bundle starts its transaction counter at 1.
        report[2] = 0x01;
        if (declareSize)
        {
            report[3] = (byte)(data.Length >> 8);
            report[4] = (byte)data.Length;
        }
        report[5] = (byte)(data.Length >> 8);
        report[6] = (byte)data.Length;
        report[7] = 0x0F;
        report[8] = command;
        data.CopyTo(report.AsSpan(9));
        report[62] = Checksum(report);
        return report;
    }

    public static byte[] CreateRequest(
        ReadOnlySpan<byte> command,
        ReadOnlySpan<byte> data = default)
    {
        if (command.Length != 3)
        {
            throw new ArgumentException(
                "A protocol40 headset command must be a three-byte triplet.", nameof(command));
        }

        if (9 + data.Length > ReportLength - 2)
        {
            throw new ArgumentException("protocol40 command and data exceed the 64-byte report.");
        }

        var report = new byte[ReportLength];
        report[0] = ReportId;
        report[2] = TransactionId;
        report[6] = command[0];
        report[7] = command[1];
        report[8] = command[2];
        data.CopyTo(report.AsSpan(9));
        report[62] = Checksum(report);
        return report;
    }

    public static bool TryReadValue(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request,
        out byte[] value)
    {
        value = [];
        if (response.Length < ReportLength ||
            request.Length < 9 ||
            response[0] != request[0] ||
            response[1] != StatusOk ||
            response[2] != request[2] ||
            response[7] != request[7] ||
            response[8] != request[8])
        {
            return false;
        }

        var length = response[6];
        if (length > ReportLength - 9)
        {
            return false;
        }

        value = response.Slice(9, length).ToArray();
        return true;
    }

    // The bundle reports both earbud levels and takes the lower one; a headset
    // reply carries the same level in both bytes.
    public static byte ReadBatteryPercent(ReadOnlySpan<byte> value)
    {
        Require(value, 2);
        var first = Math.Min(value[0], (byte)100);
        var second = Math.Min(value[1], (byte)100);
        return Math.Min(first, second);
    }

    public static bool ReadChargingStatus(ReadOnlySpan<byte> value)
    {
        Require(value, 2);
        return value[1] == 1;
    }

    // rzDevice40 replies echo [profile, source, ...].
    public static byte ReadEqPreset(ReadOnlySpan<byte> value)
    {
        Require(value, 4);
        return value[3];
    }

    public static byte[] ReadEqBands(ReadOnlySpan<byte> value)
    {
        Require(value, 12);
        return value[2..12].ToArray();
    }

    public static bool ReadMicMute(ReadOnlySpan<byte> value)
    {
        Require(value, 3);
        return value[2] == 1;
    }

    public static bool ReadSidetone(ReadOnlySpan<byte> value)
    {
        Require(value, 3);
        return value[2] == 1;
    }

    public static bool ReadDnd(ReadOnlySpan<byte> value)
    {
        Require(value, 2);
        return value[1] == 1;
    }

    // The bundle stores EQ levels as sign-magnitude bytes (0x80 | abs(level)).
    public static double DecodeEqBandLevel(byte level) =>
        (level & 0x80) != 0 ? -(level & 0x7F) : level;

    private static byte Checksum(ReadOnlySpan<byte> report)
    {
        byte checksum = 0;
        for (var index = 3; index < ReportLength - 2; index++)
        {
            checksum ^= report[index];
        }

        return checksum;
    }

    private static void Require(ReadOnlySpan<byte> value, int length)
    {
        if (value.Length < length)
        {
            throw new InvalidDataException(
                $"protocol40 response has {value.Length} data bytes; {length} required.");
        }
    }
}
