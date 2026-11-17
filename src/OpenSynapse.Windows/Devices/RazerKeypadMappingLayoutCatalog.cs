using System.Globalization;
using System.Text.Json;

namespace OpenSynapse.Windows.Devices;

/// <summary>One keypad key in Razer's own mapping-page coordinates.</summary>
public sealed record RazerKeypadMappingKey(byte Id, string Label, string Path);

/// <summary>
/// Official keypad mapping art. Razer's 0244 page draws these paths over the
/// 350x450 product image, so no anchor guessing is needed.
/// </summary>
public sealed record RazerKeypadMappingLayout(
    double Width,
    double Height,
    IReadOnlyList<RazerKeypadMappingKey> Keys)
{
    /// <summary>3x PNG data URL payload for the wheel widget, without the data: prefix.</summary>
    public string? ScrollImage { get; init; }

    /// <summary>3x PNG data URL payload for the D-pad centre, without the data: prefix.</summary>
    public string? DpadImage { get; init; }
}

/// <summary>
/// Paths copied from Razer's 580 (0244) mapping bundle groupList. Only products
/// with extracted paths are listed; every other keypad keeps the Chroma SVG.
/// </summary>
public static class RazerKeypadMappingLayoutCatalog
{
    private const string ResourceSuffix = "Devices.RazerHid.mapping-keypad-layouts.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly IReadOnlyDictionary<ushort, RazerKeypadMappingLayout> Layouts = Load();

    public static bool TryGet(ushort productId, out RazerKeypadMappingLayout layout) =>
        Layouts.TryGetValue(productId, out layout!);

    private static IReadOnlyDictionary<ushort, RazerKeypadMappingLayout> Load()
    {
        var assembly = typeof(RazerKeypadMappingLayoutCatalog).Assembly;
        var name = assembly.GetManifestResourceNames()
            .SingleOrDefault(item => item.EndsWith(ResourceSuffix, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Missing embedded resource '{ResourceSuffix}'.");
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Cannot open embedded resource '{name}'.");
        var document = JsonSerializer.Deserialize<Dictionary<string, LayoutJson>>(stream, JsonOptions)
            ?? throw new InvalidOperationException($"'{name}' is empty.");
        return document.ToDictionary(
            pair => ushort.Parse(pair.Key, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            pair => new RazerKeypadMappingLayout(
                pair.Value.Width,
                pair.Value.Height,
                pair.Value.Keys
                    .Select(key => new RazerKeypadMappingKey(key.Id, key.Label, key.Path))
                    .ToArray())
            {
                ScrollImage = pair.Value.ScrollImage,
                DpadImage = pair.Value.DpadImage,
            });
    }

    private sealed record LayoutJson(
        double Width,
        double Height,
        IReadOnlyList<KeyJson> Keys,
        string? ScrollImage,
        string? DpadImage);

    private sealed record KeyJson(byte Id, string Label, string Path);
}
