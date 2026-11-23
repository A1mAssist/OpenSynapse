using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Displays;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using static OpenSynapse.App.ViewModels.DeviceUiCatalog;

namespace OpenSynapse.App.ViewModels;

public sealed partial class BladeWorkspace
{
    public async Task ApplyChargeLimitAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSetChargeLimit ||
            ChargeLimitIndex < 0 || ChargeLimitIndex >= BladeChargeLimits.Length)
        {
            return;
        }

        await RunDeviceOperationAsync(AppStrings.Text("Text_DEE979FD"), async () =>
        {
            var target = Profile.Global.Blade;
            var actual = await _deviceTelemetryReader.SetBladeChargeLimitAsync(
                ActiveDescriptors, BladeChargeLimits[ChargeLimitIndex], cancellationToken);
            SetChargeLimit(actual);
            await BladePerformanceProfileEdit.SaveChargeLimitAsync(target, actual, SaveProfileAsync,
                cancellationToken, AppStrings.FormatText("ProfileSaveError", AppStrings.Text("Text_DEE979FD")));
        }, cancellationToken, () =>
            ChargeLimitIndex = _blade._confirmedBladeChargeLimitIndex);
    }

    public async Task ApplyLogoAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSetLogo || LogoIndex < 0 || LogoIndex >= BladeLogoModes.Length)
        {
            return;
        }

        await RunDeviceOperationAsync(AppStrings.Text("Text_7D07A709"), async () =>
        {
            var previousProfile = Profile.Clone();
            var requested = BladeLogoModes[LogoIndex];
            var target = EditableLogoProfile;
            if (!IsSelectedLogoPowerActive)
            {
                target.LogoMode = (byte)requested;
                SetLogo(requested, confirm: false);
                if (!await SaveProfileAsync(cancellationToken))
                {
                    Profile = previousProfile;
                    throw new InvalidOperationException(AppStrings.Text("Text_359F8C1A"));
                }

                RefreshLogoEditor();
                return;
            }

            var actual = await _deviceTelemetryReader.SetBladeLogoModeAsync(
                ActiveDescriptors, requested, cancellationToken);
            SetLogo(actual);
            target.LogoMode = (byte)actual;
            if (!await SaveProfileAsync(cancellationToken))
            {
                Profile = previousProfile;
                throw new InvalidOperationException(AppStrings.Text("Text_359F8C1A"));
            }
        }, cancellationToken, () => LogoIndex = _blade._confirmedBladeLogoIndex);
    }

    public async Task ToggleTouchpadAsync(CancellationToken cancellationToken = default)
    {
        await RunDeviceOperationAsync(
            AppStrings.Text("Text_19D052FF"),
            async () =>
            {
                if (!_blade._canSetBladeTouchpad || _touchpadController is null)
                {
                    throw new InvalidOperationException(AppStrings.Text("Text_EA682784"));
                }

                var actual = await Task.Run(
                    _touchpadController.ToggleVerified,
                    cancellationToken);
                TouchpadEnabled = actual;
                _blade._confirmedBladeTouchpadEnabled = actual;
                TouchpadText = actual ? AppStrings.Text("Text_F55AD712") : AppStrings.Text("Text_7E3B0F3C");
                if (!await _deviceTelemetryReader.SetBladeTouchpadStatusAsync(
                        ActiveDescriptors, actual, cancellationToken))
                {
                    // The Windows state is already committed, so retry once after the
                    // EC settles instead of rolling the touchpad back.
                    await Task.Delay(200, cancellationToken);
                    if (!await _deviceTelemetryReader.SetBladeTouchpadStatusAsync(
                            ActiveDescriptors, actual, cancellationToken))
                    {
                        _diagnosticLog.TryWrite(
                            "device-operation",
                            $"touchpad indicator did not acknowledge enabled={actual}");
                    }
                }
                TouchpadChangedByUser?.Invoke(actual);
            },
            cancellationToken,
            () =>
            {
                TouchpadEnabled = _blade._confirmedBladeTouchpadEnabled;
                TouchpadText = _blade._confirmedBladeTouchpadEnabled ? AppStrings.Text("Text_F55AD712") : AppStrings.Text("Text_7E3B0F3C");
            },
            successVerb: AppStrings.Text("Text_5CF73A23"),
            failureVerb: AppStrings.Text("Text_B9EC9D4C"));
    }

    [ProfileSetting("blade.startup-animation", DeviceCategory.Laptop, powerScoped: true)]
    public async Task ApplyStartupAnimationAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApplyStartupAnimation)
        {
            return;
        }

        await RunDeviceOperationAsync(AppStrings.Text("Text_AA91A1FF"), async () =>
        {
            var target = EditablePlatformProfile;
            var previousHardware = _blade._bladeStartupAnimationEnabled;
            var actual = await _deviceTelemetryReader.SetBladeStartupAnimationAsync(
                ActiveDescriptors, StartupAnimationEnabled, cancellationToken);
            await SavePlatformSettingAsync(
                () => target.StartupAnimationEnabled,
                value => target.StartupAnimationEnabled = value,
                (bool?)actual,
                (_, token) => previousHardware is { } previous
                    ? RestoreStartupAnimationAsync(previous, token)
                    : Task.CompletedTask,
                cancellationToken);
            _blade._bladeStartupAnimationEnabled = actual;
            StartupAnimationText = FormatOptionalState(actual);
            StartupAnimationEnabled = actual;
            RequestDeviceRefresh();
        }, cancellationToken, () =>
            StartupAnimationEnabled = _blade._bladeStartupAnimationEnabled ?? false);
    }

    [ProfileSetting("blade.auto-sleep", DeviceCategory.Laptop, powerScoped: true)]
    public async Task ApplyAutoSleepAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApplyAutoSleep)
        {
            return;
        }

        await RunDeviceOperationAsync(AppStrings.Text("BladeAutoSleepTitle/Text"), async () =>
        {
            var target = EditablePlatformProfile;
            var previousHardware = _blade._bladeAutoSleepEnabled;
            var actual = await _deviceTelemetryReader.SetBladeAutoSleepAsync(
                ActiveDescriptors, AutoSleepEnabled, cancellationToken);
            await SavePlatformSettingAsync(
                () => target.AutoSleepSeconds,
                value => target.AutoSleepSeconds = value,
                (int?)(actual ? 1 : 0),
                (_, token) => previousHardware is { } previous
                    ? RestoreAutoSleepAsync(previous ? 1 : 0, token)
                    : Task.CompletedTask,
                cancellationToken);
            SetAutoSleep(actual, _blade._bladeAutoSleepWritable);
            RequestDeviceRefresh();
        }, cancellationToken, () =>
            AutoSleepEnabled = _blade._bladeAutoSleepEnabled ?? false);
    }

    [ProfileSetting("blade.time-to-dim", DeviceCategory.Laptop, powerScoped: true)]
    public async Task ApplyTimeToDimAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApplyTimeToDim)
        {
            return;
        }

        await RunDeviceOperationAsync(AppStrings.Text("BladeTimeToDimTitle/Text"), async () =>
        {
            var target = EditablePlatformProfile;
            var previousHardware = _blade._bladeTimeToDim?.Seconds;
            var actual = await _deviceTelemetryReader.SetBladeTimeToDimAsync(
                ActiveDescriptors, TimeToDimSeconds[TimeToDimIndex], cancellationToken);
            await SavePlatformSettingAsync(
                () => target.TimeToDimSeconds,
                value => target.TimeToDimSeconds = value,
                (int?)actual.Seconds,
                (_, token) => previousHardware is { } previous
                    ? RestoreTimeToDimAsync(previous, token)
                    : Task.CompletedTask,
                cancellationToken);
            SetTimeToDim(actual, _blade._bladeTimeToDimWritable);
            RequestDeviceRefresh();
        }, cancellationToken, () =>
            TimeToDimIndex = _blade.ConfirmedTimeToDimIndex);
    }

    [ProfileSetting("blade.indicator-led", DeviceCategory.Laptop, powerScoped: true)]
    public async Task ApplyIndicatorLedAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApplyIndicatorLed)
        {
            return;
        }

        await RunDeviceOperationAsync(AppStrings.Text("BladeIndicatorLedTitle/Text"), async () =>
        {
            var target = EditablePlatformProfile;
            var previousHardware = _blade._bladeIndicatorLedEnabled;
            var actual = await _deviceTelemetryReader.SetBladeIndicatorLedAsync(
                ActiveDescriptors, IndicatorLedEnabled, cancellationToken);
            await SavePlatformSettingAsync(
                () => target.IndicatorLedEnabled,
                value => target.IndicatorLedEnabled = value,
                (bool?)actual,
                (_, token) => previousHardware is { } previous
                    ? RestoreIndicatorLedAsync(previous, token)
                    : Task.CompletedTask,
                cancellationToken);
            SetIndicatorLed(actual, _blade._bladeIndicatorLedWritable);
            RequestDeviceRefresh();
        }, cancellationToken, () =>
            IndicatorLedEnabled = _blade._bladeIndicatorLedEnabled ?? false);
    }

    [ProfileSetting("blade.gpu-card-detection", DeviceCategory.Laptop, powerScoped: true)]
    public async Task ApplyGpuCardDetectionAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApplyGpuCardDetection)
        {
            return;
        }

        await RunDeviceOperationAsync(AppStrings.Text("BladeGpuCardDetectionTitle/Text"), async () =>
        {
            var target = EditablePlatformProfile;
            var previousHardware = _blade._bladeGpuCardDetection?.Enabled;
            var actual = await _deviceTelemetryReader.SetBladeGpuCardDetectionAsync(
                ActiveDescriptors, GpuCardDetectionEnabled, cancellationToken);
            await SavePlatformSettingAsync(
                () => target.GpuCardDetectionEnabled,
                value => target.GpuCardDetectionEnabled = value,
                (bool?)actual.Enabled,
                (_, token) => previousHardware is { } previous
                    ? RestoreGpuCardDetectionAsync(previous, token)
                    : Task.CompletedTask,
                cancellationToken);
            SetGpuCardDetection(actual, _blade._bladeGpuCardDetectionWritable);
            RequestDeviceRefresh();
        }, cancellationToken, () =>
            GpuCardDetectionEnabled = _blade._bladeGpuCardDetection?.Enabled ?? false);
    }

    [ProfileSetting("blade.native-display-mode", DeviceCategory.Laptop, powerScoped: true)]
    public async Task ApplyNativeDisplayModeAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApplyNativeDisplayMode)
        {
            return;
        }

        await RunDeviceOperationAsync(AppStrings.Text("BladeNativeDisplayModeTitle/Text"), async () =>
        {
            var target = EditablePlatformProfile;
            var previousHardware = _blade._bladeNativeDisplayMode;
            var actual = await _deviceTelemetryReader.SetBladeNativeDisplayModeAsync(
                ActiveDescriptors,
                (BladeNativeDisplayMode)NativeDisplayModeIndex,
                cancellationToken);
            await SavePlatformSettingAsync(
                () => target.NativeDisplayModeEnabled,
                value => target.NativeDisplayModeEnabled = value,
                (bool?)(actual == BladeNativeDisplayMode.Fhd),
                (_, token) => previousHardware is { } previous
                    ? RestoreNativeDisplayModeAsync(previous == BladeNativeDisplayMode.Fhd, token)
                    : Task.CompletedTask,
                cancellationToken);
            _blade._bladeNativeDisplayMode = actual;
            NativeDisplayModeIndex = (int)actual;
            OnPropertyChanged(nameof(CanSetNativeDisplayMode));
            OnPropertyChanged(nameof(CanApplyNativeDisplayMode));
            OnPropertyChanged(nameof(NativeDisplayModeVisibility));
            RequestDeviceRefresh();
        }, cancellationToken, () =>
            NativeDisplayModeIndex = (int)(_blade._bladeNativeDisplayMode ?? BladeNativeDisplayMode.Uhd));
    }

    [ProfileSetting("blade.local-dimming", DeviceCategory.Laptop, powerScoped: true)]
    public async Task ApplyLocalDimmingAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApplyLocalDimming)
        {
            return;
        }

        await RunDeviceOperationAsync(AppStrings.Text("BladeLocalDimmingTitle/Text"), async () =>
        {
            var target = EditablePlatformProfile;
            var previousHardware = _blade._bladeLocalDimmingEnabled;
            var actual = await _deviceTelemetryReader.SetBladeLocalDimmingAsync(
                ActiveDescriptors, LocalDimmingEnabled, cancellationToken);
            await SavePlatformSettingAsync(
                () => target.LocalDimmingEnabled,
                value => target.LocalDimmingEnabled = value,
                (bool?)actual,
                (_, token) => previousHardware is { } previous
                    ? RestoreLocalDimmingAsync(previous, token)
                    : Task.CompletedTask,
                cancellationToken);
            _blade._bladeLocalDimmingEnabled = actual;
            LocalDimmingEnabled = actual;
            OnPropertyChanged(nameof(CanSetLocalDimming));
            OnPropertyChanged(nameof(CanApplyLocalDimming));
            OnPropertyChanged(nameof(LocalDimmingVisibility));
            RequestDeviceRefresh();
        }, cancellationToken, () =>
            LocalDimmingEnabled = _blade._bladeLocalDimmingEnabled ?? false);
    }

    [ProfileSetting("blade.cpu-power-boost", DeviceCategory.Laptop, powerScoped: true)]
    public async Task ApplyCpuPowerBoostAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApplyCpuPowerBoost)
        {
            return;
        }

        await RunDeviceOperationAsync(AppStrings.Text("BladeCpuPowerBoostTitle/Text"), async () =>
        {
            var target = EditablePlatformProfile;
            var previousHardware = _blade._bladeCpuPowerBoostEnabled;
            var actual = await _deviceTelemetryReader.SetBladeCpuPowerBoostAsync(
                ActiveDescriptors, CpuPowerBoostEnabled, cancellationToken);
            await SavePlatformSettingAsync(
                () => target.CpuPowerBoostMode,
                value => target.CpuPowerBoostMode = value,
                (byte?)(actual ? (byte)1 : (byte)0),
                (_, token) => previousHardware is { } previous
                    ? RestoreCpuPowerBoostAsync(previous ? (byte)1 : (byte)0, token)
                    : Task.CompletedTask,
                cancellationToken);
            _blade._bladeCpuPowerBoostEnabled = actual;
            CpuPowerBoostEnabled = actual;
            OnPropertyChanged(nameof(CanSetCpuPowerBoost));
            OnPropertyChanged(nameof(CanApplyCpuPowerBoost));
            OnPropertyChanged(nameof(CpuPowerBoostVisibility));
            RequestDeviceRefresh();
        }, cancellationToken, () =>
            CpuPowerBoostEnabled = _blade._bladeCpuPowerBoostEnabled ?? false);
    }

    public async Task ApplyFnKeyAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApplyFnKey)
        {
            return;
        }

        // A refresh can arrive while this operation waits for the device gate.
        // Keep the value the user clicked, not whatever the refresh restored.
        var requested = FnKeyMultimediaPrimary;
        await RunDeviceOperationAsync(AppStrings.Text("BladeFnKeyLabel"), async () =>
        {
            var actual = await _deviceTelemetryReader.SetBladeFnKeyStateAsync(
                ActiveDescriptors, requested, cancellationToken);
            SetFnKey(actual);
            // The write already returns the applied state. A full refresh would
            // reapply the lighting profile and delay the keyboard by seconds.
        }, cancellationToken, () =>
            FnKeyMultimediaPrimary = _blade._bladeFnKeyMultimediaPrimary ?? false);
        // The Fn row layer lives in the host mapping graph, so the runtime must
        // rebuild it after the device accepted a new primary layer.
        InputProfileChanged?.Invoke();
    }

    internal async Task CycleInternalDisplayRefreshRateAsync(CancellationToken cancellationToken = default)
    {
        await RunDeviceOperationAsync(AppStrings.Text("Text_8423206D"), async () =>
        {
            var display = _display();
            if (!display.CanSetInternalDisplayRefreshRate ||
                display.Controller is null ||
                display.InternalDisplayRefreshRates.Count == 0)
            {
                throw new InvalidOperationException(AppStrings.Text("Text_36D4C146"));
            }

            var configuredCycle = _refreshRateCycle();
            var includedRates = configuredCycle is { Count: > 0 }
                ? configuredCycle
                : display.InternalDisplayRefreshRates.ToHashSet();
            var target = CurrentPowerOverrides?.Blade ?? GetActiveProfile().Global.Blade;
            display.InternalDisplayRefreshRateHertz = BladePerformanceModeCycle.GetNext(
                display.ConfirmedRefreshRateHertz,
                display.InternalDisplayRefreshRates,
                includedRates);
            var requested = display.InternalDisplayRefreshRateHertz;
            var snapshot = await Task.Run(
                () => display.Controller.SetRefreshRate(requested), cancellationToken);
            display.ApplyInternalDisplaySnapshot(snapshot);
            await BladePerformanceProfileEdit.SaveRefreshRateAsync(target, snapshot.RefreshRateHertz,
                SaveProfileAsync, cancellationToken,
                AppStrings.FormatText("ProfileSaveError", AppStrings.Text("Text_8423206D")));
            _internalDisplayRefreshRateChangedByUser(snapshot.RefreshRateHertz);
        }, cancellationToken, () =>
            _display().InternalDisplayRefreshRateHertz = _display().ConfirmedRefreshRateHertz);
    }

    [NotProfileScoped]
    internal async Task ToggleOneTimeFullChargeAsync(
        CancellationToken cancellationToken = default)
    {
        await RunDeviceOperationAsync(AppStrings.Text("Text_6F8CB285"), async () =>
        {
            var current = _blade._bladeOneTimeFullChargeEnabled ??
                throw new InvalidOperationException(AppStrings.Text("Text_8F0D00AE"));
            var actual = await _deviceTelemetryReader.SetBladeOneTimeFullChargeAsync(
                ActiveDescriptors,
                !current,
                cancellationToken);
            _blade._bladeOneTimeFullChargeEnabled = actual;
            OneTimeFullChargeEnabled = actual;
            OneTimeFullChargeText = FormatOptionalState(actual);
            RequestDeviceRefresh();
            OneTimeFullChargeChangedByUser?.Invoke(actual);
        }, cancellationToken);
    }

    [NotProfileScoped]
    public async Task ApplyOneTimeFullChargeAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApplyOneTimeFullCharge)
        {
            return;
        }

        await RunDeviceOperationAsync(AppStrings.Text("Text_6F8CB285"), async () =>
        {
            var actual = await _deviceTelemetryReader.SetBladeOneTimeFullChargeAsync(
                ActiveDescriptors, OneTimeFullChargeEnabled, cancellationToken);
            _blade._bladeOneTimeFullChargeEnabled = actual;
            OneTimeFullChargeText = FormatOptionalState(actual);
            OneTimeFullChargeEnabled = actual;
            RequestDeviceRefresh();
            OneTimeFullChargeChangedByUser?.Invoke(actual);
        }, cancellationToken, () =>
            OneTimeFullChargeEnabled = _blade._bladeOneTimeFullChargeEnabled ?? false);
    }

    private PowerSourceKind SelectedPowerSource(int index) => index switch
    {
        1 => PowerSourceKind.FullPowerAdapter,
        2 => PowerSourceKind.LowPowerAdapter,
        3 => PowerSourceKind.Battery,
        _ => CurrentPowerSource,
    };

    private PowerProfileOverrides? SelectedPowerOverrides(int index) => index switch
    {
        1 => GetActiveProfile().PluggedIn,
        2 => GetActiveProfile().LowPowerAdapter,
        3 => GetActiveProfile().OnBattery,
        _ => CurrentPowerOverrides,
    };
}
