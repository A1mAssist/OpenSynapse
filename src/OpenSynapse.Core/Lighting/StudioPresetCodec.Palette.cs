using System.Text.Json;

namespace OpenSynapse.Core.Lighting;

public static partial class StudioPresetCodec
{
    private static StudioPalette ReadPalette(JsonElement element, string context)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            if (StudioPalette.TryGet(element.GetString(), out var named)) return named;
            throw Error(context, $"unknown palette '{element.GetString()}'");
        }
        Object(element, context, "stops");
        if (!element.TryGetProperty("stops", out var stops) || stops.ValueKind != JsonValueKind.Array ||
            stops.GetArrayLength() == 0)
            throw Error(context + ".stops", "must be a non-empty array");

        var result = new List<StudioColorStop>();
        var index = 0;
        foreach (var item in stops.EnumerateArray())
        {
            var stopContext = $"{context}.stops[{index}]";
            double position;
            string color;
            if (item.ValueKind == JsonValueKind.String)
            {
                position = stops.GetArrayLength() == 1 ? 0 : index * 100d / (stops.GetArrayLength() - 1);
                color = item.GetString()!;
            }
            else
            {
                Object(item, stopContext, "stop", "color");
                if (!item.TryGetProperty("stop", out _)) throw Error(stopContext + ".stop", "is required");
                position = Number(item, "stop", stopContext, 0, 0, 100);
                color = Text(item, "color", stopContext);
            }
            if (color.Length != 7 || color[0] != '#' || !color.Skip(1).All(Uri.IsHexDigit))
                throw Error(stopContext + ".color", "must be #RRGGBB");
            result.Add(new StudioColorStop((int)Math.Round(position), StudioRgb.FromHex(color)));
            index++;
        }
        return new StudioPalette("inline", result);
    }
}
