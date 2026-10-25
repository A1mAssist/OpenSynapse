namespace OpenSynapse.Windows.Lighting;

/// <summary>
/// sRGB to CIE 1931 xy, the colour space the Hue CLIP v2 API expects for <c>color.xy</c>.
/// Uses the standard sRGB primaries with a D65 white point.
/// </summary>
public static class PhilipsHueColor
{
    public static (double X, double Y) FromRgb(byte red, byte green, byte blue)
    {
        var r = Linearize(red);
        var g = Linearize(green);
        var b = Linearize(blue);

        var x = r * 0.4124 + g * 0.3576 + b * 0.1805;
        var y = r * 0.2126 + g * 0.7152 + b * 0.0722;
        var z = r * 0.0193 + g * 0.1192 + b * 0.9505;
        var sum = x + y + z;

        // Pure black has no chromaticity; fall back to the D65 white point.
        if (sum <= 0.000001)
        {
            return (0.3127, 0.3290);
        }

        return (Math.Clamp(x / sum, 0.0, 1.0), Math.Clamp(y / sum, 0.0, 1.0));
    }

    private static double Linearize(byte value)
    {
        var channel = value / 255.0;
        return channel <= 0.04045
            ? channel / 12.92
            : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }
}
