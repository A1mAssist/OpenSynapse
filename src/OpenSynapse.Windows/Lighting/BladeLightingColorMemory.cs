using System.Globalization;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Lighting;

/// <summary>
/// Per-effect colors survive switching effects and restarting the app. The
/// active effect still owns the hardware; this only remembers the UI palette.
/// </summary>
internal readonly record struct BladeLightingColorMemoryEntry(
    RazerRgb Primary,
    RazerRgb Secondary,
    RazerRgb Tertiary,
    bool SoftwareRender);

internal static class BladeLightingColorMemory
{
    internal static void Remember(
        BladeProfileSettings target,
        BladeLightingMode mode,
        BladeLightingColorMemoryEntry colors)
    {
        target.LightingColorMemory ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        target.LightingColorMemory[ModeKey(mode)] =
            $"{(colors.SoftwareRender ? 1 : 0)}|{Hex(colors.Primary)},{Hex(colors.Secondary)},{Hex(colors.Tertiary)}";
    }

    internal static bool TryRecall(
        BladeProfileSettings? source,
        BladeLightingMode mode,
        out BladeLightingColorMemoryEntry colors)
    {
        colors = default;
        if (source?.LightingColorMemory is not { } memory ||
            !memory.TryGetValue(ModeKey(mode), out var stored) ||
            stored.Length < 3 ||
            stored[0] is not ('0' or '1') ||
            stored[1] != '|')
        {
            return false;
        }

        var parts = stored[2..].Split(',');
        if (parts.Length != 3 ||
            !TryParse(parts[0], out var primary) ||
            !TryParse(parts[1], out var secondary) ||
            !TryParse(parts[2], out var tertiary))
        {
            return false;
        }

        colors = new BladeLightingColorMemoryEntry(primary, secondary, tertiary, stored[0] == '1');
        return true;
    }

    private static string ModeKey(BladeLightingMode mode) => mode.ToString().ToLowerInvariant();

    private static string Hex(RazerRgb color) =>
        $"{color.Red:X2}{color.Green:X2}{color.Blue:X2}";

    private static bool TryParse(string hex, out RazerRgb color)
    {
        color = default;
        if (hex.Length != 6 ||
            !byte.TryParse(hex.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red) ||
            !byte.TryParse(hex.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green) ||
            !byte.TryParse(hex.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue))
        {
            return false;
        }

        color = new RazerRgb(red, green, blue);
        return true;
    }
}
