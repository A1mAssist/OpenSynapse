using System.Text.Json;

namespace OpenSynapse.Core.Lighting;

/// <summary>A device currently reachable on this machine.</summary>
public sealed record StudioDeviceMatch(
    ushort ProductId,
    string InstanceId,
    StudioLedLayout? LedLayout = null);

public enum StudioImportIssueKind
{
    MissingDevice,
    AmbiguousDevice,
    RemappedDevice,
    RemappedLeds,
    DeviceDropped,
}

public sealed record StudioImportIssue(
    StudioImportIssueKind Kind,
    ushort ProductId,
    string? InstanceId,
    string Message);

public sealed record StudioImportResult(StudioProject Project, IReadOnlyList<StudioImportIssue> Issues);

/// <summary>
/// Validates and reconciles a .ChromaEffects document with the devices connected
/// here. Chroma Studio ships no JSON Schema; these checks follow its export
/// mapper, its importer (4.0.896, commit 99be5166) and two real third-party
/// profiles. Unknown properties are accepted so imports do not fail; this
/// reader/export path does not preserve them. The checks reject only structures
/// this reader would otherwise drop or misidentify. LED references stay as authored because the file carries no
/// layout to remap against. ARGB rows 0..5 are the controller's six channels;
/// legacy strip instance ids 26..31 fold into a row.
/// </summary>
public static class StudioChromaImport
{
    /// <summary>
    /// Chroma's 13 Studio effects plus the OpenSynapse names its reader restores:
    /// generate/gradient aliases and the engine-only radiate (17).
    /// </summary>
    private static readonly HashSet<string> KnownEffectNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ambient", "audio", "breathing", "chroma-generate", "fire", "reactive",
        "ripple", "spectrum", "starlight", "static", "tidal", "wave", "wheel",
        "generate", "gradient", "radiate",
    };

    public static StudioImportResult Import(string json, IReadOnlyList<StudioDeviceMatch> connected)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        Validate(json);
        StudioProject project;
        try
        {
            project = StudioProjectCodec.Deserialize(json);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidDataException(exception.Message, exception);
        }

        var matches = connected ?? [];
        var issues = new List<StudioImportIssue>();

        foreach (var profile in project.Profiles)
            ReconcileProfile(profile, matches, issues);

        return new StudioImportResult(project, issues);
    }

    private static void ReconcileProfile(
        StudioProfile profile,
        IReadOnlyList<StudioDeviceMatch> connected,
        List<StudioImportIssue> issues)
    {
        var placements = profile.Devices
            .Select(placement => new PlacementResolution(
                placement,
                Resolve(connected, placement.ProductId, placement.InstanceId, issues)))
            .ToList();
        var reportedMissing = new HashSet<StudioDevicePlacement>();

        foreach (var resolution in placements)
        {
            var match = resolution.Match;
            var placement = resolution.Placement;
            if (match is null || string.Equals(placement.InstanceId, match.InstanceId, StringComparison.OrdinalIgnoreCase))
                continue;
            issues.Add(new StudioImportIssue(
                StudioImportIssueKind.RemappedDevice,
                match.ProductId,
                match.InstanceId,
                "Remapped device placement from instance '" + placement.InstanceId + "' to '" + match.InstanceId + "'."));
        }

        foreach (var layer in profile.EnumerateLayers())
        foreach (var region in layer.Regions)
        foreach (var device in region.Devices.ToList())
        {
            StudioArgbDevice.NormalizeImportedRows(device);
            var placement = placements.FirstOrDefault(candidate => IsSameDevice(candidate.Placement, device));
            var match = placement is null
                ? Resolve(connected, device.ProductId, device.InstanceId, issues)
                : placement.Match;

            if (match is null)
            {
                region.Devices.Remove(device);
                issues.Add(new StudioImportIssue(
                    StudioImportIssueKind.MissingDevice,
                    device.ProductId,
                    device.InstanceId,
                    "No connected device matches product id " + device.ProductId + InstanceSuffix(device.InstanceId) + "."));
                issues.Add(new StudioImportIssue(
                    StudioImportIssueKind.DeviceDropped,
                    device.ProductId,
                    device.InstanceId,
                    "Dropped region device and its " + device.Leds.Count + " LED reference(s)."));
                if (placement is not null) reportedMissing.Add(placement.Placement);
                continue;
            }

            var remapped = !string.Equals(device.InstanceId, match.InstanceId, StringComparison.OrdinalIgnoreCase);
            var remappedLeds = remapped
                ? RemapLeds(device, match, placement?.Placement.Category)
                : null;
            if (remapped)
                issues.Add(new StudioImportIssue(
                    StudioImportIssueKind.RemappedLeds,
                    match.ProductId,
                    match.InstanceId,
                    "Remapped " + (remappedLeds?.Count ?? device.Leds.Count) +
                    " LED reference(s) from instance '" +
                    (device.InstanceId ?? "(none)") + "' to '" + match.InstanceId + "'."));

            device.InstanceId = match.InstanceId;
            device.ProductId = match.ProductId;
            if (!remapped) continue;
            if (remappedLeds is not null)
            {
                device.Leds.Clear();
                foreach (var led in remappedLeds) device.Leds.Add(led);
                continue;
            }

            for (var index = 0; index < device.Leds.Count; index++)
            {
                var led = device.Leds[index];
                if (!string.IsNullOrWhiteSpace(led.InstanceId))
                    device.Leds[index] = led with { InstanceId = match.InstanceId };
            }
        }

        // Applying the match after region devices keeps the authored identity
        // available for linking placements to their regions.
        foreach (var resolution in placements)
        {
            if (resolution.Match is null)
            {
                if (reportedMissing.Contains(resolution.Placement)) continue;
                var placement = resolution.Placement;
                issues.Add(new StudioImportIssue(
                    StudioImportIssueKind.MissingDevice,
                    placement.ProductId,
                    placement.InstanceId,
                    "No connected device matches profile placement product id " + placement.ProductId +
                    InstanceSuffix(placement.InstanceId) + "."));
                continue;
            }

            resolution.Placement.InstanceId = resolution.Match.InstanceId;
            resolution.Placement.ProductId = resolution.Match.ProductId;
        }
    }

    private static bool IsSameDevice(StudioDevicePlacement placement, StudioRegionDevice device) =>
        SameNonEmpty(placement.Id, device.Id) ||
        SameNonEmpty(placement.DeviceId, device.DeviceId) ||
        SameNonEmpty(placement.InstanceId, device.InstanceId) ||
        (placement.ProductId != 0 && placement.ProductId == device.ProductId &&
         string.IsNullOrWhiteSpace(placement.InstanceId) && string.IsNullOrWhiteSpace(device.InstanceId));

    private static bool SameNonEmpty(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static StudioDeviceMatch? Resolve(
        IReadOnlyList<StudioDeviceMatch> connected,
        ushort productId,
        string? instanceId,
        List<StudioImportIssue> issues)
    {
        if (!string.IsNullOrWhiteSpace(instanceId))
        {
            // Instance ids identify one physical device, but a stale or reused id must
            // not move the profile onto a different product. Unknown product ids (0)
            // still fall back to the instance identity.
            foreach (var candidate in connected)
                if (string.Equals(candidate.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase) &&
                    (productId == 0 || candidate.ProductId == productId))
                    return candidate;
        }

        var candidates = connected.Where(candidate => candidate.ProductId == productId).ToList();
        if (candidates.Count == 1) return candidates[0];
        if (candidates.Count == 0) return null;

        var picked = candidates[0];
        issues.Add(new StudioImportIssue(
            StudioImportIssueKind.AmbiguousDevice,
            productId,
            picked.InstanceId,
            "Product id " + productId + " matches " + candidates.Count + " connected devices; using the first one."));
        return picked;
    }

    /// <summary>
    /// Chroma remaps LEDs when a device is replaced: keyboard/system LEDs match
    /// on inputType+inputData, other devices on regionId+regionIndex, and a
    /// miss falls back to every LED of the target device.
    /// </summary>
    private static List<StudioLedRef>? RemapLeds(
        StudioRegionDevice device,
        StudioDeviceMatch match,
        string? category)
    {
        var layout = match.LedLayout;
        if (layout is null || (layout.Leds.Count == 0 && layout.Inputs.Count == 0)) return null;

        var hasRegions = device.Leds.Any(led => led.RegionId is not null);
        var useInput = string.Equals(category, "keyboard", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(category, "system", StringComparison.OrdinalIgnoreCase) ||
            (!hasRegions && device.Leds.Any(led =>
                string.Equals(led.InputType, "kbd", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(led.InputType, "system", StringComparison.OrdinalIgnoreCase)));

        var remapped = new List<StudioLedRef>();
        foreach (var led in device.Leds)
        {
            if (useInput)
            {
                if (!TryParseInput(led, out var type, out var code, out var modifier)) continue;
                foreach (var candidate in layout.Inputs)
                {
                    if (!string.Equals(candidate.InputType, type, StringComparison.OrdinalIgnoreCase) ||
                        candidate.InputCode != code ||
                        candidate.InputModifier != modifier)
                    {
                        continue;
                    }

                    remapped.Add(led with
                    {
                        Row = candidate.MatrixRow,
                        Col = candidate.MatrixCol,
                        InstanceId = match.InstanceId,
                    });
                    break;
                }

                continue;
            }

            if (led.RegionId is not { } regionId || led.RegionIndex is not { } regionIndex) continue;
            foreach (var candidate in layout.Leds)
            {
                if (candidate.RegionId != regionId || candidate.RegionIndex != regionIndex) continue;
                remapped.Add(led with
                {
                    Row = candidate.MatrixRow,
                    Col = candidate.MatrixCol,
                    InstanceId = match.InstanceId,
                });
                break;
            }
        }

        return remapped.Count > 0 ? remapped : AllTargetLeds(layout, match.InstanceId);
    }

    private static bool TryParseInput(
        StudioLedRef led,
        out string type,
        out int code,
        out int modifier)
    {
        type = led.InputType ?? string.Empty;
        code = 0;
        modifier = 0;
        if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(led.InputData)) return false;

        var text = led.InputData.Trim();
        if (text.Length >= 2 && text[0] == '[' && text[^1] == ']') text = text[1..^1];
        var parts = text.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || !int.TryParse(parts[0], out code)) return false;
        if (parts.Length > 1 && !int.TryParse(parts[1], out modifier)) return false;
        return true;
    }

    private static List<StudioLedRef> AllTargetLeds(
        StudioLedLayout layout,
        string instanceId)
    {
        var leds = new List<StudioLedRef>();
        foreach (var led in layout.Leds)
        {
            StudioInputMap? input = null;
            foreach (var candidate in layout.Inputs)
            {
                if (candidate.MatrixRow != led.MatrixRow || candidate.MatrixCol != led.MatrixCol) continue;
                input = candidate;
                break;
            }

            leds.Add(new StudioLedRef(
                led.MatrixRow,
                led.MatrixCol,
                led.RegionId,
                led.RegionIndex,
                InputType: input?.InputType,
                InputData: input is { } mapped
                    ? FormattableString.Invariant($"[{mapped.InputCode},{mapped.InputModifier}]")
                    : null,
                InstanceId: instanceId));
        }

        if (leds.Count > 0) return leds;

        foreach (var input in layout.Inputs)
        {
            leds.Add(new StudioLedRef(
                input.MatrixRow,
                input.MatrixCol,
                InputType: input.InputType,
                InputData: FormattableString.Invariant($"[{input.InputCode},{input.InputModifier}]"),
                InstanceId: instanceId));
        }

        return leds;
    }

    private static string InstanceSuffix(string? instanceId) =>
        string.IsNullOrWhiteSpace(instanceId) ? string.Empty : " with instance '" + instanceId + "'";

    /// <summary>Mirrors <see cref="StudioProjectCodec"/>'s v1 document detection.</summary>
    private static bool IsLegacyProject(JsonElement root) =>
        TryGet(root, "schemaVersion", out var version) &&
        version.ValueKind == JsonValueKind.Number &&
        version.TryGetDouble(out var number) && number <= 1 &&
        TryGet(root, "layers", out var layers) &&
        layers.ValueKind == JsonValueKind.Array;

    private static void Validate(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The .ChromaEffects file is not valid JSON.", exception);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw Invalid("root", "expected a JSON object");
            // Chroma's importer ignores the root version, so a missing value stays
            // accepted for older files; a present unknown value is still rejected.
            if (TryGet(root, "version", out var version) &&
                (version.ValueKind != JsonValueKind.Number ||
                 !version.TryGetInt32(out var formatVersion) || formatVersion != 1))
                throw Invalid("version", "expected the Chroma effects format version 1");
            // Older OpenSynapse project documents use the top-level schemaVersion
            // plus layers shape; let the codec's v1 migration handle them.
            if (IsLegacyProject(root)) return;
            if (!TryGet(root, "profiles", out var profiles) || profiles.ValueKind != JsonValueKind.Array)
                throw Invalid("profiles", "expected an array");
            if (profiles.GetArrayLength() == 0) throw Invalid("profiles", "must contain at least one profile");

            var index = 0;
            foreach (var profile in profiles.EnumerateArray())
                ValidateProfile(profile, "profiles[" + index++ + "]");
        }
    }

    private static void ValidateProfile(JsonElement profile, string path)
    {
        if (profile.ValueKind != JsonValueKind.Object) throw Invalid(path, "expected an object");
        ValidateOptionalString(profile, "id", path);
        ValidateOptionalString(profile, "title", path);
        if (TryGet(profile, "default", out var isDefault) &&
            isDefault.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw Invalid(path + ".default", "expected a boolean");
        if (!TryGet(profile, "layers", out var layers) || layers.ValueKind != JsonValueKind.Array)
            throw Invalid(path + ".layers", "expected an array");
        if (TryGet(profile, "devices", out var devices))
        {
            if (devices.ValueKind != JsonValueKind.Array) throw Invalid(path + ".devices", "expected an array");
            var deviceIndex = 0;
            foreach (var device in devices.EnumerateArray())
                ValidateIdentity(device, path + ".devices[" + deviceIndex++ + "]");
        }

        var layerIndex = 0;
        foreach (var layer in layers.EnumerateArray())
            ValidateLayer(layer, path + ".layers[" + layerIndex++ + "]");
    }

    private static void ValidateLayer(JsonElement layer, string path)
    {
        if (layer.ValueKind != JsonValueKind.Object) throw Invalid(path, "expected an object");
        var type = ReadString(layer, "type");
        if (type is not null &&
            !type.Equals("layer", StringComparison.OrdinalIgnoreCase) &&
            !type.Equals("group", StringComparison.OrdinalIgnoreCase))
            throw Invalid(path + ".type", "expected layer or group, got '" + type + "'");

        var isGroup = string.Equals(type, "group", StringComparison.OrdinalIgnoreCase) ||
            ReadBool(layer, "isGroup");
        if (isGroup)
        {
            if (!TryGet(layer, "layers", out var children)) return;
            if (children.ValueKind != JsonValueKind.Array) throw Invalid(path + ".layers", "expected an array");
            var childIndex = 0;
            foreach (var child in children.EnumerateArray())
                ValidateLayer(child, path + ".layers[" + childIndex++ + "]");
            return;
        }

        // A layer entry with children is a group; treating it as a plain layer
        // would silently drop those children.
        if (TryGet(layer, "layers", out var nested))
        {
            if (nested.ValueKind != JsonValueKind.Array)
                throw Invalid(path + ".layers", "expected an array");
            if (nested.GetArrayLength() > 0)
                throw Invalid(path + ".layers", "child layers require type 'group'");
        }

        if (TryGet(layer, "name", out var name) && name.ValueKind == JsonValueKind.String)
        {
            var effect = name.GetString();
            if (!string.IsNullOrWhiteSpace(effect) && !KnownEffectNames.Contains(effect))
                throw Invalid(path + ".name", "unknown effect '" + effect + "'");
        }

        ValidateOptionalObject(layer, "params", path);
        ValidateOptionalObject(layer, "params2", path);
        if (!TryGet(layer, "regions", out var regions)) return;
        if (regions.ValueKind != JsonValueKind.Array) throw Invalid(path + ".regions", "expected an array");
        var regionIndex = 0;
        foreach (var region in regions.EnumerateArray())
            ValidateRegion(region, path + ".regions[" + regionIndex++ + "]");
    }

    private static void ValidateRegion(JsonElement region, string path)
    {
        if (region.ValueKind != JsonValueKind.Object) throw Invalid(path, "expected an object");
        ValidateOptionalObject(region, "params", path);
        ValidateOptionalObject(region, "params2", path);
        if (!TryGet(region, "devices", out var devices)) return;
        if (devices.ValueKind != JsonValueKind.Array) throw Invalid(path + ".devices", "expected an array");
        var deviceIndex = 0;
        foreach (var device in devices.EnumerateArray())
            ValidateIdentity(device, path + ".devices[" + deviceIndex++ + "]");
    }

    private static void ValidateIdentity(JsonElement device, string path)
    {
        if (device.ValueKind != JsonValueKind.Object) throw Invalid(path, "expected an object");
        if (TryGet(device, "productId", out var productId) &&
            (productId.ValueKind != JsonValueKind.Number || !productId.TryGetInt32(out var id) ||
             id is < 0 or > ushort.MaxValue))
            throw Invalid(path + ".productId", "expected an integer from 0 to 65535");

        if (TryGet(device, "instanceId", out var instanceId) &&
            instanceId.ValueKind is not (JsonValueKind.Null or JsonValueKind.String) &&
            (instanceId.ValueKind != JsonValueKind.Number || !instanceId.TryGetInt64(out var instance) || instance < 0))
            throw Invalid(path + ".instanceId", "expected a non-negative integer, string or null");

        if (TryGet(device, "deviceId", out var deviceId) &&
            deviceId.ValueKind is not (JsonValueKind.Null or JsonValueKind.String or JsonValueKind.Number))
            throw Invalid(path + ".deviceId", "expected a string, number or null");

        if (TryGet(device, "layout", out var layout) &&
            layout.ValueKind is not (JsonValueKind.Null or JsonValueKind.String or JsonValueKind.Number))
            throw Invalid(path + ".layout", "expected a string, number or null");

        if (TryGet(device, "orientation", out var orientation) &&
            (orientation.ValueKind != JsonValueKind.Number || !orientation.TryGetInt32(out var angle) ||
             angle is not (0 or 90 or 180 or 270)))
            throw Invalid(path + ".orientation", "expected 0, 90, 180 or 270");

        if (TryGet(device, "parentId", out var parentId) &&
            parentId.ValueKind is not (JsonValueKind.Null or JsonValueKind.String or JsonValueKind.Number))
            throw Invalid(path + ".parentId", "expected a string, number or null");
        ValidateOptionalNumber(device, "parentX", path);
        ValidateOptionalNumber(device, "parentY", path);
        if (TryGet(device, "placementIndex", out var placementIndex) &&
            placementIndex.ValueKind != JsonValueKind.Null &&
            (placementIndex.ValueKind != JsonValueKind.Number || !placementIndex.TryGetInt32(out _)))
            throw Invalid(path + ".placementIndex", "expected an integer or null");

        ValidateLedArray(device, "leds", path);
        ValidateLedArray(device, "ledRef", path);
    }

    private static void ValidateLedArray(JsonElement device, string name, string path)
    {
        if (!TryGet(device, name, out var leds)) return;
        if (leds.ValueKind != JsonValueKind.Array) throw Invalid(path + "." + name, "expected an array");
        var ledIndex = 0;
        foreach (var led in leds.EnumerateArray())
            ValidateLed(led, path + "." + name + "[" + ledIndex++ + "]");
    }

    private static void ValidateOptionalNumber(JsonElement element, string name, string path)
    {
        if (!TryGet(element, name, out var value) || value.ValueKind == JsonValueKind.Null) return;
        if (value.ValueKind != JsonValueKind.Number)
            throw Invalid(path + "." + name, "expected a number or null");
    }

    private static void ValidateLed(JsonElement led, string path)
    {
        if (led.ValueKind != JsonValueKind.Object) throw Invalid(path, "expected an object");
        if (!TryGet(led, "row", out var row) || row.ValueKind != JsonValueKind.Number ||
            !row.TryGetInt32(out var rowValue) || rowValue < 0)
            throw Invalid(path + ".row", "expected a non-negative integer");
        if (!TryGet(led, "col", out var col) || col.ValueKind != JsonValueKind.Number ||
            !col.TryGetInt32(out var colValue) || colValue < 0)
            throw Invalid(path + ".col", "expected a non-negative integer");
        ValidateOptionalNonNegativeInt(led, "regionId", path);
        ValidateOptionalNonNegativeInt(led, "regionIndex", path);
    }

    private static void ValidateOptionalNonNegativeInt(JsonElement element, string name, string path)
    {
        if (!TryGet(element, name, out var value) || value.ValueKind == JsonValueKind.Null) return;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number) || number < 0)
            throw Invalid(path + "." + name, "expected a non-negative integer");
    }

    private static void ValidateOptionalObject(JsonElement element, string name, string path)
    {
        if (!TryGet(element, name, out var value) || value.ValueKind == JsonValueKind.Null) return;
        if (value.ValueKind != JsonValueKind.Object) throw Invalid(path + "." + name, "expected an object");
    }

    private static void ValidateOptionalString(JsonElement element, string name, string path)
    {
        if (!TryGet(element, name, out var value) || value.ValueKind == JsonValueKind.Null) return;
        if (value.ValueKind != JsonValueKind.String) throw Invalid(path + "." + name, "expected a string");
    }

    private static InvalidDataException Invalid(string path, string message) =>
        new InvalidDataException("The .ChromaEffects file is invalid at " + path + ": " + message + ".");

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

    private static string? ReadString(JsonElement element, string name) =>
        TryGet(element, name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool ReadBool(JsonElement element, string name) =>
        TryGet(element, name, out var value) && value.ValueKind == JsonValueKind.True;

    private sealed record PlacementResolution(StudioDevicePlacement Placement, StudioDeviceMatch? Match);
}
