using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using static OpenSynapse.App.ViewModels.DeviceUiCatalog;

namespace OpenSynapse.App.ViewModels;

public sealed partial class BladeWorkspace
{
    /// <summary>
    /// Asks the user to accept the cooling risk before HyperBoost is written without a
    /// Razer Cooling Pad. The UI owns the dialog, so the ViewModel stays headless.
    /// </summary>
    internal void SetPadlessHyperBoostConfirmation(Func<Task<bool>> confirmation) =>
        _confirmPadlessHyperBoost = confirmation;

    internal void SetPadlessHyperBoostAccepted(bool accepted) =>
        _padlessHyperBoostAccepted = accepted;

    private async Task<bool> ConfirmPadlessHyperBoostAsync()
    {
        if (_confirmPadlessHyperBoost is not { } confirmation)
        {
            return true;
        }

        return await confirmation();
    }

    /// <summary>
    /// Renders the adapter wattage report for the diagnostic log, so a refused
    /// HyperBoost shows which reading the decision was made on.
    /// </summary>
    private static string DescribeAdapterPower(BladeAdapterPowerSnapshot? adapterPower) =>
        adapterPower is null
            ? "not reported"
            : $"level {adapterPower.ActualLevel}/{adapterPower.RecommendedLevel}, " +
              $"watts {adapterPower.ActualWatts?.ToString() ?? "?"}/" +
              $"{adapterPower.RecommendedWatts?.ToString() ?? "?"}";

    public async Task ApplyPerformanceModeAsync(CancellationToken cancellationToken = default)
    {
        var modes = _blade.PerformanceModes;
        if (_suppressBladePerformanceApply ||
            !CanWritePerformanceMode ||
            PerformanceModeIndex < 0 || PerformanceModeIndex >= modes.Count)
        {
            _diagnosticLog.TryWrite(
                "blade-performance",
                $"Performance apply skipped: suppressed={_suppressBladePerformanceApply}, " +
                $"canWrite={CanWritePerformanceMode}, " +
                $"canSet={CanSetPerformanceMode}, " +
                $"index={PerformanceModeIndex}, count={modes.Count}, " +
                $"productId=0x{_blade._bladeProductId:X4}.");
            return;
        }

        var requested = modes[PerformanceModeIndex];
        if (requested == BladePerformanceMode.Hyperboost &&
            !BladeModelCapabilities.IsHyperBoostAvailable(_blade._bladeProductId))
        {
            _diagnosticLog.TryWrite(
                "blade-performance",
                $"HyperBoost refused: productId=0x{_blade._bladeProductId:X4}, " +
                $"adapter={DescribeAdapterPower(LastTelemetry?.BladeAdapterPower)}.");
            _suppressBladePerformanceApply = true;
            try
            {
                PerformanceModeIndex = _blade._confirmedBladePerformanceModeIndex;
            }
            finally
            {
                _suppressBladePerformanceApply = false;
            }
            SetDeviceOperationError(AppStrings.Text("BladeHyperboostUnsupportedModel"));
            return;
        }
        if (requested == BladePerformanceMode.Hyperboost &&
            HyperboostNeedsCoolingPadWarning &&
            !_padlessHyperBoostAccepted &&
            !await ConfirmPadlessHyperBoostAsync())
        {
            _diagnosticLog.TryWrite(
                "blade-performance",
                "HyperBoost declined: the cooling-pad disclaimer was dismissed.");
            _suppressBladePerformanceApply = true;
            try
            {
                PerformanceModeIndex = _blade._confirmedBladePerformanceModeIndex;
            }
            finally
            {
                _suppressBladePerformanceApply = false;
            }
            return;
        }
        // The mode still applies when the adapter cannot hold the raised limit. The
        // middleware reports that shortage separately instead of hiding the option,
        // so the user is told after the write rather than blocked before it.
        var limitedAdapter = requested == BladePerformanceMode.Hyperboost &&
            LastTelemetry?.BladeAdapterPower is { } adapter &&
            adapter.IsLimitedAdapter;
        await RunDeviceOperationAsync(AppStrings.Text("Text_98C30F5D"), async () =>
        {
            if (!IsSelectedPerformancePowerActive)
            {
                await BladePerformanceProfileEdit.SaveModeAsync(EditablePerformanceProfile, (byte)requested,
                    SaveProfileAsync, cancellationToken, AppStrings.Text("Text_CC12A6F5"));
                SetPerformanceMode(requested, confirm: false);

                RefreshPerformanceEditor();
                return;
            }

            var target = EditablePerformanceProfile;
            var previousFan = (target.FanMode, target.FanTargetRpm, target.FanCurve);
            var actual = await _deviceTelemetryReader.SetBladePerformanceModeAsync(
                ActiveDescriptors, requested, cancellationToken, _bladeCoolingPadConnected);
            SetPerformanceMode(actual);
            await ReapplyCustomPowerLevelsAsync(cancellationToken);
            await _fan().ReapplyAfterPerformanceModeChangeAsync(cancellationToken);
            var clearedFan = ClearFixedFanWhenLeavingCustom(actual, target);
            try
            {
                await BladePerformanceProfileEdit.SaveModeAsync(target, (byte)actual, SaveProfileAsync,
                    cancellationToken, AppStrings.Text("Text_CC12A6F5"));
            }
            catch
            {
                (target.FanMode, target.FanTargetRpm, target.FanCurve) = previousFan;
                RequestDeviceRefresh();
                throw;
            }
            if (clearedFan && ReferenceEquals(target, EditablePerformanceProfile)) _fan().ClearPendingMode();
            RequestDeviceRefresh();
            PerformanceModeChangedByUser?.Invoke(actual);
        }, cancellationToken, RefreshPerformanceEditor);
        if (limitedAdapter)
        {
            SetDeviceOperationError(AppStrings.Text("BladeHyperboostLimitedAdapter"));
        }
    }

    /// <summary>
    /// Drops a stored fixed fan target when the model leaves Custom mode. The 028C EC
    /// stores a target in Balanced/Silent but keeps following its own curve, so the
    /// profile would otherwise carry a value the firmware ignores.
    /// </summary>
    private bool ClearFixedFanWhenLeavingCustom(BladePerformanceMode mode, BladeProfileSettings target)
    {
        if (mode == BladePerformanceMode.Custom ||
            !BladeModelCapabilities.FixedFanRequiresCustomMode(_blade._bladeProductId) ||
            target.ResolvedFanControlMode is null or BladeFanControlMode.Automatic)
        {
            return false;
        }

        // Keep the configured curve so switching back to Custom can restore it.
        target.FanControlMode = BladeFanControlMode.Automatic;
        target.FanMode = (byte)BladeFanMode.Automatic;
        target.FanTargetRpm = null;
        return true;
    }

    public async Task ApplyCpuBoostAsync(CancellationToken cancellationToken = default)
    {
        var cpuBoostModes = BladeModelCapabilities.CpuBoostModesFor(_blade._bladeProductId);
        if (!CanSetCpuBoost || CpuBoostIndex < 0 ||
            CpuBoostIndex >= cpuBoostModes.Count)
        {
            return;
        }

        await RunDeviceOperationAsync("CPU Boost", async () =>
        {
            var previousProfile = Profile.Clone();
            var actual = await _deviceTelemetryReader.SetBladeCpuBoostModeAsync(
                ActiveDescriptors, cpuBoostModes[CpuBoostIndex], cancellationToken);
            SetCpuBoost(actual);
            Profile.Global.Blade.CpuBoostMode = (byte)actual;
            if (!await SaveProfileAsync(cancellationToken))
            {
                Profile = previousProfile;
                throw new InvalidOperationException(AppStrings.Text("Text_A257CB56"));
            }
        }, cancellationToken, () => CpuBoostIndex = _blade._confirmedBladeCpuBoostIndex);
    }

    public async Task ApplyGpuBoostAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSetGpuBoost || GpuBoostIndex < 0 || GpuBoostIndex >= BladeGpuBoostModes.Length)
        {
            return;
        }

        await RunDeviceOperationAsync("GPU Boost", async () =>
        {
            var previousProfile = Profile.Clone();
            var actual = await _deviceTelemetryReader.SetBladeGpuBoostModeAsync(
                ActiveDescriptors, BladeGpuBoostModes[GpuBoostIndex], cancellationToken);
            SetGpuBoost(actual);
            Profile.Global.Blade.GpuBoostMode = (byte)actual;
            if (!await SaveProfileAsync(cancellationToken))
            {
                Profile = previousProfile;
                throw new InvalidOperationException(AppStrings.Text("Text_AB07F412"));
            }
        }, cancellationToken, () => GpuBoostIndex = _blade._confirmedBladeGpuBoostIndex);
    }

    /// <summary>
    /// The EC re-applies the mode's stored levels when the performance byte is
    /// written. Entering Custom must re-send the levels the profile names, or the
    /// machine keeps the EC preset instead of the user's setting.
    /// </summary>
    private async Task ReapplyCustomPowerLevelsAsync(CancellationToken cancellationToken)
    {
        if (!IsCustomMode)
        {
            return;
        }

        var blade = ActiveDescriptors.FirstOrDefault(device =>
            DeviceProtocolFamilies.IsBlade(device.ProtocolFamily));
        var profile = blade is null
            ? EditablePerformanceProfile
            : ProfileResolver.Resolve(Profile, blade, CurrentPowerSource).Blade;
        if (!_blade.SupportsCpuBoost)
        {
            return;
        }

        if (profile.CpuBoostMode is byte rawCpu &&
            Enum.IsDefined(typeof(BladeCpuBoostMode), rawCpu))
        {
            try
            {
                SetCpuBoost(await _deviceTelemetryReader.SetBladeCpuBoostModeAsync(
                    ActiveDescriptors, (BladeCpuBoostMode)rawCpu, cancellationToken));
            }
            catch (Exception exception) when (RuntimeErrors.IsExpected(exception))
            {
                _diagnosticLog.TryWrite(
                    "blade-performance",
                    $"Custom CPU level reapply failed: {exception.Message}");
            }
        }

        if (profile.GpuBoostMode is byte rawGpu &&
            Enum.IsDefined(typeof(BladeGpuBoostMode), rawGpu))
        {
            try
            {
                SetGpuBoost(await _deviceTelemetryReader.SetBladeGpuBoostModeAsync(
                    ActiveDescriptors, (BladeGpuBoostMode)rawGpu, cancellationToken));
            }
            catch (Exception exception) when (RuntimeErrors.IsExpected(exception))
            {
                _diagnosticLog.TryWrite(
                    "blade-performance",
                    $"Custom GPU level reapply failed: {exception.Message}");
            }
        }
    }

    public async Task ApplyMaxFanAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSetMaxFan)
        {
            return;
        }

        await RunDeviceOperationAsync("Max Fan", async () =>
        {
            var requested = MaxFanEnabled ? BladeMaxFanMode.Enabled : BladeMaxFanMode.Disabled;

            // Max Fan lives in the Custom performance card, so it follows the performance
            // power profile the same way the performance mode itself does. Writing the
            // global slot would silently drop a per-power-source setting.
            if (!IsSelectedPerformancePowerActive)
            {
                await BladePerformanceProfileEdit.SaveMaxFanAsync(EditablePerformanceProfile, (byte)requested,
                    SaveProfileAsync, cancellationToken, AppStrings.Text("Text_CC12A6F5"));
                SetMaxFan(requested, confirm: false);

                return;
            }

            var target = EditablePerformanceProfile;
            var edit = new BladeFanProfileEdit(target);
            try
            {
                if (requested == BladeMaxFanMode.Enabled)
                {
                    await PrepareFanForMaxFanAsync(cancellationToken);
                }

                var actual = await _deviceTelemetryReader.SetBladeMaxFanModeAsync(
                    ActiveDescriptors, requested, cancellationToken);
                SetMaxFan(actual);
                if (requested == BladeMaxFanMode.Enabled)
                {
                    target.FanControlMode = BladeFanControlMode.Automatic;
                    target.FanMode = (byte)BladeFanMode.Automatic;
                    target.FanTargetRpm = null;
                }
                target.MaxFanMode = (byte)actual;
                if (!await SaveProfileAsync(cancellationToken))
                {
                    throw new InvalidOperationException(AppStrings.Text("Text_AB07F412"));
                }
            }
            catch
            {
                edit.Restore();
                RequestDeviceRefresh();
                throw;
            }

            RequestDeviceRefresh();
        }, cancellationToken, RefreshPerformanceEditor);
    }

    /// <summary>
    /// Max Fan and a manual target share the same fan owner. Release curve/fixed
    /// control and write Automatic before the Max Fan bit so a stale Manual state
    /// cannot make the switch refuse until another performance-mode write.
    /// </summary>
    private async Task PrepareFanForMaxFanAsync(CancellationToken cancellationToken)
    {
        var stopError = await _fan().StopAsync("max-fan");
        if (stopError is not null)
        {
            throw new InvalidOperationException(stopError);
        }

        var actual = await _deviceTelemetryReader.SetBladeFanAsync(
            ActiveDescriptors,
            BladeFanMode.Automatic,
            null,
            null,
            cancellationToken);
        _fan().ObserveFanTelemetry(actual.Mode, actual.TargetRpm);
    }

    /// <summary>
    /// Called by the fan workspace while it holds the device-operation gate. A curve or
    /// fixed target cannot share the Custom-mode Max Fan bit.
    /// </summary>
    internal async Task DisableMaxFanForFanControlAsync(CancellationToken cancellationToken)
    {
        if (!_blade.SupportsMaxFan || !_blade._bladeMaxFanEnabled)
        {
            return;
        }

        var actual = await _deviceTelemetryReader.SetBladeMaxFanModeAsync(
            ActiveDescriptors,
            BladeMaxFanMode.Disabled,
            cancellationToken);
        SetMaxFan(actual);
    }

    internal async Task CyclePerformanceModeAsync(CancellationToken cancellationToken = default)
    {
        await RunDeviceOperationAsync(AppStrings.Text("Text_98C30F5D"), async () =>
        {
            if (!CanWritePerformanceMode || _blade._confirmedBladePerformanceModeIndex < 0)
            {
                throw new InvalidOperationException(AppStrings.Text("Text_2263C356"));
            }

            var cycleModes = _bladePerformanceCycleModes;
            // The shortcut cycle has no place to ask for the cooling-risk
            // confirmation, so it skips HyperBoost unless the pad is connected or
            // the user already accepted the pad-less warning.
            if (!BladeModelCapabilities.IsHyperBoostAvailable(
                    _blade._bladeProductId) ||
                (HyperboostNeedsCoolingPadWarning && !_padlessHyperBoostAccepted))
            {
                cycleModes = [.. cycleModes.Where(mode => mode != BladePerformanceMode.Hyperboost)];
                if (cycleModes.Count == 0)
                {
                    cycleModes = [BladePerformanceMode.Performance];
                }
            }

            var modes = _blade.PerformanceModes;
            cycleModes = [.. cycleModes.Where(modes.Contains)];
            if (cycleModes.Count == 0)
            {
                cycleModes = [modes[0]];
            }

            var nextMode = BladePerformanceModeCycle.GetNext(
                modes[_blade._confirmedBladePerformanceModeIndex],
                modes,
                cycleModes);
            PerformanceModeIndex = modes.ToList().IndexOf(nextMode);
            var target = CurrentPowerOverrides?.Blade ?? GetActiveProfile().Global.Blade;
            var previousFan = (target.FanMode, target.FanTargetRpm, target.FanCurve);
            var actual = await _deviceTelemetryReader.SetBladePerformanceModeAsync(
                ActiveDescriptors,
                modes[PerformanceModeIndex],
                cancellationToken,
                _bladeCoolingPadConnected);
            SetPerformanceMode(actual);
            await ReapplyCustomPowerLevelsAsync(cancellationToken);
            await _fan().ReapplyAfterPerformanceModeChangeAsync(cancellationToken);
            var clearedFan = ClearFixedFanWhenLeavingCustom(actual, target);
            try
            {
                await BladePerformanceProfileEdit.SaveModeAsync(target, (byte)actual, SaveProfileAsync,
                    cancellationToken, AppStrings.Text("Text_CC12A6F5"));
            }
            catch
            {
                (target.FanMode, target.FanTargetRpm, target.FanCurve) = previousFan;
                throw;
            }
            if (clearedFan && ReferenceEquals(target, EditablePerformanceProfile)) _fan().ClearPendingMode();
            RequestDeviceRefresh();
            PerformanceModeChangedByUser?.Invoke(actual);
        }, cancellationToken, () =>
            PerformanceModeIndex = _blade._confirmedBladePerformanceModeIndex);
    }

    internal async Task ToggleGamingModeAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSetGamingMode)
        {
            return;
        }

        await RunDeviceOperationAsync(AppStrings.Text("Text_EED40F9C"), async () =>
        {
            var current = _blade._bladeGameModeState is byte state && state != 2
                ? state
                : throw new InvalidOperationException(AppStrings.Text("Text_06C879B7"));
            var actual = await SetGameModeCoreAsync(
                current == 0,
                cancellationToken);
            SetGameMode(actual);
            RequestDeviceRefresh();
            GamingModeChangedByUser?.Invoke(actual.GameMode != 0);
        }, cancellationToken);
    }

    public async Task ApplyGamingModeAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApplyGamingMode)
        {
            return;
        }

        await RunDeviceOperationAsync(AppStrings.Text("Text_EED40F9C"), async () =>
        {
            var actual = await SetGameModeCoreAsync(
                GameModeEnabled,
                cancellationToken);
            SetGameMode(actual);
            RequestDeviceRefresh();
            GamingModeChangedByUser?.Invoke(actual.GameMode != 0);
        }, cancellationToken, () =>
            GameModeEnabled = _blade._bladeGameModeState != 0);
    }

    private async Task<BladeGameModeTelemetry> SetGameModeCoreAsync(
        bool enabled,
        CancellationToken cancellationToken)
    {
        try
        {
            var previousProfileValue = Profile.Global.Blade.GamingModeEnabled;
            var previousEnabled = _blade._bladeGameModeState != 0;
            var actual = await _deviceTelemetryReader.SetBladeGameModeAsync(
                ActiveDescriptors,
                enabled,
                cancellationToken);
            Profile.Global.Blade.GamingModeEnabled = actual.GameMode != 0;
            try
            {
                if (await SaveProfileAsync(cancellationToken))
                {
                    return actual;
                }
                throw new InvalidOperationException(AppStrings.Text("GamingModeProfileSaveFailed"));
            }
            catch
            {
                Profile.Global.Blade.GamingModeEnabled = previousProfileValue;
                await _deviceTelemetryReader.SetBladeGameModeAsync(
                    ActiveDescriptors,
                    previousEnabled,
                    CancellationToken.None);
                throw;
            }
        }
        catch (NotSupportedException)
        {
            _blade._bladeGameModeWriteSupported = false;
            OnPropertyChanged(nameof(CanSetGamingMode));
            OnPropertyChanged(nameof(CanApplyGamingMode));
            OnPropertyChanged(nameof(GamingModeVisibility));
            throw;
        }
    }
}
