using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenSynapse.Core.Lighting;

/// <summary>Portable, self-contained presets; parsing also compiles the module layers.</summary>
public static partial class StudioPresetCodec
{
    public const string Extension = ".oschroma";
    private const string Format = "opensynapse-chroma";
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static StudioPresetConfig Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonDocument.Parse(json);
            return ParseRoot(document.RootElement);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Preset JSON: {exception.Message}", exception);
        }
    }

    public static StudioPresetConfig Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var json = Utf8.GetString(File.ReadAllBytes(path));
        return Parse(json.Length > 0 && json[0] == '\uFEFF' ? json[1..] : json);
    }

    public static void Save(string path, StudioPresetConfig preset) =>
        StudioProjectCodec.WriteAtomically(path, Serialize(preset));

    public static string Serialize(StudioPresetConfig preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        var kind = preset.Kind switch
        {
            StudioPresetKind.Reactive => "reactive",
            StudioPresetKind.Music => "music",
            StudioPresetKind.Screen => "screen",
            StudioPresetKind.System => "system",
            StudioPresetKind.Ambient => "ambient",
            _ => throw new InvalidDataException("Preset kind must be reactive, music, screen, system or ambient."),
        };
        var json = JsonSerializer.Serialize(new
        {
            format = Format,
            version = 1,
            id = preset.Id,
            name = preset.Name,
            kind,
            category = preset.Category,
            note = preset.Note,
            layers = preset.Layers.Select(layer => new
            {
                module = layer.ModuleId,
                blend = layer.Blend.ToString().ToLowerInvariant(),
                opacity = layer.Opacity,
                speed = layer.Speed,
                palette = new
                {
                    stops = layer.RenderPalette.Stops.Select(stop => new
                    {
                        stop = stop.Stop,
                        color = stop.Color.ToHex(),
                    }),
                },
                @params = layer.Parameters,
            }),
        }, WriteOptions);
        // Public init properties can form invalid metadata; validate before replacing a file.
        Parse(json);
        return json;
    }

    private static StudioPresetConfig ParseRoot(JsonElement root)
    {
        Object(root, "preset", "format", "version", "id", "name", "kind", "category", "note", "layers");
        if (Text(root, "format", "preset") != Format)
            throw Error("preset.format", $"must be '{Format}'");
        if (!root.TryGetProperty("version", out var version) ||
            version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != 1)
            throw Error("preset.version", "must be the supported integer version 1");

        var id = Text(root, "id", "preset");
        var context = $"preset '{id}'";
        if (!id.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-'))
            throw Error(context + ".id", "must use lowercase letters, digits and '-'");
        var kind = Text(root, "kind", context) switch
        {
            "reactive" => StudioPresetKind.Reactive,
            "music" => StudioPresetKind.Music,
            "screen" => StudioPresetKind.Screen,
            "system" => StudioPresetKind.System,
            "ambient" => StudioPresetKind.Ambient,
            _ => throw Error(context + ".kind", "must be 'reactive', 'music', 'screen', 'system' or 'ambient'"),
        };
        if (!root.TryGetProperty("layers", out var layers) || layers.ValueKind != JsonValueKind.Array ||
            layers.GetArrayLength() is < 1 or > 4)
            throw Error(context + ".layers", "must be an array of 1 to 4 layers");
        return new StudioPresetConfig
        {
            Id = id,
            Name = Text(root, "name", context),
            Kind = kind,
            Category = Text(root, "category", context, kind.ToString().ToLowerInvariant()),
            Note = root.TryGetProperty("note", out var note) ? ReadNote(note, context) : null,
            Layers = layers.EnumerateArray().Select((layer, index) => ReadLayer(layer, context, index)).ToArray(),
        };
    }

    private static string? ReadNote(JsonElement note, string context) => note.ValueKind switch
    {
        JsonValueKind.String => note.GetString(),
        _ => throw Error(context + ".note", "must be a string"),
    };

    private static StudioPresetLayer ReadLayer(JsonElement element, string preset, int index)
    {
        var context = $"{preset} layer {index + 1}";
        if (element.ValueKind != JsonValueKind.Object) throw Error(context, "must be an object");
        var moduleId = Text(element, "module", context);
        context += $" ({moduleId})";
        Object(element, context, "module", "blend", "opacity", "speed", "palette", "params");
        if (!RenderModuleRegistry.TryGet(moduleId, out var module))
            throw Error(context + ".module", "is an unknown module");
        var blend = Text(element, "blend", context, "normal") switch
        {
            "normal" => StudioBlendMode.Normal,
            "add" => StudioBlendMode.Add,
            "multiply" => StudioBlendMode.Multiply,
            "screen" => StudioBlendMode.Screen,
            "max" => StudioBlendMode.Max,
            _ => throw Error(context + ".blend", "must be normal, add, multiply, screen or max"),
        };
        var opacity = Number(element, "opacity", context, 1, 0, 1);
        var speed = Number(element, "speed", context, 1, 0.1, 5);
        var palette = element.TryGetProperty("palette", out var paletteElement)
            ? ReadPalette(paletteElement, context + ".palette") : StudioPalette.White;
        var parameters = element.TryGetProperty("params", out var supplied)
            ? supplied : JsonSerializer.SerializeToElement(new { });
        Object(parameters, context + ".params");
        try
        {
            return new StudioPresetLayer(moduleId, module.Info,
                module.Create(new RenderModuleParams(parameters, moduleId)),
                blend, opacity, palette, speed, $"{index}:{moduleId}", parameters);
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException($"{context}.params: {exception.Message}", exception);
        }
    }

    private static void Object(JsonElement element, string context, params string[] allowed)
    {
        if (element.ValueKind != JsonValueKind.Object) throw Error(context, "must be an object");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name)) throw Error(context + "." + property.Name, "is duplicated");
            if (allowed.Length > 0 && !allowed.Contains(property.Name, StringComparer.Ordinal))
                throw Error(context + "." + property.Name, "is an unknown field");
        }
    }

    private static string Text(JsonElement element, string name, string context, string? fallback = null)
    {
        if (!element.TryGetProperty(name, out var value) && fallback is not null) return fallback;
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw Error(context + "." + name, "must be a non-empty string");
        return value.GetString()!;
    }

    private static double Number(JsonElement element, string name, string context,
        double fallback, double minimum, double maximum)
    {
        if (!element.TryGetProperty(name, out var value)) return fallback;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) ||
            !double.IsFinite(number) || number < minimum || number > maximum)
            throw Error(context + "." + name, $"must be a finite number between {minimum} and {maximum}");
        return number;
    }

    private static InvalidDataException Error(string context, string message) => new($"{context}: {message}.");
}
