namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Seiren V2 Pro/X (0542/0543) microphone commands. AudioCamyT1/T2 talk to the
/// device through an eight-byte HID report: F0 | read flag | command | value |
/// 00 00 00 | checksum. The checksum is the two's complement of the low byte of
/// bytes 0..6. Every byte below was read from RzNative_0542 1.0.25.0
/// (SetMic_HPF, GetMic_HPF, SetMic_Limiter, GetMic_Limiter, SetMic_Mute,
/// GetMic_Mute, SetMic_Volume, GetMic_Volume).
/// </summary>
public static class RazerCamyT1Protocol
{
    public const byte ReportId = 0xF0;
    public const byte ReadFlag = 0x10;
    public const int ReportLength = 8;

    public const byte MicVolume = 0x01;
    public const byte MicMute = 0x02;
    public const byte MicHighPassFilter = 0x03;
    public const byte MicLimiter = 0x04;

    public static byte[] CreateReadRequest(byte command) => Create(command, 0, read: true);

    public static byte[] CreateWriteRequest(byte command, byte value) =>
        Create(command, value, read: false);

    public static byte ParseValue(ReadOnlySpan<byte> response, byte expectedCommand)
    {
        if (response.Length < ReportLength)
        {
            throw new InvalidDataException(
                $"CamyT1 response must be {ReportLength} bytes, got {response.Length}.");
        }

        if (response[0] != ReportId || response[2] != expectedCommand)
        {
            throw new InvalidDataException(
                $"CamyT1 response header {response[0]:X2}:{response[2]:X2} does not match " +
                $"{ReportId:X2}:{expectedCommand:X2}.");
        }

        return response[3];
    }

    private static byte[] Create(byte command, byte value, bool read)
    {
        var report = new byte[ReportLength];
        report[0] = ReportId;
        report[1] = read ? ReadFlag : (byte)0;
        report[2] = command;
        report[3] = value;
        report[7] = (byte)(0x100 - ((ReportId + report[1] + command + value) & 0xFF));
        return report;
    }
}
