namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Razer Kraken V4 wired 64-byte report family, matching the "Billie" headset
/// implementation in Razer's own product bundle. Every frame is an output report
/// with the report id in byte 0, the audio transaction id in byte 2, the command
/// class and id in bytes 7 and 8, and the payload from byte 10. The bundle's
/// general commands carry an XOR checksum in byte 62; its custom frames use the
/// 0F:03 class and leave that slot zero.
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

    private const byte EffectStatic = 0x01;
    // AudioMabelT3C and AudioBillieT2 both read these ids from the bundle's
    // {Off:0, Static:1, Breathing:2, SpectrumCycling:3, Wave:4, CustomFrame:8} table.
    private const byte EffectBreathing = 0x02;
    private const byte EffectSpectrumCycling = 0x03;
    private const byte EffectWave = 0x04;

    private const byte FrameCommandClass = 0x0F;
    private const byte FrameCommandId = 0x03;
    private const int FrameCommandLength = 5;
    private const int PayloadOffset = 10;
    private const int ChecksumOffset = ReportLength - 2;
    private const int MaxFrameLeds = (ReportLength - PayloadOffset - FrameCommandLength) / 3;

    /// <summary>The Kraken V4 ring carries nine LEDs.</summary>
    private const int LedCount = 9;

    internal static IReadOnlyList<byte[]> CreateOff(bool isDongle = false, int ledCount = LedCount) =>
    [
        // The firmware's Off effect starts a transition that swallows the next
        // static frame. Render black through Static so the next colour is a
        // normal static-to-static update.
        CreateCustomFrame(new RazerHidColor(0, 0, 0), ledCount, isDongle: isDongle)[0],
        CreateEffect(EffectStatic, isDongle),
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
        bool isDongle = false,
        int ledCount = LedCount) =>
    [
        // The bundle writes the colour through the frame buffer, then switches the
        // ring to the static effect. Static mode reads the frame, not C0's payload.
        CreateCustomFrame(color, ledCount, intensity, isDongle)[0],
        CreateEffect(EffectStatic, isDongle),
    ];

    internal static IReadOnlyList<byte[]> CreateWave(byte? intensity = null, bool isDongle = false) =>
    [
        CreateEffect(EffectWave, isDongle),
    ];

    internal static IReadOnlyList<byte[]> CreateBreathing(
        RazerHidColor color,
        byte? intensity = null,
        bool isDongle = false) =>
    [
        CreateEffect(EffectBreathing, isDongle, Scale(color, intensity)),
    ];

    internal static IReadOnlyList<byte[]> CreateSpectrum(bool isDongle = false) =>
    [
        CreateEffect(EffectSpectrumCycling, isDongle),
    ];

    /// <summary>
    /// AudioMabelT3C sends a single row: the bundle's frame command is
    /// [0, 0, rows - 1, 0, cols - 1] followed by the RGB triples, sent on the
    /// 0F:03 class without a checksum. A headset ring is one row, so the LED
    /// count is the column count.
    /// </summary>
    internal static IReadOnlyList<byte[]> CreateCustomFrame(
        RazerHidColor color,
        int ledCount,
        byte? intensity = null,
        bool isDongle = false)
    {
        if (ledCount is < 1 or > MaxFrameLeds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ledCount), ledCount, $"A frame carries between 1 and {MaxFrameLeds} LEDs.");
        }

        var scaled = Scale(color, intensity);
        var data = new byte[checked(ledCount * 3)];
        for (var index = 0; index < data.Length; index += 3)
        {
            data[index] = scaled.Red;
            data[index + 1] = scaled.Green;
            data[index + 2] = scaled.Blue;
        }

        return
        [
            CreateReport(
                [0x00, 0x00, 0x00, 0x00, checked((byte)(ledCount - 1))],
                data,
                isDongle,
                FrameCommandClass,
                FrameCommandId,
                checksum: false),
        ];
    }

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
        bool isDongle) =>
        CreateReport(command, data, isDongle, commandClass: 0x00, commandId: 0x00, checksum: true);

    private static byte[] CreateReport(
        IReadOnlyList<byte> command,
        IReadOnlyList<byte> data,
        bool isDongle,
        byte commandClass,
        byte commandId,
        bool checksum)
    {
        var report = new byte[ReportLength];
        report[0] = ReportId;
        report[2] = TransactionId;
        report[6] = checked((byte)(command.Count + data.Count + 1));
        report[7] = commandClass;
        report[8] = commandId;
        for (var index = 0; index < command.Count; index++)
        {
            report[PayloadOffset + index] = command[index];
        }

        for (var index = 0; index < data.Count; index++)
        {
            report[PayloadOffset + command.Count + index] = data[index];
        }

        report[9] = isDongle ? DongleFlag : WiredFlag;
        if (checksum)
        {
            report[ChecksumOffset] = CalculateChecksum(report);
        }

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
