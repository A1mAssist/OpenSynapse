using System.Collections.Frozen;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Macros;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidDeviceService
{
    // The Huntsman V3 Pro bundles expose only brightness and game mode in deviceUI;
    // the shared misc builders are not wired to these keyboards.
    private static readonly FrozenSet<ushort> HuntsmanV3ProMiscSettingsUnsupported =
        new ushort[] { 0x02A6, 0x02A7, 0x02B0, 0x02CF }.ToFrozenSet();

    internal static IReadOnlySet<RazerHidBackendCapability> GetCapabilities(RazerHidDeviceDefinition device)
    {
        var result = new HashSet<RazerHidBackendCapability>();
        var lightingZones = GetLightingZoneCapabilities(device).Values;
        Add(RazerHidBackendCapability.FirmwareRead, "get_firmware", "razer_chroma_standard_get_firmware_version");
        // Only the two DeathAdder V4 Pro entries declare set_device_mode, so only they
        // publish a write. DeviceModeRead stays unpublished to avoid a new UI row on
        // every mouse that merely declares the GET.
        AddAnyBuilder(RazerHidBackendCapability.DeviceModeWrite, "razer_chroma_standard_set_device_mode");
        Add(RazerHidBackendCapability.BatteryRead, "get_battery", "razer_chroma_misc_get_battery_level");
        Add(RazerHidBackendCapability.ChargingRead, "is_charging", "razer_chroma_misc_get_charging_status");
        // Laptops expose charge control through the keyboard HID path even when RazerHid
        // has no battery read. On every other device the battery controls are only real
        // when the device reports a battery or charging state.
        var hasBatteryTelemetry = device.Category == DeviceCategory.Laptop ||
            result.Contains(RazerHidBackendCapability.BatteryRead) ||
            result.Contains(RazerHidBackendCapability.ChargingRead);
        AddAnyDeclared(RazerHidBackendCapability.PollingRateRead, "get_poll_rate",
            "razer_chroma_misc_get_polling_rate", "razer_chroma_misc_get_polling_rate2");
        // A write control needs a known ladder; publishing it without one only adds a
        // disabled card and inflates the capability count.
        if (device.PollingRates.Count > 0)
        {
            AddAnyDeclared(RazerHidBackendCapability.PollingRateWrite, "set_poll_rate",
                "razer_chroma_misc_set_polling_rate", "razer_chroma_misc_set_polling_rate2");
        }
        AddAnySource(RazerHidBackendCapability.DpiRead, ["get_dpi_xy", "get_dpi_xy_byte"],
            "razer_chroma_misc_get_dpi_xy", "razer_chroma_misc_get_dpi_xy_byte");
        AddAnySource(RazerHidBackendCapability.DpiWrite, ["set_dpi_xy", "set_dpi_xy_byte"],
            "razer_chroma_misc_set_dpi_xy", "razer_chroma_misc_set_dpi_xy_byte");
        Add(RazerHidBackendCapability.DpiStagesRead, "get_dpi_stages", "razer_chroma_misc_get_dpi_stages");
        Add(RazerHidBackendCapability.DpiStagesWrite, "set_dpi_stages", "razer_chroma_misc_set_dpi_stages");
        Add(RazerHidBackendCapability.IdleTimeoutRead, "get_idle_time", "razer_chroma_misc_get_idle_time");
        Add(RazerHidBackendCapability.IdleTimeoutWrite, "set_idle_time", "razer_chroma_misc_set_idle_time");
        if (hasBatteryTelemetry)
        {
            Add(RazerHidBackendCapability.LowBatteryThresholdRead, "get_low_battery_threshold", "razer_chroma_misc_get_low_battery_threshold");
            Add(RazerHidBackendCapability.LowBatteryThresholdWrite, "set_low_battery_threshold", "razer_chroma_misc_set_low_battery_threshold");
            Add(RazerHidBackendCapability.BatteryChargeLimiterRead, "get_battery_charge_limiter", "razer_chroma_misc_get_battery_charge_limiter");
            Add(RazerHidBackendCapability.BatteryChargeLimiterWrite, "set_battery_charge_limiter", "razer_chroma_misc_set_battery_charge_limiter");
        }
        if (hasBatteryTelemetry)
        {
            Add(RazerHidBackendCapability.AutoSleepRead, "get_auto_sleep", "razer_chroma_misc_get_auto_sleep");
            Add(RazerHidBackendCapability.AutoSleepWrite, "set_auto_sleep", "razer_chroma_misc_set_auto_sleep");
        }
        if (!HuntsmanV3ProMiscSettingsUnsupported.Contains(device.ProductId))
        {
            Add(RazerHidBackendCapability.TimeToDimRead, "get_time_to_dim", "razer_chroma_misc_get_time_to_dim");
            Add(RazerHidBackendCapability.TimeToDimWrite, "set_time_to_dim", "razer_chroma_misc_set_time_to_dim");
            Add(RazerHidBackendCapability.IndicatorLedConfigurationRead, "get_indicator_led_configuration", "razer_chroma_misc_get_indicator_led_configuration");
            Add(RazerHidBackendCapability.IndicatorLedConfigurationWrite, "set_indicator_led_configuration", "razer_chroma_misc_set_indicator_led_configuration");
            Add(RazerHidBackendCapability.IndicatorLedConfigurationV2Read, "get_indicator_leds_configuration", "razer_chroma_misc_get_indicator_leds_configuration");
            Add(RazerHidBackendCapability.IndicatorLedConfigurationV2Write, "set_indicator_leds_configuration", "razer_chroma_misc_set_indicator_leds_configuration");
        }
        if (device.Category == DeviceCategory.Laptop)
        {
            Add(RazerHidBackendCapability.GpuCardDetectionRead, "get_gpu_card_detection", "razer_chroma_misc_get_gpu_card_detection");
            Add(RazerHidBackendCapability.GpuCardDetectionWrite, "set_gpu_card_detection", "razer_chroma_misc_set_gpu_card_detection");
        }
        Add(RazerHidBackendCapability.ForceChromaCtrlRead, "get_force_chroma_ctrl", "razer_chroma_misc_get_force_chroma_ctrl");
        Add(RazerHidBackendCapability.ForceChromaCtrlWrite, "set_force_chroma_ctrl", "razer_chroma_misc_set_force_chroma_ctrl");
        Add(RazerHidBackendCapability.ProfileDebounceTimeRead, "get_profile_debounce_time", "razer_chroma_misc_get_profile_debounce_time");
        Add(RazerHidBackendCapability.ProfileDebounceTimeWrite, "set_profile_debounce_time", "razer_chroma_misc_set_profile_debounce_time");
        Add(RazerHidBackendCapability.ChromaTapRead, "get_chroma_tap", "razer_chroma_misc_get_chroma_tap");
        Add(RazerHidBackendCapability.ChromaTapWrite, "set_chroma_tap", "razer_chroma_misc_set_chroma_tap");
        if (hasBatteryTelemetry)
        {
            Add(RazerHidBackendCapability.BatteryDischargeCurveRead, "get_battery_discharge_curve", "razer_chroma_misc_get_battery_discharge_curve");
            Add(RazerHidBackendCapability.BatteryDischargeCurveWrite, "set_battery_discharge_curve", "razer_chroma_misc_set_battery_discharge_curve");
        }
        Add(RazerHidBackendCapability.MacroStorageInfoRead, "get_macro_storage_info", "razer_chroma_misc_get_macro_storage_info");
        if (RazerOnboardMacroDevices.Products.Contains(device.ProductId))
        {
            result.Add(RazerHidBackendCapability.MacroStorageDataWrite);
            result.Add(RazerHidBackendCapability.MacroStorageNameWrite);
        }
        // The shared middleware chunks carry the LK scroll-wheel command for every mouse,
        // but only products with a real scroll-wheel settings block answer it. The two
        // supported products expose Smart Reel; the rest only inherit the shared builder.
        if (device.HasSourceCapability("get_scroll_smart_reel") ||
            device.HasSourceCapability("set_scroll_smart_reel"))
        {
            Add(RazerHidBackendCapability.LkScrollWheelModeRead, "get_lk_scroll_wheel_mode", "razer_chroma_misc_get_lk_scroll_wheel_mode");
            Add(RazerHidBackendCapability.LkScrollWheelModeWrite, "set_lk_scroll_wheel_mode", "razer_chroma_misc_set_lk_scroll_wheel_mode");
        }
        if (lightingZones.Any(zone => zone.CanReadBrightness)) result.Add(RazerHidBackendCapability.BrightnessRead);
        if (lightingZones.Any(zone => zone.CanWriteBrightness)) result.Add(RazerHidBackendCapability.BrightnessWrite);
        if (lightingZones.Any(zone => zone.CanReadState)) result.Add(RazerHidBackendCapability.LedStateRead);
        if (lightingZones.Any(zone => zone.CanWriteState)) result.Add(RazerHidBackendCapability.LedStateWrite);
        if (lightingZones.Any(zone => zone.CanReadEffect)) result.Add(RazerHidBackendCapability.LedEffectRead);
        if (lightingZones.Any(zone => zone.CanWriteEffect)) result.Add(RazerHidBackendCapability.LedEffectWrite);
        if (lightingZones.Any(zone => zone.CanWriteColor)) result.Add(RazerHidBackendCapability.LedColorWrite);
        if (lightingZones.Any(zone => zone.CanWriteBlinking)) result.Add(RazerHidBackendCapability.LedBlinkingWrite);
        if (lightingZones.Any(zone => zone.LightingEffects.Count > 0))
            result.Add(RazerHidBackendCapability.LightingEffectWrite);
        if (device.MatrixDimensions is not null && device.HasSourceCapability("set_key_row"))
            AddAnyBuilder(RazerHidBackendCapability.MatrixFrameWrite,
            "razer_chroma_standard_matrix_set_custom_frame",
            "razer_chroma_extended_matrix_set_custom_frame",
            "razer_chroma_extended_matrix_set_custom_frame2");
        Add(RazerHidBackendCapability.ReactiveTriggerWrite, "trigger_reactive_effect", "razer_chroma_misc_matrix_reactive_trigger");
        Add(RazerHidBackendCapability.ScrollModeRead, "get_scroll_mode", "razer_chroma_misc_get_scroll_mode");
        Add(RazerHidBackendCapability.ScrollModeWrite, "set_scroll_mode", "razer_chroma_misc_set_scroll_mode");
        Add(RazerHidBackendCapability.ScrollAccelerationRead, "get_scroll_acceleration", "razer_chroma_misc_get_scroll_acceleration");
        Add(RazerHidBackendCapability.ScrollAccelerationWrite, "set_scroll_acceleration", "razer_chroma_misc_set_scroll_acceleration");
        Add(RazerHidBackendCapability.SmartReelRead, "get_scroll_smart_reel", "razer_chroma_misc_get_scroll_smart_reel");
        Add(RazerHidBackendCapability.SmartReelWrite, "set_scroll_smart_reel", "razer_chroma_misc_set_scroll_smart_reel");
        Add(RazerHidBackendCapability.SmartReelThresholdRead, "get_scroll_smart_reel_threshold", "razer_chroma_misc_get_scroll_smart_reel_threshold");
        Add(RazerHidBackendCapability.SmartReelThresholdWrite, "set_scroll_smart_reel_threshold", "razer_chroma_misc_set_scroll_smart_reel_threshold");
        Add(RazerHidBackendCapability.KeyswitchOptimizationRead, "get_keyswitch_optimization", "razer_chroma_misc_get_keyswitch_optimization");
        if (device.HasSourceCapability("set_keyswitch_optimization") &&
            Has("razer_chroma_misc_set_keyswitch_optimization_command1") &&
            Has("razer_chroma_misc_set_keyswitch_optimization_command2"))
            result.Add(RazerHidBackendCapability.KeyswitchOptimizationWrite);
        Add(RazerHidBackendCapability.HyperPollingIndicatorWrite, "set_hyperpolling_wireless_dongle_indicator_led_mode",
            "razer_chroma_misc_set_hyperpolling_wireless_dongle_indicator_led_mode");
        if (device.HasSourceCapability("set_hyperpolling_wireless_dongle_pair") &&
            Has("razer_chroma_misc_set_hyperpolling_wireless_dongle_pair_step1") &&
            Has("razer_chroma_misc_set_hyperpolling_wireless_dongle_pair_step2"))
            result.Add(RazerHidBackendCapability.HyperPollingPairWrite);
        Add(RazerHidBackendCapability.HyperPollingUnpairWrite, "set_hyperpolling_wireless_dongle_unpair",
            "razer_chroma_misc_set_hyperpolling_wireless_dongle_unpair");
        return result.ToFrozenSet();

        bool Has(string builder) => device.Transactions.ContainsKey(builder) || device.CommandSequences.ContainsKey(builder);
        void Add(RazerHidBackendCapability capability, string sourceCapability, string builder)
        {
            if (device.HasSourceCapability(sourceCapability) && Has(builder)) result.Add(capability);
        }
        void AddAnyDeclared(RazerHidBackendCapability capability, string sourceCapability, params string[] builders)
        {
            if (device.HasSourceCapability(sourceCapability) && builders.Any(Has)) result.Add(capability);
        }
        void AddAnySource(RazerHidBackendCapability capability, string[] sourceCapabilities, params string[] builders)
        {
            if (sourceCapabilities.Any(device.HasSourceCapability) && builders.Any(Has)) result.Add(capability);
        }
        void AddAnyBuilder(RazerHidBackendCapability capability, params string[] builders)
        {
            if (builders.Any(Has)) result.Add(capability);
        }
    }
}
