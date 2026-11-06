using System.Text;

namespace OpenSynapse.Core.Lighting;

/// <summary>A key shape that a mapping canvas can hit-test and select.</summary>
public sealed record StudioKeyboardKey(
    string KeyName,
    string PathId,
    string Data,
    string? SelectionGroup);

/// <summary>Renderable keyboard artwork plus the keys it exposes for mapping.</summary>
public sealed record StudioKeyboardVisual(
    double Width,
    double Height,
    IReadOnlyList<StudioSvgPath> Paths,
    IReadOnlyList<StudioKeyboardKey> Keys);

/// <summary>
/// Builds a keyboard visual from a Chroma studio SVG. Product shell, lettering
/// and logo paths stay in <see cref="StudioKeyboardVisual.Paths"/>; only the
/// hit-testable <see cref="StudioKeyboardVisual.Keys"/> list is filtered.
/// </summary>
public static class StudioKeyboardVisualBuilder
{
    public static StudioKeyboardVisual Build(string svg)
    {
        var layout = StudioSvgLayoutParser.Parse(svg);
        return new StudioKeyboardVisual(layout.Width, layout.Height, layout.Paths, ReadKeys(layout.Paths));
    }

    public static StudioKeyboardVisual? Build(StudioManifestDevice manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (string.IsNullOrWhiteSpace(manifest.SvgBase64)) return null;

        try
        {
            var svg = Encoding.UTF8.GetString(Convert.FromBase64String(manifest.SvgBase64));
            return Build(svg);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return null;
        }
    }

    private static IReadOnlyList<StudioKeyboardKey> ReadKeys(IReadOnlyList<StudioSvgPath> paths)
    {
        var keys = new List<StudioKeyboardKey>();
        var indexByName = new Dictionary<string, int>(StringComparer.Ordinal);
        var ledNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var path in paths)
        {
            if (path.Role is not (StudioSvgRole.Led or StudioSvgRole.Selection)) continue;
            if (string.IsNullOrWhiteSpace(path.KeyName)) continue;
            if (IsLogo(path.KeyName, path.SelectionGroup)) continue;

            var key = new StudioKeyboardKey(path.KeyName, path.Id, path.Data, path.SelectionGroup);
            if (!indexByName.TryGetValue(path.KeyName, out var index))
            {
                indexByName[path.KeyName] = keys.Count;
                keys.Add(key);
                if (path.Role == StudioSvgRole.Led) ledNames.Add(path.KeyName);
            }
            else if (path.Role == StudioSvgRole.Led && !ledNames.Contains(path.KeyName))
            {
                keys[index] = key;
                ledNames.Add(path.KeyName);
            }
        }

        return keys;
    }

    private static bool IsLogo(string keyName, string? selectionGroup) =>
        keyName.Equals("Logo", StringComparison.OrdinalIgnoreCase) ||
        selectionGroup?.Contains("logo", StringComparison.OrdinalIgnoreCase) == true;
}
