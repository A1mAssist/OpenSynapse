using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace OpenSynapse.Core.Lighting;

/// <summary>
/// Reads Razer Synapse 3 lighting profiles. Synapse exports one XML document per profile,
/// usually packed in a ZIP. Each document carries DeviceLayout and EffectLayers; the
/// mapping follows Razer's own Chroma migration (convertSynapse3ProfilesToJSON).
/// Key maps, macros and DPI live outside LightingEffects and are not imported here.
/// </summary>
internal static class StudioSynapse3Import
{
    // Synapse DeviceLayout units are 40 Chroma canvas units.
    private const double CanvasScale = 40;
    private const long InstanceIdBase = 2_147_483_648;
    private const int AdjustedInstanceProductId = 3849;

    public static StudioImportResult Import(string path, IReadOnlyList<StudioDeviceMatch> connected)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var profiles = ReadProfiles(path, out var projectName);
        if (profiles.Count == 0)
        {
            throw new InvalidOperationException(
                "No LightingEffects data was found in the Razer Synapse 3 profile.");
        }

        var project = new StudioProject { Name = projectName };
        foreach (var profile in profiles) project.Profiles.Add(profile);
        project.SelectedProfileId = profiles[0].Id;
        ReconcilePlacements(project, connected);

        // Reuse the Chroma importer's device reconciliation by round-tripping through the
        // canonical document format; this keeps one matching rule for both import paths.
        return StudioChromaImport.Import(StudioProjectCodec.Serialize(project), connected);
    }

    internal static IReadOnlyList<StudioProfile> ReadProfiles(string path, out string projectName)
    {
        projectName = Path.GetFileNameWithoutExtension(path);
        var profiles = new List<StudioProfile>();
        if (Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var archive = ZipFile.OpenRead(path);
            foreach (var entry in archive.Entries)
            {
                if (entry.Length == 0 ||
                    !entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                using var stream = entry.Open();
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                AddProfiles(reader.ReadToEnd(), Path.GetFileNameWithoutExtension(entry.Name), profiles);
            }

            return profiles;
        }

        AddProfiles(File.ReadAllText(path), projectName, profiles);
        return profiles;
    }

    internal static IReadOnlyList<StudioProfile> ParseProfiles(string xml)
    {
        var profiles = new List<StudioProfile>();
        AddProfiles(xml, null, profiles);
        return profiles;
    }

    private static void AddProfiles(string xml, string? fallbackName, List<StudioProfile> profiles)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (XmlException exception)
        {
            throw new InvalidOperationException("The selected file is not valid XML.", exception);
        }

        if (document.Root is null) return;
        var roots = document.Root
            .DescendantsAndSelf()
            .Where(element => Is(element, "LightingEffects"))
            .ToArray();
        foreach (var root in roots)
        {
            profiles.Add(ReadProfile(root, fallbackName));
        }
    }

    private static StudioProfile ReadProfile(XElement root, string? fallbackName)
    {
        var title = Child(root, "Name")?.Value?.Trim();
        var profile = new StudioProfile(
            string.IsNullOrWhiteSpace(title) ? fallbackName ?? "Synapse 3" : title);

        ReadPlacements(root, profile);
        ReadLayers(root, profile);
        return profile;
    }

    private static void ReconcilePlacements(
        StudioProject project,
        IReadOnlyList<StudioDeviceMatch> connected)
    {
        foreach (var profile in project.Profiles)
        foreach (var placement in profile.Devices)
        {
            var exact = connected.FirstOrDefault(candidate =>
                string.Equals(candidate.InstanceId, placement.InstanceId, StringComparison.OrdinalIgnoreCase));
            if (exact is not null) continue;

            var candidates = connected.Where(candidate => candidate.ProductId == placement.ProductId).ToArray();
            if (candidates.Length != 1) continue;
            placement.InstanceId = candidates[0].InstanceId;
            placement.ProductId = candidates[0].ProductId;
        }
    }

    private static void ReadPlacements(XElement root, StudioProfile profile)
    {
        var placements = new List<(StudioDevicePlacement Placement, ushort? ParentProductId)>();
        foreach (var item in Children(Child(Child(root, "DeviceLayout"), "DevPositions"), "Item"))
        {
            var device = Child(Child(item, "Key"), "Device");
            var devPos = Child(Child(item, "Value"), "DevPos");
            if (device is null || devPos is null) continue;

            var productId = ReadProductId(Child(device, "Product_ID"));
            if (productId == 0) continue;

            var instanceId = ReadInstanceId(device, productId);
            var placement = new StudioDevicePlacement
            {
                Id = $"device-{productId}-{instanceId}",
                DeviceId = $"device-{productId}-{instanceId}",
                ProductId = productId,
                InstanceId = instanceId ?? string.Empty,
                ProductName = Child(device, "Name")?.Value?.Trim(),
                Layout = Child(device, "Layout")?.Value?.Trim(),
                X = ReadDouble(Child(devPos, "X")) * CanvasScale,
                Y = ReadDouble(Child(devPos, "Y")) * CanvasScale,
                Rotation = NormalizeRotation((int)ReadDouble(Child(devPos, "Angle"))),
            };

            var parentId = ReadProductId(Child(devPos, "ParentProductID"));
            placements.Add((placement, parentId == 0 ? null : parentId));
        }

        foreach (var (placement, parentId) in placements.ToArray())
        {
            if (parentId is not { } id) continue;
            var parent = placements.FirstOrDefault(candidate => candidate.Placement.ProductId == id).Placement;
            if (parent is null) continue;
            placement.X += parent.X;
            placement.Y += parent.Y;
        }

        foreach (var (placement, _) in placements) profile.Devices.Add(placement);
    }

    private static void ReadLayers(XElement root, StudioProfile profile)
    {
        var items = new List<StudioLayerItem>();
        var groups = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in Children(Child(root, "EffectLayers"), "EffectLayer"))
        {
            var effect = Child(element, "Effect")?.Value?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(effect)) continue;

            var groupId = Child(element, "GroupId")?.Value?.Trim();
            if (effect == "none")
            {
                var group = StudioLayerItem.CreateGroup(
                    string.IsNullOrWhiteSpace(groupId) ? "Group" : groupId);
                items.Add(group);
                if (!string.IsNullOrWhiteSpace(groupId)) groups[groupId] = items.Count - 1;
                continue;
            }

            var kind = MapEffect(effect);
            if (kind is null)
            {
                // Synapse 3 only emits the effects below; anything else is a newer or
                // third-party effect and is left out instead of being shown as solid.
                continue;
            }

            var layer = StudioLayerItem.CreateLayer(effect.ToUpperInvariant(), kind.Value);
            layer.Visible = ReadBool(Child(element, "Visible"), fallback: true);
            foreach (var regionElement in Children(Child(element, "Regions"), "EffectRegion"))
            {
                layer.Regions.Add(ReadRegion(regionElement, kind.Value));
            }

            layer.Params = layer.Regions.FirstOrDefault()?.Params.Clone()
                ?? StudioEffectCatalog.CreateDefault(kind.Value);

            if (!string.IsNullOrWhiteSpace(groupId) &&
                groups.TryGetValue(groupId, out var groupIndex) &&
                items[groupIndex] is { IsGroup: true } groupItem)
            {
                groupItem.Children.Insert(0, layer);
            }
            else
            {
                items.Add(layer);
            }
        }

        items.Reverse();
        foreach (var item in items) profile.Items.Add(item);
    }

    private static StudioRegion ReadRegion(XElement element, StudioEffectKind kind)
    {
        var parameters = StudioEffectCatalog.CreateDefault(kind);
        ApplyParameters(parameters, kind, element);
        parameters.Normalize();

        var region = new StudioRegion { Params = parameters };
        foreach (var item in Children(Child(element, "Cells"), "Item"))
        {
            var device = Child(Child(item, "Key"), "Device");
            if (device is null) continue;

            var productId = ReadProductId(Child(device, "Product_ID"));
            if (productId == 0) continue;

            var instanceId = ReadInstanceId(device, productId);
            var regionDevice = region.Devices.FirstOrDefault(
                candidate => candidate.ProductId == productId &&
                             string.Equals(candidate.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase));
            if (regionDevice is null)
            {
                regionDevice = new StudioRegionDevice
                {
                    Id = $"device-{productId}-{instanceId}",
                    ProductId = productId,
                    InstanceId = instanceId,
                };
                region.Devices.Add(regionDevice);
            }

            foreach (var cell in Children(Child(Child(item, "Value"), "ArrayOfDeviceCell"), "DeviceCell"))
            {
                var row = (int)ReadDouble(Child(cell, "Row"));
                var col = (int)ReadDouble(Child(cell, "Col"));
                if (row < 0 || col < 0) continue;
                regionDevice.Leds.Add(new StudioLedRef(row, col, InstanceId: instanceId));
            }
        }

        return region;
    }

    private static void ApplyParameters(StudioEffectParams parameters, StudioEffectKind kind, XElement region)
    {
        ApplyPlayback(parameters, region);
        switch (kind)
        {
            case StudioEffectKind.Solid:
                ApplyColors(parameters, region, 1);
                break;
            case StudioEffectKind.Breathing:
                ApplyColors(parameters, region, 2);
                parameters.RandomColor = ReadBool(Child(region, "RandomColor"));
                parameters.DurationMs = ReadDuration(region, 6_000, 14_000, 28_000);
                break;
            case StudioEffectKind.Spectrum:
                ApplyPalette(parameters, region);
                parameters.DurationMs = ReadDuration(region, 10_000, 37_740, 60_000);
                break;
            case StudioEffectKind.Wave:
                ApplyPalette(parameters, region);
                parameters.Width = ReadDouble(Child(region, "Width"), parameters.Width);
                parameters.Speed = ReadDouble(Child(region, "Speed"), parameters.Speed);
                parameters.Split = ReadDouble(Child(region, "Split"), parameters.Split);
                parameters.PauseMs = (int)Math.Round(ReadDouble(Child(region, "Pause")) * 1000);
                parameters.Angle = (int)ReadDouble(Child(region, "Angle"), parameters.Angle);
                break;
            case StudioEffectKind.Reactive:
                ApplyColors(parameters, region, 1);
                parameters.RandomColor = ReadBool(Child(region, "RandomColor"));
                parameters.DurationMs = ReadDuration(region, 500, 1_500, 2_000);
                break;
            case StudioEffectKind.Ripple:
                ApplyPalette(parameters, region);
                parameters.Width = ReadDouble(Child(region, "Width"), parameters.Width);
                parameters.Speed = ReadDouble(Child(region, "Speed"), parameters.Speed);
                break;
            case StudioEffectKind.Starlight:
                ApplyPalette(parameters, region);
                parameters.RandomColor = ReadBool(Child(region, "RandomColor"));
                parameters.Density = ReadDouble(Child(region, "Density"), parameters.Density);
                parameters.DurationMs = ReadDuration(region, 500, 1_700, 3_000);
                break;
            case StudioEffectKind.Fire:
                ApplyColors(parameters, region, 2);
                break;
            case StudioEffectKind.Ambient:
                ApplyAmbient(parameters, region);
                break;
            case StudioEffectKind.Audio:
                ApplyPalette(parameters, region);
                parameters.Boost = ReadDouble(Child(region, "BoostLevel"), parameters.Boost);
                parameters.AutoBoost = ReadBool(Child(region, "AutoBoost"));
                ApplyDecay(parameters, region);
                break;
            case StudioEffectKind.Wheel:
                ApplyPalette(parameters, region);
                parameters.Speed = ReadDouble(Child(region, "Speed"), parameters.Speed);
                break;
            case StudioEffectKind.Tidal:
                ApplyColors(parameters, region, 2);
                parameters.Speed = ReadDouble(Child(region, "Speed"), parameters.Speed);
                parameters.Split = ReadDouble(Child(region, "Split"), parameters.Split);
                parameters.Angle = (int)ReadDouble(Child(region, "Angle"), parameters.Angle);
                break;
        }
    }

    private static void ApplyColors(StudioEffectParams parameters, XElement region, int count)
    {
        var colors = ReadColors(region);
        if (colors.Count == 0) return;
        if (!colors[0].IsBlank) parameters.Primary = colors[0].Rgb;
        if (count > 1 && colors.Count > 1 && !colors[1].IsBlank) parameters.Secondary = colors[1].Rgb;
    }

    private static void ApplyPalette(StudioEffectParams parameters, XElement region)
    {
        var colors = ReadColors(region);
        if (colors.Count == 0) return;

        var stops = ReadStops(region);
        parameters.Primary = colors[0].Rgb;
        parameters.Secondary = colors[^1].Rgb;
        parameters.ColorStops.Clear();
        for (var index = 0; index < colors.Count; index++)
        {
            var stop = index < stops.Count
                ? stops[index]
                : colors.Count == 1
                    ? 0
                    : (int)Math.Round(index * 100d / (colors.Count - 1));
            parameters.ColorStops.Add(new StudioColorStop(Math.Clamp(stop, 0, 100), colors[index].Rgb));
        }

        if (parameters.ColorStops.Count == 1)
        {
            parameters.ColorStops.Add(new StudioColorStop(100, colors[0].Rgb));
        }

        parameters.ColorStopsCustom = true;
    }

    private static void ApplyAmbient(StudioEffectParams parameters, XElement region)
    {
        parameters.Blur = Math.Clamp((int)ReadDouble(Child(region, "Gradient"), parameters.Blur), 0, 9);
        var rect = Child(region, "ScreenRect");
        if (rect is null) return;

        var top = Clamp01(ReadDouble(Child(rect, "Top")));
        var right = Clamp01(ReadDouble(Child(rect, "Right")));
        var bottom = Clamp01(ReadDouble(Child(rect, "Bottom")));
        var left = Clamp01(ReadDouble(Child(rect, "Left")));
        parameters.ScreenRect = new StudioScreenRect(
            left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
        parameters.Screen = left <= 0.001 && top <= 0.001 && right >= 0.999 && bottom >= 0.999
            ? "full"
            : "custom";
    }

    private static void ApplyDecay(StudioEffectParams parameters, XElement region)
    {
        var milliseconds = ReadDouble(Child(region, "DecayMs"));
        if (milliseconds <= 0) return;
        // Synapse stores milliseconds; our inspector uses 0.1 s steps.
        parameters.Decay = Math.Round(milliseconds / 1000d / 0.1, MidpointRounding.AwayFromZero) * 0.1;
    }

    private static void ApplyPlayback(StudioEffectParams parameters, XElement region)
    {
        parameters.PlaybackStart = Child(region, "Start")?.Value?.Trim().ToLowerInvariant() switch
        {
            "onkeypress" => StudioPlaybackStart.OnPress,
            "onselectedkeypress" => StudioPlaybackStart.OnSelectedKeys,
            _ => StudioPlaybackStart.Random,
        };
        var end = Child(region, "End")?.Value?.Trim().ToLowerInvariant();
        parameters.PlaybackEnd = end switch
        {
            "never" => StudioPlaybackEnd.Never,
            _ when end?.StartsWith("after", StringComparison.Ordinal) == true => StudioPlaybackEnd.After,
            _ => StudioPlaybackEnd.Toggle,
        };
        parameters.Cycles = parameters.PlaybackEnd == StudioPlaybackEnd.After
            ? Math.Clamp((int)ReadDouble(Child(region, "Cycle"), 1), 1, 100)
            : -1;
    }

    private static IReadOnlyList<SynapseColor> ReadColors(XElement region)
    {
        var colors = new List<SynapseColor>();
        foreach (var color in Children(Child(region, "Colors"), "RzColor"))
        {
            colors.Add(new SynapseColor(
                new StudioRgb(
                    ReadByte(Child(color, "Red")),
                    ReadByte(Child(color, "Green")),
                    ReadByte(Child(color, "Blue"))),
                ReadBool(Child(color, "IsBlank"))));
        }

        return colors;
    }

    private readonly record struct SynapseColor(StudioRgb Rgb, bool IsBlank);

    private static IReadOnlyList<int> ReadStops(XElement region)
    {
        var stops = new List<int>();
        foreach (var stop in Children(Child(region, "Stops"), "unsignedInt"))
        {
            stops.Add((int)ReadDouble(stop));
        }

        return stops;
    }

    private static StudioEffectKind? MapEffect(string effect) => effect switch
    {
        "static" => StudioEffectKind.Solid,
        "breathing" => StudioEffectKind.Breathing,
        "spectrum" => StudioEffectKind.Spectrum,
        "wave" => StudioEffectKind.Wave,
        "reactive" => StudioEffectKind.Reactive,
        "ripple" => StudioEffectKind.Ripple,
        "starlight" => StudioEffectKind.Starlight,
        "fire" => StudioEffectKind.Fire,
        "ambient" => StudioEffectKind.Ambient,
        "audiometer" or "audio" => StudioEffectKind.Audio,
        "colorwheel" or "wheel" => StudioEffectKind.Wheel,
        "tidal" => StudioEffectKind.Tidal,
        _ => null,
    };

    private static ushort ReadProductId(XElement? element)
    {
        var text = element?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return 0;
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            ushort.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
        {
            return hex;
        }

        return ushort.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : (ushort)0;
    }

    private static string? ReadInstanceId(XElement device, ushort productId)
    {
        var text = Child(device, "RegionId")?.Value?.Trim();
        if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var regionId))
        {
            return null;
        }

        var instance = regionId - InstanceIdBase;
        if (productId == AdjustedInstanceProductId) instance--;
        return instance.ToString(CultureInfo.InvariantCulture);
    }

    private static int ReadDuration(XElement region, int shortMs, int normalMs, int longMs) =>
        Child(region, "Duration")?.Value?.Trim().ToLowerInvariant() switch
        {
            "short" => shortMs,
            "long" => longMs,
            _ => normalMs,
        };

    private static int NormalizeRotation(int angle)
    {
        var normalized = angle % 360;
        if (normalized < 0) normalized += 360;
        return normalized is 0 or 90 or 180 or 270 ? normalized : 0;
    }

    private static bool ReadBool(XElement? element, bool fallback = false)
    {
        var text = element?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return fallback;
        return text.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               text.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
               text == "1";
    }

    private static byte ReadByte(XElement? element) =>
        (byte)Math.Clamp((int)ReadDouble(element), 0, 255);

    private static double ReadDouble(XElement? element, double fallback = 0) =>
        double.TryParse(
            element?.Value?.Trim(),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : fallback;

    private static double Clamp01(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;

    private static XElement? Child(XElement? parent, string name) =>
        parent?.Elements().FirstOrDefault(
            element => string.Equals(element.Name.LocalName, name, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<XElement> Children(XElement? parent, string name) =>
        parent?.Elements().Where(
            element => string.Equals(element.Name.LocalName, name, StringComparison.OrdinalIgnoreCase)) ?? [];

    private static bool Is(XElement element, string name) =>
        string.Equals(element.Name.LocalName, name, StringComparison.OrdinalIgnoreCase);
}