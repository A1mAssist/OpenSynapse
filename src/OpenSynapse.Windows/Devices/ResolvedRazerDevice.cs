using OpenSynapse.Core.Devices;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// A device descriptor paired with the manifest selected by its VID/PID. Runtime
/// readers ask this type for capabilities instead of recombining the two by hand.
/// </summary>
internal sealed record ResolvedRazerDevice(
    DeviceDescriptor Descriptor,
    RazerDeviceManifest Manifest)
{
    public bool IsBlade =>
        Manifest.ProtocolFamily.StartsWith("blade-", StringComparison.Ordinal);

    public ushort? BladeProductId => IsBlade ? Descriptor.ProductId : null;

    public bool Owns(string capabilityId) => Manifest.Capabilities.ContainsKey(capabilityId);

    public RazerRequestDescriptor GetCapability(string capabilityId) =>
        Manifest.GetRequiredCapability(capabilityId);

    public IReadOnlyList<BladePerformanceMode> BladePerformanceModes =>
        BladeProductId is { } productId
            ? BladeModelCapabilities.PerformanceModesFor(productId)
            : [];

    public BladeFanTargetRange GetBladeFanTargetRange(BladePerformanceMode mode) =>
        BladeProductId is { } productId
            ? BladeModelCapabilities.FanTargetRangeFor(productId, mode)
            : default;

    public bool SupportsFanCurve => BladeProductId is { } pid && BladeModelCapabilities.SupportsFanCurve(pid);
    public bool SupportsFixedFan => BladeProductId is { } pid && BladeModelCapabilities.SupportsFixedFan(pid);
    public bool FixedFanRequiresCustomMode =>
        BladeProductId is { } pid && BladeModelCapabilities.FixedFanRequiresCustomMode(pid);
    public bool SupportsHyperBoost => BladeProductId is { } pid && BladeModelCapabilities.SupportsHyperBoost(pid);
    public bool SupportsMaxFan => BladeProductId is { } pid && BladeModelCapabilities.SupportsMaxFan(pid);
    public bool SupportsCpuBoost => BladeProductId is { } pid && BladeModelCapabilities.SupportsCpuBoost(pid);
    public bool SupportsCpuPowerBoost =>
        BladeProductId is { } pid && BladeModelCapabilities.SupportsCpuPowerBoost(pid);
    public bool AllowsControlWrites => BladeProductId is { } pid && BladeModelCapabilities.AllowsControlWrites(pid);
    public bool UsesBlade2026ThermalSelector =>
        BladeProductId is { } pid && BladeModelCapabilities.UsesBlade2026ThermalSelector(pid);
    public bool IsBlade2026 => BladeProductId is { } pid && BladeModelCapabilities.IsBlade2026(pid);
    public int MinimumFanTargetRpm => BladeProductId is { } pid
        ? BladeModelCapabilities.MinimumFanTargetRpmFor(pid)
        : BladeFanLimits.MinimumRpm;
    public int MaximumFanTargetRpm => BladeProductId is { } pid
        ? BladeModelCapabilities.MaximumFanTargetRpmFor(pid)
        : BladeFanLimits.MaximumRpm;
    public int MaximumWritableFanTargetRpm => BladeProductId is { } pid
        ? BladeModelCapabilities.MaximumWritableFanTargetRpmFor(pid)
        : BladeFanLimits.MaximumRpm;
    public IReadOnlyList<BladeCpuBoostMode> CpuBoostModes => BladeProductId is { } pid
        ? BladeModelCapabilities.CpuBoostModesFor(pid)
        : [];
    public BladeCpuBoostMode? MaximumCpuBoostMode => BladeProductId is { } pid
        ? BladeModelCapabilities.MaximumCpuBoostModeFor(pid)
        : null;

    public bool AllowsCpuBoost(BladeCpuBoostMode mode) =>
        BladeProductId is { } pid && BladeModelCapabilities.AllowsCpuBoost(pid, mode);

    public bool PublishesBladePerformanceMode(BladePerformanceMode mode) =>
        BladeProductId is { } pid && BladeModelCapabilities.PublishesPerformanceMode(pid, mode);
}
