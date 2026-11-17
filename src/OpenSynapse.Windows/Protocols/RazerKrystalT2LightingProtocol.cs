namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// AudioKrystalT2 (Kraken V3 / HyperSense) lighting from the official
/// AudioKrakenDevice bundle: a 37-byte command padded to the 62-byte MI_03
/// output report. The effect bytes match the bundle's setChromaEffect.
/// </summary>
internal static class RazerKrystalT2LightingProtocol
{
    internal const int OutputReportLength = 62;
    internal const int CommandLength = 37;

    private const byte ReportId = 0x40;
    // The bundle writes effect and brightness to region 15 (all regions).
    private const byte AllRegions = 15;

    internal static byte[] CreateConnect() =>
        Command(0x04, 0x20, 0x02, 0x00, 0x30);

    internal static byte[] CreateForceChroma(bool enabled) =>
        Command(ReportId, 0x04, 0x00, enabled ? (byte)1 : (byte)0);

    internal static byte[] CreateEffect(
        RazerHidLightingEffect effect,
        RazerHidColor? color = null)
    {
        var custom = effect == RazerHidLightingEffect.Custom;
        var hasColor = color is not null &&
            effect is not (RazerHidLightingEffect.Off or RazerHidLightingEffect.Spectrum);
        var report = Command(
            ReportId,
            0x01,
            custom ? (byte)0 : (byte)1,
            AllRegions,
            EffectId(effect),
            hasColor ? (byte)1 : (byte)0);
        if (hasColor)
        {
            report[6] = color!.Value.Red;
            report[7] = color.Value.Green;
            report[8] = color.Value.Blue;
        }

        return report;
    }

    internal static byte[] CreateFrame(RazerHidColor color) =>
        Command(ReportId, 0x03, 0x00, color.Red, color.Green, color.Blue);

    internal static byte[] Pad(byte[] command) =>
        RazerKrakenBaseProtocol.PadOutputReport(command, OutputReportLength);

    private static byte EffectId(RazerHidLightingEffect effect) => effect switch
    {
        RazerHidLightingEffect.Off => 0,
        RazerHidLightingEffect.Static => 1,
        RazerHidLightingEffect.BreathingSingle => 2,
        RazerHidLightingEffect.Spectrum => 3,
        RazerHidLightingEffect.Custom => 8,
        _ => throw new NotSupportedException(
            $"AudioKrystalT2 does not support {effect}."),
    };

    private static byte[] Command(params byte[] head)
    {
        var report = new byte[CommandLength];
        head.CopyTo(report, 0);
        return report;
    }
}
