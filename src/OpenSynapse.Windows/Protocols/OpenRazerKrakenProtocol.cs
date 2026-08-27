namespace OpenSynapse.Windows.Protocols;

internal enum OpenRazerKrakenLayout
{
    Rainie,
    Kylie,
}

internal static class OpenRazerKrakenProtocol
{
    internal const int ReportLength = 37;

    private const ushort RainieEffectAddress = 0x1008;
    private const ushort RainieBreathingAddress = 0x15DE;
    private const ushort CustomAddress = 0x1189;
    private const ushort KylieEffectAddress = 0x172D;
    private const ushort KylieBreathingOneAddress = 0x1741;
    private const ushort KylieBreathingTwoAddress = 0x1745;
    private const ushort KylieBreathingThreeAddress = 0x174D;

    internal static byte[] CreateRequest(
        byte destination,
        ushort address,
        ReadOnlySpan<byte> arguments)
    {
        if (arguments.Length > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(arguments), "Kraken requests support at most 32 argument bytes.");
        }

        var report = new byte[ReportLength];
        report[0] = 0x04;
        report[1] = destination;
        report[2] = checked((byte)arguments.Length);
        report[3] = (byte)(address >> 8);
        report[4] = (byte)address;
        arguments.CopyTo(report.AsSpan(5));
        return report;
    }

    internal static IReadOnlyList<byte[]> CreateOff(OpenRazerKrakenLayout layout) =>
        [CreateWrite(GetEffectAddress(layout), [0x00])];

    internal static IReadOnlyList<byte[]> CreateStatic(
        OpenRazerKrakenLayout layout,
        OpenRazerColor color,
        byte? intensity = null,
        bool writeColor = true)
    {
        var reports = new List<byte[]>(2);
        if (writeColor)
        {
            reports.Add(CreateWrite(GetBreathingAddress(layout, 1), ColorArguments(color, intensity)));
        }
        reports.Add(CreateWrite(GetEffectAddress(layout), [0x01]));
        return reports;
    }

    internal static IReadOnlyList<byte[]> CreateCustom(
        OpenRazerKrakenLayout layout,
        OpenRazerColor color,
        byte? intensity = null) =>
        [CreateWrite(CustomAddress, ColorArguments(color, intensity)),
            CreateWrite(GetEffectAddress(layout), [0x01])];

    internal static IReadOnlyList<byte[]> CreateSpectrum(OpenRazerKrakenLayout layout) =>
        [CreateWrite(GetEffectAddress(layout), [0x05])];

    internal static IReadOnlyList<byte[]> CreateBreathing(
        OpenRazerKrakenLayout layout,
        IReadOnlyList<OpenRazerColor> colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        if (colors.Count is < 1 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(colors), "Kraken breathing requires between one and three colors.");
        }
        if (layout == OpenRazerKrakenLayout.Rainie && colors.Count != 1)
        {
            throw new NotSupportedException("The Rainie layout supports only single-color breathing.");
        }

        var baseAddress = GetBreathingAddress(layout, colors.Count);
        var reports = new List<byte[]>(colors.Count + 1);
        for (var index = 0; index < colors.Count; index++)
        {
            var color = colors[index];
            reports.Add(CreateWrite(
                checked((ushort)(baseAddress + (index * 4))),
                [color.Red, color.Green, color.Blue]));
        }

        reports.Add(CreateWrite(GetEffectAddress(layout),
            [colors.Count switch
            {
                1 => (byte)0x0B,
                2 => (byte)0x19,
                3 => (byte)0x29,
                _ => throw new ArgumentOutOfRangeException(nameof(colors)),
            }]));
        return reports;
    }

    private static byte[] CreateWrite(ushort address, ReadOnlySpan<byte> arguments) =>
        CreateRequest(0x40, address, arguments);

    private static byte[] ColorArguments(OpenRazerColor color, byte? intensity) =>
        intensity is { } value
            ? [color.Red, color.Green, color.Blue, value]
            : [color.Red, color.Green, color.Blue];

    private static ushort GetEffectAddress(OpenRazerKrakenLayout layout) => layout switch
    {
        OpenRazerKrakenLayout.Rainie => RainieEffectAddress,
        OpenRazerKrakenLayout.Kylie => KylieEffectAddress,
        _ => throw new ArgumentOutOfRangeException(nameof(layout)),
    };

    private static ushort GetBreathingAddress(OpenRazerKrakenLayout layout, int colorCount) => layout switch
    {
        OpenRazerKrakenLayout.Rainie when colorCount == 1 => RainieBreathingAddress,
        OpenRazerKrakenLayout.Kylie when colorCount == 1 => KylieBreathingOneAddress,
        OpenRazerKrakenLayout.Kylie when colorCount == 2 => KylieBreathingTwoAddress,
        OpenRazerKrakenLayout.Kylie when colorCount == 3 => KylieBreathingThreeAddress,
        _ => throw new ArgumentOutOfRangeException(nameof(colorCount)),
    };
}
