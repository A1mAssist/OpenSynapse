using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenSynapse.Core.Lighting;

/// <summary>Turns editor drafts into the strict v1 preset objects the runtime consumes.</summary>
public static class StudioPresetCompiler
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
    };

    public static StudioPresetConfig Compile(StudioPresetDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return StudioPresetCodec.Parse(Serialize(draft));
    }

    public static string Serialize(StudioPresetDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var kind = KindId(draft.Kind);
        var json = new JsonObject
        {
            ["format"] = "opensynapse-chroma",
            ["version"] = 1,
            ["id"] = draft.Id?.Trim() ?? string.Empty,
            ["name"] = draft.Name?.Trim() ?? string.Empty,
            ["kind"] = kind,
            ["category"] = string.IsNullOrWhiteSpace(draft.Category) ? kind : draft.Category.Trim(),
            ["layers"] = new JsonArray(draft.Layers.Select(SerializeLayer).ToArray()),
        };
        if (!string.IsNullOrWhiteSpace(draft.Note))
        {
            json["note"] = draft.Note.Trim();
        }

        return json.ToJsonString(WriteOptions);
    }

    private static JsonNode SerializeLayer(StudioPresetLayerDraft layer)
    {
        var stops = new JsonArray();
        foreach (var stop in layer.Palette)
        {
            stops.Add(new JsonObject
            {
                ["stop"] = stop.Stop,
                ["color"] = stop.Color.ToHex(),
            });
        }

        var parameters = layer.Params.DeepClone() as JsonObject ?? new JsonObject();
        return new JsonObject
        {
            ["module"] = layer.ModuleId?.Trim() ?? string.Empty,
            ["blend"] = layer.Blend.ToString().ToLowerInvariant(),
            ["opacity"] = layer.Opacity,
            ["speed"] = layer.Speed,
            ["palette"] = new JsonObject { ["stops"] = stops },
            ["params"] = parameters,
        };
    }

    private static string KindId(StudioPresetKind kind) => kind switch
    {
        StudioPresetKind.Reactive => "reactive",
        StudioPresetKind.Music => "music",
        StudioPresetKind.Screen => "screen",
        StudioPresetKind.System => "system",
        StudioPresetKind.Ambient => "ambient",
        _ => throw new InvalidDataException("Preset kind is outside the supported range."),
    };
}
