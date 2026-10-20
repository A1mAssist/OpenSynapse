using Microsoft.UI.Xaml;
using Windows.UI;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;
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
        var requested = ToBladeBrightness(BladeBrightnessPercent);
        if (!IsSelectedLightingPowerActive || Volatile.Read(ref _displayAvailable) == 0)
        {
            EditableLightingBladeProfile.KeyboardBrightness = requested;
            SetBladeBrightness(requested, confirm: false);
            RefreshBladeLightingEditor();
            await SaveProfileAsync(cancellationToken);
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

    internal Task QueueBladeBrightnessAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_blade._canSetBladeBrightness)
        {
            return Task.CompletedTask;
        }

        var requested = ToBladeBrightness(BladeBrightnessPercent);
        if (!IsSelectedLightingPowerActive)
        {
            EditableLightingBladeProfile.KeyboardBrightness = requested;
            SetBladeBrightness(requested, confirm: false);
            RefreshBladeLightingEditor();
            return SaveProfileAsync(cancellationToken);
        }

        lock (_bladeBrightnessGate)
        {
            _desiredBladeBrightness = requested;
            Interlocked.Increment(ref _bladeBrightnessVerificationGeneration);
            if (!_bladeBrightnessWriterActive)
            {
                _bladeBrightnessWriterActive = true;
                _bladeBrightnessWriter = WriteDesiredBladeBrightnessAsync();
            }
        }
        return Task.CompletedTask;
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
            () => ApplyBladeLightingEffectCoreAsync(effect, cancellationToken),
            cancellationToken,
            successVerb: AppStrings.Text("Text_945C2E42"));
    }

    private async Task ApplyBladeLightingEffectCoreAsync(
        BladeLightingEffect effect,
        CancellationToken cancellationToken)
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

        if (!BladeLightingEnabled)
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

        await _bladeLightingController!.ApplyAsync(
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
            var resolved = ProfileResolver.Resolve(
                _profile,
                blade,
                CurrentPowerSource);
            _bladeLightingDevicePath = blade.Id;
            _lightingShadowFingerprint = CreateLightingFingerprint(
                resolved.Lighting,
                blade.Id,
                CurrentPowerSource,
                resolved.Blade.LightingEnabled ?? true,
                resolved.Blade.ChromaOverrideEnabled ?? true);
        }
        _ = ObserveBladeLightingRuntimeAsync(
            _bladeLightingController!.RuntimeCompletion);
    }

    public Task ApplySelectedBladeLightingEffectAsync(CancellationToken cancellationToken = default)
    {
        var modes = ActiveBladeLightingModes;
        if (BladeLightingModeIndex < 0 || BladeLightingModeIndex >= modes.Count ||
            BladeWaveDirectionIndex < 0 || BladeWaveDirectionIndex >= BladeWaveDirections.Length)
        {
            return Task.CompletedTask;
        }

        if (IsBlade2023Model)
        {
            return ApplyBlade2023LightingEffectAsync(modes[BladeLightingModeIndex], cancellationToken);
        }

        var color = new RazerRgb(BladeLightingColor.R, BladeLightingColor.G, BladeLightingColor.B);
        var secondColor = new RazerRgb(
            BladeLightingSecondColor.R,
            BladeLightingSecondColor.G,
            BladeLightingSecondColor.B);
        var tertiaryColor = new RazerRgb(
            BladeLightingTertiaryColor.R,
            BladeLightingTertiaryColor.G,
            BladeLightingTertiaryColor.B);
        var mode = modes[BladeLightingModeIndex];
        if (mode == BladeLightingMode.ChromaFile && _blade._bladeChromaAnimation is null)
        {
            SetBladeChromaStatus(AppStrings.Text("BladeChromaImportRequired"));
            return Task.CompletedTask;
        }

        var palette = mode is BladeLightingMode.Wave or BladeLightingMode.Spectrum
            ? BladeLightingRenderModeIndex == 1
                ? LightingPalette.Create(color, secondColor, tertiaryColor)
                : null
            : BladeLightingPaletteResolver.Resolve(
                mode, color, secondColor, tertiaryColor, EditableLightingProfile);
        var effect = new BladeLightingEffect(
            mode,
            color,
            BladeWaveDirections[BladeWaveDirectionIndex],
            secondColor,
            BladeReactiveSpeeds[Math.Clamp(BladeReactiveSpeedIndex, 0, BladeReactiveSpeeds.Length - 1)],
            BladeStarlightSpeeds[Math.Clamp(BladeStarlightSpeedIndex, 0, BladeStarlightSpeeds.Length - 1)],
            BladeStarlightColorModes[Math.Clamp(BladeStarlightColorModeIndex, 0, BladeStarlightColorModes.Length - 1)],
            palette,
            BladeAnimationSpeeds[Math.Clamp(BladeAnimationSpeedIndex, 0, BladeAnimationSpeeds.Length - 1)],
            mode == BladeLightingMode.ChromaFile ? _blade._bladeChromaAnimation : null);
        return ApplyBladeLightingEffectAsync(effect, cancellationToken);
    }

    public async Task ImportBladeChromaFileAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (!CanSetBladeLighting || _bladeLightingController is null)
        {
            return;
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var previousMode = BladeLightingModeIndex;
        var previousAnimation = _blade._bladeChromaAnimation;
        await RunDeviceOperationAsync(
            AppStrings.Text("BladeImportChromaButton"),
            async () =>
            {
                ChromaFileAnimation animation;
                try
                {
                    var info = new FileInfo(path);
                    if (!info.Exists ||
                        info.Length > ChromaFileAnimationParser.MaximumFileSizeBytes)
                    {
                        throw new InvalidDataException("The .chroma file is unavailable or too large.");
                    }
                    var bytes = await File.ReadAllBytesAsync(path, cancellationToken)
                        .ConfigureAwait(false);
                    animation = ChromaFileAnimationStore.Import(bytes);
                }
                catch (Exception exception) when (
                    exception is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    throw new InvalidOperationException(
                        AppStrings.Text("BladeChromaImportInvalid"), exception);
                }

                if (animation.IsUniform)
                {
                    SetBladeChromaAnimation(null);
                    await ApplyBladeLightingEffectCoreAsync(
                        new BladeLightingEffect(BladeLightingMode.Static, animation.UniformColor),
                        cancellationToken).ConfigureAwait(false);
                    BladeLightingModeIndex = Array.IndexOf(
                        ActiveBladeLightingModes.ToArray(),
                        BladeLightingMode.Static);
                    BladeLightingColor = Color.FromArgb(
                        0xFF,
                        animation.UniformColor.Red,
                        animation.UniformColor.Green,
                        animation.UniformColor.Blue);
                    SetBladeChromaStatus(AppStrings.Text("BladeChromaImportUniform"));
                    return;
                }

                SetBladeChromaAnimation(animation);
                await ApplyBladeLightingEffectCoreAsync(
                    new BladeLightingEffect(
                        BladeLightingMode.ChromaFile,
                        ChromaAnimation: animation),
                    cancellationToken).ConfigureAwait(false);
                BladeLightingModeIndex = Array.IndexOf(
                    ActiveBladeLightingModes.ToArray(),
                    BladeLightingMode.ChromaFile);
                SetBladeChromaStatus(AppStrings.FormatText(
                    "BladeChromaImportApplied",
                    Path.GetFileName(path),
                    animation.Frames.Count));
            },
            cancellationToken,
            restoreSelection: () =>
            {
                BladeLightingModeIndex = previousMode;
                SetBladeChromaAnimation(previousAnimation);
            },
            successVerb: AppStrings.Text("Text_945C2E42"));
    }

    private bool _suppressBladePerformanceApply;
    private bool _bladeCoolingPadConnected;

    public Visibility HyperboostOptionVisibility =>
        _bladeCoolingPadConnected ? Visibility.Visible : Visibility.Collapsed;

    public async Task ApplyBladePerformanceModeAsync(CancellationToken cancellationToken = default)
    {
        if (_suppressBladePerformanceApply ||
            !_blade._canSetBladePerformanceMode ||
            BladePerformanceModeIndex < 0 || BladePerformanceModeIndex >= BladePerformanceModes.Length)
        {
            return;
        }

        var requested = BladePerformanceModes[BladePerformanceModeIndex];
        bool coolingPadConnected;
        try
        {
            coolingPadConnected = requested != BladePerformanceMode.Hyperboost ||
                await RazerCoolingPad.IsConnectedAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (!coolingPadConnected)
        {
            _suppressBladePerformanceApply = true;
            try
            {
                BladePerformanceModeIndex = _blade._confirmedBladePerformanceModeIndex;
            }
            finally
            {
                _suppressBladePerformanceApply = false;
            }
            SetDeviceOperationError(AppStrings.Text("BladeHyperboostRequiresPad"));
            return;
        }
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
        var cpuBoostModes = BladeModelCapabilities.CpuBoostModesFor(_blade._bladeProductId);
        if (!CanSetBladeCpuBoost || BladeCpuBoostIndex < 0 ||
            BladeCpuBoostIndex >= cpuBoostModes.Count)
        {
            return;
        }

        await RunDeviceOperationAsync("CPU Boost", async () =>
        {
            var previousProfile = _profile.Clone();
            var actual = await _deviceTelemetryReader.SetBladeCpuBoostModeAsync(
                _deviceDescriptors, cpuBoostModes[BladeCpuBoostIndex], cancellationToken);
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

            // Max Fan lives in the Custom performance card, so it follows the performance
            // power profile the same way the performance mode itself does. Writing the
            // global slot would silently drop a per-power-source setting.
            if (!IsSelectedPerformancePowerActive)
            {
                EditablePerformanceBladeProfile.MaxFanMode = (byte)requested;
                SetBladeMaxFan(requested);
                if (!await SaveProfileAsync(cancellationToken))
                {
                    throw new InvalidOperationException(AppStrings.Text("Text_CC12A6F5"));
                }

                return;
            }

            var previousProfile = _profile.Clone();
            var actual = await _deviceTelemetryReader.SetBladeMaxFanModeAsync(
                _deviceDescriptors, requested, cancellationToken);
            SetBladeMaxFan(actual);
            EditablePerformanceBladeProfile.MaxFanMode = (byte)actual;
            if (!await SaveProfileAsync(cancellationToken))
            {
                _profile = previousProfile;
                throw new InvalidOperationException(AppStrings.Text("Text_AB07F412"));
            }

            RequestDeviceRefresh();
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
            var requested = BladeLogoModes[BladeLogoIndex];
            var target = EditableLogoBladeProfile;
            if (!IsSelectedLogoPowerActive)
            {
                target.LogoMode = (byte)requested;
                SetBladeLogo(requested, confirm: false);
                if (!await SaveProfileAsync(cancellationToken))
                {
                    _profile = previousProfile;
                    throw new InvalidOperationException(AppStrings.Text("Text_359F8C1A"));
                }

                RefreshBladeLogoEditor();
                return;
            }

            var actual = await _deviceTelemetryReader.SetBladeLogoModeAsync(
                _deviceDescriptors, requested, cancellationToken);
            SetBladeLogo(actual);
            target.LogoMode = (byte)actual;
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

            var cycleModes = _bladePerformanceCycleModes;
            if (!await RazerCoolingPad.IsConnectedAsync(cancellationToken))
            {
                cycleModes = [.. cycleModes.Where(mode => mode != BladePerformanceMode.Hyperboost)];
                if (cycleModes.Count == 0)
                {
                    cycleModes = [BladePerformanceMode.Performance];
                }
            }

            var nextMode = BladePerformanceModeCycle.GetNext(
                BladePerformanceModes[_blade._confirmedBladePerformanceModeIndex],
                BladePerformanceModes,
                cycleModes);
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

    public async Task ApplyBladeNativeDisplayModeAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApplyBladeNativeDisplayMode)
        {
            return;
        }

        await RunDeviceOperationAsync(AppStrings.Text("BladeNativeDisplayModeTitle.Text"), async () =>
        {
            var actual = await _deviceTelemetryReader.SetBladeNativeDisplayModeAsync(
                _deviceDescriptors,
                (BladeNativeDisplayMode)BladeNativeDisplayModeIndex,
                cancellationToken);
            _blade._bladeNativeDisplayMode = actual;
            BladeNativeDisplayModeIndex = (int)actual;
            OnPropertyChanged(nameof(CanSetBladeNativeDisplayMode));
            OnPropertyChanged(nameof(CanApplyBladeNativeDisplayMode));
            OnPropertyChanged(nameof(BladeNativeDisplayModeVisibility));
            RequestDeviceRefresh();
        }, cancellationToken, () =>
            BladeNativeDisplayModeIndex = (int)(_blade._bladeNativeDisplayMode ?? BladeNativeDisplayMode.Uhd));
    }

    public async Task ApplyBladeLocalDimmingAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApplyBladeLocalDimming)
        {
            return;
        }

        await RunDeviceOperationAsync(AppStrings.Text("BladeLocalDimmingTitle.Text"), async () =>
        {
            var actual = await _deviceTelemetryReader.SetBladeLocalDimmingAsync(
                _deviceDescriptors, BladeLocalDimmingEnabled, cancellationToken);
            _blade._bladeLocalDimmingEnabled = actual;
            BladeLocalDimmingEnabled = actual;
            OnPropertyChanged(nameof(CanSetBladeLocalDimming));
            OnPropertyChanged(nameof(CanApplyBladeLocalDimming));
            OnPropertyChanged(nameof(BladeLocalDimmingVisibility));
            RequestDeviceRefresh();
        }, cancellationToken, () =>
            BladeLocalDimmingEnabled = _blade._bladeLocalDimmingEnabled ?? false);
    }

    public async Task ApplyBladeFnKeyAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApplyBladeFnKey)
        {
            return;
        }

        await RunDeviceOperationAsync(AppStrings.Text("BladeFnKeyLabel"), async () =>
        {
            var actual = await _deviceTelemetryReader.SetBladeFnKeyStateAsync(
                _deviceDescriptors, BladeFnKeyMultimediaPrimary, cancellationToken);
            SetBladeFnKey(actual);
            RequestDeviceRefresh();
        }, cancellationToken, () =>
            BladeFnKeyMultimediaPrimary = _blade._bladeFnKeyMultimediaPrimary ?? false);
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
                    bool isCurrent;
                    lock (_bladeBrightnessGate)
                    {
                        isCurrent = _desiredBladeBrightness == requested;
                        if (isCurrent) _desiredBladeBrightness = null;
                    }
                    if (!isCurrent)
                    {
                        applied = true;
                        return;
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
                    bool hasNewerRequest;
                    lock (_bladeBrightnessGate)
                    {
                        hasNewerRequest = _desiredBladeBrightness is byte pending && pending != requested;
                        if (!hasNewerRequest) _desiredBladeBrightness = null;
                    }
                    try
                    {
                        var restored = await _deviceTelemetryReader.SetBladeKeyboardBrightnessAsync(
                            _deviceDescriptors,
                            original,
                            CancellationToken.None);
                        if (!hasNewerRequest) SetBladeBrightness(restored);
                    }
                    catch (Exception restoreError) when (IsExpectedRuntimeException(restoreError))
                    {
                        throw new AggregateException(writeError, restoreError);
                    }

                    throw;
                }

                bool isCurrentRequest;
                lock (_bladeBrightnessGate)
                {
                    isCurrentRequest = _desiredBladeBrightness == requested;
                    if (isCurrentRequest) _desiredBladeBrightness = null;
                }
                if (isCurrentRequest)
                {
                    SetBladeBrightness(requested);
                    lastWritten = requested;
                }
                applied = true;
            }, CancellationToken.None, () =>
            {
                bool restoreSelection;
                lock (_bladeBrightnessGate)
                {
                    restoreSelection = _desiredBladeBrightness == requested;
                    if (restoreSelection) _desiredBladeBrightness = null;
                }
                if (restoreSelection) BladeBrightnessPercent = _blade._confirmedBladeBrightnessPercent;
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
    public string BladeProtocolText => Devices.FirstOrDefault(device =>
        device.ProtocolFamily == DeviceProtocolFamilies.Blade)?.Capability ?? BladeStatusText;
    public string BladeIdentityText => Devices.FirstOrDefault(device =>
        device.ProtocolFamily == DeviceProtocolFamilies.Blade)?.Identity ?? "VID_1532 / PID_02C6";
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
                OnPropertyChanged(nameof(CanSetBladeLightingSettings));
            }
        }
    }
    public string BladePerformanceModeText { get => _blade._bladePerformanceModeText; private set => SetField(ref _blade._bladePerformanceModeText, value); }
    public IReadOnlyList<string> BladePerformanceModeOptions => _bladeCoolingPadConnected
        ? AppStrings.Texts("Text_9753B259", "Text_C1DB7AE1", "Text_598C5804", "Text_60E54E25", "HyperBoost")
        : AppStrings.Texts("Text_9753B259", "Text_C1DB7AE1", "Text_598C5804", "Text_60E54E25");
    public int BladePerformanceModeIndex { get => _blade._bladePerformanceModeIndex; set => SetField(ref _blade._bladePerformanceModeIndex, value); }
    public bool CanSetBladePerformanceMode
    {
        get => _blade._canSetBladePerformanceMode;
        private set
        {
            if (SetField(ref _blade._canSetBladePerformanceMode, value))
            {
                OnPropertyChanged(nameof(CanSetBladePerformancePowerProfile));
                OnPropertyChanged(nameof(BladePerformanceModeCardVisibility));
            }
        }
    }
    public bool CanSetBladeLightingPowerProfile => CanSetBladeBrightness;
    public bool CanSetBladePerformancePowerProfile => CanSetBladePerformanceMode;
    public bool CanSetBladeLogoPowerProfile => CanSetBladeLogo;
    public string BladeFanText { get => _blade._bladeFanText; private set => SetField(ref _blade._bladeFanText, value); }
    public bool CanSetBladeFan
    {
        get => _blade._canSetBladeFan;
        private set
        {
            if (SetField(ref _blade._canSetBladeFan, value))
            {
                OnPropertyChanged(nameof(BladeFanCardVisibility));
            }
        }
    }
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
    public IReadOnlyList<string> BladeNativeDisplayModeOptions => AppStrings.Texts("UHD", "FHD");
    public int BladeNativeDisplayModeIndex
    {
        get => _blade._bladeNativeDisplayModeIndex;
        set
        {
            if (SetField(ref _blade._bladeNativeDisplayModeIndex, value))
            {
                OnPropertyChanged(nameof(CanApplyBladeNativeDisplayMode));
            }
        }
    }
    public bool CanSetBladeNativeDisplayMode => _blade._bladeNativeDisplayMode is not null;
    public bool CanApplyBladeNativeDisplayMode =>
        CanSetBladeNativeDisplayMode &&
        BladeNativeDisplayModeIndex is >= 0 and <= 1 &&
        BladeNativeDisplayModeIndex != (int)_blade._bladeNativeDisplayMode!.Value;
    public Visibility BladeNativeDisplayModeVisibility =>
        CanSetBladeNativeDisplayMode ? Visibility.Visible : Visibility.Collapsed;
    public string BladeFnKeyText { get => _blade._bladeFnKeyText; private set => SetField(ref _blade._bladeFnKeyText, value); }
    public bool BladeFnKeyMultimediaPrimary
    {
        get => _blade._bladeFnKeySelection;
        set
        {
            if (SetField(ref _blade._bladeFnKeySelection, value))
            {
                OnPropertyChanged(nameof(CanApplyBladeFnKey));
            }
        }
    }
    public bool CanSetBladeFnKey => _blade._bladeFnKeyMultimediaPrimary is not null;
    public bool CanApplyBladeFnKey =>
        CanSetBladeFnKey && BladeFnKeyMultimediaPrimary != _blade._bladeFnKeyMultimediaPrimary;
    public bool BladeLocalDimmingEnabled
    {
        get => _blade._bladeLocalDimmingSelection;
        set
        {
            if (SetField(ref _blade._bladeLocalDimmingSelection, value))
            {
                OnPropertyChanged(nameof(CanApplyBladeLocalDimming));
            }
        }
    }
    public bool CanSetBladeLocalDimming => _blade._bladeLocalDimmingEnabled is not null;
    public bool CanApplyBladeLocalDimming =>
        CanSetBladeLocalDimming && BladeLocalDimmingEnabled != _blade._bladeLocalDimmingEnabled;
    public Visibility BladeLocalDimmingVisibility =>
        CanSetBladeLocalDimming ? Visibility.Visible : Visibility.Collapsed;
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
    public bool CanSetBladeChargeLimit
    {
        get => _blade._canSetBladeChargeLimit;
        private set
        {
            if (SetField(ref _blade._canSetBladeChargeLimit, value))
            {
                OnPropertyChanged(nameof(BladeChargeLimitCardVisibility));
            }
        }
    }
    public IReadOnlyList<string> BladeCpuBoostOptions
    {
        get
        {
            var modes = BladeModelCapabilities.CpuBoostModesFor(_blade._bladeProductId);
            return AppStrings.Texts(
                [.. modes.Select(mode => mode switch
                {
                    BladeCpuBoostMode.Low => "Text_CB5F70D1",
                    BladeCpuBoostMode.Medium => "Text_28619638",
                    BladeCpuBoostMode.High => "Text_5C1F32A7",
                    BladeCpuBoostMode.Boost => "BladeCpuBoostHighestOption",
                    BladeCpuBoostMode.Undervolt => "Text_BAA7D10B",
                    _ => throw new ArgumentOutOfRangeException(nameof(mode)),
                })]);
        }
    }
    public string BladeCpuBoostText { get => _blade._bladeCpuBoostText; private set => SetField(ref _blade._bladeCpuBoostText, value); }
    public int BladeCpuBoostIndex { get => _blade._bladeCpuBoostIndex; set => SetField(ref _blade._bladeCpuBoostIndex, value); }
    public bool CanSetBladeCpuBoost => _blade._hasBladeCpuBoost && IsBladeCustomMode;
    public IReadOnlyList<string> BladeGpuBoostOptions => AppStrings.Texts("Text_CB5F70D1", "Text_28619638", "Text_5C1F32A7");
    public string BladeGpuBoostText { get => _blade._bladeGpuBoostText; private set => SetField(ref _blade._bladeGpuBoostText, value); }
    public int BladeGpuBoostIndex { get => _blade._bladeGpuBoostIndex; set => SetField(ref _blade._bladeGpuBoostIndex, value); }
    public bool CanSetBladeGpuBoost => _blade._hasBladeGpuBoost && IsBladeCustomMode;
    public string BladeMaxFanText { get => _blade._bladeMaxFanText; private set => SetField(ref _blade._bladeMaxFanText, value); }
    public bool BladeMaxFanEnabled { get => _blade._bladeMaxFanEnabled; set => SetField(ref _blade._bladeMaxFanEnabled, value); }
    public bool CanSetBladeMaxFan => _blade._hasBladeMaxFan && _blade.SupportsMaxFan && IsBladeCustomMode;

    /// <summary>
    /// Raises the view-model properties whose visibility or option list depends on the
    /// exact Blade product id.
    /// </summary>
    public void RefreshBladeModelCapabilityUi()
    {
        OnPropertyChanged(nameof(BladeCpuBoostOptions));
        OnPropertyChanged(nameof(BladeLightingCardVisibility));
        OnPropertyChanged(nameof(BladeLightingModeOptions));
        OnPropertyChanged(nameof(BladeLightingRenderModeVisibility));
        OnPropertyChanged(nameof(BladeLightingPresetVisibility));
        OnPropertyChanged(nameof(BladeLightingColorVisibility));
        OnPropertyChanged(nameof(BladeLightingSecondColorVisibility));
        OnPropertyChanged(nameof(BladeLightingTertiaryColorVisibility));
        OnPropertyChanged(nameof(BladeChromaOverrideVisibility));
        OnPropertyChanged(nameof(BladeChromaImportVisibility));
        OnPropertyChanged(nameof(BladeWaveDirectionVisibility));
        OnPropertyChanged(nameof(BladeLightingColorVisibility));
        OnPropertyChanged(nameof(BladeLightingSecondColorVisibility));
        OnPropertyChanged(nameof(BladeLightingTertiaryColorVisibility));
        OnPropertyChanged(nameof(CanSetBladeLighting));
        OnPropertyChanged(nameof(BladeMaxFanVisibility));
        OnPropertyChanged(nameof(CanSetBladeMaxFan));
        OnPropertyChanged(nameof(CanSetBladeCpuBoost));
        OnPropertyChanged(nameof(BladeCpuBoostVisibility));
        OnPropertyChanged(nameof(BladeGpuBoostVisibility));
        OnPropertyChanged(nameof(BladePerformanceModeCardVisibility));
        OnPropertyChanged(nameof(BladeFanCardVisibility));
        OnPropertyChanged(nameof(BladeChargeLimitCardVisibility));
        OnPropertyChanged(nameof(BladeLogoCardVisibility));
        OnPropertyChanged(nameof(CanSetBladeLightingSettings));
        OnPropertyChanged(nameof(BladeFanCurveVisibility));
    }

    /// <summary>Collapses the Max Fan column entirely on models without that switch.</summary>
    public Visibility BladeMaxFanVisibility => _blade.SupportsMaxFan
        ? Visibility.Visible
        : Visibility.Collapsed;

    /// <summary>
    /// Collapses a whole control when the model answered none of its reads. An OpenRazer-only
    /// Blade never sees the Power Mode Control bitmask, so these cards would otherwise render
    /// a row of "--" placeholders that look like a broken device.
    /// </summary>
    public Visibility BladePerformanceModeCardVisibility => CanSetBladePerformanceMode
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility BladeFanCardVisibility => CanSetBladeFan
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility BladeChargeLimitCardVisibility => CanSetBladeChargeLimit
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility BladeLogoCardVisibility => CanSetBladeLogo
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility BladeCpuBoostVisibility => _blade._hasBladeCpuBoost
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility BladeGpuBoostVisibility => CanSetBladeGpuBoost
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility BladeCustomPerformanceVisibility => IsBladeCustomMode
        ? Visibility.Visible
        : Visibility.Collapsed;
    public IReadOnlyList<string> BladeLogoOptions => AppStrings.Texts("Text_39B523BD", "Text_6295D9CB", "Text_7EDA32B9");
    public string BladeLogoText { get => _blade._bladeLogoText; private set => SetField(ref _blade._bladeLogoText, value); }
    public int BladeLogoIndex { get => _blade._bladeLogoIndex; set => SetField(ref _blade._bladeLogoIndex, value); }
    public bool CanSetBladeLogo
    {
        get => _blade._canSetBladeLogo;
        private set
        {
            if (SetField(ref _blade._canSetBladeLogo, value))
            {
                OnPropertyChanged(nameof(CanSetBladeLogoPowerProfile));
                OnPropertyChanged(nameof(BladeLogoCardVisibility));
            }
        }
    }
    public string BladeTouchpadText { get => _blade._bladeTouchpadText; private set => SetField(ref _blade._bladeTouchpadText, value); }
    public bool BladeTouchpadEnabled { get => _blade._bladeTouchpadEnabled; private set => SetField(ref _blade._bladeTouchpadEnabled, value); }
    public bool CanSetBladeTouchpad => _blade._canSetBladeTouchpad;
    /// <summary>
/// Effect names parallel <see cref="BladeLightingModes"/>, so a model that admits only
/// firmware-rendered uniform effects gets a matching short list instead of entries that
/// would not apply to it.
/// </summary>
public IReadOnlyList<string> BladeLightingModeOptions => IsBlade2023Model
    ? AppStrings.Texts(
        AppStrings.Text("Text_39B523BD"),
        AppStrings.Text("Text_7EDA32B9"),
        AppStrings.Text("Text_21BE26F0"),
        AppStrings.Text("Text_165FCE9D"))
    : _blade._bladeChromaAnimation is null
        ? AppStrings.Texts(
            AppStrings.Text("Text_39B523BD"), AppStrings.Text("Text_8FF9138F"), AppStrings.Text("Text_7EDA32B9"), AppStrings.Text("Text_21BE26F0"), AppStrings.Text("Text_165FCE9D"), AppStrings.Text("Text_5DCB722E"), AppStrings.Text("Text_E2309DE4"), AppStrings.Text("Text_D1D985CC"), AppStrings.Text("Text_F3845B2A"), AppStrings.Text("Text_33F4E277"), AppStrings.Text("Text_2A3A07A1"), AppStrings.Text("Text_51212D25"), AppStrings.Text("Text_388555B3"))
        : AppStrings.Texts(
            AppStrings.Text("Text_39B523BD"), AppStrings.Text("Text_8FF9138F"), AppStrings.Text("Text_7EDA32B9"), AppStrings.Text("Text_21BE26F0"), AppStrings.Text("Text_165FCE9D"), AppStrings.Text("Text_5DCB722E"), AppStrings.Text("Text_E2309DE4"), AppStrings.Text("Text_D1D985CC"), AppStrings.Text("Text_F3845B2A"), AppStrings.Text("Text_33F4E277"), AppStrings.Text("Text_2A3A07A1"), AppStrings.Text("Text_51212D25"), AppStrings.Text("Text_388555B3"), AppStrings.Text("BladeLightingModeChromaFile"));

    private void SetBladeChromaAnimation(ChromaFileAnimation? animation)
    {
        var hadAnimation = _blade._bladeChromaAnimation is not null;
        _blade._bladeChromaAnimation = animation;
        if (hadAnimation == (animation is not null))
        {
            return;
        }

        OnPropertyChanged(nameof(BladeLightingModeOptions));
        if (animation is null &&
            BladeLightingModeIndex ==
            Array.IndexOf(BladeLightingModes, BladeLightingMode.ChromaFile))
        {
            BladeLightingModeIndex = -1;
        }
    }
    public IReadOnlyList<string> BladeLightingPowerProfileOptions =>
        [
            AppStrings.Text("BladeLightingPowerCurrent"),
            AppStrings.Text("BladeLightingPowerPluggedIn"),
            AppStrings.Text("BladeLightingPowerLowAdapter"),
            AppStrings.Text("BladeLightingPowerBattery"),
        ];
    public IReadOnlyList<string> BladePerformancePowerProfileOptions => BladeLightingPowerProfileOptions;
    public IReadOnlyList<string> BladeRefreshRatePowerProfileOptions => BladeLightingPowerProfileOptions;
    public IReadOnlyList<string> BladeLogoPowerProfileOptions => BladeLightingPowerProfileOptions;
    public bool BladeLightingEnabled
    {
        get => _blade._bladeLightingEnabled;
        set => SetField(ref _blade._bladeLightingEnabled, value);
    }
    public bool BladeChromaOverrideEnabled
    {
        get => _blade._bladeChromaOverrideEnabled;
        set => SetField(ref _blade._bladeChromaOverrideEnabled, value);
    }
    public string BladeChromaStatusText => _blade._bladeChromaStatusText;
    public Visibility BladeChromaStatusVisibility =>
        string.IsNullOrWhiteSpace(_blade._bladeChromaStatusText)
            ? Visibility.Collapsed
            : Visibility.Visible;
    private void SetBladeChromaStatus(string text)
    {
        if (string.Equals(_blade._bladeChromaStatusText, text, StringComparison.Ordinal))
        {
            return;
        }

        _blade._bladeChromaStatusText = text;
        OnPropertyChanged(nameof(BladeChromaStatusText));
        OnPropertyChanged(nameof(BladeChromaStatusVisibility));
    }
    /// <summary>
/// The 2023 generation writes brightness and effects through the telemetry reader, so it
/// needs no lighting controller. Every other generation still requires one.
/// </summary>
public bool CanSetBladeLightingSettings => _blade._canSetBladeBrightness &&
        (IsBlade2023Model || _bladeLightingController is not null);
    public int BladeLightingPowerProfileIndex
    {
        get => _bladeLightingPowerProfileIndex;
        set
        {
            var next = Math.Clamp(value, 0, 3);
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
            var next = Math.Clamp(value, 0, 3);
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
            var next = Math.Clamp(value, 0, 3);
            if (SetField(ref _bladeRefreshRatePowerProfileIndex, next))
            {
                RefreshInternalDisplayRateEditor();
            }
        }
    }
    public int BladeLogoPowerProfileIndex
    {
        get => _bladeLogoPowerProfileIndex;
        set
        {
            var next = Math.Clamp(value, 0, 3);
            if (SetField(ref _bladeLogoPowerProfileIndex, next))
            {
                RefreshBladeLogoEditor();
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
                if (SelectedBladeLightingMode is BladeLightingMode.Wave or BladeLightingMode.Spectrum)
                {
                    BladeLightingRenderModeIndex = 0;
                    var defaults = BladeLightingPaletteResolver.GetDefault(SelectedBladeLightingMode.Value).Colors;
                    BladeLightingColor = Color.FromArgb(0xFF, defaults[0].Red, defaults[0].Green, defaults[0].Blue);
                    BladeLightingSecondColor = Color.FromArgb(0xFF, defaults[1].Red, defaults[1].Green, defaults[1].Blue);
                    BladeLightingTertiaryColor = Color.FromArgb(0xFF, defaults[2].Red, defaults[2].Green, defaults[2].Blue);
                }
                OnPropertyChanged(nameof(BladeLightingColorVisibility));
                OnPropertyChanged(nameof(BladeLightingPresetVisibility));
                OnPropertyChanged(nameof(BladeLightingRenderModeVisibility));
                OnPropertyChanged(nameof(BladeLightingSecondColorVisibility));
                OnPropertyChanged(nameof(BladeLightingTertiaryColorVisibility));
                OnPropertyChanged(nameof(BladeWaveDirectionVisibility));
                OnPropertyChanged(nameof(BladeReactiveSpeedVisibility));
                OnPropertyChanged(nameof(BladeStarlightSpeedVisibility));
                OnPropertyChanged(nameof(BladeStarlightColorModeVisibility));
                OnPropertyChanged(nameof(BladeAnimationSpeedVisibility));
            }
        }
    }
    /// <summary>
    /// Resolves the selected index against the list this model actually renders. Indexing
    /// the full catalog would map the short 2023 list onto the wrong effects.
    /// </summary>
    private BladeLightingMode? SelectedBladeLightingMode
    {
        get
        {
            var modes = ActiveBladeLightingModes;
            return BladeLightingModeIndex >= 0 && BladeLightingModeIndex < modes.Count
                ? modes[BladeLightingModeIndex]
                : null;
        }
    }
    public IReadOnlyList<string> BladeLightingRenderModeOptions => AppStrings.Texts(
        "LightingRenderFirmware", "LightingRenderSoftware");
    public int BladeLightingRenderModeIndex
    {
        get => _blade._bladeLightingRenderModeIndex;
        set
        {
            if (value is not (0 or 1)) return;
            if (SetField(ref _blade._bladeLightingRenderModeIndex, value))
            {
                OnPropertyChanged(nameof(BladeLightingColorVisibility));
                OnPropertyChanged(nameof(BladeLightingPresetVisibility));
                OnPropertyChanged(nameof(BladeLightingSecondColorVisibility));
                OnPropertyChanged(nameof(BladeLightingTertiaryColorVisibility));
            }
        }
    }
    public Visibility BladeLightingRenderModeVisibility => !IsBlade2023Model &&
        SelectedBladeLightingMode is BladeLightingMode.Wave or BladeLightingMode.Spectrum
        ? Visibility.Visible
        : Visibility.Collapsed;
    private bool BladeFirmwarePaletteHidden => BladeLightingRenderModeVisibility == Visibility.Visible &&
        BladeLightingRenderModeIndex == 0;
    public Visibility BladeLightingPresetVisibility => SelectedBladeLightingMode == BladeLightingMode.Starlight
        ? BladeLightingColorVisibility : BladeLightingSecondColorVisibility;
    public Visibility BladeLightingColorVisibility => !BladeFirmwarePaletteHidden &&
        !IsBlade2023Model &&
        (SelectedBladeLightingMode is
        BladeLightingMode.Static or BladeLightingMode.Breathing or BladeLightingMode.Reactive or
        BladeLightingMode.Ripple or BladeLightingMode.Tidal or BladeLightingMode.Spectrum or
        BladeLightingMode.Wave or BladeLightingMode.Fire or BladeLightingMode.Wheel or BladeLightingMode.AudioMeter ||
        (SelectedBladeLightingMode == BladeLightingMode.Starlight && BladeStarlightColorModeIndex != 0))
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility BladeLightingSecondColorVisibility => !BladeFirmwarePaletteHidden && !IsBlade2023Model &&
        (SelectedBladeLightingMode is BladeLightingMode.Tidal or
        BladeLightingMode.Spectrum or BladeLightingMode.Wave or BladeLightingMode.Fire or BladeLightingMode.Wheel or BladeLightingMode.AudioMeter ||
        (SelectedBladeLightingMode == BladeLightingMode.Starlight && BladeStarlightColorModeIndex == 2))
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility BladeLightingTertiaryColorVisibility => !BladeFirmwarePaletteHidden && !IsBlade2023Model &&
        SelectedBladeLightingMode is
        BladeLightingMode.Spectrum or BladeLightingMode.Wave or BladeLightingMode.Fire or
        BladeLightingMode.Wheel or BladeLightingMode.AudioMeter
        ? Visibility.Visible
        : Visibility.Collapsed;
    /// <summary>
    /// The 2023 generation stores a fixed rightward wave direction, so offering the
    /// direction selector there would present a control that cannot change anything.
    /// </summary>
public Visibility BladeWaveDirectionVisibility => !IsBlade2023Model &&
        SelectedBladeLightingMode is BladeLightingMode.Wave or BladeLightingMode.Wheel
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
    public Visibility BladeAnimationSpeedVisibility => SelectedBladeLightingMode is BladeLightingMode.Wheel or BladeLightingMode.Tidal
        ? Visibility.Visible
        : Visibility.Collapsed;
    public IReadOnlyList<string> BladeAnimationSpeedOptions => AppStrings.Texts("BladeAnimationSpeedSlow", "BladeAnimationSpeedMedium", "BladeAnimationSpeedFast");
    public int BladeAnimationSpeedIndex { get => _blade._bladeAnimationSpeedIndex; set => SetField(ref _blade._bladeAnimationSpeedIndex, value); }
    public IReadOnlyList<string> BladeWaveDirectionOptions => AppStrings.Texts("Text_FB3FF0D8", "Text_883A50D7");
    public int BladeWaveDirectionIndex { get => _blade._bladeWaveDirectionIndex; set => SetField(ref _blade._bladeWaveDirectionIndex, value); }
    public IReadOnlyList<string> BladeReactiveSpeedOptions => AppStrings.Texts("BladeReactiveSpeedLevel1", "BladeReactiveSpeedLevel2", "BladeReactiveSpeedLevel3", "BladeReactiveSpeedLevel4");
    public int BladeReactiveSpeedIndex { get => _blade._bladeReactiveSpeedIndex; set => SetField(ref _blade._bladeReactiveSpeedIndex, value); }
    public IReadOnlyList<string> BladeStarlightSpeedOptions => BladeAnimationSpeedOptions;
    public int BladeStarlightSpeedIndex { get => _blade._bladeStarlightSpeedIndex; set => SetField(ref _blade._bladeStarlightSpeedIndex, value); }
    public IReadOnlyList<string> BladeStarlightColorModeOptions => AppStrings.Texts("BladeStarlightRandom", "BladeStarlightSingle", "BladeStarlightDual");
    public int BladeStarlightColorModeIndex { get => _blade._bladeStarlightColorModeIndex; set { if (SetField(ref _blade._bladeStarlightColorModeIndex, value)) { OnPropertyChanged(nameof(BladeLightingColorVisibility)); OnPropertyChanged(nameof(BladeLightingPresetVisibility)); OnPropertyChanged(nameof(BladeLightingSecondColorVisibility)); } } }
    public Color BladeLightingColor { get => _blade._bladeLightingColor; set => SetField(ref _blade._bladeLightingColor, value); }
    public Color BladeLightingSecondColor { get => _blade._bladeLightingSecondColor; set => SetField(ref _blade._bladeLightingSecondColor, value); }
    public Color BladeLightingTertiaryColor { get => _blade._bladeLightingTertiaryColor; set => SetField(ref _blade._bladeLightingTertiaryColor, value); }
    /// <summary>
/// Gate for the effect selector. The 2023 generation selects firmware effects through the
/// telemetry reader, so the matrix-based lighting controller is not required there.
/// </summary>
public bool CanSetBladeLighting => _blade._canSetBladeBrightness &&
        (IsBlade2023Model || _bladeLightingController is not null);

    /// <summary>
/// Both Blade generations have an admitted keyboard-lighting report, so the card is
/// always available; only the effect list and color controls differ by model.
/// </summary>
public Visibility BladeLightingCardVisibility => Visibility.Visible;

    private const ushort Blade2023ProductId = 0x029F;

    /// <summary>True on the Blade 16 2023, which has no per-key lighting matrix.</summary>
    private bool IsBlade2023Model => _blade._bladeProductId == Blade2023ProductId;

    /// <summary>
    /// Effect list for the selected model. A 2023 machine renders a short firmware list;
    /// every other generation keeps the full effect set with the optional Chroma entry.
    /// </summary>
    /// <summary>
    /// Page-facing view of the selected effect. Controls that need to react to the
    /// selection resolve it here so the per-model list stays in one place.
    /// </summary>
    public BladeLightingMode? SelectedBladeLightingModeForPage => SelectedBladeLightingMode;

    private IReadOnlyList<BladeLightingMode> ActiveBladeLightingModes =>
        IsBlade2023Model
            ? BladeLighting2023Admitted.Effects
            : BladeLightingModes;

    /// <summary>
    /// The Blade 16 2023 renders these effects in firmware through its own report, so the
    /// effect and brightness reach the device without the per-key frame path.
    /// </summary>
    private async Task ApplyBlade2023LightingEffectAsync(
        BladeLightingMode mode,
        CancellationToken cancellationToken)
    {
        if (!BladeLighting2023Admitted.Allows(mode))
        {
            return;
        }

        var effect = BladeLighting2023Admitted.ToWireEffect(mode);

        await RunDeviceOperationAsync("Keyboard effect", async () =>
        {
            var actual = await _deviceTelemetryReader.SetBlade2023KeyboardEffectAsync(
                _deviceDescriptors, effect, cancellationToken);
            _blade._bladeKeyboardEffect2023 = actual;

            // Go through the shared codec so the persisted key matches the one
            // BladeLightingProfileCodec.Parse accepts on the next read.
            var previousProfile = _profile.Clone();
            var target = EditableLightingProfile;
            var encoded = BladeLightingProfileCodec.Create(new BladeLightingEffect(mode));
            target.Effect = encoded.Effect;
            target.Parameters = encoded.Parameters;
            if (!await SaveProfileAsync(cancellationToken))
            {
                _profile = previousProfile;
                throw new InvalidOperationException(AppStrings.Text("Text_C7D0CD75"));
            }

            _lightingShadowFingerprint = string.Empty;
            RequestDeviceRefresh();
        }, cancellationToken);
    }

    public async Task ApplyBladeLightingSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSetBladeLightingSettings)
        {
            return;
        }

        await RunDeviceOperationAsync(
            AppStrings.Text("Text_09707A2C"),
            async () =>
            {
                var previousProfile = _profile.Clone();
                var target = EditableLightingBladeProfile;
                target.LightingEnabled = BladeLightingEnabled;
                target.ChromaOverrideEnabled = BladeChromaOverrideEnabled;
                if (!await SaveProfileAsync(cancellationToken))
                {
                    _profile = previousProfile;
                    throw new InvalidOperationException(AppStrings.Text("Text_C7D0CD75"));
                }

                if (IsSelectedLightingPowerActive && Volatile.Read(ref _displayAvailable) != 0)
                {
                    _lightingShadowFingerprint = string.Empty;
                    var blade = _deviceDescriptors.FirstOrDefault(device =>
                        device.ProtocolFamily == DeviceProtocolFamilies.Blade &&
                        device.Access == DeviceAccessState.Available);
                    await ApplyLoadedLightingProfileAsync(
                        blade,
                        CurrentPowerSource,
                        cancellationToken).ConfigureAwait(false);
                }
                RefreshBladeLightingEditor();
            },
            cancellationToken,
            () => RefreshBladeLightingEditor(),
            successVerb: AppStrings.Text("Text_945C2E42"));
    }
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
