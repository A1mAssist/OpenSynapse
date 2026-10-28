using System.Collections.Frozen;

namespace OpenSynapse.Core.Devices;

/// <summary>Fixed fan-target range the official middleware offers for one performance mode.</summary>
public readonly record struct BladeFanTargetRange(int MinimumRpm, int MaximumRpm, int DefaultRpm);

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

    /// <summary>No audited admission. Used when a product id is unknown to this table.</summary>
    private static readonly BladeCpuBoostMode[] None = [];

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

    // HyperBoost only exists on the models whose middleware lists it. It rides the same
    // thermal channel as the presets, so the write itself needs no extra capability.
    private static readonly BladePerformanceMode[] HyperBoostPerformanceModes =
    [
        .. StandardPerformanceModes,
        BladePerformanceMode.Hyperboost,
    ];

    // Exact mode lists the middleware publishes, in the order its UI shows them.
    // The Turbo mode some bundles list has no counterpart in BladePerformanceMode.
    private static readonly BladePerformanceMode[] Modes16Hyper =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.Silent,
        BladePerformanceMode.Custom,
        BladePerformanceMode.Hyperboost,
    ];

    private static readonly BladePerformanceMode[] Modes02C6 =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.Silent,
        BladePerformanceMode.Performance,
        BladePerformanceMode.Custom,
        BladePerformanceMode.Hyperboost,
        BladePerformanceMode.BatterySaver,
    ];

    private static readonly BladePerformanceMode[] Modes02C5 =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.Silent,
        BladePerformanceMode.Performance,
        BladePerformanceMode.Custom,
        BladePerformanceMode.BatterySaver,
    ];

    private static readonly BladePerformanceMode[] Modes02B7 =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.Silent,
        BladePerformanceMode.Custom,
        BladePerformanceMode.Hyperboost,
    ];

    private static readonly BladePerformanceMode[] Modes029D =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.Silent,
        BladePerformanceMode.Custom,
        BladePerformanceMode.Hyperboost,
    ];

    // 02B6 lists Turbo, which has no BladePerformanceMode counterpart.
    private static readonly BladePerformanceMode[] Modes02B6 =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.Silent,
        BladePerformanceMode.Custom,
    ];

    private static readonly BladePerformanceMode[] Modes0245 =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.Custom,
    ];

    // 02A0 and 02B8 publish Balanced, Silent, Turbo and Custom; Turbo has no
    // BladePerformanceMode counterpart.
    private static readonly BladePerformanceMode[] Modes02A0 =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.Silent,
        BladePerformanceMode.Custom,
    ];

    // 0255 lists Balanced, Productivity_Creator and Gaming. Productivity_Creator is the
    // same wire value as Performance (0x02); Gaming (0x01) has no BladePerformanceMode.
    private static readonly BladePerformanceMode[] Modes0255 =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.Performance,
    ];

    private static IReadOnlyDictionary<BladePerformanceMode, BladeFanTargetRange> Ranges(
        params (BladePerformanceMode Mode, int Minimum, int Maximum, int Default)[] items)
    {
        var map = new Dictionary<BladePerformanceMode, BladeFanTargetRange>(items.Length);
        foreach (var item in items)
        {
            map[item.Mode] = new BladeFanTargetRange(item.Minimum, item.Maximum, item.Default);
        }

        return map;
    }

    private static readonly FrozenDictionary<ushort, Contract> Contracts =
        new Dictionary<ushort, Contract>
        {
            // Blade 16 (2025) RZ09-0528. Custom CPU/GPU levels are the captured
            // 0x0D:0x07 pairs; its maximum fan is the fixed 5400 RPM fan-target
            // control, not the Custom Power Mode bit, so Max Fan is not admitted here.
            // The official bundle drives that same report as rpm/100 and its own
            // Performance and Hyperboost modes run up to 5400 RPM, so the firmware
            // accepts a target the earlier 5000 RPM ceiling wrongly refused.
            [0x02C6] = new(
                MaxFan: false,
                LowMediumHigh,
                FanCurve: true,
                FixedFan: true,
                HyperBoost: true,
                MinimumFanTargetRpm: 3300,
                MaximumFanTargetRpm: 5400,
                ModeFanRanges: Ranges(
                    (BladePerformanceMode.Balanced, 3400, 5200, 4100),
                    (BladePerformanceMode.Silent, 3400, 5200, 4100),
                    (BladePerformanceMode.Performance, 3300, 5400, 4300),
                    (BladePerformanceMode.Custom, 4000, 5300, 4600),
                    (BladePerformanceMode.Hyperboost, 3700, 5300, 4500),
                    (BladePerformanceMode.BatterySaver, 3300, 5400, 4300)),
                Modes: Modes02C6),

            // Blade 15 (Mid 2019) Mercury RZ09-03017. Owner-tested end to end: a fixed
            // target write took effect, both tachometers read back higher without a
            // restart, and switching back to automatic released the fans. A 3000 RPM
            // write also read back as 0x1E on both zones, so the firmware stores the
            // requested value rather than remapping it; the higher tachometer readings
            // are the firmware's own speed policy, not a scaling error. The fan curve
            // stays closed because the official Synapse 4 for this model offers only
            // automatic and a fixed-speed slider, so there is no OEM curve to match.
            // Maximum fan stays closed too: never exercised, and the OEM has no such mode.
            [0x0245] = new(
                MaxFan: false,
                None,
                FixedFan: true,
                MaximumFanTargetRpm: 5300,
                Modes: Modes0245),

            // Blade 16 (2023) RZ09-0483. Owner-tested maximum fans plus a CPU Boost level.
            // Its performance-mode table lists HyperBoost, and it reads the adapter
            // wattage level through the same 07:8C report the other models use.
            // The official middleware drives a fixed fan speed from every performance
            // mode, not only from Custom, so no custom-mode gate applies here. The
            // official modes span 3400 to 5200 RPM and Custom reaches 5300, and the
            // shared fan-target report takes the same rpm/100 value the official
            // middleware writes, so the write ceiling follows the firmware too.
            [0x029F] = new(
                MaxFan: true,
                [.. LowMediumHigh, BladeCpuBoostMode.Boost],
                FixedFan: true,
                MinimumFanTargetRpm: 3400,
                MaximumFanTargetRpm: 5300,
                HyperBoost: true,
                ModeFanRanges: Ranges(
                    (BladePerformanceMode.Balanced, 3400, 5200, 4100),
                    (BladePerformanceMode.Silent, 3400, 5200, 4100),
                    (BladePerformanceMode.Custom, 4000, 5300, 4600),
                    (BladePerformanceMode.Hyperboost, 3400, 5200, 4100)),
                Modes: Modes16Hyper),

            // Blade 18 (2023) RZ09-0484. Owner-reported maximum fans; its fixed fan
            // targets were reported ignored, so no fixed target is admitted.
            [0x02A0] = new(MaxFan: true, LowMediumHigh, Modes: Modes02A0),

            // Blade 14 (2022) RZ09-0427. Owner readings on 028C answered all ten Blade
            // controls, including both live fan tachometers. The owner then confirmed a
            // fixed-speed write by reading both tachometers back higher, and the write took
            // effect without restarting, so the baseline CPU levels stay admitted. Maximum
            // fan and the fan curve stay closed: neither was exercised, and a curve also
            // needs a verified restore-on-exit path.

            // Official Synapse exposes four Custom CPU levels on this model, including
            // Boost, and the fixed fan target only acts in Custom mode. Its official
            // modes run up to 5400 RPM. Maximum fan and the fan curve stay closed.
            [Blade14_2022ProductId] = new(
                MaxFan: false,
                [.. LowMediumHigh, BladeCpuBoostMode.Boost],
                FixedFan: true,
                MinimumFanTargetRpm: 3300,
                MaximumFanTargetRpm: 5400,
                FixedFanRequiresCustomMode: true),
            // Blade 14 (2023) RZ09-0482 and Blade 14 (2024) RZ09-0509. The middleware
            // enables both the maximum-fan bit and custom CPU levels.
            [0x029D] = new(
                MaxFan: true,
                LowMediumHigh,
                FanCurve: true,
                FixedFan: true,
                HyperBoost: true,
                MinimumFanTargetRpm: 2200,
                MaximumFanTargetRpm: 5400,
                ModeFanRanges: Ranges(
                    (BladePerformanceMode.Silent, 2200, 3800, 3000),
                    (BladePerformanceMode.Hyperboost, 3400, 5200, 4100)),
                Modes: Modes029D),
            [0x02B6] = new(
                MaxFan: true,
                LowMediumHigh,
                FanCurve: true,
                FixedFan: true,
                MinimumFanTargetRpm: 2200,
                MaximumFanTargetRpm: 5400,
                ModeFanRanges: Ranges(
                    (BladePerformanceMode.Silent, 2200, 3800, 3000)),
                Modes: Modes02B6),

            // Blade 18 (2024) enables maximum fan and exposes the 0D fan-target reports.
            // It has no CPU-level flag, so the custom CPU control stays closed.
            [0x02B8] = new(
                MaxFan: true,
                None,
                FanCurve: true,
                FixedFan: true,
                MaximumFanTargetRpm: 5400,
                Modes: Modes02A0),

            // Blade 14 (2025) and Blade 18 (2025) expose the 710 fan-target reports but
            // do not enable the maximum-fan bit or custom CPU levels in their middleware.
            [0x02C5] = new(
                MaxFan: false,
                None,
                FanCurve: true,
                FixedFan: true,
                MinimumFanTargetRpm: 3300,
                MaximumFanTargetRpm: 5400,
                ModeFanRanges: Ranges(
                    (BladePerformanceMode.Balanced, 3400, 5200, 4100),
                    (BladePerformanceMode.Silent, 3400, 5200, 4100),
                    (BladePerformanceMode.Performance, 3300, 5400, 4300),
                    (BladePerformanceMode.Custom, 4000, 5300, 4600),
                    (BladePerformanceMode.BatterySaver, 3300, 5400, 4300)),
                Modes: Modes02C5),
            [0x02C7] = new(
                MaxFan: false,
                None,
                FanCurve: true,
                FixedFan: true,
                MinimumFanTargetRpm: 3300,
                MaximumFanTargetRpm: 5400,
                ModeFanRanges: Ranges(
                    (BladePerformanceMode.Balanced, 3400, 5200, 4100),
                    (BladePerformanceMode.Silent, 3400, 5200, 4100),
                    (BladePerformanceMode.Performance, 3300, 5400, 4300),
                    (BladePerformanceMode.Custom, 4000, 5300, 4600),
                    (BladePerformanceMode.BatterySaver, 3300, 5400, 4300)),
                Modes: Modes02C5),

            // Blade 16 (2024) 1532:02B7. The 695 middleware uses the same fan-target,
            // panel and power reports as 02C6; Max Fan stays off because its injected
            // configuration does not enable the Custom maximum-fan bit. CPU Boost is
            // likewise not enabled by its deviceUI configuration. Its performance-mode
            // table lists HyperBoost, so the pad-less path applies here too.
            [0x02B7] = new(
                MaxFan: false,
                None,
                FanCurve: true,
                FixedFan: true,
                HyperBoost: true,
                MinimumFanTargetRpm: 3300,
                MaximumFanTargetRpm: 5400,
                ModeFanRanges: Ranges(
                    (BladePerformanceMode.Balanced, 3400, 5200, 4100),
                    (BladePerformanceMode.Silent, 3400, 5200, 4100),
                    (BladePerformanceMode.Custom, 4000, 5300, 4600),
                    (BladePerformanceMode.Hyperboost, 3400, 5200, 4100)),
                Modes: Modes16Hyper),

            // Blade 16 (2026) RZ09-0581 publishes the same six modes and fan ranges as 02C6.
            [0x02E0] = new(
                MaxFan: false,
                None,
                HyperBoost: true,
                ModeFanRanges: Ranges(
                    (BladePerformanceMode.Balanced, 3400, 5200, 4100),
                    (BladePerformanceMode.Silent, 3400, 5200, 4100),
                    (BladePerformanceMode.Performance, 3300, 5400, 4300),
                    (BladePerformanceMode.Custom, 4000, 5300, 4600),
                    (BladePerformanceMode.Hyperboost, 3700, 5300, 4500),
                    (BladePerformanceMode.BatterySaver, 3300, 5400, 4300)),
                Modes: Modes02C6),

            // Blade 15 Base (Early 2020) 1532:0255. Its middleware exposes Chroma
            // brightness, thermal, charge-limit, adapter-power and power-mode reports.
            // Fixed RPM targets stay closed because 0x0D:0x81 reports a percentage,
            // not the newer RPM layout.
            [0x0255] = new(MaxFan: false, CpuBoostModes: None, Modes: Modes0255),
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
            ? contract.MinimumFanTargetRpm
            : BladeFanLimits.MinimumRpm;

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

        if (contract.ModeFanRanges is { } ranges && ranges.TryGetValue(mode, out var range))
        {
            return range;
        }

        return new BladeFanTargetRange(
            contract.MinimumFanTargetRpm, contract.MaximumFanTargetRpm, contract.MinimumFanTargetRpm);
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
    /// Upper bound for parsing a stored fan target. This can exceed the fixed-write
    /// ceiling when a model reports a higher maximum than OpenSynapse exposes.
    /// </summary>
    public static int MaximumFanTargetRpmFor(ushort productId) =>
        Contracts.TryGetValue(productId, out var contract)
            ? contract.MaximumFanTargetRpm
            : BladeFanLimits.MaximumRpm;

    /// <summary>
    /// Upper bound for a fixed fan write and for the slider that offers one. The shared
    /// fan-target report stores the speed in 100 RPM steps, and the official middleware
    /// writes the same rpm/100 value, so each model writes up to the ceiling its own
    /// firmware reports.
    /// </summary>
    public static int MaximumWritableFanTargetRpmFor(ushort productId) =>
        MaximumFanTargetRpmFor(productId);

    private sealed record Contract(
        bool MaxFan,
        IReadOnlyList<BladeCpuBoostMode> CpuBoostModes,
        bool FanCurve = false,
        bool FixedFan = false,
        int MinimumFanTargetRpm = BladeFanLimits.MinimumRpm,
        int MaximumFanTargetRpm = BladeFanLimits.MaximumRpm,
        bool Writes = true,
        bool HyperBoost = false,
        bool FixedFanRequiresCustomMode = false,
        IReadOnlyDictionary<BladePerformanceMode, BladeFanTargetRange>? ModeFanRanges = null,
        IReadOnlyList<BladePerformanceMode>? Modes = null);

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
