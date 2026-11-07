namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Razer Kraken V4 wired 64-byte report family, matching the "Billie" headset
/// implementation in Razer's own product bundle. Every frame is an output report
/// with the report id in byte 0, the audio transaction id in byte 2, the command
/// class and id in bytes 7 and 8, and the payload from byte 10. The bundle's
/// general commands carry an XOR checksum in byte 62.
/// </summary>
internal static class RazerHidKrakenV4Protocol
{
    internal const int ReportLength = 64;

    private const byte ReportId = 0x02;
    private const byte TransactionId = 0x60;
    private const byte WiredFlag = 0x00;
    private const byte DongleFlag = 0x80;

    /// <summary>Set Chroma Effect starts with C0 00; byte 12 is the payload length.</summary>
    private const byte GetEffectCommandId = 0xC0;

    /// <summary>Force Chroma Control is E8 00 01 in the request.</summary>
    private const byte ForceChromaCommandId = 0xE8;

    /// <summary>Set Chroma Brightness is C1 00 01 in the request.</summary>
    private const byte BrightnessCommandId = 0xC1;

    private static readonly byte[] ForceChromaHeader = [ForceChromaCommandId, 0x00, 0x01];
    private static readonly byte[] BrightnessHeader = [BrightnessCommandId, 0x00, 0x01];

    private const byte EffectOff = 0x00;
    private const byte EffectStatic = 0x01;
    private const byte EffectWave = 0x04;

    private const int PayloadOffset = 10;
    private const int ChecksumOffset = ReportLength - 2;

    internal static IReadOnlyList<byte[]> CreateOff(bool isDongle = false) =>
    [
        // Billie T2 only changes the effect mode; the frame engine is not involved.
        CreateEffect(EffectOff, isDongle),
    ];

    /// <summary>
    /// The bundle's connect handshake is the general command 02 00 00. Synapse
    /// sends it before it issues any lighting command.
    /// </summary>
    internal static byte[] CreateConnect(bool isDongle = false) =>
        CreateGeneralReport([0x02, 0x00, 0x00], [], isDongle);

    internal static IReadOnlyList<byte[]> CreateStatic(
        RazerHidColor color,
        byte? intensity = null,
        bool isDongle = false) =>
    [
        // The official static path carries the colour in the effect command itself.
        CreateEffect(EffectStatic, isDongle, Scale(color, intensity)),
    ];

    internal static IReadOnlyList<byte[]> CreateWave(byte? intensity = null, bool isDongle = false) =>
    [
        CreateEffect(EffectWave, isDongle),
    ];

    /// <summary>
    /// Takes the headset out of its onboard effect and gives the host control of
    /// the Chroma ring. Billie T2 exposes this as E8 00 01.
    /// </summary>
    internal static byte[] CreateForceChroma(bool enabled, bool isDongle = false) =>
        CreateGeneralReport(ForceChromaHeader, [enabled ? (byte)0x01 : (byte)0x00], isDongle);

    /// <summary>
    /// Sets the ring brightness as a percentage. Billie T2 maps it to one byte;
    /// the ring renders nothing while that byte is zero.
    /// </summary>
    internal static byte[] CreateBrightness(int percent, bool isDongle = false)
    {
        var clamped = Math.Clamp(percent, 0, 100);
        return CreateGeneralReport(
            BrightnessHeader,
            [checked((byte)Math.Round(255d * clamped / 100, MidpointRounding.AwayFromZero))],
            isDongle);
    }

    /// <summary>
    /// Billie T2 answers every general command. Custom frames are fire-and-forget,
    /// so only the general shape has to have its reply drained.
    /// </summary>
    internal static bool ExpectsResponse(ReadOnlySpan<byte> report) =>
        report.Length == ReportLength && report[7] == 0x00 && report[8] == 0x00;

    /// <summary>
    /// A general-command reply echoes the request's command byte at 10 and the
    /// response id 0x01 at 11. The transaction id keeps audio replies out.
    /// </summary>
    internal static bool IsCommandResponse(ReadOnlySpan<byte> request, ReadOnlySpan<byte> response) =>
        request.Length == ReportLength &&
        response.Length == ReportLength &&
        response[0] == ReportId &&
        response[2] == TransactionId &&
        response[10] == request[10] &&
        response[11] == 0x01;

    private static byte[] CreateEffect(byte effectId, bool isDongle, RazerHidColor? color = null)
    {
        var data = color is { } value
            ? new byte[] { effectId, value.Red, value.Green, value.Blue }
            : [effectId];
        var header = new byte[] { GetEffectCommandId, 0x00, checked((byte)data.Length) };
        return CreateGeneralReport(header, data, isDongle);
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
    /// writes the result to byte 62. Custom frames do not use this path.
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

    private static RazerHidColor Scale(RazerHidColor color, byte? intensity)
    {
        if (intensity is not { } value || value == byte.MaxValue)
        {
            return color;
        }

        return new RazerHidColor(ScaleChannel(color.Red, value),
            ScaleChannel(color.Green, value),
            ScaleChannel(color.Blue, value));
    }

    private static byte ScaleChannel(byte channel, byte intensity) =>
        checked((byte)((channel * intensity + 127) / 255));
}
