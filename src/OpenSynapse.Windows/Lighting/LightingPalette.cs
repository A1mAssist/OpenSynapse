using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Lighting;

/// <summary>
/// Ordered colors used by software-rendered lighting effects. Colors are sampled
/// at evenly spaced positions across the palette; a single color is valid and
/// produces a solid result.
/// </summary>
public sealed class LightingPalette
{
    public LightingPalette(IEnumerable<RazerRgb> colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        Colors = Array.AsReadOnly(colors.ToArray());
        if (Colors.Count == 0)
        {
            throw new ArgumentException("A lighting palette must contain at least one color.", nameof(colors));
        }
    }

    public IReadOnlyList<RazerRgb> Colors { get; }

    public static LightingPalette Create(params RazerRgb[] colors) => new(colors);

    internal RazerRgb Sample(double position, bool wrap = false)
    {
        if (Colors.Count == 1)
        {
            return Colors[0];
        }

        var normalized = wrap
            ? position - Math.Floor(position)
            : Math.Clamp(position, 0, 1);
        var scaled = normalized * (wrap ? Colors.Count : Colors.Count - 1);
        var index = Math.Min((int)Math.Floor(scaled), Colors.Count - 1);
        var next = wrap ? (index + 1) % Colors.Count : Math.Min(index + 1, Colors.Count - 1);
        var factor = scaled - Math.Floor(scaled);
        return new RazerRgb(
            Scale(Colors[index].Red, Colors[next].Red, factor),
            Scale(Colors[index].Green, Colors[next].Green, factor),
            Scale(Colors[index].Blue, Colors[next].Blue, factor));
    }

    private static byte Scale(byte start, byte end, double factor) =>
        checked((byte)Math.Clamp(Math.Round(start + (end - start) * factor, MidpointRounding.AwayFromZero), 0, 255));
}
