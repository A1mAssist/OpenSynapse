using System.Globalization;

namespace OpenSynapse.Core.Lighting;

/// <summary>A device-independent RGB color used by the lighting studio compositor.</summary>
public readonly record struct StudioRgb(byte R, byte G, byte B)
{
    public static StudioRgb Black => new(0, 0, 0);
    public static StudioRgb White => new(255, 255, 255);

    public static StudioRgb FromHex(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var text = value.Trim().TrimStart('#');
        if (text.Length != 6 ||
            !int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var packed))
        {
            throw new FormatException($"'{value}' is not a #RRGGBB color.");
        }

        return new StudioRgb((byte)(packed >> 16), (byte)(packed >> 8), (byte)packed);
    }

    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    public static StudioRgb Lerp(StudioRgb from, StudioRgb to, double amount)
    {
        var t = Math.Clamp(amount, 0, 1);
        return new StudioRgb(
            (byte)Math.Round(from.R + (to.R - from.R) * t),
            (byte)Math.Round(from.G + (to.G - from.G) * t),
            (byte)Math.Round(from.B + (to.B - from.B) * t));
    }

    /// <summary>HSV with hue, saturation and value in the 0..1 range.</summary>
    public static StudioRgb FromHsv(double hue, double saturation, double value)
    {
        var h = (hue % 1 + 1) % 1 * 6;
        var s = Math.Clamp(saturation, 0, 1);
        var v = Math.Clamp(value, 0, 1);
        var sector = (int)Math.Floor(h);
        var fraction = h - sector;
        var p = v * (1 - s);
        var q = v * (1 - s * fraction);
        var t = v * (1 - s * (1 - fraction));
        var (r, g, b) = sector switch
        {
            0 => (v, t, p),
            1 => (q, v, p),
            2 => (p, v, t),
            3 => (p, q, v),
            4 => (t, p, v),
            _ => (v, p, q),
        };
        return new StudioRgb(ToByte(r), ToByte(g), ToByte(b));
    }

    private static byte ToByte(double value) =>
        (byte)Math.Clamp(Math.Round(value * 255), 0, 255);
}
