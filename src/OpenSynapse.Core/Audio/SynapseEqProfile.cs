using System.Globalization;
using System.Text;
using System.Text.Json;

namespace OpenSynapse.Core.Audio;

/// <summary>
/// Reads the current equalizer curve from a Synapse 4 <c>.synapse4</c> audio
/// export. The visible file only carries a base64 payload; the curve lives in
/// <c>audioEqualizer.currentFrequencyData</c>, with the selected preset's
/// <c>current</c> curve as the fallback for older exports.
/// </summary>
public static class SynapseEqProfile
{
    public static SynapseEqReadResult Read(string json, int expectedBands)
    {
        try
        {
            using var outer = JsonDocument.Parse(json);
            if (!TryGetPayload(outer.RootElement, out var payload))
            {
                return SynapseEqReadResult.Fail("noProfile");
            }

            string innerJson;
            try
            {
                innerJson = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            }
            catch (FormatException)
            {
                return SynapseEqReadResult.Fail("invalidPayload");
            }

            using var inner = JsonDocument.Parse(innerJson);
            var root = inner.RootElement;
            if (!root.TryGetProperty("audioEqualizer", out var equalizer))
            {
                return SynapseEqReadResult.Fail("noCurve");
            }

            if (!TryReadNumbers(equalizer, "currentFrequencyData", out var gains) &&
                !TryReadPresetCurve(equalizer, out gains))
            {
                return SynapseEqReadResult.Fail("noCurve");
            }

            return gains.Count == expectedBands
                ? new SynapseEqReadResult(gains.ToArray(), gains.Count, null)
                : SynapseEqReadResult.Fail("bandCount", gains.Count);
        }
        catch (JsonException)
        {
            return SynapseEqReadResult.Fail("invalidJson");
        }
    }

    private static bool TryGetPayload(JsonElement root, out string payload)
    {
        payload = string.Empty;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("profiles", out var profiles) ||
            profiles.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var profile in profiles.EnumerateArray())
        {
            if (profile.ValueKind == JsonValueKind.Object &&
                profile.TryGetProperty("payload", out var value) &&
                value.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(value.GetString()))
            {
                payload = value.GetString()!;
                return true;
            }
        }

        return false;
    }

    private static bool TryReadPresetCurve(JsonElement equalizer, out List<double> gains)
    {
        gains = [];
        if (!equalizer.TryGetProperty("presets", out var presets) ||
            presets.ValueKind != JsonValueKind.Object ||
            !presets.TryGetProperty("standard", out var standard) ||
            standard.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var selected = presets.TryGetProperty("selectedPreset", out var selection) &&
            selection.ValueKind == JsonValueKind.Object &&
            selection.TryGetProperty("standard", out var name) &&
            name.ValueKind == JsonValueKind.String
            ? name.GetString()
            : null;

        JsonElement? fallback = null;
        foreach (var entry in standard.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            fallback ??= entry;
            if (selected is not null &&
                entry.TryGetProperty("name", out var entryName) &&
                entryName.ValueKind == JsonValueKind.String &&
                string.Equals(entryName.GetString(), selected, StringComparison.OrdinalIgnoreCase))
            {
                fallback = entry;
                break;
            }
        }

        return fallback is { } curve && TryReadNumbers(curve, "current", out gains);
    }

    private static bool TryReadNumbers(JsonElement parent, string name, out List<double> values)
    {
        values = [];
        if (!parent.TryGetProperty(name, out var element))
        {
            return false;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out var number))
                {
                    values.Add(number);
                }
                else if (item.ValueKind == JsonValueKind.String &&
                         double.TryParse(item.GetString(), NumberStyles.Float,
                             CultureInfo.InvariantCulture, out var parsed))
                {
                    values.Add(parsed);
                }
                else
                {
                    return false;
                }
            }

            return values.Count > 0;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        foreach (var part in element.GetString()!.Split(
                     ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                return false;
            }

            values.Add(number);
        }

        return values.Count > 0;
    }
}

public sealed record SynapseEqReadResult(double[] Gains, int SourceBands, string? Error)
{
    public bool Success => Error is null;

    public static SynapseEqReadResult Fail(string error, int sourceBands = 0) =>
        new([], sourceBands, error);
}
