using System.Text.Json;

namespace OpenSynapse.Core.Lighting;

/// <summary>Parses the checked-in Synapse 2 fallback layout asset.</summary>
public static class StudioLegacyLayoutParser
{
    public static IReadOnlyDictionary<ushort, StudioLedLayout> Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("layouts", out var layouts) ||
            layouts.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("The legacy layout asset has no layouts object.");
        }

        var result = new Dictionary<ushort, StudioLedLayout>();
        foreach (var entry in layouts.EnumerateObject())
        {
            if (!ushort.TryParse(entry.Name, out var productId) ||
                !entry.Value.TryGetProperty("leds", out var leds) ||
                leds.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var cells = new List<StudioLedMap>();
            foreach (var cell in leds.EnumerateArray())
            {
                if (cell.GetArrayLength() < 4) continue;
                cells.Add(new StudioLedMap(
                    cell[0].GetInt32(), cell[1].GetInt32(),
                    cell[2].GetInt32(), cell[3].GetInt32()));
            }

            if (cells.Count == 0) continue;
            result[productId] = StudioLedLayout.Create(
                entry.Value.GetProperty("matrixRows").GetInt32(),
                entry.Value.GetProperty("matrixColumns").GetInt32(),
                entry.Value.GetProperty("deviceRows").GetInt32(),
                entry.Value.GetProperty("deviceColumns").GetInt32(),
                cells);
        }

        return result;
    }
}
