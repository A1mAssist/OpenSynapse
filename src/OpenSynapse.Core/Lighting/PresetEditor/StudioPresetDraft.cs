using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenSynapse.Core.Lighting;

/// <summary>Mutable editor state for one .oschroma v1 preset.</summary>
public sealed class StudioPresetDraft
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public StudioPresetKind Kind { get; set; }

    public string? Note { get; set; }

    public List<StudioPresetLayerDraft> Layers { get; } = new();

    public static StudioPresetDraft FromConfig(StudioPresetConfig preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        var draft = new StudioPresetDraft
        {
            Id = preset.Id,
            Name = preset.Name,
            Category = preset.Category,
            Kind = preset.Kind,
            Note = preset.Note,
        };
        foreach (var layer in preset.Layers)
        {
            var parameters = layer.Parameters.ValueKind == JsonValueKind.Object &&
                JsonNode.Parse(layer.Parameters.GetRawText()) is JsonObject values
                ? values
                : new JsonObject();
            var layerDraft = new StudioPresetLayerDraft
            {
                ModuleId = layer.ModuleId,
                Blend = layer.Blend,
                Opacity = layer.Opacity,
                Speed = layer.Speed,
                Params = parameters,
            };
            layerDraft.Palette.AddRange(layer.Palette.Stops);
            draft.Layers.Add(layerDraft);
        }

        if (draft.Layers.Count == 0)
        {
            draft.Layers.Add(new StudioPresetLayerDraft());
        }

        return draft;
    }

    /// <summary>Recovers a draft from JSON without compiling modules or rejecting unknown values.</summary>
    public static StudioPresetDraft FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var draft = new StudioPresetDraft
        {
            Id = ReadString(root, "id"),
            Name = ReadString(root, "name"),
            Category = ReadString(root, "category"),
            Note = root.TryGetProperty("note", out var note) && note.ValueKind == JsonValueKind.String
                ? note.GetString()
                : null,
            Kind = Enum.TryParse<StudioPresetKind>(ReadString(root, "kind"), true, out var kind)
                ? kind
                : StudioPresetKind.Ambient,
        };

        if (root.TryGetProperty("layers", out var layers) && layers.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in layers.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var layer = new StudioPresetLayerDraft
                {
                    ModuleId = ReadString(item, "module"),
                    Blend = Enum.TryParse<StudioBlendMode>(ReadString(item, "blend"), true, out var blend)
                        ? blend
                        : StudioBlendMode.Normal,
                    Opacity = ReadNumber(item, "opacity", 1),
                    Speed = ReadNumber(item, "speed", 1),
                };
                if (item.TryGetProperty("params", out var parameters) &&
                    parameters.ValueKind == JsonValueKind.Object &&
                    JsonNode.Parse(parameters.GetRawText()) is JsonObject values)
                {
                    layer.Params = values;
                }

                if (item.TryGetProperty("palette", out var palette))
                {
                    try
                    {
                        layer.Palette.AddRange(StudioPresetCodec.ReadPalette(palette, "preset.palette").Stops);
                    }
                    catch (Exception exception) when (exception is InvalidDataException or FormatException)
                    {
                        // A damaged palette stays empty so validation can point at the layer.
                    }
                }

                draft.Layers.Add(layer);
            }
        }

        if (draft.Layers.Count == 0)
        {
            draft.Layers.Add(new StudioPresetLayerDraft());
        }

        return draft;
    }

    public StudioPresetDraft Clone()
    {
        var clone = new StudioPresetDraft
        {
            Id = Id,
            Name = Name,
            Category = Category,
            Kind = Kind,
            Note = Note,
        };
        foreach (var layer in Layers)
        {
            var layerClone = new StudioPresetLayerDraft
            {
                ModuleId = layer.ModuleId,
                Blend = layer.Blend,
                Opacity = layer.Opacity,
                Speed = layer.Speed,
                Params = (JsonObject)layer.Params.DeepClone(),
            };
            layerClone.Palette.AddRange(layer.Palette);
            clone.Layers.Add(layerClone);
        }

        return clone;
    }

    private static string ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static double ReadNumber(JsonElement element, string name, double fallback) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetDouble(out var number) && double.IsFinite(number)
            ? number
            : fallback;
}

public sealed class StudioPresetLayerDraft
{
    public string ModuleId { get; set; } = string.Empty;

    public StudioBlendMode Blend { get; set; }

    public double Opacity { get; set; } = 1;

    public double Speed { get; set; } = 1;

    public List<StudioColorStop> Palette { get; } = new();

    public JsonObject Params { get; set; } = new();
}
