namespace OpenSynapse.Windows.Protocols;

public enum RazerRipsawChannel { Mic, Speaker, Hdmi, Pc, PcMic }

public sealed record RazerRipsawHdmiStatus(bool Connected, int Width, int Height, byte FramesPerSecond);

/// <summary>rzDeviceKateT2: report ID is already byte zero; no prefix, checksum or status header.</summary>
public static class RazerRipsawProtocol
{
    public const ushort VendorId = 0x1532;
    public const ushort ProductId = 0x0D03;
    public const int ReportLength = 17;
    public const byte ReportId = 0x5A;

    public static byte[] CreateInit() =>
        [90, 73, 84, 69, 32, 84, 101, 99, 104, 46, 32, 73, 110, 99, 46, 0, 0];

    public static byte[] CreateReadVolume() => CreateCommand(0x0B);
    public static byte[] CreateReadMute() => CreateCommand(0x26);
    public static byte[] CreateReadFirmware() => CreateCommand(0x16);
    public static byte[] CreateReadSerialHigh() => CreateCommand(0x18);
    public static byte[] CreateReadSerialLow() => CreateCommand(0x1A);
    public static byte[] CreateReadEdition() => CreateCommand(0x1C);
    public static byte[] CreateReadHdmiStatus() => CreateCommand(0x2D);

    public static byte[] CreateSetVolume(RazerRipsawChannel channel, byte volume)
    {
        var command = channel switch
        {
            RazerRipsawChannel.Speaker => 0x0C,
            RazerRipsawChannel.Mic => 0x0D,
            RazerRipsawChannel.Hdmi => 0x0E,
            RazerRipsawChannel.Pc => 0x0F,
            RazerRipsawChannel.PcMic => 0x10,
            _ => throw new ArgumentOutOfRangeException(nameof(channel)),
        };
        var report = CreateCommand((byte)command);
        report[7] = volume;
        return report;
    }

    public static byte[] CreateSetMute(ReadOnlySpan<byte> current, RazerRipsawChannel channel, bool muted)
    {
        ValidateResponse(current);
        if (!Enum.IsDefined(channel)) throw new ArgumentOutOfRangeException(nameof(channel));
        var report = CreateCommand(0x27);
        for (var i = 0; i < 5; i++) report[7 + i] = (byte)(current[6 + i] != 0 ? 1 : 0);
        report[7 + (int)channel] = (byte)(muted ? 1 : 0);
        return report;
    }

    public static byte[] ReadVolumes(ReadOnlySpan<byte> report)
    {
        ValidateResponse(report);
        return report[6..11].ToArray();
    }

    public static bool[] ReadMutes(ReadOnlySpan<byte> report) =>
        ReadVolumes(report).Select(value => value != 0).ToArray();

    public static RazerRipsawHdmiStatus ReadHdmiStatus(ReadOnlySpan<byte> report)
    {
        ValidateResponse(report);
        return new(report[6] != 0, report[7] | report[8] << 8, report[9] | report[10] << 8, report[11]);
    }

    public static string ReadSerial(ReadOnlySpan<byte> high, ReadOnlySpan<byte> low)
    {
        ValidateResponse(high);
        ValidateResponse(low);
        return new string(high[6..13].ToArray().Concat(low[6..14].ToArray())
            .Where(value => value is >= 32 and < 127).Select(value => (char)value).ToArray());
    }

    public static void ValidateResponse(ReadOnlySpan<byte> report)
    {
        // KateT2 accepts successful feature reads without command/status/checksum matching.
        if (report.Length != ReportLength || report[0] != ReportId)
            throw new InvalidDataException("Ripsaw HD requires a 17-byte feature report with ID 0x5A.");
    }

    private static byte[] CreateCommand(byte command)
    {
        var report = new byte[ReportLength];
        report[0] = ReportId;
        report[1] = command;
        return report;
    }
}
