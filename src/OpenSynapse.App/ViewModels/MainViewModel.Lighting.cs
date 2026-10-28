using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;
using static OpenSynapse.App.ViewModels.DeviceUiCatalog;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel
{
    private bool _chromaIntegrationEnabled = true;

    /// <summary>
    /// Chroma takes over through the per-key matrix path, which firmware-rendered models do
    /// not have, so the override is hidden there even when Chroma is enabled globally.
    /// </summary>
    public Visibility BladeChromaOverrideVisibility =>
        _chromaIntegrationEnabled && !UsesFirmwareKeyboardLighting
            ? Visibility.Visible
            : Visibility.Collapsed;

    /// <summary>
    /// Firmware-rendered models have no matrix to hand a .chroma file to, so the import button
    /// stays hidden rather than accepting a file the device cannot play.
    /// </summary>
    public Visibility BladeChromaImportVisibility => UsesFirmwareKeyboardLighting
        ? Visibility.Collapsed
        : Visibility.Visible;

    internal void SetChromaIntegrationEnabled(bool enabled)
    {
        if (_chromaIntegrationEnabled == enabled) return;
        _chromaIntegrationEnabled = enabled;
        OnPropertyChanged(nameof(BladeChromaOverrideVisibility));
        SelectedOpenRazerDevice?.SetChromaIntegrationEnabled(enabled);
    }

    public async Task PrepareForSuspendAsync()
    {
        await SetDisplayAvailableAsync(false).ConfigureAwait(false);

        var error = await StopBladeFanControlAsync("suspend").ConfigureAwait(false);
        if (error is not null)
        {
            _diagnosticLog.TryWrite("blade-fan", $"suspend restore incomplete: {error}");
        }
    }

    public async Task SetDisplayAvailableAsync(bool available)
    {
        if (Interlocked.Exchange(ref _displayAvailable, available ? 1 : 0) !=
            (available ? 1 : 0))
        {
            SignalPerformanceSamplingStateChanged();
        }
        Interlocked.Exchange(ref _displayBrightnessRestorePending, 1);
        if (!available)
        {
            Interlocked.Increment(ref _bladeBrightnessVerificationGeneration);
        }
        if (_bladeLightingController is not null)
        {
            try
            {
                await _bladeLightingController.SetDisplayAvailableAsync(available).ConfigureAwait(false);
                if (!available)
                {
                    var blade = _deviceDescriptors.FirstOrDefault(device =>
                        DeviceProtocolFamilies.IsBlade(device.ProtocolFamily) &&
                        device.Access == DeviceAccessState.Available);
                    if (blade is not null)
                    {
                        _bladeLightingDevicePath = blade.Id;
                        _lightingShadowFingerprint = $"display-off\n{blade.Id}";
                    }
                }
            }
            catch (Exception exception) when (IsExpectedRuntimeException(exception))
            {
                _diagnosticLog.TryWrite("keyboard-lighting", $"display-state lighting transition failed: {exception}");
            }
            finally
            {
                if (available)
                {
                    _bladeLightingDevicePath = string.Empty;
                    _lightingShadowFingerprint = string.Empty;
                }
            }
        }

        await _deviceOperationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            try
            {
                if (!available &&
                    _displaySuspendedLogoMode is null &&
                    _blade._canSetBladeLogo &&
                    _blade._confirmedBladeLogoIndex >= 0 &&
                    _blade._confirmedBladeLogoIndex < BladeLogoModes.Length)
                {
                    _displaySuspendedLogoMode = BladeLogoModes[_blade._confirmedBladeLogoIndex];
                    await _deviceTelemetryReader.SetBladeLogoModeAsync(
                        _deviceDescriptors,
                        BladeLogoMode.Off,
                        CancellationToken.None).ConfigureAwait(false);
                }
                else if (available && _displaySuspendedLogoMode is { } logoMode)
                {
                    await _deviceTelemetryReader.SetBladeLogoModeAsync(
                        _deviceDescriptors,
                        logoMode,
                        CancellationToken.None).ConfigureAwait(false);
                    _displaySuspendedLogoMode = null;
                }
            }
            catch (Exception exception) when (IsExpectedRuntimeException(exception))
            {
                _diagnosticLog.TryWrite(
                    "blade-logo",
                    $"display-state Logo transition failed: {exception}");
            }
        }
        finally
        {
            _deviceOperationGate.Release();
        }

        if (available)
        {
            await RestoreBladeLightingAfterExternalAsync().ConfigureAwait(false);
            RequestDeviceRefresh();
        }
    }

    private async Task<string?> ApplyLoadedLightingProfileAsync(
        DeviceDescriptor? blade,
        PowerSourceKind powerState,
        CancellationToken cancellationToken)
    {
        if (_bladeLightingController is null)
        {
            return null;
        }

        if (blade is null || blade.Access != DeviceAccessState.Available)
        {
            if (_bladeLightingDevicePath.Length > 0)
            {
                try
                {
                    await _bladeLightingController.StopAsync();
                }
                catch (Exception exception) when (IsExpectedRuntimeException(exception))
                {
                    _diagnosticLog.TryWrite("keyboard-lighting", $"disconnect restore failed: {exception}");
                }
            }

            _bladeLightingDevicePath = string.Empty;
            _lightingShadowFingerprint = string.Empty;
            return null;
        }

        // The 597 family is read-only until its keyboard write path is exercised.
        if (blade.ProtocolFamily == "blade-597")
        {
            return null;
        }

        if (Volatile.Read(ref _displayAvailable) == 0)
        {
            var offFingerprint = $"display-off\n{blade.Id}";
            if (StringComparer.Ordinal.Equals(_lightingShadowFingerprint, offFingerprint))
            {
                return null;
            }
            try
            {
                if (UsesFirmwareKeyboardLighting)
                {
                    await _deviceTelemetryReader.SetBlade2023KeyboardEffectAsync(
                        _deviceDescriptors,
                        BladeLighting2023Effect.Off,
                        cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await _bladeLightingController.ApplyAsync(
                        _deviceDescriptors,
                        BladeLightingEffect.Off,
                        cancellationToken).ConfigureAwait(false);
                }

                _bladeLightingDevicePath = blade.Id;
                _lightingShadowFingerprint = offFingerprint;
                return null;
            }
            catch (Exception exception) when (IsExpectedRuntimeException(exception))
            {
                _bladeLightingDevicePath = string.Empty;
                _lightingShadowFingerprint = string.Empty;
                return FormatOperationException(exception);
            }
        }

        var resolved = ProfileResolver.Resolve(_profile, blade, powerState);
        var profile = resolved.Lighting;
        var lightingEnabled = resolved.Blade.LightingEnabled ?? true;
        var chromaOverrideEnabled = resolved.Blade.ChromaOverrideEnabled ?? true;
        string fingerprint;
        BladeLightingEffect effect;
        try
        {
            effect = lightingEnabled
                ? BladeLightingProfileCodec.Parse(profile)
                : BladeLightingEffect.Off;
            if (effect.Mode == BladeLightingMode.ChromaFile &&
                effect.ChromaAnimation is null)
            {
                _lightingShadowFingerprint = string.Empty;
                return AppStrings.Text("BladeChromaFileMissing");
            }
            fingerprint = CreateLightingFingerprint(
                profile,
                blade.Id,
                powerState,
                lightingEnabled,
                chromaOverrideEnabled);
        }
        catch (InvalidOperationException exception)
        {
            _lightingShadowFingerprint = string.Empty;
            return exception.Message;
        }

        if (StringComparer.Ordinal.Equals(_lightingShadowFingerprint, fingerprint))
        {
            return null;
        }

        if (UsesFirmwareKeyboardLighting)
        {
            return await ApplyFirmwareKeyboardLightingProfileAsync(
                blade,
                effect,
                resolved.Blade.KeyboardBrightness,
                profile,
                powerState,
                lightingEnabled,
                chromaOverrideEnabled,
                cancellationToken).ConfigureAwait(false);
        }

        if (_bladeLightingDevicePath.Length > 0 &&
            !StringComparer.OrdinalIgnoreCase.Equals(_bladeLightingDevicePath, blade.Id))
        {
            try
            {
                await _bladeLightingController.StopAsync();
            }
            catch (Exception exception) when (IsExpectedRuntimeException(exception))
            {
                _diagnosticLog.TryWrite("keyboard-lighting", $"path-change restore failed: {exception}");
            }
        }

        try
        {
            await _bladeLightingController.ApplyAsync(_deviceDescriptors, effect, cancellationToken);
            _bladeLightingDevicePath = blade.Id;
            _lightingShadowFingerprint = fingerprint;
            _ = ObserveBladeLightingRuntimeAsync(_bladeLightingController.RuntimeCompletion);
            return null;
        }
        catch (Exception exception) when (IsExpectedRuntimeException(exception))
        {
            _bladeLightingDevicePath = string.Empty;
            _lightingShadowFingerprint = string.Empty;
            return FormatOperationException(exception);
        }
    }

    /// <summary>
    /// Applies a resolved profile on models that render keyboard lighting in firmware. They have no matrix
    /// frame path, so brightness and the firmware effect reach the device through the
    /// telemetry reader instead of the lighting controller.
    /// </summary>
    private async Task<string?> ApplyFirmwareKeyboardLightingProfileAsync(
        DeviceDescriptor blade,
        BladeLightingEffect effect,
        byte? keyboardBrightness,
        LightingProfile lightingProfile,
        PowerSourceKind powerState,
        bool lightingEnabled,
        bool chromaOverrideEnabled,
        CancellationToken cancellationToken)
    {
        try
        {
            if (keyboardBrightness is byte brightness)
            {
                _ = await _deviceTelemetryReader.SetBladeKeyboardBrightnessAsync(
                    _deviceDescriptors,
                    brightness,
                    cancellationToken).ConfigureAwait(false);
            }

            var target = BladeLighting2023Admitted.Allows(effect.Mode)
                ? BladeLighting2023Admitted.ToWireEffect(effect.Mode)
                : BladeLighting2023Effect.Off;
            _blade._bladeKeyboardEffect2023 = await _deviceTelemetryReader
                .SetBlade2023KeyboardEffectAsync(_deviceDescriptors, target, cancellationToken)
                .ConfigureAwait(false);

            _bladeLightingDevicePath = blade.Id;
            _lightingShadowFingerprint = CreateLightingFingerprint(
                lightingProfile,
                blade.Id,
                powerState,
                lightingEnabled,
                chromaOverrideEnabled);
            return null;
        }
        catch (Exception exception) when (IsExpectedRuntimeException(exception))
        {
            _bladeLightingDevicePath = string.Empty;
            _lightingShadowFingerprint = string.Empty;
            return FormatOperationException(exception);
        }
    }

    private string CreateLightingFingerprint(
        LightingProfile profile,
        string devicePath,
        PowerSourceKind powerState,
        bool lightingEnabled = true,
        bool chromaOverrideEnabled = true) =>
        $"{_profile.ActiveProfileName}\n{powerState}\n{lightingEnabled}\n{chromaOverrideEnabled}\n{BladeLightingProfileCodec.Fingerprint(profile, devicePath)}";

    internal async Task RestoreBladeLightingAfterExternalAsync()
    {
        if (_bladeLightingController is null)
        {
            return;
        }

        await _deviceOperationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            _lightingShadowFingerprint = string.Empty;
            var blade = _deviceDescriptors.FirstOrDefault(device =>
                DeviceProtocolFamilies.IsBlade(device.ProtocolFamily) &&
                device.Access == DeviceAccessState.Available);
            await ApplyLoadedLightingProfileAsync(
                blade,
                CurrentPowerSource,
                CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _deviceOperationGate.Release();
        }
    }

    internal bool IsBladeChromaOverrideEnabled()
    {
        var blade = _deviceDescriptors.FirstOrDefault(device =>
            DeviceProtocolFamilies.IsBlade(device.ProtocolFamily));
        if (blade is null)
        {
            return true;
        }

        var resolved = ProfileResolver.Resolve(_profile, blade, CurrentPowerSource).Blade;
        return (resolved.ChromaOverrideEnabled ?? true) && (resolved.LightingEnabled ?? true);
    }
}
