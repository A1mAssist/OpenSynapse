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
        Error = error;
    }

    public OpenRazerDeviceDefinition Definition { get; }
    public OpenRazerEndpointState EndpointState { get; }
    public IReadOnlySet<OpenRazerBackendCapability> Capabilities { get; }
    public IReadOnlyDictionary<OpenRazerLedZone, OpenRazerLightingZoneCapabilities> LightingZones { get; }
    public IReadOnlySet<OpenRazerLedZone> SupportedLedZones { get; }
    public IReadOnlySet<OpenRazerLightingEffect> SupportedLightingEffects { get; }
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
    IReadOnlyDictionary<string, string> Errors);
