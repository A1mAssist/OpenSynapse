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
    public const ushort Blade14_2022ProductId = 0x028C;

    /// <summary>Custom CPU levels every audited model captures (0x0D:0x07 payload level).</summary>
    private static readonly BladeCpuBoostMode[] LowMediumHigh =
        [BladeCpuBoostMode.Low, BladeCpuBoostMode.Medium, BladeCpuBoostMode.High];

    private static readonly BladePerformanceMode[] StandardPerformanceModes =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.Performance,
        BladePerformanceMode.Custom,
        BladePerformanceMode.Silent,
    ];

    // Synapse on 028C exposes the first three. The owner confirmed the firmware also
    // accepts 0x02 and reads it back, so keep it as an extra mode after the official list.
    private static readonly BladePerformanceMode[] Blade14_2022PerformanceModes =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.Silent,
        BladePerformanceMode.Custom,
        BladePerformanceMode.Performance,
    ];

    /// <summary>No audited admission. Used when a product id is unknown to this table.</summary>
    private static readonly BladeCpuBoostMode[] None = [];

    private static readonly FrozenDictionary<ushort, Contract> Contracts =
        new Dictionary<ushort, Contract>
        {
            // Blade 16 (2025) RZ09-0528. Custom CPU/GPU levels are the captured
            // 0x0D:0x07 pairs; its maximum fan is the fixed 5400 RPM fan-target
            // control, not the Custom Power Mode bit, so Max Fan is not admitted here.
            [0x02C6] = new(MaxFan: false, LowMediumHigh, FanCurve: true, FixedFan: true),

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
            // controls, including both live fan tachometers. Official Synapse exposes four
            // Custom CPU levels on this model, including Boost, and the fixed fan target only
            // acts in Custom mode with a 3500-5000 RPM range. Maximum fan and the fan curve
            // stay closed: neither was exercised, and a curve also needs a verified
            // restore-on-exit path.
            [Blade14_2022ProductId] = new(
                MaxFan: false,
                [.. LowMediumHigh, BladeCpuBoostMode.Boost],
                FixedFan: true,
                MinimumFanTargetRpm: 3500,
                FixedFanRequiresCustomMode: true),
            // Blade 14 (2023) RZ09-0482, Blade 14 (2025) RZ09-0530 and Blade 16
            // (2026) RZ09-0581. No maximum-fan evidence on any of them.
            [0x029D] = new(MaxFan: false, LowMediumHigh),
            [0x02C5] = new(MaxFan: false, LowMediumHigh),
            [0x02E0] = new(MaxFan: false, LowMediumHigh),
        }.ToFrozenDictionary();

    /// <summary>
    /// Temperature-driven fan curve admission. BladeFanProtocol stores targets only for
    /// the 02C6 fan-target control, so no other model has captured curve evidence yet.
    /// </summary>
    public static bool SupportsFanCurve(ushort productId) =>
        Contracts.TryGetValue(productId, out var contract) && contract.FanCurve;

    /// <summary>
    /// Performance modes exposed for the exact model. 028C uses the three modes its
    /// Synapse UI exposes; the protocol-only Performance value must not leak into it.
    /// </summary>
    public static IReadOnlyList<BladePerformanceMode> PerformanceModesFor(ushort productId) =>
        productId == Blade14_2022ProductId
            ? Blade14_2022PerformanceModes
            : StandardPerformanceModes;

    /// <summary>True only where a fixed fan-target write has been admitted.</summary>
    public static bool SupportsFixedFan(ushort productId) =>
        Contracts.TryGetValue(productId, out var contract) && contract.FixedFan;

    /// <summary>
    /// Upper bound for parsing a stored fan target. This can exceed the fixed-write
    /// ceiling when a model reports a higher maximum than OpenSynapse exposes.
    /// </summary>
    public static int MaximumFanTargetRpmFor(ushort productId) =>
        Contracts.TryGetValue(productId, out var contract)
            ? contract.MaximumFanTargetRpm
            : BladeFanLimits.MaximumRpm;

    /// <summary>Lower bound the exact model accepts for a fixed fan target.</summary>
    public static int MinimumFanTargetRpmFor(ushort productId) =>
        Contracts.TryGetValue(productId, out var contract)
            ? contract.MinimumFanTargetRpm
            : BladeFanLimits.MinimumRpm;

    /// <summary>
    /// True where a fixed target is only acted on in Custom performance mode. The 028C
    /// EC stores a target in Balanced/Silent but keeps following its own curve.
    /// </summary>
    public static bool FixedFanRequiresCustomMode(ushort productId) =>
        Contracts.TryGetValue(productId, out var contract) && contract.FixedFanRequiresCustomMode;

    private sealed record Contract(
        bool MaxFan,
        IReadOnlyList<BladeCpuBoostMode> CpuBoostModes,
        bool FanCurve = false,
        bool FixedFan = false,
        int MinimumFanTargetRpm = BladeFanLimits.MinimumRpm,
        int MaximumFanTargetRpm = BladeFanLimits.MaximumRpm,
        bool FixedFanRequiresCustomMode = false);

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
