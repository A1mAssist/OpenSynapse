using Microsoft.UI.Xaml;
using Windows.UI;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;
using static OpenSynapse.App.ViewModels.DeviceUiCatalog;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel
{
    public async Task ApplyBladeBrightnessAsync(CancellationToken cancellationToken = default)
    {
        if (!_blade._canSetBladeBrightness)
        {
            return;
        }

        await RunDeviceOperationAsync(
            AppStrings.Text("Text_5F0C27DB"),
            () => ApplyBladeBrightnessCoreAsync(cancellationToken),
            cancellationToken,
            () => BladeBrightnessPercent = _blade._confirmedBladeBrightnessPercent);
    }

    private async Task ApplyBladeBrightnessCoreAsync(CancellationToken cancellationToken)
    {
        var requested = checked((byte)Math.Round(
            BladeBrightnessPercent * 255 / 100,
            MidpointRounding.AwayFromZero));
        if (!IsSelectedLightingPowerActive || Volatile.Read(ref _displayAvailable) == 0)
        {
            EditableLightingBladeProfile.KeyboardBrightness = requested;
            SetBladeBrightness(requested, confirm: false);
            await SaveProfileAsync(cancellationToken);
            RefreshBladeLightingEditor();
            return;
        }

        var actual = await _deviceTelemetryReader.SetBladeKeyboardBrightnessAsync(
            _deviceDescriptors,
            requested,
            cancellationToken);
        SetBladeBrightness(actual);
        EditableLightingBladeProfile.KeyboardBrightness = actual;
        await SaveProfileAsync(cancellationToken);
    }

    public async Task ApplyBladeLightingEffectAsync(
        BladeLightingEffect effect,
        CancellationToken cancellationToken = default)
    {
        if (!CanSetBladeLighting || _bladeLightingController is null)
        {
            return;
        }

        await RunDeviceOperationAsync(
            AppStrings.Text("Text_09707A2C"),
            async () =>
            {
                var previousProfile = _profile.Clone();
                var encoded = BladeLightingProfileCodec.Create(effect);
                var targetLighting = EditableLightingProfile;
                if (!IsSelectedLightingPowerActive || Volatile.Read(ref _displayAvailable) == 0)
                {
                    targetLighting.Effect = encoded.Effect;
                    targetLighting.Parameters = encoded.Parameters;
                    if (!await SaveProfileAsync(cancellationToken))
                    {
                        _profile = previousProfile;
                        throw new InvalidOperationException(AppStrings.Text("Text_9A87CD0C"));
                    }

                    RefreshBladeLightingEditor();
                    return;
                }

                await _bladeLightingController.ApplyAsync(
                    _deviceDescriptors, effect, cancellationToken);
                targetLighting.Effect = encoded.Effect;
                targetLighting.Parameters = encoded.Parameters;
                if (!await SaveProfileAsync(cancellationToken))
                {
                    _profile = previousProfile;
                    _lightingShadowFingerprint = string.Empty;
                    throw new InvalidOperationException(AppStrings.Text("Text_C7D0CD75"));
                }

                var blade = _deviceDescriptors.FirstOrDefault(device =>
                    device.ProtocolFamily == DeviceProtocolFamilies.Blade &&
                    device.Access == DeviceAccessState.Available);
                if (blade is not null)
                {
                    _bladeLightingDevicePath = blade.Id;
                    _lightingShadowFingerprint = CreateLightingFingerprint(
                        ProfileResolver.Resolve(_profile, blade, _powerSourceProvider.IsPluggedIn).Lighting,
                        blade.Id,
                        _powerSourceProvider.IsPluggedIn);
                }
                _ = ObserveBladeLightingRuntimeAsync(
                    _bladeLightingController.RuntimeCompletion);
            },
            cancellationToken,
            successVerb: AppStrings.Text("Text_945C2E42"));
    }

    public Task ApplySelectedBladeLightingEffectAsync(CancellationToken cancellationToken = default)
    {
        if (BladeLightingModeIndex < 0 || BladeLightingModeIndex >= BladeLightingModes.Length ||
            BladeWaveDirectionIndex < 0 || BladeWaveDirectionIndex >= BladeWaveDirections.Length)
        {
            return Task.CompletedTask;
        }

        var color = new RazerRgb(BladeLightingColor.R, BladeLightingColor.G, BladeLightingColor.B);
        var secondColor = new RazerRgb(
            BladeLightingSecondColor.R,
            BladeLightingSecondColor.G,
            BladeLightingSecondColor.B);
        var effect = new BladeLightingEffect(
            BladeLightingModes[BladeLightingModeIndex],
            color,
            BladeWaveDirections[BladeWaveDirectionIndex],
            secondColor,
            BladeReactiveSpeeds[Math.Clamp(BladeReactiveSpeedIndex, 0, BladeReactiveSpeeds.Length - 1)],
            BladeStarlightSpeeds[Math.Clamp(BladeStarlightSpeedIndex, 0, BladeStarlightSpeeds.Length - 1)],
            BladeStarlightColorModes[Math.Clamp(BladeStarlightColorModeIndex, 0, BladeStarlightColorModes.Length - 1)]);
        return ApplyBladeLightingEffectAsync(effect, cancellationToken);
    }

    public async Task ApplyBladePerformanceModeAsync(CancellationToken cancellationToken = default)
    {
        if (!_blade._canSetBladePerformanceMode ||
            BladePerformanceModeIndex < 0 || BladePerformanceModeIndex >= BladePerformanceModes.Length)
        {
            return;
        }

        var requested = BladePerformanceModes[BladePerformanceModeIndex];
        await RunDeviceOperationAsync(AppStrings.Text("Text_98C30F5D"), async () =>
        {
            if (!IsSelectedPerformancePowerActive)
            {
                EditablePerformanceBladeProfile.PerformanceMode = (byte)requested;
                SetBladePerformanceMode(requested, confirm: false);
                if (!await SaveProfileAsync(cancellationToken))
                {
                    throw new InvalidOperationException(AppStrings.Text("Text_CC12A6F5"));
                }

                RefreshBladePerformanceEditor();
                return;
            }

            var actual = await _deviceTelemetryReader.SetBladePerformanceModeAsync(
                _deviceDescriptors, requested, cancellationToken);
            SetBladePerformanceMode(actual);
            EditablePerformanceBladeProfile.PerformanceMode = (byte)actual;
            await SaveProfileAsync(cancellationToken);
            RequestDeviceRefresh();
            BladePerformanceModeChangedByUser?.Invoke(actual);
        }, cancellationToken, () =>
            BladePerformanceModeIndex = _blade._confirmedBladePerformanceModeIndex);
    }

    public async Task ApplyBladeChargeLimitAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSetBladeChargeLimit ||
            BladeChargeLimitIndex < 0 || BladeChargeLimitIndex >= BladeChargeLimits.Length)
        {
            return;
        }

        await RunDeviceOperationAsync(AppStrings.Text("Text_DEE979FD"), async () =>
        {
            var actual = await _deviceTelemetryReader.SetBladeChargeLimitAsync(
                _deviceDescriptors, BladeChargeLimits[BladeChargeLimitIndex], cancellationToken);
            SetBladeChargeLimit(actual);
            _profile.Global.Blade.ChargeLimitPercent = actual;
            await SaveProfileAsync(cancellationToken);
        }, cancellationToken, () =>
            BladeChargeLimitIndex = _blade._confirmedBladeChargeLimitIndex);
    }

    public async Task ApplyBladeCpuBoostAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSetBladeCpuBoost || BladeCpuBoostIndex < 0 || BladeCpuBoostIndex >= BladeCpuBoostModes.Length)
        {
            return;
        }

        await RunDeviceOperationAsync("CPU Boost", async () =>
        {
            var previousProfile = _profile.Clone();
            var actual = await _deviceTelemetryReader.SetBladeCpuBoostModeAsync(
                _deviceDescriptors, BladeCpuBoostModes[BladeCpuBoostIndex], cancellationToken);
            SetBladeCpuBoost(actual);
            _profile.Global.Blade.CpuBoostMode = (byte)actual;
            if (!await SaveProfileAsync(cancellationToken))
            {
                _profile = previousProfile;
                throw new InvalidOperationException(AppStrings.Text("Text_A257CB56"));
            }
        }, cancellationToken, () => BladeCpuBoostIndex = _blade._confirmedBladeCpuBoostIndex);
    }

    public async Task ApplyBladeGpuBoostAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSetBladeGpuBoost || BladeGpuBoostIndex < 0 || BladeGpuBoostIndex >= BladeGpuBoostModes.Length)
        {
            return;
        }

        await RunDeviceOperationAsync("GPU Boost", async () =>
        {
            var previousProfile = _profile.Clone();
            var actual = await _deviceTelemetryReader.SetBladeGpuBoostModeAsync(
                _deviceDescriptors, BladeGpuBoostModes[BladeGpuBoostIndex], cancellationToken);
            SetBladeGpuBoost(actual);
            _profile.Global.Blade.GpuBoostMode = (byte)actual;
            if (!await SaveProfileAsync(cancellationToken))
            {
                _profile = previousProfile;
                throw new InvalidOperationException(AppStrings.Text("Text_AB07F412"));
            }
        }, cancellationToken, () => BladeGpuBoostIndex = _blade._confirmedBladeGpuBoostIndex);
    }

    public async Task ApplyBladeMaxFanAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSetBladeMaxFan)
        {
            return;
        }

        await RunDeviceOperationAsync("Max Fan", async () =>
        {
            var requested = BladeMaxFanEnabled ? BladeMaxFanMode.Enabled : BladeMaxFanMode.Disabled;
            var actual = await _deviceTelemetryReader.SetBladeMaxFanModeAsync(
                _deviceDescriptors, requested, cancellationToken);
            SetBladeMaxFan(actual);
            _profile.Global.Blade.MaxFanMode = (byte)actual;
            await SaveProfileAsync(cancellationToken);
        }, cancellationToken, () => BladeMaxFanEnabled = _blade._confirmedBladeMaxFanEnabled);
    }

    public async Task ApplyBladeLogoAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSetBladeLogo || BladeLogoIndex < 0 || BladeLogoIndex >= BladeLogoModes.Length)
        {
            return;
        }

        await RunDeviceOperationAsync(AppStrings.Text("Text_7D07A709"), async () =>
        {
            var previousProfile = _profile.Clone();
            var actual = await _deviceTelemetryReader.SetBladeLogoModeAsync(
                _deviceDescriptors, BladeLogoModes[BladeLogoIndex], cancellationToken);
            SetBladeLogo(actual);
            _profile.Global.Blade.LogoMode = (byte)actual;
            if (!await SaveProfileAsync(cancellationToken))
            {
                _profile = previousProfile;
                throw new InvalidOperationException(AppStrings.Text("Text_359F8C1A"));
            }
        }, cancellationToken, () => BladeLogoIndex = _blade._confirmedBladeLogoIndex);
    }

    public async Task ToggleBladeTouchpadAsync(CancellationToken cancellationToken = default)
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
                BladeTouchpadEnabled = actual;
                _blade._confirmedBladeTouchpadEnabled = actual;
                BladeTouchpadText = actual ? AppStrings.Text("Text_F55AD712") : AppStrings.Text("Text_7E3B0F3C");
                BladeTouchpadChangedByUser?.Invoke(actual);
            },
            cancellationToken,
            () =>
            {
                BladeTouchpadEnabled = _blade._confirmedBladeTouchpadEnabled;
                BladeTouchpadText = _blade._confirmedBladeTouchpadEnabled ? AppStrings.Text("Text_F55AD712") : AppStrings.Text("Text_7E3B0F3C");
            },
            successVerb: AppStrings.Text("Text_5CF73A23"),
            failureVerb: AppStrings.Text("Text_B9EC9D4C"));
    }

    internal async Task CycleBladePerformanceModeAsync(CancellationToken cancellationToken = default)
    {
        await RunDeviceOperationAsync(AppStrings.Text("Text_98C30F5D"), async () =>
        {
            if (!_blade._canSetBladePerformanceMode || _blade._confirmedBladePerformanceModeIndex < 0)
            {
                throw new InvalidOperationException(AppStrings.Text("Text_2263C356"));
            }

            var nextMode = BladePerformanceModeCycle.GetNext(
                BladePerformanceModes[_blade._confirmedBladePerformanceModeIndex],
                BladePerformanceModes,
                _bladePerformanceCycleModes);
            BladePerformanceModeIndex = Array.IndexOf(BladePerformanceModes, nextMode);
            var actual = await _deviceTelemetryReader.SetBladePerformanceModeAsync(
                _deviceDescriptors,
                BladePerformanceModes[BladePerformanceModeIndex],
                cancellationToken);
            SetBladePerformanceMode(actual);
            (CurrentPowerOverrides?.Blade ?? GetActiveProfile().Global.Blade).PerformanceMode = (byte)actual;
            await SaveProfileAsync(cancellationToken);
            RequestDeviceRefresh();
            BladePerformanceModeChangedByUser?.Invoke(actual);
        }, cancellationToken, () =>
            BladePerformanceModeIndex = _blade._confirmedBladePerformanceModeIndex);
    }

    internal async Task ToggleBladeGamingModeAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSetBladeGamingMode)
        {
            return;
        }

        await RunDeviceOperationAsync(AppStrings.Text("Text_EED40F9C"), async () =>
        {
            var current = _blade._bladeGameModeState is byte state && state != 2
                ? state
                : throw new InvalidOperationException(AppStrings.Text("Text_06C879B7"));
            var actual = await SetBladeGameModeCoreAsync(
                current == 0,
                cancellationToken);
            SetBladeGameMode(actual);
            RequestDeviceRefresh();
            BladeGamingModeChangedByUser?.Invoke(actual.GameMode != 0);
        }, cancellationToken);
    }

    public async Task ApplyBladeGamingModeAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApplyBladeGamingMode)
        {
            return;
        }

        await RunDeviceOperationAsync(AppStrings.Text("Text_EED40F9C"), async () =>
        {
            var actual = await SetBladeGameModeCoreAsync(
                BladeGameModeEnabled,
                cancellationToken);
            SetBladeGameMode(actual);
            RequestDeviceRefresh();
            BladeGamingModeChangedByUser?.Invoke(actual.GameMode != 0);
        }, cancellationToken, () =>
            BladeGameModeEnabled = _blade._bladeGameModeState != 0);
    }

    public async Task ApplyBladeStartupAnimationAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApplyBladeStartupAnimation)
        {
            return;
        }

        await RunDeviceOperationAsync(AppStrings.Text("Text_AA91A1FF"), async () =>
        {
            var actual = await _deviceTelemetryReader.SetBladeStartupAnimationAsync(
                _deviceDescriptors, BladeStartupAnimationEnabled, cancellationToken);
            _blade._bladeStartupAnimationEnabled = actual;
            BladeStartupAnimationText = FormatOptionalState(actual);
            BladeStartupAnimationEnabled = actual;
            RequestDeviceRefresh();
        }, cancellationToken, () =>
            BladeStartupAnimationEnabled = _blade._bladeStartupAnimationEnabled ?? false);
    }

    internal async Task CycleInternalDisplayRefreshRateAsync(CancellationToken cancellationToken = default)
    {
        await RunDeviceOperationAsync(AppStrings.Text("Text_8423206D"), async () =>
        {
            if (!_canSetInternalDisplayRefreshRate ||
                _internalDisplayController is null ||
                InternalDisplayRefreshRates.Count == 0)
            {
                throw new InvalidOperationException(AppStrings.Text("Text_36D4C146"));
            }

            var includedRates = _internalDisplayRefreshRateCycleHertz is { Count: > 0 }
                ? _internalDisplayRefreshRateCycleHertz
                : InternalDisplayRefreshRates.ToHashSet();
            InternalDisplayRefreshRateHertz = BladePerformanceModeCycle.GetNext(
                _confirmedInternalDisplayRefreshRateHertz,
                InternalDisplayRefreshRates,
                includedRates);
            var snapshot = _internalDisplayController.SetRefreshRate(
                InternalDisplayRefreshRateHertz);
            ApplyInternalDisplaySnapshot(snapshot);
            (CurrentPowerOverrides?.Blade ?? GetActiveProfile().Global.Blade).RefreshRateHertz =
                snapshot.RefreshRateHertz;
            await SaveProfileAsync(cancellationToken);
            InternalDisplayRefreshRateChangedByUser?.Invoke(snapshot.RefreshRateHertz);
        }, cancellationToken, () =>
            InternalDisplayRefreshRateHertz = _confirmedInternalDisplayRefreshRateHertz);
    }

    internal Task StepBladeBrightnessAsync(
        bool increase,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_blade._canSetBladeBrightness)
        {
            throw new InvalidOperationException(AppStrings.Text("Text_F00EAF88"));
        }

        if (Volatile.Read(ref _displayAvailable) == 0)
        {
            var current = ToBladeBrightness(BladeBrightnessPercent);
            var requested = (byte)Math.Clamp(current + (increase ? 16 : -16), 0, 255);
            if (requested == current)
            {
                return Task.CompletedTask;
            }
            BladeBrightnessPercent = Math.Round(
                requested * 100d / 255,
                MidpointRounding.AwayFromZero);
            (CurrentPowerOverrides?.Blade ?? GetActiveProfile().Global.Blade).KeyboardBrightness = requested;
            return SaveProfileAsync(cancellationToken);
        }

        lock (_bladeBrightnessGate)
        {
            var current = _desiredBladeBrightness ?? ToBladeBrightness(BladeBrightnessPercent);
            var requested = (byte)Math.Clamp(current + (increase ? 16 : -16), 0, 255);
            if (requested == current)
            {
                return Task.CompletedTask;
            }

            _desiredBladeBrightness = requested;
            BladeBrightnessPercent = Math.Round(
                requested * 100d / 255,
                MidpointRounding.AwayFromZero);
            Interlocked.Increment(ref _bladeBrightnessVerificationGeneration);
            if (!_bladeBrightnessWriterActive)
            {
                _bladeBrightnessWriterActive = true;
                _bladeBrightnessWriter = WriteDesiredBladeBrightnessAsync();
            }
        }
        return Task.CompletedTask;
    }

    private async Task WriteDesiredBladeBrightnessAsync()
    {
        try
        {
            await WriteDesiredBladeBrightnessCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            lock (_bladeBrightnessGate)
            {
                _bladeBrightnessWriterActive = false;
                if (_desiredBladeBrightness is not null && Volatile.Read(ref _disposed) == 0)
                {
                    _bladeBrightnessWriterActive = true;
                    _bladeBrightnessWriter = WriteDesiredBladeBrightnessAsync();
                }
            }
        }
    }

    private async Task WriteDesiredBladeBrightnessCoreAsync()
    {
        byte? lastWritten = null;
        while (Volatile.Read(ref _disposed) == 0)
        {
            byte requested;
            lock (_bladeBrightnessGate)
            {
                if (_desiredBladeBrightness is not byte desired)
                {
                    break;
                }
                requested = desired;
            }

            var original = ToBladeBrightness(_blade._confirmedBladeBrightnessPercent);
            var applied = false;
            await RunDeviceOperationAsync(AppStrings.Text("Text_5F0C27DB"), async () =>
            {
                if (Volatile.Read(ref _displayAvailable) == 0)
                {
                    lock (_bladeBrightnessGate)
                    {
                        if (_desiredBladeBrightness == requested)
                        {
                            _desiredBladeBrightness = null;
                        }
                    }
                    (CurrentPowerOverrides?.Blade ?? GetActiveProfile().Global.Blade)
                        .KeyboardBrightness = requested;
                    SetBladeBrightness(requested, confirm: false);
                    await SaveProfileAsync(CancellationToken.None);
                    return;
                }

                try
                {
                    await _deviceTelemetryReader.SetBladeKeyboardBrightnessAsync(
                        _deviceDescriptors,
                        requested,
                        CancellationToken.None,
                        verifyReadback: false);
                }
                catch (Exception writeError) when (IsExpectedRuntimeException(writeError))
                {
                    try
                    {
                        var restored = await _deviceTelemetryReader.SetBladeKeyboardBrightnessAsync(
                            _deviceDescriptors,
                            original,
                            CancellationToken.None);
                        lock (_bladeBrightnessGate)
                        {
                            _desiredBladeBrightness = null;
                        }
                        SetBladeBrightness(restored);
                    }
                    catch (Exception restoreError) when (IsExpectedRuntimeException(restoreError))
                    {
                        throw new AggregateException(writeError, restoreError);
                    }

                    throw;
                }

                lock (_bladeBrightnessGate)
                {
                    if (_desiredBladeBrightness == requested)
                    {
                        _desiredBladeBrightness = null;
                    }
                }
                SetBladeBrightness(requested);
                lastWritten = requested;
                applied = true;
            }, CancellationToken.None, () =>
            {
                lock (_bladeBrightnessGate)
                {
                    _desiredBladeBrightness = null;
                }
                BladeBrightnessPercent = _blade._confirmedBladeBrightnessPercent;
            }, successVerb: AppStrings.Text("Text_D25102CE"));

            if (!applied)
            {
                return;
            }
        }

        if (lastWritten is byte persistedBrightness &&
            Volatile.Read(ref _displayAvailable) == 0)
        {
            (CurrentPowerOverrides?.Blade ?? GetActiveProfile().Global.Blade)
                .KeyboardBrightness = persistedBrightness;
            await SaveProfileAsync(CancellationToken.None);
            return;
        }

        if (lastWritten is not null && Volatile.Read(ref _disposed) == 0)
        {
            ScheduleBladeBrightnessVerification();
        }
    }

    private void ScheduleBladeBrightnessVerification()
    {
        var generation = Interlocked.Increment(ref _bladeBrightnessVerificationGeneration);
        _bladeBrightnessVerification = VerifyBladeBrightnessAfterIdleAsync(generation);
    }

    private async Task VerifyBladeBrightnessAfterIdleAsync(long generation)
    {
        await Task.Delay(BladeBrightnessVerificationDelay);
        if (generation != Volatile.Read(ref _bladeBrightnessVerificationGeneration) ||
            Volatile.Read(ref _displayAvailable) == 0 ||
            Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        await RunDeviceOperationAsync(AppStrings.Text("Text_5F0C27DB"), async () =>
        {
            bool hasPendingBrightness;
            lock (_bladeBrightnessGate)
            {
                hasPendingBrightness = _desiredBladeBrightness is not null;
            }
            if (generation != Volatile.Read(ref _bladeBrightnessVerificationGeneration) ||
                Volatile.Read(ref _displayAvailable) == 0 ||
                hasPendingBrightness)
            {
                return;
            }

            var actual = await _deviceTelemetryReader.ReadBladeKeyboardBrightnessAsync(
                _deviceDescriptors,
                CancellationToken.None);
            if (generation != Volatile.Read(ref _bladeBrightnessVerificationGeneration) ||
                Volatile.Read(ref _displayAvailable) == 0)
            {
                return;
            }
            SetBladeBrightness(actual);
            _profile.Global.Blade.KeyboardBrightness = actual;
            await SaveProfileAsync(CancellationToken.None);
        }, CancellationToken.None, () =>
            RequestDeviceRefresh());
    }

    private static byte ToBladeBrightness(double percent) => checked((byte)Math.Round(
        Math.Clamp(percent, 0, 100) * 255 / 100,
        MidpointRounding.AwayFromZero));

    internal async Task ToggleBladeOneTimeFullChargeAsync(
        CancellationToken cancellationToken = default)
    {
        await RunDeviceOperationAsync(AppStrings.Text("Text_6F8CB285"), async () =>
        {
            var current = _blade._bladeOneTimeFullChargeEnabled ??
                throw new InvalidOperationException(AppStrings.Text("Text_8F0D00AE"));
            var actual = await _deviceTelemetryReader.SetBladeOneTimeFullChargeAsync(
                _deviceDescriptors,
                !current,
                cancellationToken);
            _blade._bladeOneTimeFullChargeEnabled = actual;
            BladeOneTimeFullChargeEnabled = actual;
            BladeOneTimeFullChargeText = FormatOptionalState(actual);
            RequestDeviceRefresh();
            BladeOneTimeFullChargeChangedByUser?.Invoke(actual);
        }, cancellationToken);
    }

    public async Task ApplyBladeOneTimeFullChargeAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApplyBladeOneTimeFullCharge)
        {
            return;
        }

        await RunDeviceOperationAsync(AppStrings.Text("Text_6F8CB285"), async () =>
        {
            var actual = await _deviceTelemetryReader.SetBladeOneTimeFullChargeAsync(
                _deviceDescriptors, BladeOneTimeFullChargeEnabled, cancellationToken);
            _blade._bladeOneTimeFullChargeEnabled = actual;
            BladeOneTimeFullChargeText = FormatOptionalState(actual);
            BladeOneTimeFullChargeEnabled = actual;
            RequestDeviceRefresh();
            BladeOneTimeFullChargeChangedByUser?.Invoke(actual);
        }, cancellationToken, () =>
            BladeOneTimeFullChargeEnabled = _blade._bladeOneTimeFullChargeEnabled ?? false);
    }

    public string BladeDeviceName { get => _blade._bladeDeviceName; private set => SetField(ref _blade._bladeDeviceName, value); }
    public string BladeStatusText { get => _blade._bladeStatusText; private set => SetField(ref _blade._bladeStatusText, value); }
    public string BladeBrightnessText { get => _blade._bladeBrightnessText; private set => SetField(ref _blade._bladeBrightnessText, value); }
    public string BladeBrightnessSelectionText { get => _blade._bladeBrightnessSelectionText; private set => SetField(ref _blade._bladeBrightnessSelectionText, value); }
    public double BladeBrightnessPercent
    {
        get => _blade._bladeBrightnessPercent;
        set
        {
            if (SetField(ref _blade._bladeBrightnessPercent, Math.Clamp(value, 0, 100)))
            {
                BladeBrightnessSelectionText = $"{_blade._bladeBrightnessPercent:0}%";
            }
        }
    }
    public bool CanSetBladeBrightness
    {
        get => _blade._canSetBladeBrightness;
        private set
        {
            if (SetField(ref _blade._canSetBladeBrightness, value))
            {
                OnPropertyChanged(nameof(CanSetBladeLighting));
                OnPropertyChanged(nameof(CanSetBladeLightingPowerProfile));
            }
        }
    }
    public string BladePerformanceModeText { get => _blade._bladePerformanceModeText; private set => SetField(ref _blade._bladePerformanceModeText, value); }
    public IReadOnlyList<string> BladePerformanceModeOptions => AppStrings.Texts("Text_9753B259", "Text_C1DB7AE1", "Text_598C5804", "Text_60E54E25", "HyperBoost");
    public int BladePerformanceModeIndex { get => _blade._bladePerformanceModeIndex; set => SetField(ref _blade._bladePerformanceModeIndex, value); }
    public bool CanSetBladePerformanceMode
    {
        get => _blade._canSetBladePerformanceMode;
        private set
        {
            if (SetField(ref _blade._canSetBladePerformanceMode, value))
            {
                OnPropertyChanged(nameof(CanSetBladePerformancePowerProfile));
            }
        }
    }
    public bool CanSetBladeLightingPowerProfile => CanSetBladeBrightness;
    public bool CanSetBladePerformancePowerProfile => CanSetBladePerformanceMode;
    public string BladeFanText { get => _blade._bladeFanText; private set => SetField(ref _blade._bladeFanText, value); }
    public string BladeFanModeText { get => _blade._bladeFanModeText; private set => SetField(ref _blade._bladeFanModeText, value); }
    public string BladeFanTargetRpmText { get => _blade._bladeFanTargetRpmText; private set => SetField(ref _blade._bladeFanTargetRpmText, value); }
    public string BladeCurrentFanCpuRpmText { get => _blade._bladeCurrentFanCpuRpmText; private set => SetField(ref _blade._bladeCurrentFanCpuRpmText, value); }
    public string BladeCurrentFanGpuRpmText { get => _blade._bladeCurrentFanGpuRpmText; private set => SetField(ref _blade._bladeCurrentFanGpuRpmText, value); }
    public string BladeAdvancedFanCpuModeRawText { get => _blade._bladeAdvancedFanCpuModeRawText; private set => SetField(ref _blade._bladeAdvancedFanCpuModeRawText, value); }
    public string BladeAdvancedFanGpuModeRawText { get => _blade._bladeAdvancedFanGpuModeRawText; private set => SetField(ref _blade._bladeAdvancedFanGpuModeRawText, value); }
    public string BladeGameModeText { get => _blade._bladeGameModeText; private set => SetField(ref _blade._bladeGameModeText, value); }
    public bool BladeGameModeEnabled
    {
        get => _blade._bladeGameModeEnabled;
        set
        {
            if (SetField(ref _blade._bladeGameModeEnabled, value))
            {
                OnPropertyChanged(nameof(CanApplyBladeGamingMode));
            }
        }
    }
    public bool CanSetBladeGamingMode =>
        _blade._bladeGameModeWriteSupported && _blade._bladeGameModeState is byte state && state != 2;
    public bool CanApplyBladeGamingMode =>
        CanSetBladeGamingMode && BladeGameModeEnabled != (_blade._bladeGameModeState != 0);
    public string BladeStartupAnimationText { get => _blade._bladeStartupAnimationText; private set => SetField(ref _blade._bladeStartupAnimationText, value); }
    public bool BladeStartupAnimationEnabled
    {
        get => _blade._bladeStartupAnimationSelection;
        set
        {
            if (SetField(ref _blade._bladeStartupAnimationSelection, value))
            {
                OnPropertyChanged(nameof(CanApplyBladeStartupAnimation));
            }
        }
    }
    public bool CanSetBladeStartupAnimation => _blade._bladeStartupAnimationEnabled is not null;
    public bool CanApplyBladeStartupAnimation =>
        CanSetBladeStartupAnimation && BladeStartupAnimationEnabled != _blade._bladeStartupAnimationEnabled;
    public string BladeNativeDisplayModeText { get => _blade._bladeNativeDisplayModeText; private set => SetField(ref _blade._bladeNativeDisplayModeText, value); }
    public string BladeSkuHardwareText { get => _blade._bladeSkuHardwareText; private set => SetField(ref _blade._bladeSkuHardwareText, value); }
    public string BladeLocalDimmingText { get => _blade._bladeLocalDimmingText; private set => SetField(ref _blade._bladeLocalDimmingText, value); }
    public string BladeOneTimeFullChargeText { get => _blade._bladeOneTimeFullChargeText; private set => SetField(ref _blade._bladeOneTimeFullChargeText, value); }
    public bool BladeOneTimeFullChargeEnabled
    {
        get => _blade._bladeOneTimeFullChargeSelection;
        set
        {
            if (SetField(ref _blade._bladeOneTimeFullChargeSelection, value))
            {
                OnPropertyChanged(nameof(CanApplyBladeOneTimeFullCharge));
            }
        }
    }
    public bool CanSetBladeOneTimeFullCharge =>
        _blade._bladeOneTimeFullChargeEnabled is not null && _blade._confirmedBladeChargeLimitIndex >= 0 &&
        BladeChargeLimits[_blade._confirmedBladeChargeLimitIndex] < 100;
    public bool CanApplyBladeOneTimeFullCharge =>
        CanSetBladeOneTimeFullCharge && BladeOneTimeFullChargeEnabled != _blade._bladeOneTimeFullChargeEnabled;
    public string BladeChargeLimitText { get => _blade._bladeChargeLimitText; private set => SetField(ref _blade._bladeChargeLimitText, value); }
    public IReadOnlyList<string> BladeChargeLimitOptions => AppStrings.Texts("50%", "55%", "60%", "65%", "70%", "75%", "80%", "Text_0E45FC12");
    public int BladeChargeLimitIndex { get => _blade._bladeChargeLimitIndex; set => SetField(ref _blade._bladeChargeLimitIndex, value); }
    public bool CanSetBladeChargeLimit { get => _blade._canSetBladeChargeLimit; private set => SetField(ref _blade._canSetBladeChargeLimit, value); }
    public IReadOnlyList<string> BladeCpuBoostOptions => AppStrings.Texts("Text_CB5F70D1", "Text_28619638", "Text_5C1F32A7", "Boost", "Text_BAA7D10B");
    public string BladeCpuBoostText { get => _blade._bladeCpuBoostText; private set => SetField(ref _blade._bladeCpuBoostText, value); }
    public int BladeCpuBoostIndex { get => _blade._bladeCpuBoostIndex; set => SetField(ref _blade._bladeCpuBoostIndex, value); }
    public bool CanSetBladeCpuBoost => _blade._hasBladeCpuBoost && IsBladeCustomMode;
    public IReadOnlyList<string> BladeGpuBoostOptions => AppStrings.Texts("Text_CB5F70D1", "Text_28619638", "Text_5C1F32A7");
    public string BladeGpuBoostText { get => _blade._bladeGpuBoostText; private set => SetField(ref _blade._bladeGpuBoostText, value); }
    public int BladeGpuBoostIndex { get => _blade._bladeGpuBoostIndex; set => SetField(ref _blade._bladeGpuBoostIndex, value); }
    public bool CanSetBladeGpuBoost => _blade._hasBladeGpuBoost && IsBladeCustomMode;
    public string BladeMaxFanText { get => _blade._bladeMaxFanText; private set => SetField(ref _blade._bladeMaxFanText, value); }
    public bool BladeMaxFanEnabled { get => _blade._bladeMaxFanEnabled; set => SetField(ref _blade._bladeMaxFanEnabled, value); }
    public bool CanSetBladeMaxFan => _blade._hasBladeMaxFan && IsBladeCustomMode;
    public Visibility BladeCustomPerformanceVisibility => IsBladeCustomMode
        ? Visibility.Visible
        : Visibility.Collapsed;
    public IReadOnlyList<string> BladeLogoOptions => AppStrings.Texts("Text_39B523BD", "Text_6295D9CB", "Text_7EDA32B9");
    public string BladeLogoText { get => _blade._bladeLogoText; private set => SetField(ref _blade._bladeLogoText, value); }
    public int BladeLogoIndex { get => _blade._bladeLogoIndex; set => SetField(ref _blade._bladeLogoIndex, value); }
    public bool CanSetBladeLogo { get => _blade._canSetBladeLogo; private set => SetField(ref _blade._canSetBladeLogo, value); }
    public string BladeTouchpadText { get => _blade._bladeTouchpadText; private set => SetField(ref _blade._bladeTouchpadText, value); }
    public bool BladeTouchpadEnabled { get => _blade._bladeTouchpadEnabled; private set => SetField(ref _blade._bladeTouchpadEnabled, value); }
    public bool CanSetBladeTouchpad => _blade._canSetBladeTouchpad;
    public IReadOnlyList<string> BladeLightingModeOptions => AppStrings.Texts(
        AppStrings.Text("Text_39B523BD"), AppStrings.Text("Text_8FF9138F"), AppStrings.Text("Text_7EDA32B9"), AppStrings.Text("Text_21BE26F0"), AppStrings.Text("Text_165FCE9D"), AppStrings.Text("Text_5DCB722E"), AppStrings.Text("Text_E2309DE4"), AppStrings.Text("Text_D1D985CC"), AppStrings.Text("Text_F3845B2A"), AppStrings.Text("Text_33F4E277"), AppStrings.Text("Text_2A3A07A1"), AppStrings.Text("Text_51212D25"), AppStrings.Text("Text_388555B3"));
    public IReadOnlyList<string> BladeLightingPowerProfileOptions =>
        [
            AppStrings.Text("BladeLightingPowerCurrent"),
            AppStrings.Text("BladeLightingPowerPluggedIn"),
            AppStrings.Text("BladeLightingPowerBattery"),
        ];
    public IReadOnlyList<string> BladePerformancePowerProfileOptions => BladeLightingPowerProfileOptions;
    public IReadOnlyList<string> BladeRefreshRatePowerProfileOptions => BladeLightingPowerProfileOptions;
    public int BladeLightingPowerProfileIndex
    {
        get => _bladeLightingPowerProfileIndex;
        set
        {
            var next = Math.Clamp(value, 0, 2);
            if (SetField(ref _bladeLightingPowerProfileIndex, next))
            {
                RefreshBladeLightingEditor();
            }
        }
    }
    public int BladePerformancePowerProfileIndex
    {
        get => _bladePerformancePowerProfileIndex;
        set
        {
            var next = Math.Clamp(value, 0, 2);
            if (SetField(ref _bladePerformancePowerProfileIndex, next))
            {
                RefreshBladePerformanceEditor();
            }
        }
    }
    public int BladeRefreshRatePowerProfileIndex
    {
        get => _bladeRefreshRatePowerProfileIndex;
        set
        {
            var next = Math.Clamp(value, 0, 2);
            if (SetField(ref _bladeRefreshRatePowerProfileIndex, next))
            {
                RefreshInternalDisplayRateEditor();
            }
        }
    }
    public int BladeLightingModeIndex
    {
        get => _blade._bladeLightingModeIndex;
        set
        {
            if (SetField(ref _blade._bladeLightingModeIndex, value))
            {
                OnPropertyChanged(nameof(BladeLightingColorVisibility));
                OnPropertyChanged(nameof(BladeLightingSecondColorVisibility));
                OnPropertyChanged(nameof(BladeWaveDirectionVisibility));
                OnPropertyChanged(nameof(BladeReactiveSpeedVisibility));
                OnPropertyChanged(nameof(BladeStarlightSpeedVisibility));
                OnPropertyChanged(nameof(BladeStarlightColorModeVisibility));
            }
        }
    }
    private BladeLightingMode? SelectedBladeLightingMode =>
        BladeLightingModeIndex >= 0 && BladeLightingModeIndex < BladeLightingModes.Length
            ? BladeLightingModes[BladeLightingModeIndex]
            : null;
    public Visibility BladeLightingColorVisibility => SelectedBladeLightingMode is
        BladeLightingMode.Static or BladeLightingMode.Breathing or BladeLightingMode.Reactive or
        BladeLightingMode.Ripple or BladeLightingMode.Tidal ||
        (SelectedBladeLightingMode == BladeLightingMode.Starlight && BladeStarlightColorModeIndex != 0)
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility BladeLightingSecondColorVisibility => SelectedBladeLightingMode == BladeLightingMode.Tidal ||
        (SelectedBladeLightingMode == BladeLightingMode.Starlight && BladeStarlightColorModeIndex == 2)
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility BladeWaveDirectionVisibility => SelectedBladeLightingMode is BladeLightingMode.Wave or BladeLightingMode.Wheel
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility BladeReactiveSpeedVisibility => SelectedBladeLightingMode == BladeLightingMode.Reactive
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility BladeStarlightSpeedVisibility => SelectedBladeLightingMode == BladeLightingMode.Starlight
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility BladeStarlightColorModeVisibility => SelectedBladeLightingMode == BladeLightingMode.Starlight
        ? Visibility.Visible
        : Visibility.Collapsed;
    public IReadOnlyList<string> BladeWaveDirectionOptions => AppStrings.Texts("Text_FB3FF0D8", "Text_883A50D7");
    public int BladeWaveDirectionIndex { get => _blade._bladeWaveDirectionIndex; set => SetField(ref _blade._bladeWaveDirectionIndex, value); }
    public IReadOnlyList<byte> BladeReactiveSpeedOptions => BladeReactiveSpeeds;
    public int BladeReactiveSpeedIndex { get => _blade._bladeReactiveSpeedIndex; set => SetField(ref _blade._bladeReactiveSpeedIndex, value); }
    public IReadOnlyList<byte> BladeStarlightSpeedOptions => BladeStarlightSpeeds;
    public int BladeStarlightSpeedIndex { get => _blade._bladeStarlightSpeedIndex; set => SetField(ref _blade._bladeStarlightSpeedIndex, value); }
    public IReadOnlyList<string> BladeStarlightColorModeOptions => AppStrings.Texts("BladeStarlightRandom", "BladeStarlightSingle", "BladeStarlightDual");
    public int BladeStarlightColorModeIndex { get => _blade._bladeStarlightColorModeIndex; set { if (SetField(ref _blade._bladeStarlightColorModeIndex, value)) { OnPropertyChanged(nameof(BladeLightingColorVisibility)); OnPropertyChanged(nameof(BladeLightingSecondColorVisibility)); } } }
    public Color BladeLightingColor { get => _blade._bladeLightingColor; set => SetField(ref _blade._bladeLightingColor, value); }
    public Color BladeLightingSecondColor { get => _blade._bladeLightingSecondColor; set => SetField(ref _blade._bladeLightingSecondColor, value); }
    public bool CanSetBladeLighting => _blade._canSetBladeBrightness && _bladeLightingController is not null;
    private async Task<BladeGameModeTelemetry> SetBladeGameModeCoreAsync(
        bool enabled,
        CancellationToken cancellationToken)
    {
        try
        {
            var previousProfileValue = _profile.Global.Blade.GamingModeEnabled;
            var previousEnabled = _blade._bladeGameModeState != 0;
            var actual = await _deviceTelemetryReader.SetBladeGameModeAsync(
                _deviceDescriptors,
                enabled,
                cancellationToken);
            _profile.Global.Blade.GamingModeEnabled = actual.GameMode != 0;
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
                _profile.Global.Blade.GamingModeEnabled = previousProfileValue;
                await _deviceTelemetryReader.SetBladeGameModeAsync(
                    _deviceDescriptors,
                    previousEnabled,
                    CancellationToken.None);
                throw;
            }
        }
        catch (NotSupportedException)
        {
            _blade._bladeGameModeWriteSupported = false;
            OnPropertyChanged(nameof(CanSetBladeGamingMode));
            OnPropertyChanged(nameof(CanApplyBladeGamingMode));
            throw;
        }
    }
}
