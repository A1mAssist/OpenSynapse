using System.Collections.Frozen;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public enum OpenRazerEndpointState
{
    Resolved,
    RecognizedButUnresolved,
    BusyOrUnavailable,
}

/// <summary>Onboard button-assignment function ids used by the middleware.</summary>
public static class OpenRazerMappingFunctions
{
    public const byte Off = 0x00;
    public const byte ButtonCode = 0x01;
    public const byte KeyCode = 0x02;
    public const byte MacroTypeI = 0x03;
    public const byte MacroTypeII = 0x04;
    public const byte MacroTypeIII = 0x05;
    public const byte Dpi = 0x06;
    public const byte Profile = 0x07;
    public const byte Lighting = 0x08;
    public const byte PowerKeys = 0x09;
    public const byte MediaKeys = 0x0A;
    public const byte DoubleClick = 0x0B;
    public const byte ModeButtonkey = 0x0C;
    public const byte TurboModeKey = 0x0D;
    public const byte TurboModeButton = 0x0E;
    public const byte MacroTypeIV = 0x0F;
    public const byte Controller = 0x10;
    public const byte RazerKey = 0x11;
    public const byte ScrollWheelMode = 0x12;
    public const byte Win8ShortcutsKey = 0x15;
}

public enum OpenRazerBackendCapability
{
    FirmwareRead,
    SerialRead,
    DeviceModeRead,
    DeviceModeWrite,
    BatteryRead,
    ChargingRead,
    PollingRateRead,
    PollingRateWrite,
    DpiRead,
    DpiWrite,
    DpiStagesRead,
    DpiStagesWrite,
    IdleTimeoutRead,
    IdleTimeoutWrite,
    LowBatteryThresholdRead,
    LowBatteryThresholdWrite,
    AutoSleepRead,
    AutoSleepWrite,
    BatteryChargeLimiterRead,
    BatteryChargeLimiterWrite,
    TimeToDimRead,
    TimeToDimWrite,
    IndicatorLedConfigurationRead,
    IndicatorLedConfigurationWrite,
    GpuCardDetectionRead,
    GpuCardDetectionWrite,
    ForceChromaCtrlRead,
    ForceChromaCtrlWrite,
    ProfileDebounceTimeRead,
    ProfileDebounceTimeWrite,
    ChromaTapRead,
    ChromaTapWrite,
    BatteryDischargeCurveRead,
    BatteryDischargeCurveWrite,
    MacroStorageInfoRead,
    LkScrollWheelModeRead,
    LkScrollWheelModeWrite,
    BrightnessRead,
    BrightnessWrite,
    LedStateRead,
    LedStateWrite,
    LedEffectRead,
    LedEffectWrite,
    LedColorWrite,
    LedBlinkingWrite,
    LightingEffectWrite,
    MatrixFrameWrite,
    ReactiveTriggerWrite,
    ScrollModeRead,
    ScrollModeWrite,
    MappingRead,
    MappingWrite,
    ScrollAccelerationRead,
    ScrollAccelerationWrite,
    SmartReelRead,
    SmartReelWrite,
    FnPrimaryWrite,
    KeyswitchOptimizationRead,
    KeyswitchOptimizationWrite,
    HyperPollingIndicatorWrite,
    HyperPollingPairWrite,
    HyperPollingUnpairWrite,
}

public sealed record OpenRazerLightingZoneCapabilities(
    OpenRazerLedZone Zone,
    bool CanReadBrightness,
    bool CanWriteBrightness,
    bool CanReadState,
    bool CanWriteState,
    bool CanReadEffect,
    bool CanWriteEffect,
    bool CanReadColor,
    bool CanWriteColor,
    bool CanWriteBlinking,
    IReadOnlySet<OpenRazerLightingEffect> LightingEffects);

public sealed record OpenRazerCapabilitySnapshot(
    IReadOnlySet<OpenRazerBackendCapability> Protocols,
    OpenRazerLayoutSupport LayoutSupport,
    OpenRazerMatrixDimensions? MatrixDimensions,
    bool SupportsFirmwareLighting,
    bool SupportsZoneLighting,
    bool SupportsMatrixFrames,
    bool SupportsNamedKeyEffects)
{
    public bool Has(OpenRazerBackendCapability capability) => Protocols.Contains(capability);

    internal static OpenRazerCapabilitySnapshot Create(
        OpenRazerDeviceDefinition definition,
        IReadOnlySet<OpenRazerBackendCapability> protocols,
        IReadOnlyDictionary<OpenRazerLedZone, OpenRazerLightingZoneCapabilities> lightingZones)
    {
        var supportsFirmwareLighting = lightingZones.Values.Any(zone =>
            zone.LightingEffects.Count > 0 || zone.CanWriteBrightness || zone.CanWriteState);
        var supportsZoneLighting = lightingZones.Count > 1 ||
            lightingZones.Values.Any(zone => zone.Zone != definition.DefaultLedZone);
        var supportsMatrixFrames = protocols.Contains(OpenRazerBackendCapability.MatrixFrameWrite) &&
            definition.MatrixDimensions is not null;
        return new(
            protocols.ToFrozenSet(),
            definition.LayoutSupport,
            definition.MatrixDimensions,
            supportsFirmwareLighting,
            supportsZoneLighting,
            supportsMatrixFrames,
            definition.LayoutSupport == OpenRazerLayoutSupport.Verified &&
                supportsMatrixFrames &&
                protocols.Contains(OpenRazerBackendCapability.ReactiveTriggerWrite));
    }
}

public sealed class OpenRazerDeviceConnection
{
    internal OpenRazerDeviceConnection(
        OpenRazerDeviceDefinition definition,
        string? devicePath,
        string? physicalDeviceKey,
        OpenRazerEndpointState endpointState,
        IReadOnlySet<OpenRazerBackendCapability> capabilities,
        IReadOnlyDictionary<OpenRazerLedZone, OpenRazerLightingZoneCapabilities> lightingZones,
        string? error)
    {
        Definition = definition;
        DevicePath = devicePath;
        InstanceId = physicalDeviceKey ?? $"1532:{definition.ProductId:X4}";
        EndpointState = endpointState;
        Capabilities = capabilities;
        LightingZones = lightingZones;
        SupportedLedZones = lightingZones.Keys.ToFrozenSet();
        SupportedLightingEffects = lightingZones.Values
            .SelectMany(zone => zone.LightingEffects)
            .ToFrozenSet();
        CapabilitySnapshot = OpenRazerCapabilitySnapshot.Create(definition, capabilities, lightingZones);
        Error = error;
    }

    public OpenRazerDeviceDefinition Definition { get; }
    public OpenRazerEndpointState EndpointState { get; }
    public IReadOnlySet<OpenRazerBackendCapability> Capabilities { get; }
    public IReadOnlyDictionary<OpenRazerLedZone, OpenRazerLightingZoneCapabilities> LightingZones { get; }
    public IReadOnlySet<OpenRazerLedZone> SupportedLedZones { get; }
    public IReadOnlySet<OpenRazerLightingEffect> SupportedLightingEffects { get; }
    public OpenRazerCapabilitySnapshot CapabilitySnapshot { get; }
    public string? Error { get; }
    public bool IsReady => EndpointState == OpenRazerEndpointState.Resolved;
    public string InstanceId { get; }
    internal string? DevicePath { get; }

    public DeviceDescriptor ToDescriptor() => new(
        DevicePath ?? $"openrazer://1532/{Definition.ProductId:X4}/{Uri.EscapeDataString(InstanceId)}",
        Definition.DisplayName,
        OpenRazerDeviceDefinition.VendorId,
        Definition.ProductId,
        EndpointState == OpenRazerEndpointState.Resolved
            ? DeviceAccessState.Available
            : DeviceAccessState.BusyOrUnavailable,
        EndpointState == OpenRazerEndpointState.Resolved
            ? DeviceCapabilityState.PendingValidation
            : DeviceCapabilityState.Blocked,
        91,
        0,
        0,
        "openrazer-standard",
        Definition.Category);
}

public sealed record OpenRazerBasicState(
    Version? Firmware,
    string? Serial,
    bool? SoftwareMode,
    int? BatteryPercent,
    bool? IsCharging,
    int? PollingRate,
    int? DpiX,
    int? DpiY,
    int? IdleTimeoutSeconds,
    int? LowBatteryThresholdPercent,
    bool? AutoSleepEnabled,
    bool? BatteryChargeLimiterEnabled,
    int? BatteryChargeLimiterPercent,
    bool? TimeToDimEnabled,
    int? TimeToDimSeconds,
    byte? TimeToDimPercent,
    byte? IndicatorLedConfiguration,
    bool? GpuCardDetectionEnabled,
    bool? ForceChromaCtrlEnabled,
    OpenRazerProfileDebounce? ProfileDebounce,
    OpenRazerChromaTap? ChromaTap,
    byte? BatteryDischargeCurve,
    OpenRazerMacroStorageInfo? MacroStorageInfo,
    byte? LkScrollWheelModeMask,
    byte? Brightness,
    IReadOnlyDictionary<string, string> Errors,
    IReadOnlySet<OpenRazerBackendCapability> UnsupportedCapabilities);
