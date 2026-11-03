using System.Collections.ObjectModel;
using System.Text.Json;
namespace OpenSynapse.Core.Lighting;
public static partial class StudioProjectCodec
{
    private static StudioProfile ReadProfile(JsonElement element)
    {
        var title = GetString(element, "title");
        if (string.IsNullOrWhiteSpace(title)) title = "Profile";
        var id = GetString(element, "id");
        var profile = new StudioProfile(title) { Id = string.IsNullOrWhiteSpace(id) ? StudioProfile.NewId() : id };
        if (TryGet(element, "layers", out var layers) && layers.ValueKind == JsonValueKind.Array)
            AttachLayers(ReadLayers(layers, new HashSet<Guid>()), profile.Items);
        ReadProfileDevices(element, profile);
        return profile;
    }

    private static void ReadProfileDevices(JsonElement element, StudioProfile profile)
    {
        if (TryGet(element, "devices", out var devices) && devices.ValueKind == JsonValueKind.Array)
            foreach (var device in devices.EnumerateArray())
                if (device.ValueKind == JsonValueKind.Object) MergePlacement(profile.Devices, ReadPlacement(device));

        if (TryGet(element, "openSynapse", out var extras) && extras.ValueKind == JsonValueKind.Object &&
            TryGet(extras, "devices", out var saved) && saved.ValueKind == JsonValueKind.Array)
            foreach (var device in saved.EnumerateArray())
                if (device.ValueKind == JsonValueKind.Object) MergePlacement(profile.Devices, ReadPlacement(device));
    }

    private static StudioDevicePlacement ReadPlacement(JsonElement element) => new()
    {
        Id = ReadFlexibleString(element, "id"),
        ProductId = (ushort)Math.Clamp(ReadInt(element, "productId"), 0, ushort.MaxValue),
        InstanceId = ReadFlexibleString(element, "instanceId") ?? string.Empty,
        DeviceId = ReadFlexibleString(element, "deviceId"),
        Category = GetString(element, "category"),
        ProductName = GetString(element, "productName"),
        ProductNameZhCn = GetString(element, "productNameZhCn"),
        ConfigJson = ReadRawJsonText(element, "config"),
        LedsJson = ReadRawJsonText(element, "leds"),
        Layout = ReadFlexibleString(element, "layout"),
        Width = ReadDouble(element, "width"),
        Height = ReadDouble(element, "height"),
        X = ReadDouble(element, "x", 5000),
        Y = ReadDouble(element, "y", 5000),
        Rotation = TryGet(element, "orientation", out _)
            ? ReadInt(element, "orientation")
            : ReadInt(element, "rotation"),
    };

    private static void MergePlacement(ObservableCollection<StudioDevicePlacement> placements, StudioDevicePlacement placement)
    {
        var existing = placements.FirstOrDefault(item => IsSamePlacement(item, placement));
        if (existing is null)
        {
            placements.Add(placement);
            return;
        }

        if (!string.IsNullOrWhiteSpace(placement.Id)) existing.Id = placement.Id;
        if (placement.ProductId != 0) existing.ProductId = placement.ProductId;
        if (!string.IsNullOrWhiteSpace(placement.InstanceId)) existing.InstanceId = placement.InstanceId;
        if (!string.IsNullOrWhiteSpace(placement.DeviceId)) existing.DeviceId = placement.DeviceId;
        if (!string.IsNullOrWhiteSpace(placement.Category)) existing.Category = placement.Category;
        if (!string.IsNullOrWhiteSpace(placement.ProductName)) existing.ProductName = placement.ProductName;
        if (!string.IsNullOrWhiteSpace(placement.ProductNameZhCn)) existing.ProductNameZhCn = placement.ProductNameZhCn;
        if (!string.IsNullOrWhiteSpace(placement.ConfigJson)) existing.ConfigJson = placement.ConfigJson;
        if (!string.IsNullOrWhiteSpace(placement.LedsJson)) existing.LedsJson = placement.LedsJson;
        if (!string.IsNullOrWhiteSpace(placement.Layout)) existing.Layout = placement.Layout;
        if (placement.Width > 0) existing.Width = placement.Width;
        if (placement.Height > 0) existing.Height = placement.Height;
        existing.X = placement.X;
        existing.Y = placement.Y;
        existing.Rotation = placement.Rotation;
    }

    private static bool IsSamePlacement(StudioDevicePlacement left, StudioDevicePlacement right) =>
        SameNonEmpty(left.Id, right.Id) ||
        SameNonEmpty(left.DeviceId, right.DeviceId) ||
        SameNonEmpty(left.InstanceId, right.InstanceId) ||
        (left.ProductId != 0 && left.ProductId == right.ProductId &&
         string.IsNullOrWhiteSpace(left.InstanceId) && string.IsNullOrWhiteSpace(right.InstanceId));
    private static List<ParsedLayer> ReadLayers(JsonElement array, HashSet<Guid> seen)
    {
        var result = new List<ParsedLayer>();
        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object) continue;
            var isGroup = IsGroup(element);
            var item = ReadLayer(element, isGroup);
            if (!seen.Add(item.Id)) continue;
            if (isGroup && TryGet(element, "layers", out var children) && children.ValueKind == JsonValueKind.Array)
                AttachLayers(ReadLayers(children, seen), item.Children);
            Guid? parentGroupId = null;
            if (Guid.TryParse(GetString(element, "groupId"), out var groupId)) parentGroupId = groupId;
            result.Add(new ParsedLayer(item, parentGroupId));
        }
        return result;
    }
    private static void AttachLayers(List<ParsedLayer> layers, ObservableCollection<StudioLayerItem> target)
    {
        var groups = layers.Where(layer => layer.Item.IsGroup).ToDictionary(layer => layer.Item.Id, layer => layer.Item);
        foreach (var layer in layers)
        {
            if (layer.GroupId is Guid groupId && groups.TryGetValue(groupId, out var group) && group != layer.Item)
                group.Children.Add(layer.Item);
            else
                target.Add(layer.Item);
        }
    }
    private static StudioLayerItem ReadLayer(JsonElement element, bool isGroup)
    {
        var paramsElement = TryGet(element, "params", out var rawParams) ? rawParams : default;
        var parameters = ReadParams(paramsElement);
        parameters.Effect = ReadEffect(element, paramsElement);
        var title = GetString(element, "title") ?? GetString(element, "name");
        if (string.IsNullOrWhiteSpace(title)) title = isGroup ? "Group" : "Layer";
        var opacity = 100d;
        var blend = StudioBlendMode.Normal;
        if (TryGet(element, "openSynapse", out var extras) && extras.ValueKind == JsonValueKind.Object)
        {
            opacity = ReadDouble(extras, "opacity", 100);
            blend = ReadBlend(GetString(extras, "blend"));
        }
        var item = new StudioLayerItem
        {
            Id = Guid.TryParse(GetString(element, "id"), out var id) ? id : Guid.NewGuid(),
            Type = isGroup ? StudioLayerType.Group : StudioLayerType.Layer,
            Title = title,
            Visible = ReadBool(element, "visible", true),
            Opened = ReadBool(element, "opened"),
            Opacity = opacity,
            Blend = blend,
            Params = parameters,
        };
        ReadRegions(element, item);
        return item;
    }
    private static void ReadRegions(JsonElement layer, StudioLayerItem item)
    {
        if (!TryGet(layer, "regions", out var regions) || regions.ValueKind != JsonValueKind.Array) return;
        foreach (var region in regions.EnumerateArray())
        {
            if (region.ValueKind != JsonValueKind.Object) continue;
            TryGet(region, "params", out var rp);
            var parsed = new StudioRegion { Params = ReadParams(rp) };
            // Chroma keeps the effect name on the layer and only effect params on the region.
            if (!TryGet(rp, "effect", out _)) parsed.Params.Effect = item.Params.Effect;
            if (TryGet(region, "devices", out var devices) && devices.ValueKind == JsonValueKind.Array)
                foreach (var device in devices.EnumerateArray())
                    if (device.ValueKind == JsonValueKind.Object) parsed.Devices.Add(ReadRegionDevice(device));
            item.Regions.Add(parsed);
        }
    }
    private static StudioRegionDevice ReadRegionDevice(JsonElement element)
    {
        var device = new StudioRegionDevice
        {
            Id = ReadFlexibleString(element, "id"),
            ProductId = (ushort)Math.Clamp(ReadInt(element, "productId"), 0, ushort.MaxValue),
            InstanceId = TryGet(element, "openSynapse", out var extras) && extras.ValueKind == JsonValueKind.Object
                ? ReadFlexibleString(extras, "instanceId") ?? ReadFlexibleString(element, "instanceId")
                : ReadFlexibleString(element, "instanceId"),
            DeviceId = ReadFlexibleString(element, "deviceId"),
        };
        if (TryGet(element, "leds", out var leds) && leds.ValueKind == JsonValueKind.Array)
            foreach (var led in leds.EnumerateArray())
            {
                if (led.ValueKind != JsonValueKind.Object) continue;
                device.Leds.Add(new StudioLedRef(
                    ReadInt(led, "row"), ReadInt(led, "col"),
                    ReadNullableInt(led, "regionId"), ReadNullableInt(led, "regionIndex"),
                    ReadFlexibleString(led, "inputType"), GetString(led, "inputData")));
            }
        return device;
    }
    private static StudioEffectParams ReadParams(JsonElement element)
    {
        var parameters = new StudioEffectParams();
        if (element.ValueKind != JsonValueKind.Object) return parameters;
        if (TryGet(element, "color", out var color) && TryReadColor(color, out var primary)) parameters.Primary = primary;
        if (TryGet(element, "color2", out var color2) && TryReadColor(color2, out var secondary)) parameters.Secondary = secondary;
        if (TryParseEffectName(GetString(element, "effect"), out var paramsEffect)) parameters.Effect = paramsEffect;
        if (TryGet(element, "colorStops", out var stops) && stops.ValueKind == JsonValueKind.Array)
            foreach (var stop in stops.EnumerateArray())
            {
                if (stop.ValueKind != JsonValueKind.Object) continue;
                var stopColor = TryGet(stop, "Color", out var rawColor) ? rawColor : default;
                var rgb = TryReadColor(stopColor, out var parsedColor) ? parsedColor : StudioRgb.Black;
                parameters.ColorStops.Add(new StudioColorStop(Math.Clamp(ReadInt(stop, "Stop"), 0, 100), rgb));
            }
        parameters.ColorStopsCustom = ReadBool(element, "colorStopsCustom");
        parameters.RandomColor = ReadBool(element, "randomColor");
        parameters.DurationMs = ReadInt(element, "duration");
        parameters.PauseMs = ReadInt(element, "pause");
        parameters.Cycles = ReadInt(element, "cycles", -1);
        parameters.Angle = ReadInt(element, "angle");
        parameters.PlaybackStart = ReadStart(GetString(element, "playbackStart"));
        parameters.PlaybackEnd = ReadEnd(GetString(element, "playbackEnd"));
        parameters.Width = ReadDouble(element, "width", 50);
        parameters.Speed = ReadDouble(element, "speed", 1);
        parameters.Split = ReadSplit(element);
        parameters.Density = ReadDouble(element, "density", 50);
        parameters.Boost = ReadDouble(element, "boost", 1);
        parameters.Decay = ReadDouble(element, "decay", 50);
        parameters.AutoBoost = ReadBool(element, "autoBoost");
        parameters.Screen = GetString(element, "screen");
        parameters.Blur = ReadInt(element, "blur");
        parameters.MediaSrc = GetString(element, "mediaSrc");
        if (TryGet(element, "currentConfig", out var config) && config.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
            parameters.CurrentConfigJson = config.ValueKind == JsonValueKind.String ? config.GetString() : config.GetRawText();
        if (TryGet(element, "effectConfigList", out var effectConfigs) && effectConfigs.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
            parameters.EffectConfigListJson = effectConfigs.ValueKind == JsonValueKind.String ? effectConfigs.GetString() : effectConfigs.GetRawText();
        parameters.CurrentConfigId = ReadInt(element, "currentConfigId", -1);
        if (TryGet(element, "screenRect", out var rect) && rect.ValueKind == JsonValueKind.Object)
            parameters.ScreenRect = new StudioScreenRect(
                ToUnit(ReadInt(rect, "Left")), ToUnit(ReadInt(rect, "Top")),
                ToUnit(ReadInt(rect, "Right")), ToUnit(ReadInt(rect, "Bottom")));
        return parameters;
    }
    private static StudioEffectKind ReadEffect(JsonElement layer, JsonElement paramsElement)
    {
        if (TryGet(layer, "openSynapse", out var extras) && TryParseEffectName(GetString(extras, "effect"), out var extraEffect))
            return extraEffect;
        if (TryGet(layer, "value", out var value) && TryGetInt(value, out var uiValue) &&
            StudioEffectMap.TryFromUiValue(uiValue, out var uiEffect))
            return uiEffect;
        if (TryParseEffectName(GetString(layer, "name"), out var namedEffect)) return namedEffect;
        if (TryParseEffectName(GetString(paramsElement, "effect"), out var paramsEffect)) return paramsEffect;
        if (paramsElement.ValueKind == JsonValueKind.Object)
        {
            if (TryGet(paramsElement, "mediaSrc", out _)) return StudioEffectKind.ChromaGenerate;
            if (TryGet(paramsElement, "screen", out _) || TryGet(paramsElement, "screenRect", out _)) return StudioEffectKind.Ambient;
            if (ReadBool(paramsElement, "autoBoost")) return StudioEffectKind.Audio;
            if (TryGet(paramsElement, "density", out _)) return StudioEffectKind.Starlight;
        }
        return StudioEffectKind.Solid;
    }
    private static StudioProject MigrateV1(JsonElement root)
    {
        var defaults = new StudioEffectParams();
        var documentName = GetString(root, "name");
        var project = new StudioProject();
        if (!string.IsNullOrWhiteSpace(documentName)) project.Name = documentName;
        var profile = new StudioProfile(string.IsNullOrWhiteSpace(documentName) ? "Profile" : documentName);
        if (TryGet(root, "layers", out var layers) && layers.ValueKind == JsonValueKind.Array)
            foreach (var layer in layers.EnumerateArray())
            {
                if (layer.ValueKind != JsonValueKind.Object) continue;
                var effect = ReadV1Effect(layer);
                // v1 speed was a multiplier; Chroma stores a rate for these effects.
                var speed = ReadDouble(layer, "speed", 1);
                if (effect is StudioEffectKind.Wave or StudioEffectKind.Ripple or StudioEffectKind.Tidal) speed *= 10;
                else if (effect is StudioEffectKind.Wheel) speed *= 60;
                profile.Items.Add(new StudioLayerItem
                {
                    Id = Guid.TryParse(GetString(layer, "id"), out var id) ? id : Guid.NewGuid(),
                    Type = StudioLayerType.Layer,
                    Title = GetString(layer, "name") ?? "Layer",
                    Visible = ReadBool(layer, "enabled", true),
                    Opacity = ReadDouble(layer, "opacity", 100),
                    Blend = ReadBlend(GetString(layer, "blend")),
                    Params = new StudioEffectParams
                    {
                        Effect = effect,
                        Primary = TryGet(layer, "primary", out var primary) && TryReadColor(primary, out var primaryColor) ? primaryColor : defaults.Primary,
                        Secondary = TryGet(layer, "secondary", out var secondary) && TryReadColor(secondary, out var secondaryColor) ? secondaryColor : defaults.Secondary,
                        Speed = speed,
                    },
                });
            }
        project.Profiles.Add(profile);
        project.SelectedProfileId = profile.Id;
        return project;
    }
    private static StudioEffectKind ReadV1Effect(JsonElement layer)
    {
        if (!TryGet(layer, "effect", out var value)) return StudioEffectKind.Solid;
        if (value.ValueKind == JsonValueKind.Number && TryGetInt(value, out var number) && Enum.IsDefined((StudioEffectKind)number))
            return (StudioEffectKind)number;
        return value.ValueKind == JsonValueKind.String && TryParseEffectName(value.GetString(), out var effect)
            ? effect
            : StudioEffectKind.Solid;
    }

    private readonly record struct ParsedLayer(StudioLayerItem Item, Guid? GroupId);
}
