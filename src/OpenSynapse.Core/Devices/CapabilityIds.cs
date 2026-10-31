namespace OpenSynapse.Core.Devices;

/// <summary>
/// Capability ids shared by the manifest parser and the protocol readers. The
/// string values are part of the external manifest contract; do not rename them.
/// </summary>
public static class CapabilityIds
{
    public const string AdapterPowerGet = "adapter-power.get";
    public const string AdvancedFanModeGet = "advanced-fan-mode.get";
    public const string BatteryChemistrySet = "battery-chemistry.set";
    public const string BatteryGet = "battery.get";
    public const string BoostGet = "boost.get";
    public const string BoostSet = "boost.set";
    public const string ChargeLimitGet = "charge-limit.get";
    public const string ChargeLimitSet = "charge-limit.set";
    public const string CurrentDpiGet = "current-dpi.get";
    public const string CurrentDpiSet = "current-dpi.set";
    public const string CurrentFanRpmGet = "current-fan-rpm.get";
    public const string DisplayMuxCapabilitiesGet = "display-mux-capabilities.get";
    public const string DisplayMuxGet = "display-mux.get";
    public const string DisplayMuxSet = "display-mux.set";
    public const string LegacyDisplayMuxCapabilitiesGet = "legacy-display-mux-capabilities.get";
    public const string LegacyDisplayMuxGet = "legacy-display-mux.get";
    public const string LegacyDisplayMuxSet = "legacy-display-mux.set";
    public const string DpiStagesGet = "dpi-stages.get";
    public const string DpiStagesSet = "dpi-stages.set";
    public const string FanTargetGet = "fan-target.get";
    public const string FnKeyGet = "fn-key.get";
    public const string FnKeySet = "fn-key.set";
    public const string GamingModeGet = "gaming-mode.get";
    public const string GamingModeSet = "gaming-mode.set";
    public const string IdleTimeoutGet = "idle-timeout.get";
    public const string IdleTimeoutSet = "idle-timeout.set";
    public const string KeyboardBrightnessGet = "keyboard-brightness.get";
    public const string KeyboardBrightnessSet = "keyboard-brightness.set";
    public const string KeyboardEffectGet = "keyboard-effect.get";
    public const string KeyboardEffectSet = "keyboard-effect.set";
    public const string LogoEffectSet = "logo-effect.set";
    public const string LogoModeGet = "logo-mode.get";
    public const string LogoModeSet = "logo-mode.set";
    public const string LogoPowerGet = "logo-power.get";
    public const string LogoPowerSet = "logo-power.set";
    public const string LogoStateSet = "logo-state.set";
    public const string LowBatteryThresholdGet = "low-battery-threshold.get";
    public const string LowBatteryThresholdSet = "low-battery-threshold.set";
    public const string MaxFanGet = "max-fan.get";
    public const string MaxFanSet = "max-fan.set";
    public const string McuVersionGet = "mcu-version.get";
    public const string NativeDisplayModeGet = "native-display-mode.get";
    public const string NativeDisplayModeSet = "native-display-mode.set";
    public const string ObmAssignmentGet = "obm-assignment.get";
    public const string ObmAssignmentSet = "obm-assignment.set";
    public const string ObmButtonIdsGet = "obm-button-ids.get";
    public const string ObmMaximumProfilesGet = "obm-maximum-profiles.get";
    public const string ObmProfileCountGet = "obm-profile-count.get";
    public const string ObmProfileIdsGet = "obm-profile-ids.get";
    public const string PollingRateGet = "polling-rate.get";
    public const string PollingRateSet = "polling-rate.set";
    public const string PowerModeGet = "power-mode.get";
    public const string PowerModeSet = "power-mode.set";
    public const string SkuHardwareConfigurationGet = "sku-hardware-configuration.get";
    public const string StartupAnimationGet = "startup-animation.get";
    public const string StartupAnimationSet = "startup-animation.set";
    public const string ThermalStateGet = "thermal-state.get";
    public const string ThermalStateSet = "thermal-state.set";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
    {
        AdapterPowerGet,
        AdvancedFanModeGet,
        BatteryChemistrySet,
        BatteryGet,
        BoostGet,
        BoostSet,
        ChargeLimitGet,
        ChargeLimitSet,
        CurrentDpiGet,
        CurrentDpiSet,
        CurrentFanRpmGet,
        DisplayMuxCapabilitiesGet,
        DisplayMuxGet,
        DisplayMuxSet,
        DpiStagesGet,
        DpiStagesSet,
        FanTargetGet,
        FnKeyGet,
        FnKeySet,
        GamingModeGet,
        GamingModeSet,
        IdleTimeoutGet,
        IdleTimeoutSet,
        KeyboardBrightnessGet,
        KeyboardBrightnessSet,
        KeyboardEffectGet,
        KeyboardEffectSet,
        LegacyDisplayMuxCapabilitiesGet,
        LegacyDisplayMuxGet,
        LegacyDisplayMuxSet,
        LogoEffectSet,
        LogoModeGet,
        LogoModeSet,
        LogoPowerGet,
        LogoPowerSet,
        LogoStateSet,
        LowBatteryThresholdGet,
        LowBatteryThresholdSet,
        MaxFanGet,
        MaxFanSet,
        McuVersionGet,
        NativeDisplayModeGet,
        NativeDisplayModeSet,
        ObmAssignmentGet,
        ObmAssignmentSet,
        ObmButtonIdsGet,
        ObmMaximumProfilesGet,
        ObmProfileCountGet,
        ObmProfileIdsGet,
        PollingRateGet,
        PollingRateSet,
        PowerModeGet,
        PowerModeSet,
        SkuHardwareConfigurationGet,
        StartupAnimationGet,
        StartupAnimationSet,
        ThermalStateGet,
        ThermalStateSet,
    });
}
