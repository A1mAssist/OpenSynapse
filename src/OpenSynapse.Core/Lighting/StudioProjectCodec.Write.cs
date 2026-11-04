using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace OpenSynapse.Core.Lighting;
public static partial class StudioProjectCodec
{
    private static ProfileDto ToProfileDto(StudioProfile profile, bool isDefault, bool export)
    {
        var devices = CollectPlacements(profile);
        var deviceDtos = devices.Select((device, index) => ToProfileDeviceDto(device, index)).ToList();
        var dto = new ProfileDto
        {
            Id = profile.Id,
            Title = profile.Title,
            Default = isDefault ? true : null,
            Devices = deviceDtos,
            Layers = profile.Items.Select(item => ToLayerDto(item, null, export)).ToList(),
        };
        if (devices.Count > 0)
            dto.OpenSynapse = new ProfileExtrasDto
            {
                Devices = devices.Select((device, index) => new DeviceDto
                {
                    // Must match the official array entry; otherwise import reads two placements.
                    Id = deviceDtos[index].Id,
                    InstanceId = device.InstanceId,
                    ProductId = device.ProductId,
                    DeviceId = device.DeviceId,
                    Category = device.Category,
                    ProductName = device.ProductName,
                    ProductNameZhCn = device.ProductNameZhCn,
                    Layout = device.Layout,
                    Width = device.Width,
                    Height = device.Height,
                    X = device.X,
                    Y = device.Y,
                    Rotation = device.Rotation,
                }).ToList(),
            };
        return dto;
    }

    /// <summary>Profile placements plus any device that only appears in a region.</summary>
    private static List<StudioDevicePlacement> CollectPlacements(StudioProfile profile)
    {
        var result = profile.Devices.Select(device => device.Clone()).ToList();
        foreach (var layer in profile.EnumerateLayers())
        foreach (var region in layer.Regions)
        foreach (var device in region.Devices)
        {
            var placement = result.FirstOrDefault(item => IsSameDevice(item, device));
            if (placement is null)
            {
                result.Add(new StudioDevicePlacement
                {
                    Id = device.Id,
                    ProductId = device.ProductId,
                    InstanceId = device.InstanceId ?? string.Empty,
                    DeviceId = device.DeviceId,
                });
                continue;
            }

            if (placement.ProductId == 0) placement.ProductId = device.ProductId;
            if (string.IsNullOrWhiteSpace(placement.InstanceId)) placement.InstanceId = device.InstanceId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(placement.DeviceId)) placement.DeviceId = device.DeviceId;
            if (string.IsNullOrWhiteSpace(placement.Id)) placement.Id = device.Id;
        }

        return result;
    }

    private static bool IsSameDevice(StudioDevicePlacement placement, StudioRegionDevice device) =>
        SameNonEmpty(placement.Id, device.Id) ||
        SameNonEmpty(placement.DeviceId, device.DeviceId) ||
        SameNonEmpty(placement.InstanceId, device.InstanceId) ||
        (placement.ProductId != 0 && placement.ProductId == device.ProductId &&
         string.IsNullOrWhiteSpace(placement.InstanceId) && string.IsNullOrWhiteSpace(device.InstanceId));

    private static bool SameNonEmpty(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static ProfileDeviceDto ToProfileDeviceDto(StudioDevicePlacement device, int index) => new()
    {
        Id = string.IsNullOrWhiteSpace(device.Id)
            ? GeneratedDeviceId(device.ProductId, device.InstanceId, device.DeviceId, index)
            : device.Id,
        ProductId = device.ProductId,
        InstanceId = ChromaInstanceId(device.InstanceId),
        DeviceId = string.IsNullOrWhiteSpace(device.DeviceId) ? null : device.DeviceId,
        Category = device.Category,
        ProductName = device.ProductName,
        ProductNameZhCn = device.ProductNameZhCn,
        Config = string.IsNullOrWhiteSpace(device.ConfigJson) ? null : RawJson(device.ConfigJson),
        Leds = string.IsNullOrWhiteSpace(device.LedsJson) ? null : RawJson(device.LedsJson),
        Layout = FlexibleNumber(device.Layout),
        Width = device.Width > 0 ? device.Width : null,
        Height = device.Height > 0 ? device.Height : null,
        X = device.X,
        Y = device.Y,
        Orientation = NormalizeRotation(device.Rotation),
        ParentId = FlexibleNumber(device.ParentId),
        ParentX = device.ParentX,
        ParentY = device.ParentY,
        PlacementIndex = device.PlacementIndex,
        LedRef = string.IsNullOrWhiteSpace(device.LedRefJson) ? null : RawJson(device.LedRefJson),
    };

    private static string GeneratedDeviceId(ushort productId, string? instanceId, string? deviceId, int index)
    {
        var parts = new List<string> { "device", productId.ToString(CultureInfo.InvariantCulture) };
        if (!string.IsNullOrWhiteSpace(instanceId)) parts.Add(instanceId);
        if (!string.IsNullOrWhiteSpace(deviceId)) parts.Add(deviceId);
        if (parts.Count == 2) parts.Add(index.ToString(CultureInfo.InvariantCulture));
        return string.Join("-", parts);
    }

    private static object? FlexibleNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var trimmed = text.Trim();
        return long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) &&
               number.ToString(CultureInfo.InvariantCulture) == trimmed
            ? number
            : text;
    }

    /// <summary>Chroma requires an integer instance id; non-numeric identities live in openSynapse.</summary>
    private static object? ChromaInstanceId(string? text) =>
        FlexibleNumber(text) is long number ? number : string.IsNullOrWhiteSpace(text) ? null : 0L;

    private static int NormalizeRotation(int rotation) => rotation is 90 or 180 or 270 ? rotation : 0;
    private static LayerDto ToLayerDto(StudioLayerItem item, Guid? parentGroupId, bool export)
    {
        var dto = new LayerDto
        {
            Id = item.Id.ToString(),
            Type = item.IsGroup ? "group" : "layer",
            Title = item.Title,
            Visible = item.Visible,
        };
        if (item.IsGroup)
        {
            dto.Opened = item.Opened;
            dto.Layers = item.Children.Select(child => ToLayerDto(child, item.Id, export)).ToList();
        }
        else
        {
            // Engine-only radiate has no Studio name; export the official spectrum
            // shape and restore radiate from the openSynapse extension.
            var exportedEffect = item.Params.Effect == StudioEffectKind.Radiate
                ? StudioEffectKind.Spectrum
                : item.Params.Effect;
            dto.Name = StudioEffectMap.ToName(exportedEffect);
            dto.Value = StudioEffectMap.ToUiValue(exportedEffect);
            dto.Params = ToParamsDto(item.Params, export: export);
            dto.Params2 = ToParams2Dto(item.Params);
            dto.Regions = item.Regions.Select(region => ToRegionDto(region, export)).ToList();
        }
        if (parentGroupId is Guid groupId) dto.GroupId = groupId.ToString();
        var extras = new LayerExtrasDto();
        var hasExtras = false;
        if (item.Opacity != 100) { extras.Opacity = item.Opacity; hasExtras = true; }
        if (item.Blend != StudioBlendMode.Normal) { extras.Blend = item.Blend.ToString(); hasExtras = true; }
        if (item.Params.Effect is StudioEffectKind.Gradient or StudioEffectKind.Radiate)
        {
            extras.Effect = item.Params.Effect == StudioEffectKind.Gradient ? "gradient" : "radiate";
            hasExtras = true;
        }
        if (hasExtras) dto.OpenSynapse = extras;
        return dto;
    }
    private static RegionDto ToRegionDto(StudioRegion region, bool export) => new()
    {
        Params = ToParamsDto(region.Params, true, export),
        Params2 = ToParams2Dto(region.Params),
        Devices = region.Devices.Select(ToRegionDeviceDto).ToList(),
    };

    private static RegionDeviceDto ToRegionDeviceDto(StudioRegionDevice device, int index)
    {
        var instance = FlexibleNumber(device.InstanceId);
        var dto = new RegionDeviceDto
        {
            Id = string.IsNullOrWhiteSpace(device.Id)
                ? GeneratedDeviceId(device.ProductId, device.InstanceId, device.DeviceId, index)
                : device.Id,
            ProductId = device.ProductId,
            InstanceId = instance is long ? instance : string.IsNullOrWhiteSpace(device.InstanceId) ? null : 0L,
            DeviceId = string.IsNullOrWhiteSpace(device.DeviceId) ? null : device.DeviceId,
            Leds = device.Leds.Select(led => new LedDto
            {
                Row = led.Row,
                Col = led.Col,
                RegionId = led.RegionId,
                RegionIndex = led.RegionIndex,
                InputType = led.InputType,
                InputData = led.InputData,
                OpenSynapse = string.IsNullOrWhiteSpace(led.InstanceId)
                    ? null
                    : new LedExtrasDto { InstanceId = led.InstanceId },
            }).ToList(),
        };
        if (instance is not long && !string.IsNullOrWhiteSpace(device.InstanceId))
            dto.OpenSynapse = new RegionDeviceExtrasDto { InstanceId = device.InstanceId };
        return dto;
    }
    private static ParamsDto ToParamsDto(StudioEffectParams p, bool includeEffect = false, bool export = false)
    {
        var dto = new ParamsDto { Color = Pack(p.Primary), Color2 = Pack(p.Secondary) };
        if (includeEffect && p.Effect != StudioEffectKind.Solid) dto.Effect = p.Effect == StudioEffectKind.Gradient ? "gradient" : StudioEffectMap.ToName(p.Effect);
        if (p.ColorStops.Count > 0)
            dto.ColorStops = p.ColorStops.Select(stop => new ColorStopDto { Stop = stop.Stop, Color = Pack(stop.Color) }).ToList();
        if (p.ColorStopsCustom) dto.ColorStopsCustom = true;
        if (p.RandomColor) dto.RandomColor = true;
        if (p.DurationMs != 0) dto.Duration = p.DurationMs;
        if (p.PauseMs != 0) dto.Pause = p.PauseMs;
        if (p.Cycles != -1) dto.Cycles = p.Cycles;
        if (p.Angle != 0) dto.Angle = p.Angle;
        if (p.PlaybackStart != StudioPlaybackStart.Random) dto.PlaybackStart = p.PlaybackStart;
        if (p.PlaybackEnd != StudioPlaybackEnd.Never) dto.PlaybackEnd = p.PlaybackEnd;
        if (p.Width != 50) dto.Width = p.Width;
        if (p.Speed != 1) dto.Speed = p.Speed;
        if (p.Effect is StudioEffectKind.Wave or StudioEffectKind.Tidal) dto.Split = p.Split >= 50;
        if (p.Density != 50) dto.Density = p.Density;
        if (p.Boost != 1) dto.Boost = p.Boost;
        if (p.Decay != 50) dto.Decay = p.Decay;
        if (p.AutoBoost) dto.AutoBoost = true;
        if (!string.IsNullOrWhiteSpace(p.Screen)) dto.Screen = p.Screen;
        if (p.ScreenRect is { } rect)
            dto.ScreenRect = new ScreenRectDto { Left = FromUnit(rect.X), Top = FromUnit(rect.Y), Right = FromUnit(rect.Width), Bottom = FromUnit(rect.Height) };
        if (p.Blur != 0) dto.Blur = p.Blur;
        if (!string.IsNullOrWhiteSpace(p.MediaSrc)) dto.MediaSrc = p.MediaSrc;
        if (!string.IsNullOrWhiteSpace(p.CurrentConfigJson)) dto.CurrentConfig = RawJson(p.CurrentConfigJson);
        if (export && p.Effect == StudioEffectKind.ChromaGenerate &&
            !string.IsNullOrWhiteSpace(p.CurrentConfigJson))
        {
            dto.EffectConfigList = RawJson($"[{p.CurrentConfigJson}]");
            dto.CurrentConfigId = 0;
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(p.EffectConfigListJson)) dto.EffectConfigList = RawJson(p.EffectConfigListJson);
            if (p.CurrentConfigId != -1) dto.CurrentConfigId = p.CurrentConfigId;
        }
        return dto;
    }

    /// <summary>
    /// Chroma keeps the tidal/wheel center in per-layer editor state that its own
    /// export drops; OpenSynapse writes it so the setting survives a save.
    /// </summary>
    private static Params2Dto? ToParams2Dto(StudioEffectParams p)
    {
        var hasWheelCenter = p.Effect == StudioEffectKind.Wheel && p.CenterRow >= 0 && p.CenterCol >= 0;
        if (p.Effect != StudioEffectKind.Tidal && !hasWheelCenter &&
            p.CenterPointX == 5_000 && p.CenterPointY == 5_000 &&
            !p.Counterclockwise && string.IsNullOrWhiteSpace(p.DeviceWithCenterPointJson))
            return null;

        var centerX = hasWheelCenter ? p.CenterCol * 40d : p.CenterPointX;
        var centerY = hasWheelCenter ? p.CenterRow * 40d : p.CenterPointY;
        return new Params2Dto
        {
            CenterPointX = centerX / 40,
            CenterPointX2 = centerX,
            CenterPointY = centerY / 40,
            CenterPointY2 = centerY,
            CenterRow = hasWheelCenter ? p.CenterRow : null,
            CenterCol = hasWheelCenter ? p.CenterCol : null,
            Counterclockwise = p.Counterclockwise,
            DeviceWithCenterPoint = string.IsNullOrWhiteSpace(p.DeviceWithCenterPointJson)
                ? null
                : RawJson(p.DeviceWithCenterPointJson),
        };
    }

    private sealed class RootDto { public int Version { get; set; } public List<ProfileDto> Profiles { get; set; } = new(); public RootExtrasDto? OpenSynapse { get; set; } }
    private sealed class RootExtrasDto { public int SchemaVersion { get; set; } public string Name { get; set; } = string.Empty; public string? SelectedProfileId { get; set; } }
    private sealed class ProfileDto
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public bool? Default { get; set; }
        public List<ProfileDeviceDto> Devices { get; set; } = new();
        public List<LayerDto> Layers { get; set; } = new();
        public ProfileExtrasDto? OpenSynapse { get; set; }
    }
    private sealed class ProfileDeviceDto
    {
        public string Id { get; set; } = string.Empty;
        public ushort ProductId { get; set; }
        public object? InstanceId { get; set; }
        public string? DeviceId { get; set; }
        public string? Category { get; set; }
        public string? ProductName { get; set; }
        public string? ProductNameZhCn { get; set; }
        public JsonElement? Config { get; set; }
        public JsonElement? Leds { get; set; }
        public object? Layout { get; set; }
        public double? Width { get; set; }
        public double? Height { get; set; }
        public double X { get; set; } = 5000;
        public double Y { get; set; } = 5000;
        public int Orientation { get; set; }
        public object? ParentId { get; set; }
        public double? ParentX { get; set; }
        public double? ParentY { get; set; }
        public int? PlacementIndex { get; set; }
        public JsonElement? LedRef { get; set; }
    }
    private sealed class ProfileExtrasDto { public List<DeviceDto> Devices { get; set; } = new(); }
    private sealed class DeviceDto
    {
        public string? Id { get; set; }
        public string InstanceId { get; set; } = string.Empty;
        public ushort ProductId { get; set; }
        public string? DeviceId { get; set; }
        public string? Category { get; set; }
        public string? ProductName { get; set; }
        public string? ProductNameZhCn { get; set; }
        public string? Layout { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double X { get; set; } = 5000;
        public double Y { get; set; } = 5000;
        public int Rotation { get; set; }
    }
    private sealed class LayerDto
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = "layer";
        public string Title { get; set; } = string.Empty;
        public string? Name { get; set; }
        public int? Value { get; set; }
        public bool Visible { get; set; } = true;
        public bool? Opened { get; set; }
        public ParamsDto? Params { get; set; }
        public Params2Dto? Params2 { get; set; }
        public List<RegionDto>? Regions { get; set; }
        public string? GroupId { get; set; }
        public List<LayerDto>? Layers { get; set; }
        public LayerExtrasDto? OpenSynapse { get; set; }
    }
    private sealed class LayerExtrasDto { public double? Opacity { get; set; } public string? Blend { get; set; } public string? Effect { get; set; } }
    private sealed class ParamsDto
    {
        public int Color { get; set; }
        public int Color2 { get; set; }
        public string? Effect { get; set; }
        public List<ColorStopDto>? ColorStops { get; set; }
        public bool? ColorStopsCustom { get; set; }
        public bool? RandomColor { get; set; }
        public int? Duration { get; set; }
        public int? Pause { get; set; }
        public int? Cycles { get; set; }
        public int? Angle { get; set; }
        public StudioPlaybackStart? PlaybackStart { get; set; }
        public StudioPlaybackEnd? PlaybackEnd { get; set; }
        public double? Width { get; set; }
        public double? Speed { get; set; }
        public bool? Split { get; set; }
        public double? Density { get; set; }
        public double? Boost { get; set; }
        public double? Decay { get; set; }
        public bool? AutoBoost { get; set; }
        public string? Screen { get; set; }
        public ScreenRectDto? ScreenRect { get; set; }
        public int? Blur { get; set; }
        public string? MediaSrc { get; set; }
        public JsonElement? CurrentConfig { get; set; }
        public JsonElement? EffectConfigList { get; set; }
        public int? CurrentConfigId { get; set; }
    }
    private sealed class Params2Dto
    {
        public double CenterPointX { get; set; }
        public double CenterPointX2 { get; set; }
        public double CenterPointY { get; set; }
        public double CenterPointY2 { get; set; }
        public int? CenterRow { get; set; }
        public int? CenterCol { get; set; }
        public bool Counterclockwise { get; set; }
        public JsonElement? DeviceWithCenterPoint { get; set; }
    }
    private sealed class ColorStopDto
    {
        [JsonPropertyName("Stop")] public int Stop { get; set; }
        [JsonPropertyName("Color")] public int Color { get; set; }
    }
    private sealed class ScreenRectDto
    {
        [JsonPropertyName("Left")] public int Left { get; set; }
        [JsonPropertyName("Top")] public int Top { get; set; }
        [JsonPropertyName("Right")] public int Right { get; set; }
        [JsonPropertyName("Bottom")] public int Bottom { get; set; }
    }
    private sealed class RegionDto
    {
        public ParamsDto Params { get; set; } = new();
        public Params2Dto? Params2 { get; set; }
        public List<RegionDeviceDto> Devices { get; set; } = new();
    }
    private sealed class RegionDeviceDto
    {
        public string Id { get; set; } = string.Empty;
        public ushort ProductId { get; set; }
        public object? InstanceId { get; set; }
        public string? DeviceId { get; set; }
        public List<LedDto> Leds { get; set; } = new();
        public RegionDeviceExtrasDto? OpenSynapse { get; set; }
    }
    private sealed class RegionDeviceExtrasDto { public string? InstanceId { get; set; } }
    private sealed class LedExtrasDto { public string? InstanceId { get; set; } }
    private sealed class LedDto
    {
        public int Row { get; set; }
        public int Col { get; set; }
        public int? RegionId { get; set; }
        public int? RegionIndex { get; set; }
        public string? InputType { get; set; }
        public string? InputData { get; set; }
        public LedExtrasDto? OpenSynapse { get; set; }
    }
}
