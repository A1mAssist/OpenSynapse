using OpenSynapse.Core.Devices;

namespace OpenSynapse.Windows.Devices;

internal sealed partial class RazerDeviceRegistry
{
    private static readonly IReadOnlyDictionary<string, CapabilityContract> Blade710Capabilities =
        new Dictionary<string, CapabilityContract>(StringComparer.Ordinal)
        {
                [CapabilityIds.KeyboardBrightnessGet] = new(0x02, 0x0E, 0x84, "0100"),
                [CapabilityIds.KeyboardBrightnessSet] = new(0x02, 0x0E, 0x04, ""),
                [CapabilityIds.ThermalStateGet] = new(0x04, 0x0D, 0x82, ""),
                [CapabilityIds.ThermalStateSet] = new(0x04, 0x0D, 0x02, ""),
                [CapabilityIds.FanTargetGet] = new(0x03, 0x0D, 0x81, ""),
                [CapabilityIds.CurrentFanRpmGet] = new(0x03, 0x0D, 0x88, ""),
                [CapabilityIds.AdvancedFanModeGet] = new(0x03, 0x0D, 0x87, ""),
                [CapabilityIds.BoostGet] = new(0x03, 0x0D, 0x87, ""),
                [CapabilityIds.BoostSet] = new(0x03, 0x0D, 0x07, ""),
                [CapabilityIds.AdapterPowerGet] = new(0x02, 0x07, 0x8C, "0000", true, true),
                [CapabilityIds.AutoSleepGet] = new(0x02, 0x07, 0x88, "00"),
                [CapabilityIds.AutoSleepSet] = new(0x02, 0x07, 0x08, ""),
                [CapabilityIds.ChargeLimitGet] = new(0x01, 0x07, 0x92, "00", true),
                [CapabilityIds.ChargeLimitSet] = new(0x01, 0x07, 0x12, ""),
                [CapabilityIds.GpuCardDetectionGet] = new(0x03, 0x03, 0xA0, "00"),
                [CapabilityIds.GpuCardDetectionSet] = new(0x03, 0x03, 0x20, ""),
                [CapabilityIds.IndicatorLedGet] = new(0x01, 0x07, 0x90, "00"),
                [CapabilityIds.IndicatorLedSet] = new(0x01, 0x07, 0x10, ""),
                [CapabilityIds.PowerModeGet] = new(0x01, 0x07, 0x8F, "00", true),
                [CapabilityIds.PowerModeSet] = new(0x01, 0x07, 0x0F, ""),
                [CapabilityIds.TimeToDimGet] = new(0x05, 0x07, 0x8B, "00"),
                [CapabilityIds.TimeToDimSet] = new(0x05, 0x07, 0x0B, ""),
                [CapabilityIds.GamingModeSet] = new(0x04, 0x00, 0x08, "", AllowZeroTransactionId: true),
                [CapabilityIds.FnKeyGet] = new(0x02, 0x02, 0x86, "0100", AllowZeroTransactionId: true),
                [CapabilityIds.McuVersionGet] = new(0x04, 0x00, 0x87, "00000000", AllowZeroTransactionId: true),
                [CapabilityIds.FnKeySet] = new(0x02, 0x02, 0x06, "", AllowZeroTransactionId: true),
                [CapabilityIds.StartupAnimationGet] = new(0x01, 0x0F, 0x98, "00"),
                [CapabilityIds.StartupAnimationSet] = new(0x02, 0x0F, 0x18, ""),
                [CapabilityIds.NativeDisplayModeGet] = new(0x01, 0x0D, 0x8E, "00", true),
                [CapabilityIds.NativeDisplayModeSet] = new(0x01, 0x0D, 0x0E, ""),
                [CapabilityIds.SkuHardwareConfigurationGet] = new(0x01, 0x0D, 0x8F, "00", true),
                [CapabilityIds.LogoPowerGet] = new(0x03, 0x03, 0x80, "010400"),
                [CapabilityIds.LogoPowerSet] = new(0x03, 0x03, 0x00, ""),
                [CapabilityIds.LogoModeGet] = new(0x03, 0x03, 0x82, "010400"),
                [CapabilityIds.LogoModeSet] = new(0x03, 0x03, 0x02, ""),
                [CapabilityIds.LogoEffectSet] = new(0x03, 0x03, 0x02, "", AllowZeroTransactionId: true),
                [CapabilityIds.LogoStateSet] = new(0x03, 0x03, 0x00, "", AllowZeroTransactionId: true),
                [CapabilityIds.DisplayMuxGet] = new(0x02, 0x0D, 0x89, "0000"),
                [CapabilityIds.DisplayMuxSet] = new(0x02, 0x0D, 0x09, ""),
                [CapabilityIds.DisplayMuxCapabilitiesGet] = new(0x02, 0x0D, 0x8A, "0000"),
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, CapabilityContract>> FamilyCapabilities =
        new Dictionary<string, IReadOnlyDictionary<string, CapabilityContract>>(StringComparer.Ordinal)
        {
            ["blade-710"] = Blade710Capabilities,
            // Later Blade middleware moved keyboard brightness from 0E:84/04 to the
            // 03:83/03 Chroma region command. All other reports remain 710-compatible.
            ["blade-710-chroma"] = new Dictionary<string, CapabilityContract>(Blade710Capabilities, StringComparer.Ordinal)
            {
                [CapabilityIds.KeyboardBrightnessGet] = new(0x03, 0x03, 0x83, "010500", true),
                [CapabilityIds.KeyboardBrightnessSet] = new(0x03, 0x03, 0x03, "", true),
            },
            // Blade 16 (2023) RZ09-0483 shares the 710 thermal, power-mode and charge
            // commands but has its own lighting matrix report, so it stays a separate family.
            ["blade-671"] = new Dictionary<string, CapabilityContract>(StringComparer.Ordinal)
            {
                [CapabilityIds.ThermalStateGet] = new(0x04, 0x0D, 0x82, ""),
                [CapabilityIds.ThermalStateSet] = new(0x04, 0x0D, 0x02, ""),
                [CapabilityIds.FanTargetGet] = new(0x03, 0x0D, 0x81, ""),
                [CapabilityIds.CurrentFanRpmGet] = new(0x03, 0x0D, 0x88, ""),
                [CapabilityIds.BoostGet] = new(0x03, 0x0D, 0x87, ""),
                [CapabilityIds.BoostSet] = new(0x03, 0x0D, 0x07, ""),
                [CapabilityIds.AdapterPowerGet] = new(0x02, 0x07, 0x8C, "0000", true, true),
                [CapabilityIds.AutoSleepGet] = new(0x02, 0x07, 0x88, "00"),
                [CapabilityIds.AutoSleepSet] = new(0x02, 0x07, 0x08, ""),
                [CapabilityIds.ChargeLimitGet] = new(0x01, 0x07, 0x92, "00", true),
                [CapabilityIds.ChargeLimitSet] = new(0x01, 0x07, 0x12, ""),
                [CapabilityIds.GpuCardDetectionGet] = new(0x03, 0x03, 0xA0, "00"),
                [CapabilityIds.GpuCardDetectionSet] = new(0x03, 0x03, 0x20, ""),
                [CapabilityIds.IndicatorLedGet] = new(0x01, 0x07, 0x90, "00"),
                [CapabilityIds.IndicatorLedSet] = new(0x01, 0x07, 0x10, ""),
                [CapabilityIds.PowerModeGet] = new(0x01, 0x07, 0x8F, "00", true),
                [CapabilityIds.PowerModeSet] = new(0x01, 0x07, 0x0F, ""),
                [CapabilityIds.TimeToDimGet] = new(0x05, 0x07, 0x8B, "00"),
                [CapabilityIds.TimeToDimSet] = new(0x05, 0x07, 0x0B, ""),
                [CapabilityIds.SkuHardwareConfigurationGet] = new(0x01, 0x0D, 0x8F, "00", true),
                [CapabilityIds.GamingModeGet] = new(0x04, 0x00, 0x88, "", AllowZeroTransactionId: true),
                [CapabilityIds.GamingModeSet] = new(0x04, 0x00, 0x08, "", AllowZeroTransactionId: true),
                [CapabilityIds.StartupAnimationGet] = new(0x01, 0x0F, 0x98, "00"),
                [CapabilityIds.StartupAnimationSet] = new(0x02, 0x0F, 0x18, ""),
                [CapabilityIds.KeyboardBrightnessGet] = new(0x03, 0x03, 0x83, "010500", true),
                [CapabilityIds.KeyboardBrightnessSet] = new(0x03, 0x03, 0x03, "", true),
                [CapabilityIds.KeyboardEffectGet] = new(0x03, 0x0F, 0x82, "010500", true),
                [CapabilityIds.KeyboardEffectSet] = new(0x06, 0x0F, 0x02, "", true),
                [CapabilityIds.LogoPowerGet] = new(0x03, 0x03, 0x80, "000400"),
                [CapabilityIds.LogoPowerSet] = new(0x03, 0x03, 0x00, ""),
                [CapabilityIds.LogoModeGet] = new(0x03, 0x03, 0x82, "000400"),
                [CapabilityIds.LogoModeSet] = new(0x03, 0x03, 0x02, ""),
                [CapabilityIds.LogoEffectSet] = new(0x03, 0x03, 0x02, ""),
                [CapabilityIds.LogoStateSet] = new(0x03, 0x03, 0x00, ""),
                [CapabilityIds.DisplayMuxGet] = new(0x02, 0x0D, 0x89, "0000"),
                [CapabilityIds.DisplayMuxSet] = new(0x02, 0x0D, 0x09, ""),
                [CapabilityIds.DisplayMuxCapabilitiesGet] = new(0x02, 0x0D, 0x8A, "0000"),
                [CapabilityIds.LegacyDisplayMuxGet] = new(0x03, 0x0D, 0x90, "000000"),
                [CapabilityIds.LegacyDisplayMuxSet] = new(0x03, 0x0D, 0x10, ""),
                [CapabilityIds.LegacyDisplayMuxCapabilitiesGet] = new(0x02, 0x0D, 0x8B, "0000"),
                [CapabilityIds.NativeDisplayModeGet] = new(0x01, 0x0D, 0x8E, "00", true),
                [CapabilityIds.NativeDisplayModeSet] = new(0x01, 0x0D, 0x0E, ""),
            },
            // 0255 answers only 0D thermal, Chroma brightness and 07:92 charge; its
            // middleware declares no hasGPUmode or SKU report and the other reports fail.
            ["blade-597"] = new Dictionary<string, CapabilityContract>(StringComparer.Ordinal)
            {
                [CapabilityIds.ThermalStateGet] = new(0x04, 0x0D, 0x82, ""),
                [CapabilityIds.ThermalStateSet] = new(0x04, 0x0D, 0x02, ""),
                [CapabilityIds.BoostGet] = new(0x03, 0x0D, 0x87, ""),
                [CapabilityIds.BoostSet] = new(0x03, 0x0D, 0x07, ""),
                [CapabilityIds.AdapterPowerGet] = new(0x02, 0x07, 0x8C, "0000", true, true),
                [CapabilityIds.AutoSleepGet] = new(0x02, 0x07, 0x88, "00"),
                [CapabilityIds.AutoSleepSet] = new(0x02, 0x07, 0x08, ""),
                [CapabilityIds.TimeToDimGet] = new(0x05, 0x07, 0x8B, "00"),
                [CapabilityIds.TimeToDimSet] = new(0x05, 0x07, 0x0B, ""),
                [CapabilityIds.IndicatorLedGet] = new(0x01, 0x07, 0x90, "00"),
                [CapabilityIds.IndicatorLedSet] = new(0x01, 0x07, 0x10, ""),
                [CapabilityIds.GpuCardDetectionGet] = new(0x03, 0x03, 0xA0, "00"),
                [CapabilityIds.GpuCardDetectionSet] = new(0x03, 0x03, 0x20, ""),
                [CapabilityIds.KeyboardBrightnessGet] = new(0x03, 0x0F, 0x84, "0105", true),
                [CapabilityIds.KeyboardBrightnessSet] = new(0x03, 0x0F, 0x04, ""),
                [CapabilityIds.ChargeLimitGet] = new(0x01, 0x07, 0x92, "00", true),
                [CapabilityIds.ChargeLimitSet] = new(0x01, 0x07, 0x12, ""),
            },
            ["viper-184"] = new Dictionary<string, CapabilityContract>(StringComparer.Ordinal)
            {
                [CapabilityIds.BatteryGet] = new(0x02, 0x07, 0x80, ""),
                [CapabilityIds.PollingRateGet] = new(0x01, 0x00, 0x85, ""),
                [CapabilityIds.PollingRateSet] = new(0x01, 0x00, 0x05, ""),
                [CapabilityIds.CurrentDpiGet] = new(0x07, 0x04, 0x85, "00"),
                [CapabilityIds.CurrentDpiSet] = new(0x07, 0x04, 0x05, ""),
                [CapabilityIds.IdleTimeoutGet] = new(0x02, 0x07, 0x83, ""),
                [CapabilityIds.IdleTimeoutSet] = new(0x02, 0x07, 0x03, ""),
                [CapabilityIds.BatteryChemistrySet] = new(0x01, 0x07, 0x14, ""),
                [CapabilityIds.DpiStagesGet] = new(0x50, 0x04, 0x86, "01"),
                [CapabilityIds.DpiStagesSet] = new(0x50, 0x04, 0x06, ""),
                [CapabilityIds.LowBatteryThresholdGet] = new(0x01, 0x07, 0x81, ""),
                [CapabilityIds.LowBatteryThresholdSet] = new(0x01, 0x07, 0x01, ""),
                [CapabilityIds.ObmMaximumProfilesGet] = new(0x01, 0x05, 0x8A, ""),
                [CapabilityIds.ObmProfileCountGet] = new(0x01, 0x05, 0x80, ""),
                [CapabilityIds.ObmProfileIdsGet] = new(0x50, 0x05, 0x81, ""),
                [CapabilityIds.ObmButtonIdsGet] = new(0x50, 0x02, 0x84, ""),
                [CapabilityIds.ObmAssignmentGet] = new(0x50, 0x02, 0x8C, ""),
                [CapabilityIds.ObmAssignmentSet] = new(0x50, 0x02, 0x0C, ""),
            },
            // Future RazerHid devices need an audited built-in manifest and
            // verified Windows transport before any capability is admitted.
            ["openrazer-standard"] = new Dictionary<string, CapabilityContract>(StringComparer.Ordinal),
        };
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> FamilyRequiredCapabilities =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["blade-710"] = FamilyCapabilities["blade-710"].Keys
                .Where(capability => capability is not (CapabilityIds.AutoSleepGet or CapabilityIds.AutoSleepSet or
                    CapabilityIds.TimeToDimGet or CapabilityIds.TimeToDimSet or
                    CapabilityIds.IndicatorLedGet or CapabilityIds.IndicatorLedSet or
                    CapabilityIds.GpuCardDetectionGet or CapabilityIds.GpuCardDetectionSet))
                .ToHashSet(StringComparer.Ordinal),
            // The Blade 18 (2024) firmware rejects 0F:98/18, even though the family
            // shares those reports. Keep them optional; the other manifests still own them.
            ["blade-710-chroma"] = FamilyCapabilities["blade-710-chroma"].Keys
                .Where(capability => capability is not (CapabilityIds.StartupAnimationGet or CapabilityIds.StartupAnimationSet or
                    CapabilityIds.AutoSleepGet or CapabilityIds.AutoSleepSet or
                    CapabilityIds.TimeToDimGet or CapabilityIds.TimeToDimSet or
                    CapabilityIds.IndicatorLedGet or CapabilityIds.IndicatorLedSet or
                    CapabilityIds.GpuCardDetectionGet or CapabilityIds.GpuCardDetectionSet))
                .ToHashSet(StringComparer.Ordinal),
            // Per-model reports are optional: 0270 rejects gaming-mode 00:08;
            // 024C rejects Power Mode Control with 0x05.
            ["blade-671"] = FamilyCapabilities["blade-671"].Keys
                .Where(capability => capability is not (CapabilityIds.AdapterPowerGet or
                    CapabilityIds.PowerModeGet or CapabilityIds.PowerModeSet or
                    CapabilityIds.LogoPowerGet or CapabilityIds.LogoPowerSet or
                    CapabilityIds.LogoModeGet or CapabilityIds.LogoModeSet or
                    CapabilityIds.LogoEffectSet or CapabilityIds.LogoStateSet or
                    CapabilityIds.DisplayMuxGet or CapabilityIds.DisplayMuxSet or CapabilityIds.DisplayMuxCapabilitiesGet or
                    CapabilityIds.LegacyDisplayMuxGet or CapabilityIds.LegacyDisplayMuxSet or CapabilityIds.LegacyDisplayMuxCapabilitiesGet or
                    CapabilityIds.SkuHardwareConfigurationGet or
                    CapabilityIds.NativeDisplayModeGet or CapabilityIds.NativeDisplayModeSet or CapabilityIds.GamingModeGet or CapabilityIds.GamingModeSet or CapabilityIds.StartupAnimationGet or CapabilityIds.StartupAnimationSet or
                    CapabilityIds.AutoSleepGet or CapabilityIds.AutoSleepSet or
                    CapabilityIds.TimeToDimGet or CapabilityIds.TimeToDimSet or
                    CapabilityIds.IndicatorLedGet or CapabilityIds.IndicatorLedSet or
                    CapabilityIds.GpuCardDetectionGet or CapabilityIds.GpuCardDetectionSet))
                .ToHashSet(StringComparer.Ordinal),
            ["blade-597"] = FamilyCapabilities["blade-597"].Keys
                .Where(capability => capability is not (CapabilityIds.BoostGet or CapabilityIds.BoostSet or
                    CapabilityIds.AdapterPowerGet or
                    CapabilityIds.AutoSleepGet or CapabilityIds.AutoSleepSet or
                    CapabilityIds.TimeToDimGet or CapabilityIds.TimeToDimSet or
                    CapabilityIds.IndicatorLedGet or CapabilityIds.IndicatorLedSet or
                    CapabilityIds.GpuCardDetectionGet or CapabilityIds.GpuCardDetectionSet))
                .ToHashSet(StringComparer.Ordinal),
            ["viper-184"] = FamilyCapabilities["viper-184"].Keys.ToHashSet(StringComparer.Ordinal),
            ["openrazer-standard"] = new HashSet<string>(StringComparer.Ordinal),
        };
}
