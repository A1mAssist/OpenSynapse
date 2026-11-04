namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Razer Kraken V4 wired 64-byte report family. Static colour follows the
/// audio bundle's Set Chroma Effect + Set Chroma Frame sequence: effect
/// C0 00 01 01, then 0F 03 with nine RGB triples. The custom frame carries
/// no checksum. Off and wave stay on the C0 effect command, with the
/// brightness set first.
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
    private const byte BrightnessCommandId = 0xC1;
    private const byte FrameCommandClass = 0x0F;
    private const byte FrameCommandId = 0x03;
    private const byte EffectOff = 0x00;
    private const byte EffectStatic = 0x01;
    private const byte EffectWave = 0x04;
    private const byte MaxBrightness = 0xFF;
    private const int LedCount = 9;

    private const int PayloadOffset = 10;
    private const int ChecksumOffset = ReportLength - 2;

    internal static IReadOnlyList<byte[]> CreateOff(bool isDongle = false) =>
        [CreateEffect(EffectOff, isDongle)];

    /// <summary>
    /// The bundle's connect handshake is the general command 02 00 00.
    /// </summary>
    internal static byte[] CreateConnect(bool isDongle = false) =>
        CreateGeneralReport([0x02, 0x00, 0x00], [], isDongle);

    internal static IReadOnlyList<byte[]> CreateStatic(
        OpenRazerColor color,
        byte? intensity = null,
        bool isDongle = false) =>
        [CreateEffect(EffectStatic, isDongle), CreateFrame(Scale(color, intensity), isDongle)];

    internal static IReadOnlyList<byte[]> CreateWave(byte? intensity = null, bool isDongle = false) =>
        [CreateBrightness(MaxBrightness, isDongle), CreateEffect(EffectWave, isDongle)];

    /// <summary>
    /// Set Chroma Effect is C0 00 &lt;payload length&gt;. Off and Wave carry
    /// only the effect id; the colour path uses the direct frame instead.
    /// </summary>
    private static byte[] CreateEffect(byte effectId, bool isDongle)
    {
        var data = new[] { effectId };
        return CreateGeneralReport(
            [EffectCommandId, EffectCommandClass, checked((byte)data.Length)],
            data,
            isDongle);
    }

    /// <summary>
    /// Set Chroma Effect Brightness is C1 00 01 with one byte from 0 to 255.
    /// </summary>
    private static byte[] CreateBrightness(byte brightness, bool isDongle) =>
        CreateGeneralReport([BrightnessCommandId, 0x00, 0x01], [brightness], isDongle);

    /// <summary>
    /// Set Chroma Frame is 0F 03. The arguments are five header bytes
    /// (0, 0, row, 0, column) followed by one RGB triple per LED. The custom
    /// frame builder leaves byte 62 at zero; it is not checksummed.
    /// </summary>
    private static byte[] CreateFrame(OpenRazerColor color, bool isDongle)
    {
        var report = new byte[ReportLength];
        report[0] = ReportId;
        report[2] = TransactionId;
        // The bundle's custom-frame builder includes the five-byte header in
        // its size field and adds the one-byte trailing count: 5 + 27 + 1.
        report[6] = checked((byte)(6 + (LedCount * 3)));
        report[7] = FrameCommandClass;
        report[8] = FrameCommandId;
        report[9] = isDongle ? DongleFlag : WiredFlag;
        report[PayloadOffset + 0] = 0;
        report[PayloadOffset + 1] = 0;
        report[PayloadOffset + 2] = 0;
        report[PayloadOffset + 3] = 0;
        report[PayloadOffset + 4] = LedCount - 1;

        for (var index = 0; index < LedCount; index++)
        {
            var offset = PayloadOffset + 5 + (index * 3);
            report[offset] = color.Red;
            report[offset + 1] = color.Green;
            report[offset + 2] = color.Blue;
        }

        return report;
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
