namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Razer Kraken V4 wired 64-byte report family, matching the "Billie" headset
/// implementation in Razer's own product bundle. Every frame is an output report
/// with the report id in byte 0, the audio transaction id in byte 2, the command
/// class and id in bytes 7 and 8, the payload from byte 10, and an XOR checksum
/// over bytes 0..61 in byte 62. The firmware drops a frame whose checksum is
/// wrong, so the checksum is part of the frame, not an optional extra.
/// </summary>
internal static class OpenRazerKrakenV4Protocol
{
    internal const int ReportLength = 64;
    internal const int LedCount = 9;

    private const byte ReportId = 0x02;
    private const byte TransactionId = 0x60;
    private const byte WiredFlag = 0x00;

    /// <summary>
    /// "Set Chroma Frame". The bundle's command table pairs that name with 0F:03;
    /// 0F:10 is "Force Chroma Control", a different command.
    /// </summary>
    private const byte FrameCommandClass = 0x0F;
    private const byte FrameCommandId = 0x03;

    /// <summary>Chroma command class 0x00, "Get/Set Chroma Effect".</summary>
    private const byte EffectCommandClass = 0x00;
    private const byte GetEffectCommandId = 0xC0;
    private const byte SetEffectCommandId = 0xC1;

    /// <summary>
    /// The effect command travels as a three byte header in the payload, the same
    /// shape the firmware uses for the other general commands. Set Chroma Effect is
    /// declared as C0 00 01 in the bundle's command table.
    /// </summary>
    private static readonly byte[] SetEffectHeader = [GetEffectCommandId, 0x00, 0x01];

    private const byte EffectOff = 0x00;
    private const byte EffectStatic = 0x01;
    private const byte EffectWave = 0x04;

    private const int PayloadOffset = 10;
    private const int ChecksumOffset = ReportLength - 2;

    internal static IReadOnlyList<byte[]> CreateOff() =>
    [
        CreateEffect(EffectOff),
        CreateFrame(0, LedCount - 1, new OpenRazerColor(0, 0, 0)),
    ];

    internal static IReadOnlyList<byte[]> CreateStatic(OpenRazerColor color, byte? intensity = null) =>
    [
        CreateEffect(EffectStatic),
        CreateFrame(0, LedCount - 1, Scale(color, intensity)),
    ];

    internal static IReadOnlyList<byte[]> CreateWave(byte? intensity = null) =>
    [
        CreateEffect(EffectWave),
    ];

    /// <summary>
    /// "Set Chroma Frame" takes the row and column bounds followed by the colour
    /// bytes. The bounds travel in the payload, so a one row frame starts at
    /// column zero and the colours follow.
    /// </summary>
    private static byte[] CreateFrame(int row, int column, OpenRazerColor color)
    {
        var payload = new List<byte> { 0x00, 0x00, (byte)row, 0x00, (byte)column };
        for (var led = 0; led < LedCount; led++)
        {
            payload.Add(color.Red);
            payload.Add(color.Green);
            payload.Add(color.Blue);
        }

        return CreateReport(FrameCommandClass, FrameCommandId, payload);
    }

    private static byte[] CreateEffect(byte effectId) =>
        CreateGeneralReport(SetEffectHeader, [effectId]);

    private static byte[] CreateReport(byte commandClass, byte commandId, IReadOnlyList<byte> payload)
    {
        var report = new byte[ReportLength];
        report[0] = ReportId;
        report[2] = TransactionId;
        report[6] = checked((byte)(payload.Count + 1));
        report[7] = commandClass;
        report[8] = commandId;
        report[9] = WiredFlag;
        for (var index = 0; index < payload.Count; index++)
        {
            report[PayloadOffset + index] = payload[index];
        }

        report[ChecksumOffset] = CalculateChecksum(report);
        return report;
    }

    /// <summary>
    /// The general command shape carries its command header in the payload instead
    /// of the class and id bytes, and its size counts both the header and the data.
    /// </summary>
    private static byte[] CreateGeneralReport(IReadOnlyList<byte> command, IReadOnlyList<byte> data)
    {
        var report = new byte[ReportLength];
        report[0] = ReportId;
        report[2] = TransactionId;
        report[6] = checked((byte)(command.Count + data.Count + 1));
        for (var index = 0; index < command.Count; index++)
        {
            report[PayloadOffset + index] = command[index];
        }

        for (var index = 0; index < data.Count; index++)
        {
            report[PayloadOffset + command.Count + index] = data[index];
        }

        report[ChecksumOffset] = CalculateChecksum(report);
        return report;
    }

    /// <summary>
    /// The firmware XORs every byte before the checksum slot and compares the
    /// result with byte 62.
    /// </summary>
    private static byte CalculateChecksum(IReadOnlyList<byte> report)
    {
        byte checksum = 0;
        for (var index = 0; index < ChecksumOffset; index++)
        {
            checksum ^= report[index];
        }

        return checksum;
    }

    private static OpenRazerColor Scale(OpenRazerColor color, byte? intensity)
    {
        if (intensity is not { } value || value == byte.MaxValue)
        {
            return color;
        }

        return new OpenRazerColor(ScaleChannel(color.Red, value),
            ScaleChannel(color.Green, value),
            ScaleChannel(color.Blue, value));
    }

    private static byte ScaleChannel(byte channel, byte intensity) =>
        checked((byte)((channel * intensity + 127) / 255));
}
