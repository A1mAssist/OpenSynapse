namespace OpenSynapse.Windows.Protocols;

internal static class OpenRazerArgbProtocol
{
    internal const int ReportLength = 320;
    internal const int MaximumLedCount = 105;

    internal static byte[] CreateFrame(byte channel, IReadOnlyList<OpenRazerColor> colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        if (colors.Count is < 1 or > MaximumLedCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(colors),
                $"ARGB frames must contain between 1 and {MaximumLedCount} colors.");
        }

        var report = new byte[ReportLength];
        report[0] = channel < 5 ? (byte)0x04 : (byte)0x84;
        report[1] = channel;
        report[2] = channel;
        report[4] = checked((byte)(colors.Count - 1));

        for (var index = 0; index < colors.Count; index++)
        {
            var color = colors[index];
            var offset = 5 + (index * 3);
            report[offset] = color.Red;
            report[offset + 1] = color.Green;
            report[offset + 2] = color.Blue;
        }

        return report;
    }
}
