namespace OpenSynapse.Core.Devices;

/// <summary>Fixed fan-target range the official middleware offers for one performance mode.</summary>
public readonly record struct BladeFanTargetRange(int MinimumRpm, int MaximumRpm, int DefaultRpm);

/// <summary>
/// Per-model Blade capability admission. A capability is admitted only when the exact
/// USB product id has source-backed evidence for that control. A successful protocol
/// readback is not evidence of support: Synapse shares one Power Mode Control bitmask
/// across models, so bits a model never implemented still read back cleanly.
/// </summary>
public static partial class BladeModelCapabilities
{
    /// <summary>Product ids with an audited model contract, for structure checks and diagnostics.</summary>
    internal static IReadOnlyCollection<ushort> AdmittedProductIds => Contracts.Keys;

    /// <summary>
    /// Temperature-driven fan curve admission. A curve writes the same 0D:01 fan-target
    /// report as a fixed speed, so an admitted fixed target also admits the curve; the
    /// explicit flag is kept for a model that publishes only the curve path.
    /// </summary>
    public static bool SupportsFanCurve(ushort productId) =>
        Contracts.TryGetValue(productId, out var contract) && (contract.FanCurve || contract.FixedFan);

    /// <summary>True only where a fixed fan-target write has been admitted.</summary>
    public static bool SupportsFixedFan(ushort productId) =>
        Contracts.TryGetValue(productId, out var contract) && contract.FixedFan;

    /// <summary>
    /// Performance modes exposed for the exact model. 028C uses the three modes its
    /// Synapse UI exposes; the protocol-only Performance value must not leak into it.
    /// </summary>
    public static IReadOnlyList<BladePerformanceMode> PerformanceModesFor(ushort productId) =>
        Contracts.TryGetValue(productId, out var contract) && contract.Modes is { } modes
            ? modes
            : productId == Blade14_2022ProductId
                ? Blade14_2022PerformanceModes
                : SupportsHyperBoost(productId)
                    ? HyperBoostPerformanceModes
                    : StandardPerformanceModes;

    /// <summary>Lower bound the exact model accepts for a fixed fan target.</summary>
    public static int MinimumFanTargetRpmFor(ushort productId) =>
        Contracts.TryGetValue(productId, out var contract)
            ? FanTargetBounds(contract).Minimum
            : BladeFanLimits.MinimumRpm;

    /// <summary>
    /// Effective model-wide bounds. A per-mode range can sit outside the base bounds
    /// (02B8 Gaming starts at 3300, 0239 battery saver reaches 5500), and the readback
    /// and write guards must cover every range the bundle publishes.
    /// </summary>
    private static (int Minimum, int Maximum) FanTargetBounds(Contract contract) =>
        contract.FixedFan && contract.ModeFanRanges is { } ranges
            ? (Math.Min(contract.MinimumFanTargetRpm, ranges.Values.Min(range => range.MinimumRpm)),
               Math.Max(contract.MaximumFanTargetRpm, ranges.Values.Max(range => range.MaximumRpm)))
            : (contract.MinimumFanTargetRpm, contract.MaximumFanTargetRpm);

    /// <summary>
    /// True when the exact model publishes the given performance mode. Firmware can
    /// report a mode the model does not list, and that is not a mode change: the
    /// machine may still be in Custom, and treating it as one would drop the
    /// CPU/GPU level and Max Fan readings the firmware reported.
    /// </summary>
    public static bool PublishesPerformanceMode(ushort productId, BladePerformanceMode mode) =>
        PerformanceModesFor(productId).Contains(mode);

    /// <summary>
    /// Fixed fan-target range the official middleware offers for one performance mode.
    /// Synapse publishes a separate min/max/default per mode, so a single per-model range
    /// would either refuse a speed the UI offers or allow one it does not.
    /// </summary>
    public static BladeFanTargetRange FanTargetRangeFor(ushort productId, BladePerformanceMode mode)
    {
        if (!Contracts.TryGetValue(productId, out var contract))
        {
            return new BladeFanTargetRange(
                BladeFanLimits.MinimumRpm, BladeFanLimits.MaximumRpm, BladeFanLimits.MinimumRpm);
        }

        BladeFanTargetRange range;
        if (contract.ModeFanRanges is { } ranges && ranges.TryGetValue(mode, out var modeRange))
        {
            range = modeRange;
        }
        else
        {
            range = new BladeFanTargetRange(
                contract.MinimumFanTargetRpm, contract.MaximumFanTargetRpm, contract.MinimumFanTargetRpm);
        }

        return contract.MinimumManualFanTargetRpm is int manualMinimum
            ? range with { MinimumRpm = Math.Min(range.MinimumRpm, manualMinimum) }
            : range;
    }

    /// <summary>
    /// Target range for a fan curve. A curve is a continuous policy, so it uses the
    /// model-wide floor instead of the fixed slider's per-mode minimum, unless the model
    /// overrides that floor because its EC accepts a lower curve target. The maximum is
    /// the current mode's fixed range, limited by the model's writable ceiling.
    /// </summary>
    public static BladeFanTargetRange FanCurveTargetRangeFor(ushort productId, BladePerformanceMode mode)
    {
        var fixedRange = FanTargetRangeFor(productId, mode);
        var minimum = Contracts.TryGetValue(productId, out var contract)
            && contract.MinimumCurveFanTargetRpm is int curveMinimum
                ? curveMinimum
                : MinimumFanTargetRpmFor(productId);
        return new BladeFanTargetRange(
            minimum,
            Math.Min(fixedRange.MaximumRpm, MaximumWritableFanTargetRpmFor(productId)),
            fixedRange.DefaultRpm);
    }

    /// <summary>
    /// True where a fixed target is only acted on in Custom performance mode. The 028C
    /// EC stores a target in Balanced/Silent but keeps following its own curve.
    /// </summary>
    public static bool FixedFanRequiresCustomMode(ushort productId) =>
        Contracts.TryGetValue(productId, out var contract) && contract.FixedFanRequiresCustomMode;

    /// <summary>
    /// True where the model's own performance-mode table lists HyperBoost. The mode is
    /// written through the shared 0x0D:0x02 thermal channel, so it does not need a
    /// Razer Cooling Pad; the pad only drives the temperature-to-fan curve.
    /// </summary>
    public static bool SupportsHyperBoost(ushort productId) =>
        Contracts.TryGetValue(productId, out var contract) && contract.HyperBoost;

    /// <summary>
    /// HyperBoost is offered on every model whose middleware lists it. The adapter
    /// wattage report only tells the user that the current supply may not hold the
    /// raised power limit, so it must not close the option: the middleware keeps the
    /// mode selectable and reports the shortage separately. The cooling pad is not
    /// required either, because the pad only hosts the temperature-to-fan curve.
    /// </summary>
    public static bool IsHyperBoostAvailable(ushort productId) =>
        SupportsHyperBoost(productId);

    /// <summary>
    /// Upper bound for parsing a stored fan target, covering every per-mode range
    /// the model publishes, including modes the UI does not expose.
    /// </summary>
    public static int MaximumFanTargetRpmFor(ushort productId) =>
        Contracts.TryGetValue(productId, out var contract)
            ? FanTargetBounds(contract).Maximum
            : BladeFanLimits.MaximumRpm;

    /// <summary>
    /// Upper bound for a fixed fan write and for the slider that offers one. The shared
    /// fan-target report stores the speed in 100 RPM steps, and the official middleware
    /// writes the same rpm/100 value, so each model writes up to the ceiling its own
    /// firmware reports. The 029F EC can retain a 5500 RPM readback; its write ceiling is 5300.
    /// </summary>
    public static int MaximumWritableFanTargetRpmFor(ushort productId) =>
        productId == 0x029F ? 5300 : MaximumFanTargetRpmFor(productId);

    /// <summary>
    /// True for the 2026 Blade generation. Its thermal reports select profile 1 and its
    /// fan-target reports select class 1, unlike the older 710/671 reports which use 0.
    /// </summary>
    public static bool UsesBlade2026ThermalSelector(ushort productId) =>
        productId is Blade16_2026ProductId or Blade18_2026ProductId;

    /// <summary>True for the 2026 Blade 16/18 generation.</summary>
    public static bool IsBlade2026(ushort productId) =>
        productId is Blade16_2026ProductId or Blade18_2026ProductId;

    /// <summary>
    /// True only where the exact model has an audited write path. Unknown models stay
    /// closed so a read-only probe cannot be promoted by a stale profile.
    /// </summary>
    public static bool AllowsControlWrites(ushort productId) =>
        Contracts.TryGetValue(productId, out var contract) && contract.Writes;

    public static bool SupportsMaxFan(ushort productId) =>
        Contracts.TryGetValue(productId, out var contract) && contract.MaxFan;

    /// <summary>
    /// True only where the device manifest declares startup-animation.get. The shared
    /// Blade report block reads on models that never exposed the selector, which made
    /// 02B7 look like it had a missing capability instead of an unsupported one.
    /// </summary>
    public static bool SupportsStartupAnimation(ushort productId) =>
        productId is 0x02C5 or 0x02C6 or 0x02C7 or 0x02E0 or 0x02E1;

    /// <summary>
    /// True only where the model's deviceUI publishes hasLocalDimming. A MiniLED panel
    /// alone is not evidence: 029F/02A0/02B6/02C7 carry the dual-resolution panel but
    /// their middleware never exposes the dimming switch, and the shared 07:0F write is
    /// rejected with 0x05 on them.
    /// </summary>
    public static bool SupportsLocalDimming(ushort productId) =>
        productId is 0x02B7 or 0x02B8 or 0x02C5 or 0x02C6 or 0x02E0 or 0x02E1;

    /// <summary>
    /// True only where the model's deviceUI publishes hasNativeDisplayMode.
    /// </summary>
    public static bool SupportsNativeDisplayMode(ushort productId) =>
        productId is 0x029F or 0x02A0 or 0x02B6 or 0x02B7 or 0x02B8 or
            0x02C5 or 0x02C6 or 0x02C7 or 0x02E0 or 0x02E1;

    /// <summary>
    /// True when the model publishes the selector and the panel reports the dual-mode
    /// bit. Blade 18 (02C7) is the exception: its 0D:8F byte can report 0x01 without
    /// the mini-LED bit, and the official deviceUI forces the selector on for it.
    /// </summary>
    public static bool SupportsNativeDisplayMode(
        ushort productId,
        BladeSkuHardwareConfiguration? sku) =>
        SupportsNativeDisplayMode(productId) &&
        (sku?.MiniLedResolution == true || productId == 0x02C7);

    /// <summary>
    /// True only where the deviceUI publishes hasCpuBoostEnabled (028C, 029D, 02B6).
    /// The bit reads back on every model that shares the Power Mode Control mask, so a
    /// readable bit is not evidence of support.
    /// </summary>
    public static bool SupportsCpuPowerBoost(ushort productId) =>
        Contracts.TryGetValue(productId, out var contract) && contract.CpuPowerBoost;

    /// <summary>
    /// True when the exact model has audited Custom CPU levels to show. Decided by the
    /// model contract, not by the last readback: firmware on a Low/Medium/High model can
    /// report Boost or Undervolt, which maps to no admitted index.
    /// </summary>
    public static bool SupportsCpuBoost(ushort productId) =>
        Contracts.TryGetValue(productId, out var contract) && contract.CpuBoostModes.Count > 0;
    public static bool SupportsGpuBoost(ushort productId) => SupportsCpuBoost(productId);
    public static bool AllowsCpuBoost(ushort productId, BladeCpuBoostMode mode) =>
        Contracts.TryGetValue(productId, out var contract) && contract.CpuBoostModes.Contains(mode);

    /// <summary>
    /// Custom CPU levels admitted for a model, in display order. Empty when the exact
    /// product id has no audited contract.
    /// </summary>
    public static IReadOnlyList<BladeCpuBoostMode> CpuBoostModesFor(ushort productId) =>
        Contracts.TryGetValue(productId, out var contract) ? contract.CpuBoostModes : None;

    /// <summary>
    /// Highest CPU level this model admits. HyperBoost carries no power budget of its
    /// own, so it applies this level to reach the ceiling Custom can already select.
    /// </summary>
    public static BladeCpuBoostMode? MaximumCpuBoostModeFor(ushort productId) =>
        Contracts.TryGetValue(productId, out var contract) && contract.CpuBoostModes.Count > 0
            ? contract.CpuBoostModes.Max()
            : null;
}
