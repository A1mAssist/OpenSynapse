using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenSynapse.Core.Lighting;

/// <summary>Builds the profile-device ledRef snapshot Chroma keeps for layout changes.</summary>
public static class StudioLedRefJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(StudioLedLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var leds = new List<LedRefDto>(layout.Leds.Count);
        foreach (var led in layout.Leds)
        {
            var input = layout.FindInputAt(led.MatrixRow, led.MatrixCol);
            leds.Add(new LedRefDto
            {
                Row = led.MatrixRow,
                Col = led.MatrixCol,
                RegionId = led.RegionId,
                RegionIndex = led.RegionIndex,
                InputType = input?.InputType,
                InputData = input is { } mapped
                    ? FormattableString.Invariant($"[{mapped.InputCode},{mapped.InputModifier}]")
                    : null,
            });
        }

        return JsonSerializer.Serialize(leds, Options);
    }

    private sealed class LedRefDto
    {
        public int Row { get; set; }

        public int Col { get; set; }

        public int? RegionId { get; set; }

        public int? RegionIndex { get; set; }

        public string? InputType { get; set; }

        public string? InputData { get; set; }
    }
}