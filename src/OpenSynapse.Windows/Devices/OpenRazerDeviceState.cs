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
    byte? Brightness,
    IReadOnlyDictionary<string, string> Errors,
    IReadOnlySet<OpenRazerBackendCapability> UnsupportedCapabilities);
