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
    public const ushort Blade16_2026ProductId = 0x02E0;
    public const ushort Blade18_2026ProductId = 0x02E1;

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
        BladePerformanceMode.Gaming,
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

    // 02B6 publishes Gaming as wire value 0x01.
    private static readonly BladePerformanceMode[] Modes02B6 =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.Silent,
        BladePerformanceMode.Gaming,
        BladePerformanceMode.Custom,
    ];

    private static readonly BladePerformanceMode[] Modes0245 =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.Custom,
    ];

    // 02B8 publishes Balanced, Silent, Gaming and Custom. 02A0 publishes only
    // Balanced, Silent and Custom, so it keeps the three-mode list below.
    private static readonly BladePerformanceMode[] Modes02B8 =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.Silent,
        BladePerformanceMode.Gaming,
        BladePerformanceMode.Custom,
    ];

    // 02C7 and 02E1 publish Gaming in addition to the 02C5 list.
    private static readonly BladePerformanceMode[] Modes02C7 =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.Silent,
        BladePerformanceMode.Performance,
        BladePerformanceMode.Gaming,
        BladePerformanceMode.Custom,
        BladePerformanceMode.BatterySaver,
    ];

    // 02A0/028A/028B/029E share the older three-mode list; only 02B8 adds Gaming.
    private static readonly BladePerformanceMode[] Modes028A =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.Silent,
        BladePerformanceMode.Custom,
    ];

    // 0255 lists Balanced, Productivity_Creator and Gaming. Productivity_Creator is the
    // same wire value as Performance (0x02); Gaming is 0x01.
    private static readonly BladePerformanceMode[] Modes0255 =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.Performance,
        BladePerformanceMode.Gaming,
    ];

    private static readonly BladePerformanceMode[] ModesBalancedPerformance =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.Performance,
    ];

    private static readonly BladePerformanceMode[] ModesBalancedGaming =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.Gaming,
    ];

    private static readonly BladePerformanceMode[] ModesBalancedPerformanceGaming =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.Performance,
        BladePerformanceMode.Gaming,
    ];

    private static readonly BladePerformanceMode[] ModesBalancedBatterySaverGaming =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.BatterySaver,
        BladePerformanceMode.Gaming,
    ];

    private static readonly BladePerformanceMode[] ModesBalancedPerformanceBatterySaver =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.Performance,
        BladePerformanceMode.BatterySaver,
    ];

    private static readonly BladePerformanceMode[] ModesBalancedCustom =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.Custom,
    ];

    private static readonly BladePerformanceMode[] ModesBalancedOnly =
    [
        BladePerformanceMode.Balanced,
    ];

    private static readonly BladePerformanceMode[] ModesBalancedBatterySaver =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.BatterySaver,
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
            // Its bundle publishes boost.get/set and Custom CPU/GPU levels, so the
            // Low/Medium/High baseline is admitted even though the boost toggle is not.
            [0x0245] = new(
                MaxFan: false,
                LowMediumHigh,
                FixedFan: true,
                MinimumFanTargetRpm: 3700,
                MaximumFanTargetRpm: 5300,
                ModeFanRanges: Ranges(
                    (BladePerformanceMode.Balanced, 3700, 5300, 4500),
                    (BladePerformanceMode.Custom, 4000, 5300, 4600)),
                Modes: Modes0245),

            // Blade 16 (2023) RZ09-0483. Owner-tested maximum fans plus a CPU Boost level.
            // Its performance-mode table lists HyperBoost, and it reads the adapter
            // wattage level through the same 07:8C report the other models use.
            // The official middleware drives a fixed fan speed from every performance
            // mode, not only from Custom, so no custom-mode gate applies here. The
            // official modes span 3400 to 5200 RPM and Custom reaches 5300. A user
            // readback from this EC can retain 5500 RPM, so accept that as a read
            // ceiling while keeping writes inside the bundle-published 5300 limit.
            [0x029F] = new(
                MaxFan: true,
                [.. LowMediumHigh, BladeCpuBoostMode.Boost],
                FanCurve: true, FixedFan: true,
                MinimumFanTargetRpm: 3400,
                MaximumFanTargetRpm: 5500,
                HyperBoost: true,
                ModeFanRanges: Ranges(
                    (BladePerformanceMode.Balanced, 3400, 5200, 4100),
                    (BladePerformanceMode.Silent, 3400, 5200, 4100),
                    (BladePerformanceMode.Custom, 4000, 5300, 4600),
                    (BladePerformanceMode.Hyperboost, 3400, 5200, 4100)),
                Modes: Modes16Hyper),

            // Blade 18 (2023) RZ09-0484. Owner-reported maximum fans; its fixed fan
            // targets were reported ignored, so no fixed target is admitted.
            [0x02A0] = new(
                MaxFan: true,
                LowMediumHigh,
                ModeFanRanges: Ranges(
                    (BladePerformanceMode.Balanced, 2200, 3800, 3000),
                    (BladePerformanceMode.Silent, 3700, 5300, 4500),
                    (BladePerformanceMode.Custom, 1700, 4300, 3000)),
                Modes: Modes028A),

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
                FixedFanRequiresCustomMode: true,
                ModeFanRanges: Ranges(
                    (BladePerformanceMode.Balanced, 3300, 5400, 4300),
                    (BladePerformanceMode.Silent, 3300, 5400, 4300),
                    (BladePerformanceMode.Custom, 3300, 5400, 4300)),
                CpuPowerBoost: true),
            // Blade 14 (2023) RZ09-0482 and Blade 14 (2024) RZ09-0509. The middleware
            // enables both the maximum-fan bit and custom CPU levels.
            [0x029D] = new(
                MaxFan: true,
                LowMediumHigh,
                FanCurve: true,
                FixedFan: true,
                HyperBoost: true,
                MinimumFanTargetRpm: 2000,
                MaximumFanTargetRpm: 5400,
                ModeFanRanges: Ranges(
                    (BladePerformanceMode.Balanced, 2000, 5400, 3800),
                    (BladePerformanceMode.Silent, 2200, 3800, 3000),
                    (BladePerformanceMode.Custom, 2000, 5400, 3800),
                    (BladePerformanceMode.Hyperboost, 3400, 5200, 4100)),
                Modes: Modes029D,
                CpuPowerBoost: true),
            [0x02B6] = new(
                MaxFan: true,
                LowMediumHigh,
                FanCurve: true,
                FixedFan: true,
                MinimumFanTargetRpm: 2000,
                MaximumFanTargetRpm: 5400,
                ModeFanRanges: Ranges(
                    (BladePerformanceMode.Balanced, 2000, 5400, 3800),
                    (BladePerformanceMode.Silent, 2200, 3800, 3000),
                    (BladePerformanceMode.Gaming, 2000, 5400, 3800),
                    (BladePerformanceMode.Custom, 3300, 5400, 4300)),
                Modes: Modes02B6,
                CpuPowerBoost: true),

            // Blade 18 (2024) enables maximum fan and exposes the 0D fan-target reports.
            // Its bundle publishes boost.get/set and Custom CPU/GPU levels, but not the
            // hasCpuBoostEnabled toggle, so it admits Low/Medium/High only.
            [0x02B8] = new(
                MaxFan: true,
                LowMediumHigh,
                FanCurve: true,
                FixedFan: true,
                MinimumFanTargetRpm: 3300,
                MaximumFanTargetRpm: 5400,
                ModeFanRanges: Ranges(
                    (BladePerformanceMode.Balanced, 3700, 5300, 4500),
                    (BladePerformanceMode.Silent, 3700, 5300, 4500),
                    (BladePerformanceMode.Gaming, 3300, 5400, 4300),
                    (BladePerformanceMode.Custom, 4000, 5300, 4600)),
                Modes: Modes02B8),

            // Blade 14 (2025) and Blade 18 (2025) expose the 710 fan-target reports but
            // do not enable the maximum-fan bit in their middleware. Both still publish
            // boost.get/set and Custom CPU/GPU levels, so the Low/Medium/High baseline applies.
            [0x02C5] = new(
                MaxFan: false,
                LowMediumHigh,
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
                LowMediumHigh,
                FanCurve: true,
                FixedFan: true,
                MinimumFanTargetRpm: 3300,
                MaximumFanTargetRpm: 5400,
                ModeFanRanges: Ranges(
                    (BladePerformanceMode.Balanced, 3400, 5200, 4100),
                    (BladePerformanceMode.Silent, 3400, 5200, 4100),
                    (BladePerformanceMode.Performance, 3300, 5400, 4300),
                    (BladePerformanceMode.Gaming, 3300, 5400, 4300),
                    (BladePerformanceMode.Custom, 4000, 5300, 4600),
                    (BladePerformanceMode.BatterySaver, 3300, 5400, 4300)),
                Modes: Modes02C7),

            // Blade 16 (2024) 1532:02B7. The 695 middleware uses the same fan-target,
            // panel and power reports as 02C6; Max Fan stays off because its injected
            // configuration does not enable the Custom maximum-fan bit. The
            // hasCpuBoostEnabled toggle is not published either, but the bundle's
            // boost.get/set and Custom CPU/GPU levels still admit Low/Medium/High.
            // Its performance-mode table lists HyperBoost, so the pad-less path applies here too.
            [0x02B7] = new(
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
                    (BladePerformanceMode.Gaming, 3300, 5400, 4300),
                    (BladePerformanceMode.Custom, 4000, 5300, 4600),
                    (BladePerformanceMode.Hyperboost, 3400, 5200, 4100)),
                Modes: Modes02B7),

            // The remaining rzDevice25LedMatrixBladeWattage bundles publish RPM
            // ranges, but their 0D:81/0D:01 target is the legacy percentage report
            // (0255 reads 0x35 as 53%), not BladeFanProtocol's rpm/100 target. Keep
            // fixed RPM closed until a model-specific RPM capture exists. 0245 is
            // the exception because its 0x35 readback is 5300 RPM.
            [0x0233] = new(
                MaxFan: false,
                None,
                ModeFanRanges: Ranges(
                    (BladePerformanceMode.Balanced, 3500, 5700, 4500),
                    (BladePerformanceMode.Performance, 4500, 5700, 5000)),
                Modes: ModesBalancedPerformance),

            [0x023B] = new(
                MaxFan: false,
                None,
                ModeFanRanges: Ranges(
                    (BladePerformanceMode.Balanced, 3700, 5300, 4500),
                    (BladePerformanceMode.Gaming, 4000, 5300, 4600)),
                Modes: ModesBalancedGaming),

            [0x0240] = new(
                MaxFan: false,
                None,
                ModeFanRanges: Ranges(
                    (BladePerformanceMode.Balanced, 3500, 5700, 4500),
                    (BladePerformanceMode.Performance, 4500, 5700, 5000)),
                Modes: ModesBalancedPerformance),

            [0x0246] = new(
                MaxFan: false,
                None,
                ModeFanRanges: Ranges(
                    (BladePerformanceMode.Balanced, 3500, 5500, 4200),
                    (BladePerformanceMode.Performance, 3900, 5500, 4500),
                    (BladePerformanceMode.Gaming, 3900, 5500, 4500)),
                Modes: ModesBalancedPerformanceGaming),

            [0x024A] = new(
                MaxFan: false,
                None,
                ModeFanRanges: Ranges(
                    (BladePerformanceMode.Balanced, 2500, 5500, 4000),
                    (BladePerformanceMode.BatterySaver, 2500, 5500, 4000),
                    (BladePerformanceMode.Gaming, 2500, 5500, 4000)),
                Modes: ModesBalancedBatterySaverGaming),

            [0x024B] = new(
                MaxFan: false,
                LowMediumHigh,
                ModeFanRanges: Ranges(
                    (BladePerformanceMode.Balanced, 3700, 5300, 4500),
                    (BladePerformanceMode.Custom, 4000, 5300, 4600)),
                Modes: ModesBalancedCustom),

            [0x024C] = new(
                MaxFan: false,
                LowMediumHigh,
                ModeFanRanges: Ranges(
                    (BladePerformanceMode.Balanced, 3400, 5200, 4100),
                    (BladePerformanceMode.Custom, 4000, 5300, 4600)),
                Modes: ModesBalancedCustom),

            [0x024D] = new(
                MaxFan: false,
                LowMediumHigh,
                ModeFanRanges: Ranges(
                    (BladePerformanceMode.Balanced, 3700, 5300, 4500),
                    (BladePerformanceMode.Custom, 4000, 5300, 4600)),
                Modes: ModesBalancedCustom),

            [0x0253] = new(
                MaxFan: false,
                LowMediumHigh,
                ModeFanRanges: Ranges(
                    (BladePerformanceMode.Balanced, 3700, 5300, 4500),
                    (BladePerformanceMode.Custom, 4000, 5300, 4600)),
                Modes: ModesBalancedCustom),

            [0x0256] = new(
                MaxFan: false,
                LowMediumHigh,
                ModeFanRanges: Ranges(
                    (BladePerformanceMode.Balanced, 2300, 4300, 2900),
                    (BladePerformanceMode.Custom, 2300, 4300, 2900)),
                Modes: ModesBalancedCustom),

            [0x0268] = new(
                MaxFan: false,
                None,
                ModeFanRanges: Ranges(
                    (BladePerformanceMode.Balanced, 3600, 5200, 4300),
                    (BladePerformanceMode.Performance, 3600, 5200, 4300),
                    (BladePerformanceMode.Gaming, 3600, 5200, 4300)),
                Modes: ModesBalancedPerformanceGaming),

            [0x026D] = new(
                MaxFan: false,
                LowMediumHigh,
                ModeFanRanges: Ranges(
                    (BladePerformanceMode.Balanced, 3700, 5300, 4500),
                    (BladePerformanceMode.Custom, 4000, 5300, 4600)),
                Modes: ModesBalancedCustom),

            [0x026E] = new(
                MaxFan: false,
                LowMediumHigh,
                ModeFanRanges: Ranges(
                    (BladePerformanceMode.Balanced, 2300, 4300, 2900),
                    (BladePerformanceMode.Custom, 2300, 4300, 2900)),
                Modes: ModesBalancedCustom),

            [0x026F] = new(
                MaxFan: false,
                None,
                ModeFanRanges: Ranges(
                    (BladePerformanceMode.Balanced, 4200, 5400, 4800),
                    (BladePerformanceMode.Performance, 4200, 5400, 4800),
                    (BladePerformanceMode.Gaming, 4200, 5400, 4800)),
                Modes: ModesBalancedPerformanceGaming),

            [0x0270] = new(
                MaxFan: false,
                LowMediumHigh,
                ModeFanRanges: Ranges(
                    (BladePerformanceMode.Balanced, 3300, 5400, 4300),
                    (BladePerformanceMode.Custom, 3300, 5400, 4300)),
                Modes: ModesBalancedCustom),

            [0x0276] = new(
                MaxFan: false,
                LowMediumHigh, FixedFan: true,
                ModeFanRanges: Ranges(
                    (BladePerformanceMode.Balanced, 3400, 5200, 4100),
                    (BladePerformanceMode.Custom, 4000, 5300, 4600)),
                Modes: ModesBalancedCustom),

            [0x0279] = new(
                MaxFan: false,
                LowMediumHigh,
                ModeFanRanges: Ranges(
                    (BladePerformanceMode.Balanced, 2300, 4300, 2900),
                    (BladePerformanceMode.Custom, 2300, 4300, 2900)),
                Modes: ModesBalancedCustom),

            // Blade-group bundles use the 0D:01/0D:81 fan-target path already
            // implemented by BladeFanProtocol, and their published ranges are RPM,
            // so fixed RPM is admitted for the same report.
            [0x0234] = new(
                MaxFan: false,
                LowMediumHigh,
                FixedFan: true,
                MinimumFanTargetRpm: 3400,
                MaximumFanTargetRpm: 5300,
                ModeFanRanges: Ranges(
                    (BladePerformanceMode.Balanced, 3400, 5200, 4100),
                    (BladePerformanceMode.Custom, 4000, 5300, 4600)),
                Modes: ModesBalancedCustom),

            // Blade Stealth (2019). Its bundle publishes BatterySaver with a
            // 2200..5500 range, so the mode and the readback ceiling both follow it.
            [0x0239] = new(
                MaxFan: false,
                None,
                FixedFan: true,
                MinimumFanTargetRpm: 2200,
                MaximumFanTargetRpm: 5500,
                ModeFanRanges: Ranges(
                    (BladePerformanceMode.Balanced, 2200, 5300, 3800),
                    (BladePerformanceMode.Performance, 2200, 5300, 3800),
                    (BladePerformanceMode.BatterySaver, 2200, 5500, 3800)),
                Modes: ModesBalancedPerformanceBatterySaver),

            [0x023A] = new(
                MaxFan: false,
                None,
                FixedFan: true,
                MinimumFanTargetRpm: 3700,
                MaximumFanTargetRpm: 5300,
                ModeFanRanges: Ranges(
                    (BladePerformanceMode.Balanced, 3700, 5300, 4500),
                    (BladePerformanceMode.Performance, 4000, 5300, 4600),
                    (BladePerformanceMode.Gaming, 4000, 5300, 4600)),
                Modes: ModesBalancedPerformanceGaming),

            [0x0252] = new(
                MaxFan: false,
                None,
                FixedFan: true,
                MinimumFanTargetRpm: 2500,
                MaximumFanTargetRpm: 5500,
                ModeFanRanges: Ranges(
                    (BladePerformanceMode.Balanced, 2500, 5500, 4000),
                    (BladePerformanceMode.BatterySaver, 2500, 5500, 4000),
                    (BladePerformanceMode.Gaming, 2500, 5500, 4000)),
                Modes: ModesBalancedBatterySaverGaming),

            [0x0259] = new(
                MaxFan: false,
                None,
                FixedFan: true,
                MinimumFanTargetRpm: 2500,
                MaximumFanTargetRpm: 5600,
                ModeFanRanges: Ranges(
                    (BladePerformanceMode.Balanced, 2500, 5600, 4000),
                    (BladePerformanceMode.BatterySaver, 2500, 5600, 4000),
                    (BladePerformanceMode.Gaming, 2500, 5600, 4000)),
                Modes: ModesBalancedBatterySaverGaming),

            [0x027A] = new(
                MaxFan: false,
                None,
                FixedFan: true,
                MinimumFanTargetRpm: 4200,
                MaximumFanTargetRpm: 5400,
                ModeFanRanges: Ranges(
                    (BladePerformanceMode.Balanced, 4200, 5400, 4800),
                    (BladePerformanceMode.Performance, 4800, 5400, 5100),
                    (BladePerformanceMode.Gaming, 4800, 5400, 5100)),
                Modes: ModesBalancedPerformanceGaming),

            [0x028A] = new(
                MaxFan: false,
                LowMediumHigh,
                FixedFan: true,
                MinimumFanTargetRpm: 3400,
                MaximumFanTargetRpm: 5300,
                ModeFanRanges: Ranges(
                    (BladePerformanceMode.Balanced, 3400, 5200, 4100),
                    (BladePerformanceMode.Silent, 3400, 5200, 4100),
                    (BladePerformanceMode.Custom, 4000, 5300, 4600)),
                Modes: Modes028A),

            [0x028B] = new(
                MaxFan: false,
                LowMediumHigh,
                FixedFan: true,
                MinimumFanTargetRpm: 2200,
                MaximumFanTargetRpm: 5300,
                ModeFanRanges: Ranges(
                    (BladePerformanceMode.Balanced, 2200, 3800, 3000),
                    (BladePerformanceMode.Silent, 2200, 3800, 3000),
                    (BladePerformanceMode.Custom, 4000, 5300, 4600)),
                Modes: Modes028A),

            [0x029E] = new(
                MaxFan: true,
                LowMediumHigh,
                FixedFan: true,
                MinimumFanTargetRpm: 3400,
                MaximumFanTargetRpm: 5300,
                ModeFanRanges: Ranges(
                    (BladePerformanceMode.Balanced, 3400, 5200, 4100),
                    (BladePerformanceMode.Silent, 3400, 5200, 4100),
                    (BladePerformanceMode.Custom, 4000, 5300, 4600)),
                Modes: Modes028A),

            // Blade 16 (2026) RZ09-0581 publishes the same six modes and fan ranges as 02C6.
            // Its deviceFeatures omit hasMaxFanSpeedModeEnabled, so Max Fan stays closed.
            [0x02E0] = new(
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

            // Blade 18 (2026) RZ09-0582 publishes the same fan controls as the 2025
    // Blade 18 family. Its bundle maps Gaming to wire value 0x01. Its deviceUI
            // performance deviceFeatures omit hasMaxFanSpeedModeEnabled.
            [0x02E1] = new(
                MaxFan: false,
                LowMediumHigh,
                FanCurve: true,
                FixedFan: true,
                MinimumFanTargetRpm: 3300,
                MaximumFanTargetRpm: 5400,
                ModeFanRanges: Ranges(
                    (BladePerformanceMode.Balanced, 3400, 5200, 4100),
                    (BladePerformanceMode.Silent, 3400, 5200, 4100),
                    (BladePerformanceMode.Performance, 3300, 5400, 4300),
                    (BladePerformanceMode.Gaming, 3300, 5400, 4300),
                    (BladePerformanceMode.Custom, 4000, 5300, 4600),
                    (BladePerformanceMode.BatterySaver, 3300, 5400, 4300)),
                Modes: Modes02C7),

            // Blade 15 Base (Early 2020) 1532:0255. Its middleware exposes Chroma
            // brightness, thermal, charge-limit, adapter-power and power-mode reports.
            // Fixed RPM targets stay closed because 0x0D:0x81 reports a percentage,
            // not the newer RPM layout.
            [0x0255] = new(
                MaxFan: false,
                None,
                ModeFanRanges: Ranges(
                    (BladePerformanceMode.Balanced, 4200, 5500, 4900),
                    (BladePerformanceMode.Performance, 4900, 5500, 5200),
                    (BladePerformanceMode.Gaming, 4900, 5500, 5200)),
                Modes: Modes0255),
        }.ToFrozenDictionary();

    /// <summary>Product ids with an audited model contract, for structure checks and diagnostics.</summary>
    internal static IReadOnlyCollection<ushort> AdmittedProductIds => Contracts.Keys;

    /// <summary>
    /// Temperature-driven fan curve admission. The curve is user-defined and reuses the
    /// same 0D:01 fan-target write as fixed speed; the bundle does not need to publish an
    /// OEM curve table for it. Models remain gated by their fan-target family.
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
        IReadOnlyList<BladePerformanceMode>? Modes = null,
        bool CpuPowerBoost = false);

    /// <summary>
    /// True only where the exact model has an audited write path. Unknown models stay
    /// closed so a read-only probe cannot be promoted by a stale profile.
    /// </summary>
    public static bool AllowsControlWrites(ushort productId) =>
        Contracts.TryGetValue(productId, out var contract) && contract.Writes;

    public static bool SupportsMaxFan(ushort productId) =>
        Contracts.TryGetValue(productId, out var contract) && contract.MaxFan;

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
