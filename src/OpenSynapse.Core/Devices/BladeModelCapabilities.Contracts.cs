using System.Collections.Frozen;

namespace OpenSynapse.Core.Devices;

public static partial class BladeModelCapabilities
{
    private static readonly FrozenDictionary<ushort, Contract> Contracts;

    // The table captures the mode arrays declared in BladeModelCapabilities.Modes.cs, and
    // partial file order decides field-initializer order, so build it from the static
    // constructor where every static field is already assigned.
    static BladeModelCapabilities()
    {
        Contracts = CreateContracts();
    }

    private static FrozenDictionary<ushort, Contract> CreateContracts() =>
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
            // are the firmware's own speed policy, not a scaling error. The curve reuses
            // that admitted target write; it is not an OEM table copied from Synapse.
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
            // The preset minima are 3400/4000, but the manual target can reach the
            // model floor of 2000; keep the preset table intact and lower the
            // fixed-speed slider and the curve through the per-device floors.
            [0x029F] = new(
                MaxFan: true,
                [.. LowMediumHigh, BladeCpuBoostMode.Boost],
                FanCurve: true, FixedFan: true,
                MinimumFanTargetRpm: 3400,
                MaximumFanTargetRpm: 5500,
                MinimumManualFanTargetRpm: 2000,
                MinimumCurveFanTargetRpm: 2000,
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
            // fan stays closed; the curve reuses the fixed write and the runtime restores
            // the original mode and target when it stops.

            // Official Synapse exposes four Custom CPU levels on this model, including
            // Boost, and the fixed fan target only acts in Custom mode. Its official
            // modes run up to 5400 RPM. Maximum fan stays closed.
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

            // The remaining rzDevice25LedMatrixBladeWattage bundles publish RPM ranges and
            // speak the same RPM/100 target, but no hardware capture has confirmed the
            // write on 0233/0240/024A yet. Keep fixed RPM closed there until it does.
            // 0245 is the exception because its 0x35 readback is 5300 RPM.
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
                FixedFan: true,
                MinimumFanTargetRpm: 3400,
                MaximumFanTargetRpm: 5300,
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
                LowMediumHigh, FixedFan: true,
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

            // Razer Book 13 (2020) 1532:026A. Its bundle publishes Balanced, Battery Saver
            // and Performance only, each with a 2700..5500 RPM fan range and a 4000 RPM
            // default, and its fixed-speed writes the same RPM/100 value on 0D:01. It has
            // no Custom mode or hasCustomPerformance flag, so no CPU boost level is
            // admitted; the family-required 0D:87/07 report stays protocol-only.
            [RazerBook13ProductId] = new(
                MaxFan: false,
                None,
                FixedFan: true,
                MinimumFanTargetRpm: 2700,
                MaximumFanTargetRpm: 5500,
                ModeFanRanges: Ranges(
                    (BladePerformanceMode.Balanced, 2700, 5500, 4000),
                    (BladePerformanceMode.BatterySaver, 2700, 5500, 4000),
                    (BladePerformanceMode.Performance, 2700, 5500, 4000)),
                Modes: Modes026A),

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
            // 0x0D:0x81/0x01 is RPM/100 (not %): wire = floor(rpm/100), info raw*100. Range 4200-5500 => 42-55.
            [0x0255] = new(
                MaxFan: false,
                None,
                FixedFan: true,
                ModeFanRanges: Ranges(
                    (BladePerformanceMode.Balanced, 4200, 5500, 4900),
                    (BladePerformanceMode.Performance, 4900, 5500, 5200),
                    (BladePerformanceMode.Gaming, 4900, 5500, 5200)),
                Modes: Modes0255),
        }.ToFrozenDictionary();

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
        bool CpuPowerBoost = false,
        int? MinimumManualFanTargetRpm = null,
        int? MinimumCurveFanTargetRpm = null);
}
