namespace OpenSynapse.Core.Profiles;

public enum ProfileSummaryScope
{
    Global,
    Device,
    PluggedIn,
    LowPowerAdapter,
    OnBattery,
}

public sealed record ProfileSummaryItem(
    string LabelKey,
    ProfileSummaryScope Scope,
    IReadOnlyList<object?> Arguments);

public sealed record ProfileSummaryGroup(
    string TitleKey,
    IReadOnlyList<ProfileSummaryItem> Items);

/// <summary>
/// Describes what a profile contains without reading hardware. Values from the
/// global, device and power layers are listed separately so the page never
/// presents a layer override as the currently active value.
/// </summary>
public static class ProfileSummaryBuilder
{
    public static IReadOnlyList<ProfileSummaryGroup> Build(ProfileDocument document) =>
        Build(document, document.GetActiveProfileDefinition());

    public static IReadOnlyList<ProfileSummaryGroup> Build(
        ProfileDocument document,
        ProfileDefinition profile)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(profile);

        var blade = new GroupBuilder("ProfileSummary_Group_Blade");
        var mouse = new GroupBuilder("ProfileSummary_Group_Mouse");
        var keyboard = new GroupBuilder("ProfileSummary_Group_Keyboard");
        var headset = new GroupBuilder("ProfileSummary_Group_Headset");
        var coolingPad = new GroupBuilder("ProfileSummary_Group_CoolingPad");
        var gamepad = new GroupBuilder("ProfileSummary_Group_Gamepad");
        var mixer = new GroupBuilder("ProfileSummary_Group_Mixer");
        var kiyo = new GroupBuilder("ProfileSummary_Group_Kiyo");
        var display = new GroupBuilder("ProfileSummary_Group_Display");
        var lighting = new GroupBuilder("ProfileSummary_Group_Lighting");

        AddLayer(
            profile.Global,
            ProfileSummaryScope.Global,
            blade,
            mouse,
            keyboard,
            headset,
            coolingPad,
            gamepad,
            mixer,
            kiyo,
            display,
            lighting);
        foreach (var (deviceKey, settings) in profile.Devices
                     .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (settings is not null)
            {
                AddDeviceLayer(
                    deviceKey,
                    settings,
                    blade,
                    mouse,
                    keyboard,
                    headset,
                    coolingPad,
                    gamepad,
                    mixer,
                    kiyo,
                    display,
                    lighting);
            }
        }

        AddPowerLayer(profile.PluggedIn, ProfileSummaryScope.PluggedIn, blade, mouse, keyboard,
            headset, coolingPad, gamepad, mixer, kiyo, display, lighting);
        AddPowerLayer(profile.LowPowerAdapter, ProfileSummaryScope.LowPowerAdapter, blade, mouse, keyboard,
            headset, coolingPad, gamepad, mixer, kiyo, display, lighting);
        AddPowerLayer(profile.OnBattery, ProfileSummaryScope.OnBattery, blade, mouse, keyboard,
            headset, coolingPad, gamepad, mixer, kiyo, display, lighting);

        return new[]
            {
                blade, mouse, keyboard, headset, coolingPad, gamepad, mixer, kiyo, display, lighting,
            }
            .Where(group => group.Items.Count > 0)
            .Select(group => group.ToGroup())
            .ToArray();
    }

    private static void AddLayer(
        ProfileSettings settings,
        ProfileSummaryScope scope,
        GroupBuilder blade,
        GroupBuilder mouse,
        GroupBuilder keyboard,
        GroupBuilder headset,
        GroupBuilder coolingPad,
        GroupBuilder gamepad,
        GroupBuilder mixer,
        GroupBuilder kiyo,
        GroupBuilder display,
        GroupBuilder lighting)
    {
        AddBlade(blade, settings.Blade, scope);
        AddMouse(mouse, settings.Viper, scope);
        AddKeyboard(keyboard, settings.Keyboard, scope);
        AddHeadset(headset, settings.Headset, scope);
        AddCoolingPad(coolingPad, settings.CoolingPad, scope);
        AddGamepad(gamepad, settings.Gamepad, scope);
        AddMixer(mixer, settings.Mixer, scope);
        AddKiyo(kiyo, settings.Kiyo, scope);
        AddDisplay(display, settings.Display, scope);
        AddLighting(
            lighting,
            settings.Lighting,
            settings.LightingZones,
            scope,
            deviceKey: null,
            null,
            null);
    }

    private static void AddDeviceLayer(
        string deviceKey,
        DeviceProfileSettings settings,
        GroupBuilder blade,
        GroupBuilder mouse,
        GroupBuilder keyboard,
        GroupBuilder headset,
        GroupBuilder coolingPad,
        GroupBuilder gamepad,
        GroupBuilder mixer,
        GroupBuilder kiyo,
        GroupBuilder display,
        GroupBuilder lighting)
    {
        AddBlade(blade, settings.Blade, ProfileSummaryScope.Device);
        AddMouse(mouse, settings.Viper, ProfileSummaryScope.Device);
        AddKeyboard(keyboard, settings.Keyboard, ProfileSummaryScope.Device);
        AddHeadset(headset, settings.Headset, ProfileSummaryScope.Device);
        AddCoolingPad(coolingPad, settings.CoolingPad, ProfileSummaryScope.Device);
        AddGamepad(gamepad, settings.Gamepad, ProfileSummaryScope.Device);
        AddMixer(mixer, settings.Mixer, ProfileSummaryScope.Device);
        AddKiyo(kiyo, settings.Kiyo, ProfileSummaryScope.Device);
        AddDisplay(display, settings.Display, ProfileSummaryScope.Device);
        AddLighting(
            lighting,
            settings.Lighting,
            settings.LightingZones,
            ProfileSummaryScope.Device,
            deviceKey,
            settings.LightingEnabled is not null
                ? "ProfileSummary_Item_LightingSwitch"
                : null,
            settings.ChromaOverrideEnabled is not null
                ? "ProfileSummary_Item_ChromaOverride"
                : null);
    }

    private static void AddPowerLayer(
        PowerProfileOverrides? overrides,
        ProfileSummaryScope scope,
        GroupBuilder blade,
        GroupBuilder mouse,
        GroupBuilder keyboard,
        GroupBuilder headset,
        GroupBuilder coolingPad,
        GroupBuilder gamepad,
        GroupBuilder mixer,
        GroupBuilder kiyo,
        GroupBuilder display,
        GroupBuilder lighting)
    {
        if (overrides is null)
        {
            return;
        }

        AddBlade(blade, overrides.Blade, scope);
        AddMouse(mouse, overrides.Viper, scope);
        AddKeyboard(keyboard, overrides.Keyboard, scope);
        AddHeadset(headset, overrides.Headset, scope);
        AddCoolingPad(coolingPad, overrides.CoolingPad, scope);
        AddGamepad(gamepad, overrides.Gamepad, scope);
        AddMixer(mixer, overrides.Mixer, scope);
        AddKiyo(kiyo, overrides.Kiyo, scope);
        AddDisplay(display, overrides.Display, scope);
        AddLighting(
            lighting,
            overrides.Lighting,
            overrides.LightingZones,
            scope,
            deviceKey: null,
            null,
            null);
        foreach (var (deviceKey, profile) in overrides.RazerHidLighting
                     .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (HasLighting(profile))
            {
                lighting.Add("ProfileSummary_Item_DeviceLighting", scope, deviceKey);
            }
        }
        foreach (var (deviceKey, zones) in overrides.RazerHidLightingZones
                     .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            AddZones(lighting, zones, scope, deviceKey);
        }
    }

    private static void AddBlade(
        GroupBuilder group,
        BladeProfileSettings? settings,
        ProfileSummaryScope scope)
    {
        if (settings is null)
        {
            return;
        }

        AddIf(group, settings.KeyboardBrightness, "ProfileSummary_Item_BladeKeyboardBrightness", scope);
        AddIf(group, settings.PerformanceMode, "ProfileSummary_Item_BladePerformanceMode", scope);
        AddIf(group, settings.FanMode, "ProfileSummary_Item_FanMode", scope);
        AddIf(group, settings.FanTargetRpm, "ProfileSummary_Item_FanTarget", scope);
        AddIf(group, settings.FanCurve, "ProfileSummary_Item_FanCurve", scope);
        AddIf(group, settings.ChargeLimitPercent, "ProfileSummary_Item_ChargeLimit", scope);
        AddIf(group, settings.RefreshRateHertz, "ProfileSummary_Item_RefreshRate", scope);
        AddIf(group, settings.MaxFanMode, "ProfileSummary_Item_MaxFanMode", scope);
        AddIf(group, settings.CpuBoostMode, "ProfileSummary_Item_CpuBoost", scope);
        AddIf(group, settings.GpuBoostMode, "ProfileSummary_Item_GpuBoost", scope);
        AddIf(group, settings.LogoMode, "ProfileSummary_Item_BladeLogo", scope);
        AddIf(group, settings.GamingModeEnabled, "ProfileSummary_Item_GamingMode", scope);
        if (settings.SnapTapEnabled == true)
        {
            group.Add("ProfileSummary_Item_SnapTap", scope);
        }
        if (!string.IsNullOrWhiteSpace(settings.MappingPreset) &&
            !StringComparer.Ordinal.Equals(
                settings.MappingPreset,
                BladeProfileSettings.Product710DefaultMappingPreset))
        {
            group.Add("ProfileSummary_Item_MappingPreset", scope);
        }
        AddIf(group, settings.LightingEnabled, "ProfileSummary_Item_LightingSwitch", scope);
        AddIf(group, settings.ChromaOverrideEnabled, "ProfileSummary_Item_ChromaOverride", scope);
        AddIf(group, settings.DisplayBrightnessPercent, "ProfileSummary_Item_DisplayBrightness", scope);
        AddIf(group, settings.StartupAnimationEnabled, "ProfileSummary_Item_StartupAnimation", scope);
        AddIf(group, settings.AutoSleepSeconds, "ProfileSummary_Item_AutoSleep", scope);
        AddIf(group, settings.TimeToDimSeconds, "ProfileSummary_Item_TimeToDim", scope);
        AddIf(group, settings.IndicatorLedEnabled, "ProfileSummary_Item_IndicatorLed", scope);
        AddIf(group, settings.GpuCardDetectionEnabled, "ProfileSummary_Item_GpuCardDetection", scope);
        AddIf(group, settings.NativeDisplayModeEnabled, "ProfileSummary_Item_NativeDisplayMode", scope);
        AddIf(group, settings.LocalDimmingEnabled, "ProfileSummary_Item_LocalDimming", scope);
        AddIf(group, settings.CpuPowerBoostMode, "ProfileSummary_Item_CpuPowerBoost", scope);
        AddIf(group, settings.GpuCoreClockOffsetMhz, "ProfileSummary_Item_GpuClockOffset", scope);
        AddIf(group, settings.GpuMemoryClockOffsetMhz, "ProfileSummary_Item_GpuClockOffset", scope);
        AddIf(group, settings.GpuCoreClockLimitMhz, "ProfileSummary_Item_GpuClockOffset", scope);
        AddIf(group, settings.CpuCurveOffset, "ProfileSummary_Item_CpuCurve", scope);
        AddIf(group, settings.CpuPowerLimitWatts, "ProfileSummary_Item_CpuPowerLimit", scope);
    }

    private static void AddMouse(
        GroupBuilder group,
        MouseProfileSettings? settings,
        ProfileSummaryScope scope)
    {
        if (settings is null)
        {
            return;
        }

        AddIf(group, settings.DpiX ?? settings.DpiY, "ProfileSummary_Item_Dpi", scope);
        AddIf(group, settings.DpiStages, "ProfileSummary_Item_DpiStages", scope);
        AddIf(group, settings.PollingRateHertz, "ProfileSummary_Item_PollingRate", scope);
        AddIf(group, settings.IdleSeconds, "ProfileSummary_Item_IdleTimeout", scope);
        AddIf(group, settings.LowBatteryThresholdPercent, "ProfileSummary_Item_LowBattery", scope);
        AddIf(group, settings.AutoSleepSeconds, "ProfileSummary_Item_AutoSleep", scope);
        AddIf(group, settings.ChargeLimitPercent, "ProfileSummary_Item_ChargeLimit", scope);
        AddIf(group, settings.BatteryChemistry, "ProfileSummary_Item_BatteryChemistry", scope);
        AddIf(group, settings.ScrollMode, "ProfileSummary_Item_ScrollMode", scope);
        AddIf(group, settings.ScrollFreeNotched, "ProfileSummary_Item_ScrollMode", scope);
        AddIf(group, settings.ScrollAccelerationEnabled ?? (settings.ScrollAccelerationLevel is null
            ? null
            : true), "ProfileSummary_Item_ScrollAcceleration", scope);
        AddIf(group, settings.SmartReelEnabled ?? (settings.SmartReelThreshold is null
            ? null
            : true), "ProfileSummary_Item_SmartReel", scope);
        AddIf(group, settings.ActiveOnboardProfileId, "ProfileSummary_Item_OnboardProfile", scope);
        AddIf(group, settings.HapticScrollLevel, "ProfileSummary_Item_HapticScroll", scope);
        AddIf(group, settings.PowerPlan, "ProfileSummary_Item_PowerPlan", scope);
        AddIf(group, settings.SmartIlluminationEnabled, "ProfileSummary_Item_SmartIllumination", scope);
        AddIf(group, settings.SleepTimeoutSeconds, "ProfileSummary_Item_SleepTimeout", scope);
        AddIf(group, settings.SnapTapEnabled, "ProfileSummary_Item_SnapTap", scope);
        AddIf(group, settings.DebounceMilliseconds, "ProfileSummary_Item_Debounce", scope);
        AddIf(
            group,
            settings.HapticMode ?? settings.HapticAccelerationLevel ?? settings.HapticMaskTime ?? settings.HapticLevel,
            "ProfileSummary_Item_HapticFeedback",
            scope);
        if (settings.ButtonAssignments is { Count: > 0 } || settings.ButtonMappings is { Count: > 0 })
        {
            group.Add("ProfileSummary_Item_Mapping", scope);
        }
    }

    private static void AddKeyboard(
        GroupBuilder group,
        KeyboardProfileSettings? settings,
        ProfileSummaryScope scope)
    {
        if (settings is null)
        {
            return;
        }

        AddIf(group, settings.PollingRateHertz, "ProfileSummary_Item_PollingRate", scope);
        AddIf(group, settings.GamingModeEnabled, "ProfileSummary_Item_GamingMode", scope);
        AddIf(group, settings.SwitchType, "ProfileSummary_Item_SwitchType", scope);
        AddIf(group, settings.AnalogKeyEnabled, "ProfileSummary_Item_AnalogKey", scope);
        AddIf(group, settings.SnapTapEnabled, "ProfileSummary_Item_SnapTap", scope);
        AddIf(group, settings.FnKeyPrimary, "ProfileSummary_Item_FnPrimary", scope);
        AddIf(group, settings.DebounceMilliseconds, "ProfileSummary_Item_Debounce", scope);
        AddIf(group, settings.OledBrightness, "ProfileSummary_Item_OledBrightness", scope);
        AddIf(group, settings.OledLanguage, "ProfileSummary_Item_OledLanguage", scope);
        AddIf(group, settings.KnobMode, "ProfileSummary_Item_KnobMode", scope);
        AddIf(group, settings.WristRestLightingEnabled, "ProfileSummary_Item_WristRest", scope);
        if (settings.ButtonMappings is { Count: > 0 })
        {
            group.Add("ProfileSummary_Item_Mapping", scope);
        }
    }

    private static void AddHeadset(
        GroupBuilder group,
        HeadsetProfileSettings? settings,
        ProfileSummaryScope scope)
    {
        if (settings is null)
        {
            return;
        }

        AddIf(group, settings.VolumePercent, "ProfileSummary_Item_Volume", scope);
        AddIf(group, settings.SideTone, "ProfileSummary_Item_SideTone", scope);
        AddIf(group, settings.GameModeEnabled, "ProfileSummary_Item_GameMode", scope);
        AddIf(group, settings.GameChatBalance, "ProfileSummary_Item_GameChat", scope);
        AddIf(group, FirstPresent(settings.AncEnabled, settings.AncModeIndex, settings.AncLevel),
            "ProfileSummary_Item_Anc", scope);
        AddIf(group, settings.ThxEnabled ?? settings.ThxSpatialEnabled ?? settings.ThxEqualizerEnabled,
            "ProfileSummary_Item_Thx", scope);
        AddIf(group, FirstPresent(settings.ThxPreset, settings.ThxBands), "ProfileSummary_Item_ThxPreset", scope);
        AddIf(
            group,
            settings.ThxNormalizationEnabled ?? settings.ThxVoiceClarityEnabled ??
            settings.ThxBassBoostEnabled ?? settings.ThxSidetoneEnabled,
            "ProfileSummary_Item_ThxEnhancements",
            scope);
        AddIf(group, FirstPresent(settings.EqPreset, settings.EqBands), "ProfileSummary_Item_EqPreset", scope);
        AddIf(group, FirstPresent(settings.MicEqPreset, settings.MicEqBands), "ProfileSummary_Item_MicEq", scope);
        AddIf(group, settings.Bass, "ProfileSummary_Item_Bass", scope);
        AddIf(group, FirstPresent(settings.MicrophoneVolumePercent, settings.MicrophoneMute),
            "ProfileSummary_Item_Microphone", scope);
        AddIf(group, settings.NoiseSuppression, "ProfileSummary_Item_NoiseSuppression", scope);
        AddIf(group, settings.AudioSourceIndex, "ProfileSummary_Item_AudioSource", scope);
        AddIf(group, settings.Dolby ?? settings.NightMode ?? settings.VoicePrompts,
            "ProfileSummary_Item_AdvancedAudio", scope);
        AddIf(group, FirstPresent(settings.MicDeEsser, settings.MicLimiterDb, settings.MicReverbIndex,
            settings.MicReverbIntensity, settings.MicCompressorEnabled, settings.MicCompressorProfile),
            "ProfileSummary_Item_MicProcessing", scope);
        AddIf(group, settings.MicMonitoringEnabled, "ProfileSummary_Item_MicMonitoring", scope);
        AddIf(group, FirstPresent(settings.ParametricEqEnabled, settings.ParametricEqBandIndex,
            settings.ParametricEqFrequency, settings.ParametricEqGain, settings.ParametricEqQ),
            "ProfileSummary_Item_ParametricEq", scope);
        AddIf(group, FirstPresent(settings.HapticEnabled, settings.HapticsStrength), "ProfileSummary_Item_Haptics", scope);
        AddIf(group, settings.AutoPause, "ProfileSummary_Item_AutoPause", scope);
        AddIf(group, settings.AutoShutdownMinutes, "ProfileSummary_Item_AutoShutdown", scope);
        AddIf(group, settings.Dnd, "ProfileSummary_Item_Dnd", scope);
        AddIf(group, settings.GestureControl, "ProfileSummary_Item_Gesture", scope);
        AddIf(group, FirstPresent(settings.ChromaBrightness, settings.ChromaEffect, settings.ChromaParameters),
            "ProfileSummary_Item_Chroma", scope);
    }

    private static void AddCoolingPad(
        GroupBuilder group,
        CoolingPadProfileSettings? settings,
        ProfileSummaryScope scope)
    {
        if (settings is null)
        {
            return;
        }

        AddIf(group, settings.FanMode, "ProfileSummary_Item_FanMode", scope);
        AddIf(group, settings.FanTargetRpm, "ProfileSummary_Item_FanTarget", scope);
        AddIf(group, settings.FanCurve, "ProfileSummary_Item_FanCurve", scope);
        AddIf(group, FirstPresent(settings.LightingEnabled, settings.LightingEffect, settings.LightingParameters),
            "ProfileSummary_Item_Lighting", scope);
    }

    private static void AddGamepad(
        GroupBuilder group,
        GamepadProfileSettings? settings,
        ProfileSummaryScope scope)
    {
        if (settings is null)
        {
            return;
        }

        AddIf(group, settings.PollingRateHertz, "ProfileSummary_Item_PollingRate", scope);
        AddIf(group, settings.VibrationStrengthPercent, "ProfileSummary_Item_Vibration", scope);
        AddIf(group, settings.LeftStickDeadzone ?? settings.RightStickDeadzone,
            "ProfileSummary_Item_Deadzone", scope);
        AddIf(group, settings.TriggerMode, "ProfileSummary_Item_TriggerMode", scope);
        AddIf(group, settings.IgnoreDpadDiagonals, "ProfileSummary_Item_Dpad", scope);
        AddIf(group, settings.SocdModeIndex, "ProfileSummary_Item_Socd", scope);
        AddIf(group, FirstPresent(settings.LightingEnabled, settings.LightingEffect, settings.LightingParameters),
            "ProfileSummary_Item_Lighting", scope);
    }

    private static void AddMixer(
        GroupBuilder group,
        MixerProfileSettings? settings,
        ProfileSummaryScope scope)
    {
        if (settings is null)
        {
            return;
        }

        AddIf(group, settings.ChannelMutes, "ProfileSummary_Item_MixerChannels", scope);
        AddIf(group, settings.MicMute, "ProfileSummary_Item_MixerMicMute", scope);
        AddIf(group, settings.BrightnessPercent, "ProfileSummary_Item_MixerBrightness", scope);
    }

    private static void AddKiyo(
        GroupBuilder group,
        KiyoProfileSettings? settings,
        ProfileSummaryScope scope)
    {
        if (settings is null)
        {
            return;
        }

        AddIf(
            group,
            FirstPresent(settings.WhiteBalanceAuto, settings.WhiteBalanceTemperature),
            "ProfileSummary_Item_KiyoWhiteBalance",
            scope);
    }

    private static void AddDisplay(
        GroupBuilder group,
        DisplayProfileSettings? settings,
        ProfileSummaryScope scope)
    {
        if (settings is null)
        {
            return;
        }

        AddIf(group, settings.BrightnessPercent, "ProfileSummary_Item_DisplayBrightness", scope);
        AddIf(group, settings.RefreshRateHertz, "ProfileSummary_Item_RefreshRate", scope);
        AddIf(group, settings.LocalDimmingEnabled, "ProfileSummary_Item_LocalDimming", scope);
        AddIf(group, settings.NativeDisplayModeEnabled, "ProfileSummary_Item_NativeDisplayMode", scope);
    }

    private static void AddLighting(
        GroupBuilder group,
        LightingProfile? lighting,
        IReadOnlyDictionary<string, LightingProfile>? zones,
        ProfileSummaryScope scope,
        string? deviceKey,
        string? switchKey,
        string? chromaKey)
    {
        if (!string.IsNullOrWhiteSpace(switchKey))
        {
            group.Add(switchKey, scope);
        }
        if (!string.IsNullOrWhiteSpace(chromaKey))
        {
            group.Add(chromaKey, scope);
        }
        if (HasLighting(lighting))
        {
            if (deviceKey is null)
            {
                group.Add("ProfileSummary_Item_Lighting", scope);
            }
            else
            {
                group.Add("ProfileSummary_Item_DeviceLighting", scope, deviceKey);
            }
        }
        AddZones(group, zones, scope, deviceKey);
    }

    private static void AddZones(
        GroupBuilder group,
        IReadOnlyDictionary<string, LightingProfile>? zones,
        ProfileSummaryScope scope,
        string? deviceKey)
    {
        if (zones is null)
        {
            return;
        }

        foreach (var (zoneKey, zone) in zones.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!HasLighting(zone))
            {
                continue;
            }
            if (deviceKey is null)
            {
                group.Add("ProfileSummary_Item_LightingZone", scope, zoneKey);
            }
            else
            {
                group.Add("ProfileSummary_Item_DeviceLightingZone", scope, deviceKey, zoneKey);
            }
        }
    }

    private static bool HasLighting(LightingProfile? profile) =>
        profile is not null &&
        (!string.IsNullOrWhiteSpace(profile.Effect) || profile.Parameters.Count > 0);

    private static object? FirstPresent(params object?[] values) =>
        values.FirstOrDefault(value => value is not null);

    private static void AddIf(
        GroupBuilder group,
        object? value,
        string labelKey,
        ProfileSummaryScope scope)
    {
        if (value is not null)
        {
            group.Add(labelKey, scope);
        }
    }

    private sealed class GroupBuilder(string titleKey)
    {
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

        public string TitleKey { get; } = titleKey;
        public List<ProfileSummaryItem> Items { get; } = [];

        public void Add(string labelKey, ProfileSummaryScope scope, params object?[] arguments)
        {
            var signature = string.Join(
                '\u001F',
                labelKey,
                scope.ToString(),
                string.Join('\u001E', arguments.Select(argument => argument?.ToString() ?? string.Empty)));
            if (_seen.Add(signature))
            {
                Items.Add(new ProfileSummaryItem(labelKey, scope, arguments));
            }
        }

        public ProfileSummaryGroup ToGroup() => new(TitleKey, Items);
    }
}
