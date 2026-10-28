using System.Text.Json;
using System.Text.Json.Serialization;
using OpenSynapse.Core.Devices;

namespace OpenSynapse.Core.Profiles;

/// <summary>
/// Reads and writes the Blade performance mode by name. The wire byte 0x01 is named
/// Gaming in every middleware bundle; profiles written before that rename call it
/// Turbo, and a profile file that no longer parses would be silently replaced by the
/// defaults, so the old name is still accepted on the way in.
/// </summary>
public sealed class BladePerformanceModeJsonConverter : JsonConverter<BladePerformanceMode>
{
    public override BladePerformanceMode Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var name = reader.GetString();
            if (string.Equals(name, "Turbo", StringComparison.OrdinalIgnoreCase))
            {
                return BladePerformanceMode.Gaming;
            }

            if (Enum.TryParse<BladePerformanceMode>(name, ignoreCase: true, out var mode) &&
                Enum.IsDefined(mode))
            {
                return mode;
            }

            throw new JsonException($"Unknown Blade performance mode '{name}'.");
        }

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetByte(out var raw))
        {
            return Enum.IsDefined((BladePerformanceMode)raw)
                ? (BladePerformanceMode)raw
                : throw new JsonException($"Unknown Blade performance mode value {raw}.");
        }

        throw new JsonException("A Blade performance mode must be a name or a number.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        BladePerformanceMode value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
