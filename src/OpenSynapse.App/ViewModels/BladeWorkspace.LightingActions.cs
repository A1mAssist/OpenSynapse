using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;
using Windows.UI;
using static OpenSynapse.App.ViewModels.DeviceUiCatalog;

namespace OpenSynapse.App.ViewModels;

public sealed partial class BladeWorkspace
{
    internal async Task ApplyLightingEffectAsync(
        BladeLightingEffect effect,
        CancellationToken cancellationToken = default,
        BladeLightingColorMemoryEntry? colorMemory = null)
    {
        if (!CanSetLighting ||
            _bladeLightingController is null)
        {
            return;
        }

        await RunDeviceOperationAsync(
            AppStrings.Text("Text_09707A2C"),
            () => ApplyLightingEffectCoreAsync(effect, cancellationToken, colorMemory),
            cancellationToken,
            successVerb: AppStrings.Text("Text_945C2E42"));
    }

    private async Task ApplyLightingEffectCoreAsync(
        BladeLightingEffect effect,
        CancellationToken cancellationToken,
        BladeLightingColorMemoryEntry? colorMemory = null)
    {
        var previousProfile = Profile.Clone();
        var encoded = BladeLightingProfileCodec.Create(effect);
        var targetLighting = EditableLightingProfile;
        var targetBlade = EditableLightingBladeProfile;
        void ApplyToProfile()
        {
            targetLighting.Effect = encoded.Effect;
            targetLighting.Parameters = encoded.Parameters;
            if (colorMemory is { } memory)
            {
                BladeLightingColorMemory.Remember(targetBlade, effect.Mode, memory);
            }
        }
        if (!IsSelectedLightingPowerActive || !IsDisplayAvailable)
        {
            ApplyToProfile();
            if (!await SaveProfileAsync(cancellationToken))
            {
                Profile = previousProfile;
                throw new InvalidOperationException(AppStrings.Text("Text_9A87CD0C"));
            }

            RefreshLightingEditor();
            return;
        }

        if (!LightingEnabled)
        {
            ApplyToProfile();
            if (!await SaveProfileAsync(cancellationToken))
            {
                Profile = previousProfile;
                throw new InvalidOperationException(AppStrings.Text("Text_9A87CD0C"));
            }

            RefreshLightingEditor();
            return;
        }

        try
        {
            await _bladeLightingController!.ApplyAsync(
                ActiveDescriptors, effect, cancellationToken);
        }
        catch
        {
            NotifyLightingDialectChangedIfDowngraded();
            throw;
        }
        ApplyToProfile();
        if (!await SaveProfileAsync(cancellationToken))
        {
            Profile = previousProfile;
            _lighting().ShadowFingerprint = string.Empty;
            throw new InvalidOperationException(AppStrings.Text("Text_C7D0CD75"));
        }

        var blade = ActiveDescriptors.FirstOrDefault(device =>
            DeviceProtocolFamilies.IsBlade(device.ProtocolFamily) &&
            device.Access == DeviceAccessState.Available);
        if (blade is not null)
        {
            var resolved = ProfileResolver.Resolve(
                Profile,
                blade,
                CurrentPowerSource);
            _lighting().LightingDevicePath = blade.Id;
            _lighting().ShadowFingerprint = _lighting().CreateFingerprint(
                resolved.Lighting,
                blade.Id,
                CurrentPowerSource,
                resolved.Blade.LightingEnabled ?? true,
                resolved.Blade.ChromaOverrideEnabled ?? true);
        }
        _ = _lighting().ObserveRuntime(
            _bladeLightingController!.RuntimeCompletion);
    }

    public Task ApplySelectedLightingEffectAsync(CancellationToken cancellationToken = default)
    {
        var modes = ActiveBladeLightingModes;
        if (LightingModeIndex < 0 || LightingModeIndex >= modes.Count)
        {
            return Task.CompletedTask;
        }

        if (UsesFirmwareKeyboardLighting)
        {
            return ApplyFirmwareKeyboardLightingEffectAsync(modes[LightingModeIndex], cancellationToken);
        }

        var color = new RazerRgb(LightingColor.R, LightingColor.G, LightingColor.B);
        var secondColor = new RazerRgb(
            LightingSecondColor.R,
            LightingSecondColor.G,
            LightingSecondColor.B);
        var tertiaryColor = new RazerRgb(
            LightingTertiaryColor.R,
            LightingTertiaryColor.G,
            LightingTertiaryColor.B);
        var mode = modes[LightingModeIndex];
        if (mode == BladeLightingMode.ChromaFile && _blade._bladeChromaAnimation is null)
        {
            SetChromaStatus(AppStrings.Text("BladeChromaImportRequired"));
            return Task.CompletedTask;
        }

        var palette = mode is BladeLightingMode.Wave or BladeLightingMode.Spectrum
            ? LightingRenderModeIndex == 1
                ? LightingPalette.Create(color, secondColor, tertiaryColor)
                : null
            : BladeLightingPaletteResolver.Resolve(
                mode, color, secondColor, tertiaryColor, EditableLightingProfile);
        var effect = new BladeLightingEffect(
            mode,
            color,
            BladeWaveDirections[Math.Clamp(WaveDirectionIndex, 0, BladeWaveDirections.Length - 1)],
            secondColor,
            BladeReactiveSpeeds[Math.Clamp(ReactiveSpeedIndex, 0, BladeReactiveSpeeds.Length - 1)],
            BladeStarlightSpeeds[Math.Clamp(StarlightSpeedIndex, 0, BladeStarlightSpeeds.Length - 1)],
            BladeStarlightColorModes[Math.Clamp(StarlightColorModeIndex, 0, BladeStarlightColorModes.Length - 1)],
            palette,
            BladeAnimationSpeeds[Math.Clamp(AnimationSpeedIndex, 0, BladeAnimationSpeeds.Length - 1)],
            mode == BladeLightingMode.ChromaFile ? _blade._bladeChromaAnimation : null);
        var colorMemory = new BladeLightingColorMemoryEntry(
            color,
            secondColor,
            tertiaryColor,
            mode is BladeLightingMode.Wave or BladeLightingMode.Spectrum && LightingRenderModeIndex == 1);
        return ApplyLightingEffectAsync(effect, cancellationToken, colorMemory);
    }

    public async Task ImportChromaFileAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (!CanSetLighting || _bladeLightingController is null)
        {
            return;
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var previousMode = LightingModeIndex;
        var previousAnimation = _blade._bladeChromaAnimation;
        await RunDeviceOperationAsync(
            AppStrings.Text("BladeImportChromaButton/Content"),
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
                    var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
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
                    SetChromaAnimation(null);
                    await ApplyLightingEffectCoreAsync(
                        new BladeLightingEffect(BladeLightingMode.Static, animation.UniformColor),
                        cancellationToken);
                    LightingModeIndex = Array.IndexOf(
                        ActiveBladeLightingModes.ToArray(),
                        BladeLightingMode.Static);
                    LightingColor = Color.FromArgb(
                        0xFF,
                        animation.UniformColor.Red,
                        animation.UniformColor.Green,
                        animation.UniformColor.Blue);
                    SetChromaStatus(AppStrings.Text("BladeChromaImportUniform"));
                    return;
                }

                SetChromaAnimation(animation);
                await ApplyLightingEffectCoreAsync(
                    new BladeLightingEffect(
                        BladeLightingMode.ChromaFile,
                        ChromaAnimation: animation),
                    cancellationToken);
                LightingModeIndex = Array.IndexOf(
                    ActiveBladeLightingModes.ToArray(),
                    BladeLightingMode.ChromaFile);
                SetChromaStatus(AppStrings.FormatText(
                    "BladeChromaImportApplied",
                    Path.GetFileName(path),
                    animation.Frames.Count));
            },
            cancellationToken,
            restoreSelection: () =>
            {
                LightingModeIndex = previousMode;
                SetChromaAnimation(previousAnimation);
            },
            successVerb: AppStrings.Text("Text_945C2E42"));
    }

    /// <summary>
    /// The Blade 16 2023 renders these effects in firmware through its own report, so the
    /// effect and brightness reach the device without the per-key frame path.
    /// </summary>
    private async Task ApplyFirmwareKeyboardLightingEffectAsync(
        BladeLightingMode mode,
        CancellationToken cancellationToken)
    {
        if (!BladeLighting2023Admitted.Allows(mode))
        {
            return;
        }

        var effect = BladeLighting2023Admitted.ToWireEffect(mode);
        var edit = new BladeLightingProfileEdit(Profile, SelectedLightingPowerState);
        var enabled = LightingEnabled;
        var descriptors = ActiveDescriptors.ToArray();

        await RunDeviceOperationAsync("Keyboard effect", async () =>
        {
            if (edit.CanWriteEffect(Profile, CurrentPowerSource, IsDisplayAvailable, enabled))
            {
                try
                {
                    _blade._bladeKeyboardEffect2023 = await _deviceTelemetryReader.SetBlade2023KeyboardEffectAsync(
                        descriptors, effect, cancellationToken);
                }
                catch
                {
                    NotifyLightingDialectChangedIfDowngraded();
                    throw;
                }
            }

            // Go through the shared codec so the persisted key matches the one
            // BladeLightingProfileCodec.Parse accepts on the next read.
            var encoded = BladeLightingProfileCodec.Create(new BladeLightingEffect(mode));
            await edit.SaveEffectAsync(Profile, encoded, SaveProfileAsync, cancellationToken,
                AppStrings.Text("Text_C7D0CD75"));

            _lighting().ShadowFingerprint = string.Empty;
            RequestDeviceRefresh();
            RefreshLightingEditor();
        }, cancellationToken, RefreshLightingEditor);
    }

    public async Task ApplyLightingSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSetLightingSettings)
        {
            return;
        }

        await RunDeviceOperationAsync(
            AppStrings.Text("Text_09707A2C"),
            async () =>
            {
                var previousProfile = Profile.Clone();
                var target = EditableLightingBladeProfile;
                target.LightingEnabled = LightingEnabled;
                target.ChromaOverrideEnabled = ChromaOverrideEnabled;
                if (!await SaveProfileAsync(cancellationToken))
                {
                    Profile = previousProfile;
                    throw new InvalidOperationException(AppStrings.Text("Text_C7D0CD75"));
                }

                if (IsSelectedLightingPowerActive && IsDisplayAvailable)
                {
                    _lighting().ShadowFingerprint = string.Empty;
                    var blade = ActiveDescriptors.FirstOrDefault(device =>
                        DeviceProtocolFamilies.IsBlade(device.ProtocolFamily) &&
                        device.Access == DeviceAccessState.Available);
                    await _lighting().ApplyLoadedProfileAsync(
                        blade,
                        CurrentPowerSource,
                        cancellationToken);
                }
                RefreshLightingEditor();
            },
            cancellationToken,
            () => RefreshLightingEditor(),
            successVerb: AppStrings.Text("Text_945C2E42"));
    }

    private void NotifyLightingDialectChangedIfDowngraded()
    {
        if (!BladeLightingRuntimeDialect.IsDowngraded(_blade._bladeProductId))
        {
            return;
        }

        OnPropertyChanged(nameof(LightingModeOptions));
        OnPropertyChanged(nameof(LightingEffectVisibility));
        OnPropertyChanged(nameof(LightingRenderModeVisibility));
        OnPropertyChanged(nameof(LightingPresetVisibility));
        OnPropertyChanged(nameof(LightingColorVisibility));
        OnPropertyChanged(nameof(LightingSecondColorVisibility));
        OnPropertyChanged(nameof(LightingTertiaryColorVisibility));
        OnPropertyChanged(nameof(WaveDirectionVisibility));
        OnPropertyChanged(nameof(ReactiveSpeedVisibility));
        OnPropertyChanged(nameof(StarlightSpeedVisibility));
        OnPropertyChanged(nameof(StarlightColorModeVisibility));
        OnPropertyChanged(nameof(AnimationSpeedVisibility));
        OnPropertyChanged(nameof(CanSetLighting));
        OnPropertyChanged(nameof(CanSetLightingSettings));
    }

    internal void RefreshLightingEditor()
    {
        if (Profile.Profiles.Count == 0 || IsLightingColorFlyoutOpen)
        {
            return;
        }

        var blade = ActiveDescriptors.FirstOrDefault(device =>
            DeviceProtocolFamilies.IsBlade(device.ProtocolFamily));
        var powerState = SelectedLightingPowerState;
        var resolved = blade is null
            ? null
            : ProfileResolver.Resolve(Profile, blade, powerState);
        var lighting = resolved?.Lighting ?? EditableLightingProfile;
        var bladeProfile = resolved?.Blade ?? EditableLightingBladeProfile;
        LightingEnabled = bladeProfile.LightingEnabled ?? true;
        ChromaOverrideEnabled = bladeProfile.ChromaOverrideEnabled ?? true;
        var effect = LightingEnabled
            ? BladeLightingProfileCodec.Parse(lighting)
            : BladeLightingEffect.Off;
        SetChromaAnimation(effect.ChromaAnimation);

        // Index against the list this model actually renders; the full catalog would
        // select an effect the 2023 generation does not offer.
        var activeModes = ActiveBladeLightingModes;
        var lightingIndex = activeModes.ToList().IndexOf(effect.Mode);
        if (effect.Mode == BladeLightingMode.ChromaFile && effect.ChromaAnimation is null)
        {
            LightingModeIndex = -1;
        }
        else if (lightingIndex >= 0)
        {
            LightingModeIndex = lightingIndex;
            LightingRenderModeIndex = effect.Palette is not null ? 1 : 0;
            WaveDirectionIndex = Array.IndexOf(BladeWaveDirections, effect.Direction);
            LightingColor = Color.FromArgb(0xFF, effect.Color.Red, effect.Color.Green, effect.Color.Blue);
            LightingSecondColor = Color.FromArgb(
                0xFF, effect.SecondColor.Red, effect.SecondColor.Green, effect.SecondColor.Blue);
            var selectedPalette = effect.Palette;
            if (selectedPalette is null && effect.Mode is
                BladeLightingMode.Spectrum or BladeLightingMode.Wave or BladeLightingMode.Fire or
                BladeLightingMode.Wheel or BladeLightingMode.AudioMeter)
            {
                if (BladeLightingColorMemory.TryRecall(bladeProfile, effect.Mode, out var memory))
                {
                    LightingColor = ToColor(memory.Primary);
                    LightingSecondColor = ToColor(memory.Secondary);
                    LightingTertiaryColor = ToColor(memory.Tertiary);
                    if (effect.Mode is BladeLightingMode.Wave or BladeLightingMode.Spectrum)
                    {
                        LightingRenderModeIndex = memory.SoftwareRender ? 1 : 0;
                    }
                }
                else
                {
                    selectedPalette = BladeLightingPaletteResolver.GetDefault(effect.Mode);
                }
            }
            if (selectedPalette is not null)
            {
                var palette = selectedPalette;
                LightingColor = Color.FromArgb(0xFF, palette.Colors[0].Red, palette.Colors[0].Green, palette.Colors[0].Blue);
                LightingSecondColor = Color.FromArgb(0xFF, palette.Colors[Math.Min(1, palette.Colors.Count - 1)].Red,
                    palette.Colors[Math.Min(1, palette.Colors.Count - 1)].Green, palette.Colors[Math.Min(1, palette.Colors.Count - 1)].Blue);
                LightingTertiaryColor = Color.FromArgb(0xFF, palette.Colors[Math.Min(2, palette.Colors.Count - 1)].Red,
                    palette.Colors[Math.Min(2, palette.Colors.Count - 1)].Green, palette.Colors[Math.Min(2, palette.Colors.Count - 1)].Blue);
            }
            ReactiveSpeedIndex = Array.IndexOf(BladeReactiveSpeeds, effect.ReactiveSpeed);
            StarlightSpeedIndex = Array.IndexOf(BladeStarlightSpeeds, effect.StarlightSpeed);
            StarlightColorModeIndex = (int)effect.StarlightColorMode;
            AnimationSpeedIndex = Array.IndexOf(BladeAnimationSpeeds, effect.AnimationSpeed);
        }

        if (bladeProfile.KeyboardBrightness is byte brightness)
        {
            SetBrightness(brightness, confirm: false);
        }
    }

    private void RestoreLightingColors(BladeLightingMode mode)
    {
        if (BladeLightingColorMemory.TryRecall(EditableLightingBladeProfile, mode, out var memory))
        {
            LightingColor = ToColor(memory.Primary);
            LightingSecondColor = ToColor(memory.Secondary);
            LightingTertiaryColor = ToColor(memory.Tertiary);
            if (mode is BladeLightingMode.Wave or BladeLightingMode.Spectrum)
            {
                LightingRenderModeIndex = memory.SoftwareRender ? 1 : 0;
            }

            return;
        }

        if (mode is BladeLightingMode.Spectrum or BladeLightingMode.Wave or BladeLightingMode.Fire or
            BladeLightingMode.Wheel or BladeLightingMode.AudioMeter)
        {
            var defaults = BladeLightingPaletteResolver.GetDefault(mode).Colors;
            LightingColor = ToColor(defaults[0]);
            LightingSecondColor = ToColor(defaults[Math.Min(1, defaults.Count - 1)]);
            LightingTertiaryColor = ToColor(defaults[Math.Min(2, defaults.Count - 1)]);
            if (mode is BladeLightingMode.Wave or BladeLightingMode.Spectrum)
            {
                LightingRenderModeIndex = 0;
            }
        }
    }

    private static Color ToColor(RazerRgb color) =>
        Color.FromArgb(0xFF, color.Red, color.Green, color.Blue);
}
