using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Devices;
using OpenSynapse.App.Runtime;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RefreshWorkspace
{
    private readonly DeviceProfileApplyCoordinator? _razerHidProfileApplier;

    [NotProfileScoped]
    private async Task ApplyDeviceApplicationRulesAsync(string? executablePath, CancellationToken token)
    {
        if (_razerHidProfileApplier is null) return;
        await _deviceOperationGate.WaitAsync(token);
        try
        {
            await ApplyDeviceApplicationRulesCoreAsync(executablePath, token);
        }
        finally
        {
            _deviceOperationGate.Release();
        }
    }

    [NotProfileScoped]
    private async Task ApplyDeviceApplicationRulesCoreAsync(string? executablePath, CancellationToken token)
    {
        if (_razerHidProfileApplier is null) return;
        var connections = _razerHid.CurrentConnections
            .Where(connection => connection.Definition.Category is DeviceCategory.Mouse or DeviceCategory.Keyboard)
            .Select(connection => new DeviceApplicationSwitchDevice(
                new DeviceBindingKey(RazerHidDeviceDefinition.VendorId, connection.Definition.ProductId, connection.InstanceId),
                connection.ToDescriptor()))
            .ToArray();
        var foregroundChanged = !StringComparer.OrdinalIgnoreCase.Equals(_lastObservedForegroundExecutablePath, executablePath);
        var changes = _applicationSwitch.Update(_rulesDocument(), connections, Profile, executablePath,
            foregroundChanged: foregroundChanged,
            manualChange: Interlocked.Exchange(ref _applicationSwitchRequested, 0) == 2);
        _lastObservedForegroundExecutablePath = executablePath;
        foreach (var change in changes)
        {
            var descriptor = connections.Single(device => device.Binding.Equals(change.Device)).Descriptor;
            var selected = DeviceProfileSelection.For(Profile, change.ProfileName);
            var result = await _razerHidProfileApplier.ApplyAsync(
                selected, [descriptor], _currentPowerSource(), token);
            _applicationSwitch.Complete(change.Device, change.ProfileName, change.RuleId, result.Succeeded);
        }
        _rulesChanged();
    }

    [NotProfileScoped]
    internal async Task<bool> ApplyProfileToDeviceAsync(object source, string profileName, CancellationToken token)
    {
        if (_razerHidProfileApplier is null || source is not RazerHidDeviceRowViewModel row ||
            !Profile.Profiles.ContainsKey(profileName)) return false;
        await _deviceOperationGate.WaitAsync(token);
        try
        {
            var descriptor = row.Connection.ToDescriptor();
            var selected = DeviceProfileSelection.For(Profile, profileName);
            var result = await _razerHidProfileApplier.ApplyAsync(selected, [descriptor], _currentPowerSource(), token);
            _applicationSwitch.MarkManualOverride(new DeviceBindingKey(descriptor.VendorId, descriptor.ProductId, row.Connection.InstanceId));
            return result.Succeeded;
        }
        finally { _deviceOperationGate.Release(); }
    }

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

        var powerEvidence = PowerSourceResolver.ResolveEvidence(
            _powerSourceProvider.IsPluggedIn,
            telemetry.BladeAdapterPower);
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
            adapterPowerVerified: PowerSourceResolver.IsVerifiedFullPower(powerEvidence),
            displayController: _liteMode ? null : _display.Controller,
            applyBladeTuningAsync: _liteMode ? null : _blade.ApplyProfileTuningAsync);
        if (_razerHidProfileApplier is null)
        {
            return result;
        }

        var razerHidDevices = devices
            .Where(device => device.Category is DeviceCategory.Mouse or DeviceCategory.Keyboard)
            .Where(device => device.ProtocolFamily != DeviceProtocolFamilies.Viper)
            .Where(device => _razerHid.ResolveConnection(device) is not null)
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
