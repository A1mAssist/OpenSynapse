namespace OpenSynapse.Windows.Protocols;

internal static class OpenRazerArgbFrame
{
    internal const int ReportLength = 320;
    internal const int MaxLeds = 105;
    internal const byte MaxChannel = 5;

    /// <summary>
    /// OpenRazer's <c>razer_send_argb_msg</c> frame: report id, the channel twice,
    /// a pad byte, the last LED index, then up to 105 RGB triples.
    /// </summary>
    internal static byte[] Create(byte channel, IReadOnlyList<OpenRazerColor> colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        if (channel > MaxChannel)
            throw new ArgumentOutOfRangeException(nameof(channel), "ARGB channel must be 0..5.");
        if (colors.Count is 0 or > MaxLeds)
            throw new ArgumentOutOfRangeException(nameof(colors), $"ARGB frame needs 1..{MaxLeds} colors.");

        var frame = new byte[ReportLength];
        frame[0] = channel < 5 ? (byte)0x04 : (byte)0x84;
        frame[1] = channel;
        frame[2] = channel;
        frame[4] = (byte)(colors.Count - 1);
        for (var index = 0; index < colors.Count; index++)
        {
            frame[5 + (index * 3)] = colors[index].Red;
            frame[6 + (index * 3)] = colors[index].Green;
            frame[7 + (index * 3)] = colors[index].Blue;
        }

        return frame;
    }
}
