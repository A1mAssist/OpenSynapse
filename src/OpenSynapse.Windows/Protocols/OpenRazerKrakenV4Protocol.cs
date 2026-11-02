namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Razer Kraken V4 wired 64-byte report family, matching the "Billie" headset
/// implementation in Razer's own product bundle. Every frame is an output report
/// with the report id in byte 0, the audio transaction id in byte 2, the command
/// class and id in bytes 7 and 8, and the payload from byte 10. General commands
/// carry an XOR checksum in byte 62.
/// </summary>
internal static class OpenRazerKrakenV4Protocol
{
    internal const int ReportLength = 64;

    private const byte ReportId = 0x02;
    private const byte TransactionId = 0x60;
    private const byte WiredFlag = 0x00;
    private const byte DongleFlag = 0x80;

    private const byte EffectCommandClass = 0x00;
    private const byte EffectCommandId = 0xC0;
    private const byte EffectOff = 0x00;
    private const byte EffectStatic = 0x01;
    private const byte EffectWave = 0x04;

    private const int PayloadOffset = 10;
    private const int ChecksumOffset = ReportLength - 2;

    internal static IReadOnlyList<byte[]> CreateOff(bool isDongle = false) =>
        [CreateEffect(EffectOff, null, isDongle)];

    /// <summary>
    /// The bundle's connect handshake is the general command 02 00 00. Synapse
    /// sends it before it issues any lighting command.
    /// </summary>
    internal static byte[] CreateConnect(bool isDongle = false) =>
        CreateGeneralReport([0x02, 0x00, 0x00], [], isDongle);

    internal static IReadOnlyList<byte[]> CreateStatic(
        OpenRazerColor color,
        byte? intensity = null,
        bool isDongle = false) =>
        [CreateEffect(EffectStatic, Scale(color, intensity), isDongle)];

    internal static IReadOnlyList<byte[]> CreateWave(byte? intensity = null, bool isDongle = false) =>
        [CreateEffect(EffectWave, null, isDongle)];

    /// <summary>
    /// Set Chroma Effect is C0 00 &lt;payload length&gt;. Static carries the
    /// effect id plus the RGB colour; Off and Wave carry only the effect id.
    /// </summary>
    private static byte[] CreateEffect(
        byte effectId,
        OpenRazerColor? color,
        bool isDongle)
    {
        var data = color is { } value
            ? new byte[] { effectId, value.Red, value.Green, value.Blue }
            : new byte[] { effectId };
        return CreateGeneralReport(
            [EffectCommandId, EffectCommandClass, checked((byte)data.Length)],
            data,
            isDongle);
    }

    /// <summary>
    /// The general command shape carries its command header in the payload instead
    /// of the class and id bytes, and its size counts both the header and the data.
    /// </summary>
    private static byte[] CreateGeneralReport(
        IReadOnlyList<byte> command,
        IReadOnlyList<byte> data,
        bool isDongle)
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

        report[9] = isDongle ? DongleFlag : WiredFlag;
        report[ChecksumOffset] = CalculateChecksum(report);
        return report;
    }

    /// <summary>
    /// The general command builder XORs every byte before the checksum slot and
    /// writes the result to byte 62.
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
