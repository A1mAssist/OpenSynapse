using System.Globalization;
using System.Text.Json;
using OpenSynapse.Core.Audio;

namespace OpenSynapse.Windows.Audio;

internal static class ThxV3PresetCatalog
{
    internal static IReadOnlyList<ThxAudioPreset> Filter(
        IReadOnlyList<ThxAudioPreset> presets,
        string directory,
        ushort productId,
        bool spatialEnabled,
        string? selected)
    {
        var hardwareId = $"usb\\1532\\{productId.ToString("x4", CultureInfo.InvariantCulture)}";
        var known = new HashSet<string>(StringComparer.Ordinal);
        var compatible = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            // GetRenderPresetList includes both modes; SetRenderPreset requires a
            // matching hardwareId, outputDevice and spatialEnabled package entry.
            foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
            {
                using var stream = File.OpenRead(path);
                using var document = JsonDocument.Parse(stream);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("presetName", out var name))
                {
                    continue;
                }
                if (name.ValueKind != JsonValueKind.String ||
                    !root.TryGetProperty("hardwareId", out var hardware) ||
                    hardware.ValueKind != JsonValueKind.String)
                {
                    return presets;
                }
                if (!string.Equals(hardware.GetString(), hardwareId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (!root.TryGetProperty("outputDevice", out var output) ||
                    output.ValueKind != JsonValueKind.String ||
                    !root.TryGetProperty("spatialEnabled", out var spatial) ||
                    spatial.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    return presets;
                }

                known.Add(name.GetString()!);
                if (output.GetString() == "Headphones" && spatial.GetBoolean() == spatialEnabled)
                {
                    compatible.Add(name.GetString()!);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // Incomplete metadata cannot prove that a native preset is unavailable.
            return presets;
        }

        return presets.Where(preset => preset.Id == selected ||
            !known.Contains(preset.Id) || compatible.Contains(preset.Id)).ToArray();
    }
}
