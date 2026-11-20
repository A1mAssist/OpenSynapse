using OpenSynapse.Core.Devices;
using static OpenSynapse.App.ViewModels.DeviceUiCatalog;

namespace OpenSynapse.App.ViewModels;

public sealed partial class LightingWorkspace
{
    public async Task SetDisplayAvailableAsync(bool available)
    {
        _setDisplayAvailable(available);
        if (!available && UsesFirmwareKeyboardLighting)
        {
            await ForceFirmwareBladeLightingOffAsync().ConfigureAwait(false);
        }

        if (_bladeLightingController is not null)
        {
            try
            {
                await _bladeLightingController
                    .SetDisplayAvailableAsync(available, _deviceDescriptors())
                    .ConfigureAwait(false);
                if (!available)
                {
                    var blade = FindBlade();
                    if (blade is not null)
                    {
                        _bladeLightingDevicePath = blade.Id;
                        _lightingShadowFingerprint = $"display-off\n{blade.Id}";
                    }
                }
            }
            catch (Exception exception) when (RuntimeErrors.IsExpected(exception))
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
                        _deviceDescriptors(),
                        BladeLogoMode.Off,
                        CancellationToken.None).ConfigureAwait(false);
                }
                else if (available && _displaySuspendedLogoMode is { } logoMode)
                {
                    await _deviceTelemetryReader.SetBladeLogoModeAsync(
                        _deviceDescriptors(),
                        logoMode,
                        CancellationToken.None).ConfigureAwait(false);
                    _displaySuspendedLogoMode = null;
                }
            }
            catch (Exception exception) when (RuntimeErrors.IsExpected(exception))
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
            _requestDeviceRefresh();
        }
    }

    private DeviceDescriptor? FindBlade() =>
        _deviceDescriptors().FirstOrDefault(device =>
            DeviceProtocolFamilies.IsBlade(device.ProtocolFamily) &&
            device.Access == DeviceAccessState.Available);

    private async Task ForceFirmwareBladeLightingOffAsync()
    {
        var blade = FindBlade();
        if (blade is null)
        {
            return;
        }

        try
        {
            _ = await _deviceTelemetryReader.SetBlade2023KeyboardEffectAsync(
                _deviceDescriptors(),
                BladeLighting2023Effect.Off,
                CancellationToken.None).ConfigureAwait(false);
            _bladeLightingDevicePath = blade.Id;
            _lightingShadowFingerprint = $"display-off\n{blade.Id}";
        }
        catch (Exception exception) when (RuntimeErrors.IsExpected(exception))
        {
            _diagnosticLog.TryWrite(
                "keyboard-lighting",
                $"display-off firmware lighting failed: {exception}");
        }
    }
}
