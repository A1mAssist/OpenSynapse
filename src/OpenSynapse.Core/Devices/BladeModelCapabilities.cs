using System.Collections.Frozen;

namespace OpenSynapse.Core.Devices;

/// <summary>
/// Per-model Blade capability admission. A capability is admitted only when the exact
/// USB product id has source-backed evidence for that control. A successful protocol
/// readback is not evidence of support: Synapse shares one Power Mode Control bitmask
/// across models, so bits a model never implemented still read back cleanly.
/// </summary>
public static class BladeModelCapabilities
{
    /// <summary>Custom CPU levels every audited model captures (0x0D:0x07 payload level).</summary>
    private static readonly BladeCpuBoostMode[] LowMediumHigh =
        [BladeCpuBoostMode.Low, BladeCpuBoostMode.Medium, BladeCpuBoostMode.High];

    /// <summary>No audited admission. Used when a product id is unknown to this table.</summary>
    private static readonly BladeCpuBoostMode[] None = [];

    private static readonly FrozenDictionary<ushort, Contract> Contracts =
        new Dictionary<ushort, Contract>
        {
            // Blade 16 (2025) RZ09-0528. Custom CPU/GPU levels are the captured
            // 0x0D:0x07 pairs; its maximum fan is the fixed 5400 RPM fan-target
            // control, not the Custom Power Mode bit, so Max Fan is not admitted here.
            [0x02C6] = new(MaxFan: false, LowMediumHigh, FanCurve: true, FixedFan: true, PadlessHyperBoost: true),

            // Blade 15 (Mid 2019) Mercury RZ09-03017. Owner-tested end to end: a fixed
            // target write took effect, both tachometers read back higher without a
            // restart, and switching back to automatic released the fans. A 3000 RPM
            // write also read back as 0x1E on both zones, so the firmware stores the
            // requested value rather than remapping it; the higher tachometer readings
            // are the firmware's own speed policy, not a scaling error. The fan curve
            // stays closed because the official Synapse 4 for this model offers only
            // automatic and a fixed-speed slider, so there is no OEM curve to match.
            // Maximum fan stays closed too: never exercised, and the OEM has no such mode.
            [0x0245] = new(MaxFan: false, None, FixedFan: true, MaximumFanTargetRpm: 5300),

            // Blade 16 (2023) RZ09-0483. Owner-tested maximum fans plus a CPU Boost level.
            [0x029F] = new(MaxFan: true, [.. LowMediumHigh, BladeCpuBoostMode.Boost]),

            // Blade 18 (2023) RZ09-0484. Owner-reported maximum fans; its fixed fan
            // targets were reported ignored, so no fixed target is admitted.
            [0x02A0] = new(MaxFan: true, LowMediumHigh),

            // Blade 14 (2022) RZ09-0427. Owner readings on 028C answered all ten Blade
            // controls, including both live fan tachometers. The owner then confirmed a
            // fixed-speed write by reading both tachometers back higher, and the write took
            // effect without restarting, so the baseline CPU levels stay admitted. Maximum
            // fan and the fan curve stay closed: neither was exercised, and a curve also
            // needs a verified restore-on-exit path.

            [0x028C] = new(MaxFan: false, LowMediumHigh),
            // Blade 14 (2023) RZ09-0482 and Blade 14 (2024) RZ09-0509. The middleware
            // enables both the maximum-fan bit and custom CPU levels.
            [0x029D] = new(MaxFan: true, LowMediumHigh, FanCurve: true, FixedFan: true),
            [0x02B6] = new(MaxFan: true, LowMediumHigh, FanCurve: true, FixedFan: true),

            // Blade 18 (2024) enables maximum fan and exposes the 0D fan-target reports.
            // It has no CPU-level flag, so the custom CPU control stays closed.
            [0x02B8] = new(MaxFan: true, None, FanCurve: true, FixedFan: true),

            // Blade 14 (2025) and Blade 18 (2025) expose the 710 fan-target reports but
            // do not enable the maximum-fan bit or custom CPU levels in their middleware.
            [0x02C5] = new(MaxFan: false, None, FanCurve: true, FixedFan: true),
            [0x02C7] = new(MaxFan: false, None, FanCurve: true, FixedFan: true),

            // Blade 16 (2024) 1532:02B7. The 695 middleware uses the same fan-target,
            // panel and power reports as 02C6; Max Fan stays off because its injected
            // configuration does not enable the Custom maximum-fan bit. CPU Boost is
            // likewise not enabled by its deviceUI configuration.
            [0x02B7] = new(MaxFan: false, None, FanCurve: true, FixedFan: true),

            // Blade 16 (2026) RZ09-0581 remains read-only until its middleware is audited.
            [0x02E0] = new(MaxFan: false, None),

            // Blade 15 Base (Early 2020) 1532:0255. Its middleware exposes Chroma
            // brightness, thermal, charge-limit, adapter-power and power-mode reports.
            // Fixed RPM targets stay closed because 0x0D:0x81 reports a percentage,
            // not the newer RPM layout.
            [0x0255] = new(MaxFan: false, CpuBoostModes: None),
        }.ToFrozenDictionary();

    /// <summary>
    /// Temperature-driven fan curve admission. BladeFanProtocol stores targets only for
    /// the 02C6 fan-target control, so no other model has captured curve evidence yet.
    /// </summary>
    public static bool SupportsFanCurve(ushort productId) =>
        Contracts.TryGetValue(productId, out var contract) && contract.FanCurve;

    /// <summary>True only where a fixed fan-target write has been admitted.</summary>
    public static bool SupportsFixedFan(ushort productId) =>
        Contracts.TryGetValue(productId, out var contract) && contract.FixedFan;

    /// <summary>
    /// True only for the 02C6 pad-less HyperBoost path. The mode writes 0x07 to both
    /// thermal channels and requires the full-power adapter; it does not emulate a
    /// cooling pad or inherit the pad-present policy.
    /// </summary>
    public static bool SupportsPadlessHyperBoost(ushort productId) =>
        Contracts.TryGetValue(productId, out var contract) && contract.PadlessHyperBoost;

    /// <summary>
    /// HyperBoost is available through the Razer pad on every admitted Blade, and
    /// through the pad-less 02C6 path only on the full-power adapter.
    /// </summary>
    public static bool IsHyperBoostAvailable(
        ushort productId,
        BladeAdapterPowerSnapshot? adapterPower,
        bool coolingPadConnected) =>
        coolingPadConnected ||
        (SupportsPadlessHyperBoost(productId) &&
         adapterPower is { ActualWatts: > 0, RecommendedWatts: > 0 } power &&
         !power.IsLimitedAdapter);

    /// <summary>
    /// Upper bound for parsing a stored fan target. This can exceed the fixed-write
    /// ceiling when a model reports a higher maximum than OpenSynapse exposes.
    /// </summary>
    public static int MaximumFanTargetRpmFor(ushort productId) =>
        Contracts.TryGetValue(productId, out var contract)
            ? contract.MaximumFanTargetRpm
            : BladeFanLimits.MaximumRpm;

    private sealed record Contract(
        bool MaxFan,
        IReadOnlyList<BladeCpuBoostMode> CpuBoostModes,
        bool FanCurve = false,
        bool FixedFan = false,
        int MaximumFanTargetRpm = BladeFanLimits.MaximumRpm,
        bool Writes = true,
        bool PadlessHyperBoost = false);

    /// <summary>
    /// True only where the exact model has an audited write path. Unknown models stay
    /// closed so a read-only probe cannot be promoted by a stale profile.
    /// </summary>
    public static bool AllowsControlWrites(ushort productId) =>
        Contracts.TryGetValue(productId, out var contract) && contract.Writes;

    public static bool SupportsMaxFan(ushort productId) =>
        Contracts.TryGetValue(productId, out var contract) && contract.MaxFan;

    /// <summary>
    /// True when the exact model has audited Custom CPU levels to show. Decided by the
    /// model contract, not by the last readback: firmware on a Low/Medium/High model can
    /// report Boost or Undervolt, which maps to no admitted index.
    /// </summary>
    public static bool SupportsCpuBoost(ushort productId) =>
        Contracts.TryGetValue(productId, out var contract) && contract.CpuBoostModes.Count > 0;

    public static bool AllowsCpuBoost(ushort productId, BladeCpuBoostMode mode) =>
        Contracts.TryGetValue(productId, out var contract) && contract.CpuBoostModes.Contains(mode);

    /// <summary>
    /// Custom CPU levels admitted for a model, in display order. Empty when the exact
    /// product id has no audited contract.
    /// </summary>
    public static IReadOnlyList<BladeCpuBoostMode> CpuBoostModesFor(ushort productId) =>
        Contracts.TryGetValue(productId, out var contract) ? contract.CpuBoostModes : None;
}
