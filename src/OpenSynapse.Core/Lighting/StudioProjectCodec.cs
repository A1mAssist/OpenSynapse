using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace OpenSynapse.Core.Lighting;
public static partial class StudioProjectCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
    /// <summary>
    /// Writes the selected profile subset and marks one exported profile with
    /// Chroma's <c>default</c> flag. When no profile is marked, the selected
    /// profile (or the first) becomes the default.
    /// </summary>
    public static string Serialize(
        StudioProject project,
        IReadOnlyCollection<string>? profileIds = null,
        bool export = false)
    {
        ArgumentNullException.ThrowIfNull(project);
        var requested = profileIds is { Count: > 0 }
            ? profileIds.Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : null;
        var profiles = requested is null
            ? project.Profiles.ToList()
            : project.Profiles.Where(profile => requested.Contains(profile.Id)).ToList();
        if (requested is not null && profiles.Count == 0)
            throw new ArgumentException(
                "None of the selected profile ids exist in this project.", nameof(profileIds));
        var selected = project.SelectedProfile is { } current && profiles.Contains(current)
            ? current
            : profiles.FirstOrDefault();
        var defaultProfile = profiles.FirstOrDefault(profile => profile.IsDefault) ?? selected;
        return JsonSerializer.Serialize(new RootDto
        {
            Version = 1,
            Profiles = profiles.Select(profile =>
                ToProfileDto(profile, ReferenceEquals(profile, defaultProfile), export)).ToList(),
            OpenSynapse = new RootExtrasDto
            {
                SchemaVersion = StudioProject.CurrentSchemaVersion,
                Name = project.Name,
                SelectedProfileId = selected?.Id,
            },
        }, Options);
    }
    /// <summary>
    /// Reads a Studio project. A missing <c>openSynapse.schemaVersion</c> is
    /// treated as the current schema; newer schemas are rejected rather than
    /// read with silently missing fields. Legacy v1 documents still migrate.
    /// </summary>
    public static StudioProject Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("The lighting project root must be a JSON object.");
            var schemaVersion = ReadSchemaVersion(root);
            if (schemaVersion > StudioProject.CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"The lighting project schema {schemaVersion} is newer than this build supports " +
                    $"({StudioProject.CurrentSchemaVersion}).");
            }

            // Only v1 has a different document shape; older profile documents
            // continue through the current reader.
            if (IsSchemaV1(root)) return MigrateV1(root);
            if (!TryGet(root, "profiles", out var profiles) || profiles.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("The lighting project must contain a profiles array.");
            var project = new StudioProject();
            if (TryGet(root, "openSynapse", out var extras) && extras.ValueKind == JsonValueKind.Object)
            {
                var name = GetString(extras, "name");
                if (!string.IsNullOrWhiteSpace(name)) project.Name = name;
            }
            foreach (var profile in profiles.EnumerateArray())
                if (profile.ValueKind == JsonValueKind.Object) project.Profiles.Add(ReadProfile(profile));
            if (project.Profiles.Count == 0) project.Profiles.Add(new StudioProfile("Default"));
            NormalizeDefaultProfile(project);
            var selected = TryGet(root, "openSynapse", out extras) && extras.ValueKind == JsonValueKind.Object
                ? GetString(extras, "selectedProfileId")
                : null;
            project.SelectedProfileId = project.Profiles.Any(p => p.Id == selected)
                ? selected
                : project.Profiles.FirstOrDefault(p => p.IsDefault)?.Id ?? project.Profiles.FirstOrDefault()?.Id;
            return project;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("The lighting project JSON is invalid.", ex);
        }
    }
    public static StudioProject Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Deserialize(File.ReadAllText(path));
    }
    public static void Save(
        string path,
        StudioProject project,
        IReadOnlyCollection<string>? profileIds = null,
        bool export = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(project);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var tempPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tempPath, Serialize(project, profileIds, export));
            File.Move(tempPath, fullPath, true);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private static bool TryParseEffectName(string? name, out StudioEffectKind effect)
    {
        effect = StudioEffectKind.Solid;
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (Enum.TryParse(name, true, out StudioEffectKind parsed) && Enum.IsDefined(parsed))
        {
            effect = parsed;
            return true;
        }
        var mapped = StudioEffectMap.FromName(name);
        if (mapped == StudioEffectKind.Solid &&
            !name.Equals("solid", StringComparison.OrdinalIgnoreCase) &&
            !name.Equals("static", StringComparison.OrdinalIgnoreCase)) return false;
        effect = mapped;
        return true;
    }
    private static StudioBlendMode ReadBlend(string? name) => Enum.TryParse(name, true, out StudioBlendMode blend) && Enum.IsDefined(blend) ? blend : StudioBlendMode.Normal;
    private static StudioPlaybackStart ReadStart(string? value) => value?.Trim().ToLowerInvariant() switch { "onpress" => StudioPlaybackStart.OnPress, "onselectedkeys" => StudioPlaybackStart.OnSelectedKeys, _ => StudioPlaybackStart.Random };
    private static StudioPlaybackEnd ReadEnd(string? value) => value?.Trim().ToLowerInvariant() switch { "after" => StudioPlaybackEnd.After, "toggle" => StudioPlaybackEnd.Toggle, _ => StudioPlaybackEnd.Never };
    /// <summary>
    /// A present but malformed version is rejected instead of falling back to the
    /// current schema, which would silently downgrade an unknown document.
    /// </summary>
    private static int ReadSchemaVersion(JsonElement root)
    {
        if (!TryGet(root, "openSynapse", out var extras) || extras.ValueKind == JsonValueKind.Null)
            return StudioProject.CurrentSchemaVersion;
        if (extras.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("The lighting project openSynapse value must be an object.");
        if (!TryGet(extras, "schemaVersion", out var value) || value.ValueKind == JsonValueKind.Null)
            return StudioProject.CurrentSchemaVersion;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var version) || version < 0)
            throw new InvalidOperationException(
                "The lighting project openSynapse.schemaVersion must be a non-negative integer.");
        return version;
    }

    /// <summary>Chroma exports at most one default profile; an absent marker means the first.</summary>
    private static void NormalizeDefaultProfile(StudioProject project)
    {
        var defaultProfile = project.Profiles.FirstOrDefault(profile => profile.IsDefault)
            ?? project.Profiles.FirstOrDefault();
        foreach (var profile in project.Profiles)
            profile.IsDefault = ReferenceEquals(profile, defaultProfile);
    }

    private static bool IsSchemaV1(JsonElement root) =>
        TryGet(root, "schemaVersion", out var version) && version.ValueKind == JsonValueKind.Number &&
        version.TryGetDouble(out var number) && number <= 1;
    private static bool IsGroup(JsonElement element) =>
        (GetString(element, "type")?.Equals("group", StringComparison.OrdinalIgnoreCase) ?? false) ||
        ReadBool(element, "isGroup");
    private static bool TryReadColor(JsonElement element, out StudioRgb color)
    {
        color = default;
        if (element.ValueKind == JsonValueKind.Number)
        {
            if (element.TryGetInt32(out var packed)) { color = FromPacked(packed); return true; }
            if (element.TryGetDouble(out var raw)) { color = FromPacked((int)Math.Round(raw)); return true; }
            return false;
        }
        if (element.ValueKind == JsonValueKind.String)
        {
            var text = element.GetString();
            if (string.IsNullOrWhiteSpace(text)) return false;
            try { color = StudioRgb.FromHex(text); return true; }
            catch (FormatException) { return false; }
        }
        if (element.ValueKind == JsonValueKind.Object &&
            TryGet(element, "r", out var r) && TryGet(element, "g", out var g) && TryGet(element, "b", out var b))
        {
            color = new StudioRgb(ToByte(r), ToByte(g), ToByte(b));
            return true;
        }
        return false;
    }
    private static byte ToByte(JsonElement element) =>
        element.TryGetInt32(out var value) ? (byte)Math.Clamp(value, 0, 255)
        : element.TryGetDouble(out var raw) ? (byte)Math.Clamp(Math.Round(raw), 0, 255)
        : (byte)0;
    private static int Pack(StudioRgb color) => (color.R << 16) | (color.G << 8) | color.B;
    private static StudioRgb FromPacked(int packed) =>
        new((byte)((packed >> 16) & 0xFF), (byte)((packed >> 8) & 0xFF), (byte)(packed & 0xFF));
    private static int FromUnit(double value) =>
        Math.Clamp((int)Math.Round(value * 65535, MidpointRounding.AwayFromZero), 0, 65535);
    private static double ToUnit(int value) => Math.Clamp(value / 65535d, 0, 1);
    private static JsonElement RawJson(string raw)
    {
        try
        {
            using var document = JsonDocument.Parse(raw);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(raw);
        }
    }
    private static bool TryGet(JsonElement element, string name, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object) return false;
        if (element.TryGetProperty(name, out value)) return true;
        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            value = property.Value;
            return true;
        }
        return false;
    }
    private static string? GetString(JsonElement element, string name) =>
        TryGet(element, name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string? ReadFlexibleString(JsonElement element, string name)
    {
        if (!TryGet(element, name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }
    private static string? ReadRawJsonText(JsonElement element, string name)
    {
        if (!TryGet(element, name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
    }
    private static bool ReadBool(JsonElement element, string name, bool fallback = false)
    {
        if (!TryGet(element, name, out var value)) return fallback;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => fallback,
        };
    }
    private static int ReadInt(JsonElement element, string name, int fallback = 0) =>
        TryGet(element, name, out var value) && TryGetInt(value, out var number) ? number : fallback;
    private static int? ReadNullableInt(JsonElement element, string name) =>
        TryGet(element, name, out var value) && TryGetInt(value, out var number) ? number : null;
    private static bool TryGetInt(JsonElement value, out int number)
    {
        number = 0;
        if (value.ValueKind == JsonValueKind.Number)
        {
            if (value.TryGetInt32(out number)) return true;
            if (!value.TryGetDouble(out var raw)) return false;
            number = (int)Math.Round(raw);
            return true;
        }

        return value.ValueKind == JsonValueKind.String &&
               int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
    }
    private static double ReadDouble(JsonElement element, string name, double fallback = 0)
    {
        if (!TryGet(element, name, out var value)) return fallback;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)) return number;
        return value.ValueKind == JsonValueKind.String &&
               double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)
            ? number
            : fallback;
    }
    private static double? ReadNullableDouble(JsonElement element, string name)
    {
        var number = ReadDouble(element, name, double.NaN);
        return double.IsNaN(number) ? null : number;
    }
    private static double ReadSplit(JsonElement element)
    {
        if (!TryGet(element, "split", out var value)) return 0;
        if (value.ValueKind == JsonValueKind.True) return 100;
        return ReadDouble(element, "split");
    }
}
