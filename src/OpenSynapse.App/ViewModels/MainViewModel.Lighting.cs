using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;
using static OpenSynapse.App.ViewModels.DeviceUiCatalog;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel
{
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
                        device.ProtocolFamily == DeviceProtocolFamilies.Blade &&
                        device.Access == DeviceAccessState.Available);
                    if (blade is not null)
                    {
                        await _bladeLightingController.ApplyAsync(
                            _deviceDescriptors,
                            BladeLightingEffect.Off,
                            CancellationToken.None).ConfigureAwait(false);
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
        bool? powerState,
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

        if (Volatile.Read(ref _displayAvailable) == 0)
        {
            var offFingerprint = $"display-off\n{blade.Id}";
            if (StringComparer.Ordinal.Equals(_lightingShadowFingerprint, offFingerprint))
            {
                return null;
            }
            try
            {
                await _bladeLightingController.ApplyAsync(
                    _deviceDescriptors,
                    BladeLightingEffect.Off,
                    cancellationToken).ConfigureAwait(false);
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

        var profile = ProfileResolver.Resolve(_profile, blade, powerState).Lighting;
        string fingerprint;
        BladeLightingEffect effect;
        try
        {
            effect = BladeLightingProfileCodec.Parse(profile);
            fingerprint = CreateLightingFingerprint(profile, blade.Id, powerState);
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

    private string CreateLightingFingerprint(
        LightingProfile profile,
        string devicePath,
        bool? powerState) =>
        $"{_profile.ActiveProfileName}\n{powerState}\n{BladeLightingProfileCodec.Fingerprint(profile, devicePath)}";

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
                device.ProtocolFamily == DeviceProtocolFamilies.Blade &&
                device.Access == DeviceAccessState.Available);
            await ApplyLoadedLightingProfileAsync(
                blade,
                _powerSourceProvider.IsPluggedIn,
                CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _deviceOperationGate.Release();
        }
    }
}
