namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// GlitterWireless ARGB frames (0F2C/0F2E). The bundle exposes three region ids
/// (26..28) and sends one 381-byte feature report per row:
/// [regionId, 04/84, row, row, start, end, RGB...].
/// </summary>
internal static class RazerWirelessArgbProtocol
{
    internal const int ReportLength = 381;
    internal const int PortCount = 3;
    internal const int MaxColorsPerPort = 125;
    internal const byte FirstRegionId = 26;

    internal static byte RegionId(int port)
    {
        if ((uint)port >= PortCount)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "Wireless ARGB port must be 0..2.");
        }

        return (byte)(FirstRegionId + port);
    }

    internal static byte[] Create(int port, IReadOnlyList<RazerHidColor> colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        var regionId = RegionId(port);
        if (colors.Count is 0 or > MaxColorsPerPort)
        {
            throw new ArgumentOutOfRangeException(
                nameof(colors),
                $"Wireless ARGB frames need 1..{MaxColorsPerPort} colors.");
        }

        var frame = new byte[ReportLength];
        frame[0] = regionId;
        frame[1] = port == PortCount - 1 ? (byte)0x84 : (byte)0x04;
        frame[2] = (byte)port;
        frame[3] = (byte)port;
        frame[4] = 0;
        frame[5] = (byte)(colors.Count - 1);
        for (var index = 0; index < colors.Count; index++)
        {
            frame[6 + (index * 3)] = colors[index].Red;
            frame[7 + (index * 3)] = colors[index].Green;
            frame[8 + (index * 3)] = colors[index].Blue;
        }

        return frame;
    }
}
