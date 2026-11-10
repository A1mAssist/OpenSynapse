using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RefreshWorkspace
{
    private readonly DeviceProfileApplyCoordinator? _razerHidProfileApplier;

    [NotProfileScoped]
    private async Task<ProfileApplyResult?> ApplyLoadedProfileAsync(
        RazerDeviceTelemetry telemetry,
        PowerSourceKind powerState,
        CancellationToken cancellationToken)
    {
        var devices = _liteMode
            ? ActiveDescriptors
                .Where(device => !DeviceProtocolFamilies.IsBlade(device.ProtocolFamily))
                .ToArray()
            : ActiveDescriptors;
        if (devices.Count == 0)
        {
            return null;
        }

        var result = await _profileApplier.ApplyAsync(
            Profile,
            devices,
            telemetry,
            _deviceTelemetryReader,
            powerState,
            cancellationToken,
            applyKeyboardBrightness: IsDisplayAvailable,
            coolingPadConnected: _blade.CoolingPadConnected,
            padlessHyperBoostAccepted: _blade.PadlessHyperBoostAccepted,
            displayController: _liteMode ? null : _display.Controller,
            applyBladeTuningAsync: _liteMode ? null : _blade.ApplyProfileTuningAsync);
        if (_razerHidProfileApplier is null)
        {
            return result;
        }

        var razerHidDevices = devices
            .Where(device => device.Category is DeviceCategory.Mouse or DeviceCategory.Keyboard)
            .Where(device => device.ProtocolFamily != DeviceProtocolFamilies.Viper)
            .Where(device => _openRazer.ResolveConnection(device) is not null)
            .ToArray();
        if (razerHidDevices.Length == 0)
        {
            return result;
        }

        var razerHid = await _razerHidProfileApplier.ApplyAsync(
            Profile,
            razerHidDevices,
            powerState,
            cancellationToken);
        var applied = razerHid.Devices.Sum(device => device.Items.Count(item =>
            item.Status is ProfileSettingApplyStatus.Applied or ProfileSettingApplyStatus.AppliedUnverified));
        return new ProfileApplyResult(
            result.AppliedCount + applied,
            result.Errors.Concat(razerHid.Errors).ToArray());
    }

    [NotProfileScoped]
    private async Task ApplyBleProfileAsync(PowerSourceKind powerState, CancellationToken cancellationToken)
    {
        if (_applyBleProfile is null)
        {
            return;
        }

        try
        {
            await _applyBleProfile(powerState, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _diagnosticLog.TryWrite("ble", $"profile apply failed: {exception.Message}");
        }
    }

    [NotProfileScoped]
    private async Task ApplyAccessoryProfilesAsync(
        PowerSourceKind powerState,
        CancellationToken cancellationToken)
    {
        if (_applyAccessoryProfiles is null)
        {
            return;
        }

        try
        {
            await _applyAccessoryProfiles(powerState, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _diagnosticLog.TryWrite("profile", $"accessory profile apply failed: {exception.Message}");
        }
    }
}
