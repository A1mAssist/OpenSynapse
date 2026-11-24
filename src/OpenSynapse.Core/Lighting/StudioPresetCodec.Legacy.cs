using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenSynapse.Core.Lighting;

public static partial class StudioPresetCodec
{
    /// <summary>Only the catalog accepts the former headerless JSON and shared fields inside params.</summary>
    internal static StudioPresetConfig ParseCatalog(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.TryGetProperty("format", out _) ||
            root.TryGetProperty("version", out _)) return Parse(json);

        var legacy = JsonNode.Parse(json)!.AsObject();
        legacy["format"] = Format;
        legacy["version"] = 1;
        legacy["id"] = Text(root, "id", "preset").Trim().ToLowerInvariant();
        legacy["kind"] = Text(root, "kind", "preset").ToLowerInvariant();
        if (legacy["layers"] is JsonArray layers)
        {
            foreach (var item in layers)
            {
                if (item is not JsonObject layer) continue;
                if (layer["blend"] is JsonValue blend && blend.TryGetValue<string>(out var name))
                    layer["blend"] = name.ToLowerInvariant();
                if (layer["module"] is JsonValue module && module.TryGetValue<string>(out var moduleId))
                    layer["module"] = moduleId.Trim();
                if (layer["params"] is not JsonObject parameters) continue;
                if (parameters.Remove("speed", out var speed)) layer["speed"] = speed;
                if (parameters.Remove("palette", out var palette)) layer["palette"] = palette;
                if (parameters.Remove("stops", out var stops))
                    layer["palette"] = new JsonObject { ["stops"] = stops };
            }
        }
        return Parse(legacy.ToJsonString());
    }
}
