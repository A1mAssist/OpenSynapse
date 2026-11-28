namespace OpenSynapse.Windows.Protocols;

internal static class RazerKraken40LightingProtocol
{
    internal static bool IsSupported(ushort productId) => productId is 0x0521 or 0x0526 or 0x0527 or 0x0554 or 0x0560;

    internal static byte[] CreateBrightness(ushort productId, byte percent)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percent, (byte)100);
        var value = (byte)((percent * 255 + 50) / 100);
        return productId == 0x0526
            ? Command(0x40, 0x02, 0, value)
            : Command(0x40, 0x02, 0, productId == 0x0527 ? (byte)0 : (byte)15, value);
    }

    internal static byte[] CreateEffect(ushort productId, RazerHidLightingSettings settings)
    {
        var effect = settings.Effect switch
        {
            RazerHidLightingEffect.Off => (byte)0,
            RazerHidLightingEffect.Static => (byte)1,
            RazerHidLightingEffect.BreathingSingle or RazerHidLightingEffect.BreathingDual => (byte)2,
            RazerHidLightingEffect.Spectrum => (byte)3,
            RazerHidLightingEffect.Custom => (byte)8,
            _ => throw new NotSupportedException("This Kraken effect has no 0x40 command."),
        };
        if (productId == 0x0526) return Command(0x40, 0x01, 0, effect, 1, 0, 255, 0);
        if (productId == 0x0527) return Command(0x40, 0x01, 0, 0, effect);
        var count = settings.Effect switch
        {
            RazerHidLightingEffect.Static or RazerHidLightingEffect.BreathingSingle => (byte)1,
            RazerHidLightingEffect.BreathingDual => (byte)2,
            _ => (byte)0,
        };
        var report = Command(0x40, 0x01, effect == 8 ? (byte)0 : (byte)1, 15, effect, count);
        if (count > 0) PutColor(report, 6, settings.Primary);
        if (count > 1) PutColor(report, 9, settings.Secondary);
        return report;
    }

    internal static byte[] CreateFrame(ushort productId, RazerHidColor color)
    {
        var report = Command(0x40, 0x03, 0);
        var start = productId == 0x0526 ? 2 : 3;
        for (var i = 0; i < (productId == 0x0554 ? 4 : 1); i++) PutColor(report, start + 3 * i, color);
        return report;
    }

    private static void PutColor(byte[] report, int index, RazerHidColor color)
    {
        report[index] = color.Red;
        report[index + 1] = color.Green;
        report[index + 2] = color.Blue;
    }

    private static byte[] Command(params byte[] header)
    {
        var report = new byte[37];
        header.CopyTo(report, 0);
        return report;
    }
}
