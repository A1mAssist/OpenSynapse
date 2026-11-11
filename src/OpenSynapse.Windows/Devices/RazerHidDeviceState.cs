using System.Collections.Frozen;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public enum RazerHidEndpointState
{
    Resolved,
    RecognizedButUnresolved,
    BusyOrUnavailable,
}

/// <summary>Onboard button-assignment function ids used by the middleware.</summary>
public static class RazerHidMappingFunctions
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

public enum RazerHidBackendCapability
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
    IndicatorLedConfigurationV2Read,
    IndicatorLedConfigurationV2Write,
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
    MacroStorageDataWrite,
    MacroStorageNameWrite,
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
    SmartReelThresholdRead,
    SmartReelThresholdWrite,
    FnPrimaryWrite,
    KeyswitchOptimizationRead,
    KeyswitchOptimizationWrite,
    HyperPollingIndicatorWrite,
    HyperPollingPairWrite,
    HyperPollingUnpairWrite,
    ScrollFreeNotchedModeRead,
    ScrollFreeNotchedModeWrite,
    ScrollAccelerationLevelRead,
    ScrollAccelerationLevelWrite,
    OnboardProfileRead,
    OnboardProfileWrite,
    WirelessPairingWrite,
    WirelessConnectionStateRead,
    ProximitySensorRead,
    ProximitySensorWrite,
    SensorRotationRead,
    SensorRotationWrite,
    KnobRead,
    KnobWrite,
    GameModeRead,
    GameModeWrite,
    AnalogKeyRead,
    AnalogKeyWrite,
    OledRead,
    OledWrite,
    HapticScrollRead,
    HapticScrollWrite,
}

public sealed record RazerHidLightingZoneCapabilities(
    RazerHidLedZone Zone,
    bool CanReadBrightness,
    bool CanWriteBrightness,
    bool CanReadState,
    bool CanWriteState,
    bool CanReadEffect,
    bool CanWriteEffect,
    bool CanReadColor,
    bool CanWriteColor,
    bool CanWriteBlinking,
    IReadOnlySet<RazerHidLightingEffect> LightingEffects);

public sealed record RazerHidCapabilitySnapshot(
    IReadOnlySet<RazerHidBackendCapability> Protocols,
    RazerHidLayoutSupport LayoutSupport,
    RazerHidMatrixDimensions? MatrixDimensions,
    bool SupportsFirmwareLighting,
    bool SupportsZoneLighting,
    bool SupportsMatrixFrames,
    bool SupportsNamedKeyEffects)
{
    public bool Has(RazerHidBackendCapability capability) => Protocols.Contains(capability);

    internal static RazerHidCapabilitySnapshot Create(
        RazerHidDeviceDefinition definition,
        IReadOnlySet<RazerHidBackendCapability> protocols,
        IReadOnlyDictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities> lightingZones)
    {
        var supportsFirmwareLighting = lightingZones.Values.Any(zone =>
            zone.LightingEffects.Count > 0 || zone.CanWriteBrightness || zone.CanWriteState);
        var supportsZoneLighting = lightingZones.Count > 1 ||
            lightingZones.Values.Any(zone => zone.Zone != definition.DefaultLedZone);
        var supportsMatrixFrames = protocols.Contains(RazerHidBackendCapability.MatrixFrameWrite) &&
            definition.MatrixDimensions is not null;
        return new(
            protocols.ToFrozenSet(),
            definition.LayoutSupport,
            definition.MatrixDimensions,
            supportsFirmwareLighting,
            supportsZoneLighting,
            supportsMatrixFrames,
            definition.LayoutSupport == RazerHidLayoutSupport.Verified &&
                supportsMatrixFrames &&
                protocols.Contains(RazerHidBackendCapability.ReactiveTriggerWrite));
    }
}

public sealed class RazerHidDeviceConnection
{
    internal RazerHidDeviceConnection(
        RazerHidDeviceDefinition definition,
        string? devicePath,
        string? physicalDeviceKey,
        RazerHidEndpointState endpointState,
        IReadOnlySet<RazerHidBackendCapability> capabilities,
        IReadOnlyDictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities> lightingZones,
        string? error)
    {
        Definition = definition;
        DevicePath = devicePath;
        InstanceId = ResolveInstanceId(definition, devicePath, physicalDeviceKey);
        EndpointState = endpointState;
        Capabilities = capabilities;
        LightingZones = lightingZones;
        SupportedLedZones = lightingZones.Keys.ToFrozenSet();
        SupportedLightingEffects = lightingZones.Values
            .SelectMany(zone => zone.LightingEffects)
            .ToFrozenSet();
        CapabilitySnapshot = RazerHidCapabilitySnapshot.Create(definition, capabilities, lightingZones);
        Error = error;
    }

    private static string ResolveInstanceId(
        RazerHidDeviceDefinition definition,
        string? devicePath,
        string? physicalDeviceKey)
    {
        if (!string.IsNullOrWhiteSpace(physicalDeviceKey))
        {
            return physicalDeviceKey;
        }

        // The container key is preferred; without one the endpoint path is the only
        // stable discriminator. A bare PID merges two same-model devices into one output.
        return string.IsNullOrWhiteSpace(devicePath)
            ? $"1532:{definition.ProductId:X4}"
            : WindowsHidDiscovery.GetPhysicalDeviceKey(devicePath);
    }

    public RazerHidDeviceDefinition Definition { get; }
    public RazerHidEndpointState EndpointState { get; }
    public IReadOnlySet<RazerHidBackendCapability> Capabilities { get; }
    public IReadOnlyDictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities> LightingZones { get; }
    public IReadOnlySet<RazerHidLedZone> SupportedLedZones { get; }
    public IReadOnlySet<RazerHidLightingEffect> SupportedLightingEffects { get; }
    public RazerHidCapabilitySnapshot CapabilitySnapshot { get; }
    public string? Error { get; }
    public bool IsReady => EndpointState == RazerHidEndpointState.Resolved;
    public string InstanceId { get; }
    internal string? DevicePath { get; }

    public DeviceDescriptor ToDescriptor() => new(
        DevicePath ?? $"openrazer://1532/{Definition.ProductId:X4}/{Uri.EscapeDataString(InstanceId)}",
        Definition.DisplayName,
        RazerHidDeviceDefinition.VendorId,
        Definition.ProductId,
        EndpointState == RazerHidEndpointState.Resolved
            ? DeviceAccessState.Available
            : DeviceAccessState.BusyOrUnavailable,
        EndpointState == RazerHidEndpointState.Resolved
            ? DeviceCapabilityState.PendingValidation
            : DeviceCapabilityState.Blocked,
        91,
        0,
        0,
        "openrazer-standard",
        Definition.Category);
}

public sealed record RazerHidBasicState(
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
    RazerHidProfileDebounce? ProfileDebounce,
    RazerHidChromaTap? ChromaTap,
    byte? BatteryDischargeCurve,
    RazerHidMacroStorageInfo? MacroStorageInfo,
    byte? LkScrollWheelModeMask,
    byte? Brightness,
    IReadOnlyDictionary<string, string> Errors,
    IReadOnlySet<RazerHidBackendCapability> UnsupportedCapabilities,
    RazerHidIndicatorLedConfigurationV2? IndicatorLedConfigurationV2 = null,
    RazerHidSensorRotation? SensorRotation = null);
