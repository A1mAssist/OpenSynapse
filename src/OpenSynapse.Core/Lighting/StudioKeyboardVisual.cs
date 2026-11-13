using System.Text.RegularExpressions;

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
    IReadOnlyList<StudioKeyboardKey> Keys)
{
    /// <summary>True when the keys came from numbered keypad selection paths.</summary>
    public bool IsKeypad { get; init; }
}

/// <summary>
/// Builds a keyboard visual from a Chroma studio SVG. Product shell, lettering
/// and logo paths stay in <see cref="StudioKeyboardVisual.Paths"/>; only the
/// hit-testable <see cref="StudioKeyboardVisual.Keys"/> list is filtered.
/// </summary>
public static class StudioKeyboardVisualBuilder
{
    private static readonly Regex KeypadSelectionRegex = new(
        """^selection-(?<index>\d+)$""",
        RegexOptions.CultureInvariant);

    public static StudioKeyboardVisual Build(string svg)
    {
        var layout = StudioSvgLayoutParser.Parse(svg);
        return BuildVisual(layout);
    }

    public static StudioKeyboardVisual? Build(StudioManifestDevice manifest)
        => Build(manifest, null);

    public static StudioKeyboardVisual? Build(
        StudioManifestDevice manifest,
        Func<string, string?>? svgFileResolver)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var layout = StudioSvgParts.Select(manifest, svgFileResolver);
        return layout is null ? null : BuildVisual(layout);
    }

    /// <summary>
    /// Keypads have numbered selection paths instead of keyboard key names.
    /// Their first N paths map to the keypad counters in bundle order.
    /// </summary>
    public static StudioKeyboardVisual? BuildKeypad(StudioManifestDevice manifest)
        => BuildKeypad(manifest, null);

    public static StudioKeyboardVisual? BuildKeypad(
        StudioManifestDevice manifest,
        Func<string, string?>? svgFileResolver)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var layout = StudioSvgParts.Select(manifest, svgFileResolver);
        return layout is null ? null : BuildKeypad(layout);
    }

    private static StudioKeyboardVisual BuildVisual(StudioSvgLayout layout)
    {
        var keys = ReadKeys(layout.Paths);
        // 0244 ships no category, so its keypad must be recognised from the SVG itself:
        // 21 numbered selection paths, one of them the scroll click.
        if (keys.Count == 0 && LooksLikeKeypad(layout.Paths) && BuildKeypad(layout) is { } keypad)
        {
            return keypad;
        }

        return new StudioKeyboardVisual(layout.Width, layout.Height, layout.Paths, keys);
    }

    private static bool LooksLikeKeypad(IReadOnlyList<StudioSvgPath> paths)
    {
        var numbered = 0;
        var hasScroll = false;
        foreach (var path in paths)
        {
            if (path.Role != StudioSvgRole.Selection ||
                !KeypadSelectionRegex.IsMatch(path.Id))
            {
                continue;
            }

            numbered++;
            hasScroll |= path.SelectionGroup?.Contains("scroll", StringComparison.OrdinalIgnoreCase) == true ||
                path.Id.Contains("scroll", StringComparison.OrdinalIgnoreCase);
        }

        return numbered == 21 && hasScroll;
    }

    private static StudioKeyboardVisual? BuildKeypad(StudioSvgLayout layout)
    {
        var keys = new List<StudioKeyboardKey>();
        var keyNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var keyIndex = 0;
        foreach (var path in layout.Paths)
        {
            if (path.Role != StudioSvgRole.Selection ||
                !KeypadSelectionRegex.IsMatch(path.Id))
            {
                continue;
            }

            var isScroll = path.SelectionGroup?.Contains("scroll", StringComparison.OrdinalIgnoreCase) == true ||
                path.Id.Contains("scroll", StringComparison.OrdinalIgnoreCase);
            var keyName = isScroll ? "scroll" : $"key{++keyIndex}";
            keyNames[path.Id] = keyName;
            keys.Add(new StudioKeyboardKey(keyName, path.Id, path.Data, path.SelectionGroup));
        }

        if (keys.Count == 0)
        {
            return null;
        }

        var paths = layout.Paths
            .Select(path => keyNames.TryGetValue(path.Id, out var keyName)
                ? path with { KeyName = keyName }
                : path)
            .ToArray();
        return new StudioKeyboardVisual(layout.Width, layout.Height, paths, keys) { IsKeypad = true };
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
