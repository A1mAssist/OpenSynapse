namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Razer Kraken V4 wired 64-byte report family. The merged OpenRGB implementation
/// validates these frames on the USB-C path (PID 056B); the 2.4 GHz receiver follows
/// a separate, unvalidated path and is not exposed here.
/// </summary>
internal static class OpenRazerKrakenV4Protocol
{
    internal const int ReportLength = 64;
    internal const int LedCount = 9;

    private const byte ReportId = 0x02;
    private const byte TransactionId = 0x60;
    private const byte WiredFlag = 0x00;
    private const byte DirectCommandClass = 0x0F;
    private const byte DirectCommandId = 0x03;
    private const byte EncapsulatedCommandClass = 0x00;
    private const byte EncapsulatedCommandId = 0x00;

    internal static IReadOnlyList<byte[]> CreateOff() =>
        [CreateDirect(new OpenRazerColor(0, 0, 0))];

    internal static IReadOnlyList<byte[]> CreateStatic(OpenRazerColor color, byte? intensity = null) =>
        [CreateDirect(Scale(color, intensity))];

    internal static IReadOnlyList<byte[]> CreateWave(byte? intensity = null)
    {
        var reports = new List<byte[]> { CreateEncapsulated(0xC0, 0x04) };
        if (intensity is { } value)
        {
            reports.Add(CreateEncapsulated(0xC1, value));
        }
        return reports;
    }

    private static byte[] CreateDirect(OpenRazerColor color)
    {
        var report = CreateReport(DirectCommandClass, DirectCommandId, 5 + (LedCount * 3));
        report[14] = LedCount - 1;
        for (var index = 0; index < LedCount; index++)
        {
            var offset = 15 + (index * 3);
            report[offset] = color.Red;
            report[offset + 1] = color.Green;
            report[offset + 2] = color.Blue;
        }
        return report;
    }

    private static byte[] CreateEncapsulated(byte command, byte value)
    {
        var report = CreateReport(EncapsulatedCommandClass, EncapsulatedCommandId, 5);
        report[10] = command;
        report[12] = 0x01;
        report[13] = value;
        return report;
    }

    private static byte[] CreateReport(byte commandClass, byte commandId, int dataSize)
    {
        var report = new byte[ReportLength];
        report[0] = ReportId;
        report[2] = TransactionId;
        report[6] = checked((byte)dataSize);
        report[7] = commandClass;
        report[8] = commandId;
        report[9] = WiredFlag;
        // OpenRGB sends zero here and the wired device accepts the frame.
        return report;
    }

    private static OpenRazerColor Scale(OpenRazerColor color, byte? intensity)
    {
        if (intensity is not { } value || value == byte.MaxValue)
        {
            return color;
        }

        return new OpenRazerColor(ScaleChannel(color.Red, value),
            ScaleChannel(color.Green, value), ScaleChannel(color.Blue, value));
    }

    private static byte ScaleChannel(byte channel, byte intensity) =>
        checked((byte)((channel * intensity + 127) / 255));
}
