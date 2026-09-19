using OpenSynapse.Core.Devices;
using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel
{
    private async Task RefreshDevicesCoreAsync(
        DeviceSnapshot? knownSnapshot,
        CancellationToken cancellationToken,
        bool applyDisplayProfile = false)
    {
        if (!await TryEnterOperationAsync(cancellationToken))
        {
            return;
        }
        SetDeviceQueryError(string.Empty);
        SetDeviceOperationError(string.Empty);
        var powerState = _powerSourceProvider.IsPluggedIn;
        applyDisplayProfile |= _lastPowerState != powerState;
        _lastPowerState = powerState;
        if (_bladeLightingPowerProfileIndex == 0)
        {
            RefreshBladeLightingEditor();
        }
        if (_bladePerformancePowerProfileIndex == 0)
        {
            RefreshBladePerformanceEditor();
        }
        try
        {
            var snapshot = knownSnapshot ?? await _discovery.DiscoverAsync(cancellationToken);
            if (_openRazerDeviceService is not null)
            {
                var openRazerConnections = await _openRazerDeviceService.DiscoverAsync(cancellationToken);
                if (OpenRazerDevices.Count != openRazerConnections.Count ||
                    !OpenRazerDevices.Zip(openRazerConnections)
                        .All(pair => OpenRazerConnectionMatches(pair.First.Connection, pair.Second)))
                {
                    var selectedInstanceId = SelectedOpenRazerDevice?.InstanceId;
                    _openRazerSelectionCancellation?.Cancel();
                    _openRazerSelectionCancellation?.Dispose();
                    _openRazerSelectionCancellation = null;
                    OpenRazerDevices.Clear();
                    foreach (var connection in openRazerConnections)
                    {
                        OpenRazerDevices.Add(new OpenRazerDeviceRowViewModel(connection));
                    }
                    var selectedRow = OpenRazerDevices.FirstOrDefault(row =>
                        StringComparer.OrdinalIgnoreCase.Equals(row.Connection.InstanceId, selectedInstanceId));
                    if (selectedRow is null)
                    {
                        SelectedOpenRazerDevice = null;
                    }
                    else
                    {
                        await SelectOpenRazerDeviceAsync(selectedRow, cancellationToken);
                    }
                }
                else if (SelectedOpenRazerDevice is { } selectedDevice)
                {
                    await selectedDevice.LoadBasicStateAsync(cancellationToken);
                    if (applyDisplayProfile)
                    {
                        await selectedDevice.ApplyConfiguredLightingAsync(cancellationToken);
                    }
                }
            }
            if (_openRazerSpecialLightingService is not null)
            {
                var connections = await _openRazerSpecialLightingService.DiscoverAsync(cancellationToken);
                var krakenConnections = connections.Where(connection =>
                    connection.Kind == OpenRazerSpecialLightingKind.Kraken37).ToArray();
                if (OpenRazerKrakenDevices.Count != krakenConnections.Length ||
                    !OpenRazerKrakenDevices.Zip(krakenConnections)
                        .All(pair => OpenRazerKrakenConnectionMatches(pair.First.Connection, pair.Second)))
                {
                    var selectedKrakenId = SelectedOpenRazerKraken?.InstanceId;
                    OpenRazerKrakenDevices.Clear();
                    foreach (var connection in krakenConnections)
                    {
                        OpenRazerKrakenDevices.Add(new OpenRazerKrakenDeviceRowViewModel(connection));
                    }
                    var selectedKraken = OpenRazerKrakenDevices.FirstOrDefault(row =>
                        StringComparer.OrdinalIgnoreCase.Equals(row.Connection.InstanceId, selectedKrakenId));
                    SelectedOpenRazerKraken = selectedKraken is null
                        ? null
                        : new OpenRazerKrakenViewModel(_openRazerSpecialLightingService, selectedKraken.Connection);
                }
            }
            var nextFingerprint = CreateDeviceFingerprint(snapshot);
            if (!StringComparer.Ordinal.Equals(_deviceFingerprint, nextFingerprint))
            {
                ResetDeviceTelemetry();
            }
            _deviceFingerprint = nextFingerprint;
            _deviceDescriptors = snapshot.Devices;
            RefreshInternalDisplay(powerState, applyDisplayProfile);

            var blade = snapshot.Devices.FirstOrDefault(device => device.ProtocolFamily == DeviceProtocolFamilies.Blade);
            var viper = snapshot.Devices.FirstOrDefault(device => device.ProtocolFamily == DeviceProtocolFamilies.Viper);
            SetBladeControlDevicePath(
                blade is
                {
                    Access: DeviceAccessState.Available,
                    FeatureReportByteLength: RazerFeatureReport.Length,
                }
                    ? blade.Id
                    : null);
            BladeDeviceName = blade?.Name ?? "Razer Blade";
            ViperDeviceName = viper?.Name ?? "Razer Viper";
            BladeStatusText = FormatDeviceStatus(blade);
            ViperStatusText = FormatDeviceStatus(viper);

            var telemetry = await _deviceTelemetryReader.ReadAsync(snapshot.Devices, cancellationToken);
            ApplyDeviceTelemetry(telemetry);
            var profileApply = await ApplyLoadedProfileAsync(telemetry, cancellationToken);
            if (profileApply is { AppliedCount: > 0 })
            {
                telemetry = await _deviceTelemetryReader.ReadAsync(snapshot.Devices, cancellationToken);
                ApplyDeviceTelemetry(telemetry);
            }
            RefreshBladeLightingEditor();
            RefreshBladePerformanceEditor();
            var viperAvailable = viper is not null &&
                (telemetry.CapabilitySummaries?.GetValueOrDefault(viper.Id)
                    ?? DeviceCapabilitySummaryCalculator.Calculate(viper, telemetry)).Available > 0;
            var bladeProfileBlocked = profileApply?.Errors.Any(error =>
                error.StartsWith("Blade", StringComparison.OrdinalIgnoreCase)) == true;
            var fanApply = bladeProfileBlocked
                ? new BladeFanProfileApplyResult(
                    await StopBladeFanControlAsync("profile-error"),
                    Changed: true)
                : await ApplyLoadedFanProfileAsync(blade, powerState, cancellationToken);
            var viperMappingError = await ApplyLoadedViperMappingProfileAsync(
                viperAvailable ? viper : null, powerState, cancellationToken);
            if (fanApply.Changed && blade is { Access: DeviceAccessState.Available })
            {
                telemetry = await _deviceTelemetryReader.ReadAsync(snapshot.Devices, cancellationToken);
                ApplyDeviceTelemetry(telemetry);
            }
            var lightingError = profileApply?.Errors.Any(error =>
                    error.StartsWith("Blade", StringComparison.OrdinalIgnoreCase)) == true
                ? null
                : await ApplyLoadedLightingProfileAsync(blade, powerState, cancellationToken);
            var profileOperationErrors = profileApply?.Errors
                .Where(error => viperAvailable ||
                    !error.StartsWith("Viper", StringComparison.OrdinalIgnoreCase))
                .ToArray() ?? [];
            var profileOperationError = profileOperationErrors.Length == 0
                ? string.Empty
                : AppStrings.FormatText("ProfileApplyError",
                    string.Join("; ", profileOperationErrors));
            SetDeviceOperationError(string.Join(
                Environment.NewLine,
                new[]
                {
                    profileOperationError,
                    viperAvailable ? viperMappingError : null,
                }.Where(error => !string.IsNullOrWhiteSpace(error))));
            var visibleDevices = viperAvailable
                ? snapshot.Devices
                : snapshot.Devices.Where(device => device.ProtocolFamily != DeviceProtocolFamilies.Viper).ToArray();
            _deviceDescriptors = visibleDevices;
            ViperDeviceVisibility = viperAvailable ? Visibility.Visible : Visibility.Collapsed;
            if (!viperAvailable)
            {
                ResetViperTelemetry();
                ViperStatusText = AppStrings.Text("Text_DB0974DC");
            }
            Devices.Clear();
            foreach (var device in visibleDevices)
            {
                Devices.Add(new DeviceRowViewModel(device, telemetry));
            }

            var errors = telemetry.Errors
                .Where(error => viperAvailable ||
                    !error.StartsWith(AppStrings.Text("Text_4B32CEE8"), StringComparison.Ordinal))
                .ToList();
            if (profileApply is { Errors.Count: > 0 })
            {
                errors.AddRange(profileApply.Errors
                    .Where(error => viperAvailable || !error.StartsWith("Viper", StringComparison.OrdinalIgnoreCase))
                    .Select(error => AppStrings.FormatText("ProfileApplyError", error)));
            }
            if (!string.IsNullOrWhiteSpace(lightingError))
            {
                errors.Add(AppStrings.FormatText("LightingError", lightingError));
            }
            if (!string.IsNullOrWhiteSpace(fanApply.Error))
            {
                errors.Add(AppStrings.FormatText("FanControlError", fanApply.Error));
            }
            if (!string.IsNullOrWhiteSpace(viperMappingError) && viperAvailable)
            {
                errors.Add(viperMappingError);
            }
            if (!string.IsNullOrWhiteSpace(snapshot.ErrorMessage))
            {
                errors.Insert(0, snapshot.ErrorMessage);
            }

            if (Volatile.Read(ref _displayAvailable) != 0 &&
                Interlocked.Exchange(ref _displayBrightnessRestorePending, 0) != 0)
            {
                RefreshBladeLightingEditor();
            }

            RebuildDiagnostics(snapshot with { Devices = visibleDevices }, telemetry, errors);
            SetDeviceQueryError(errors.Count == 0
                ? string.Empty
                : AppStrings.FormatText("HardwareQueryFailureCount",
                    errors.Count));
            LastDeviceRefreshText = AppStrings.FormatText("DeviceScanTime",
                snapshot.CapturedAt.ToLocalTime());
            _nextFullDeviceRefresh = DateTimeOffset.UtcNow + ForegroundDeviceScanInterval;
            Interlocked.Exchange(ref _deviceRefreshRequested, 0);
            OnPropertyChanged(nameof(EmptyStateText));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedRuntimeException(exception))
        {
            var hadBlade = _deviceDescriptors.Any(device => device.ProtocolFamily == DeviceProtocolFamilies.Blade);
            var hadViper = _deviceDescriptors.Any(device => device.ProtocolFamily == DeviceProtocolFamilies.Viper);
            _deviceDescriptors = Array.Empty<DeviceDescriptor>();
            ViperDeviceVisibility = Visibility.Collapsed;
            if (_bladeLightingController is not null && _bladeLightingDevicePath.Length > 0)
            {
                try
                {
                    await _bladeLightingController.StopAsync();
                }
                catch (Exception restoreException) when (IsExpectedRuntimeException(restoreException))
                {
                    _diagnosticLog.TryWrite(
                        "keyboard-lighting",
                        $"refresh-failure restore failed: {restoreException}");
                }
            }
            _ = await StopBladeFanControlAsync("refresh-failure");
            SetBladeControlDevicePath(null);
            _lightingShadowFingerprint = string.Empty;
            _bladeLightingDevicePath = string.Empty;
            RefreshInternalDisplay(powerState, applyDisplayProfile);
            BladeStatusText = hadBlade ? AppStrings.Text("Text_A70924BE") : AppStrings.Text("Text_DB0974DC");
            ViperStatusText = hadViper ? AppStrings.Text("Text_A70924BE") : AppStrings.Text("Text_DB0974DC");
            SetDeviceQueryError(exception.Message);
            LastDeviceRefreshText = AppStrings.Text("Text_F9E7EDEF");
            OnPropertyChanged(nameof(EmptyStateText));
        }
        finally
        {
            _deviceOperationGate.Release();
        }
    }

    private static bool OpenRazerConnectionMatches(
        OpenRazerDeviceConnection previous,
        OpenRazerDeviceConnection current) =>
        StringComparer.OrdinalIgnoreCase.Equals(previous.InstanceId, current.InstanceId) &&
        StringComparer.OrdinalIgnoreCase.Equals(previous.DevicePath, current.DevicePath) &&
        previous.EndpointState == current.EndpointState &&
        StringComparer.Ordinal.Equals(previous.Error, current.Error);

    private static bool OpenRazerKrakenConnectionMatches(
        OpenRazerSpecialLightingConnection previous,
        OpenRazerSpecialLightingConnection current) =>
        StringComparer.OrdinalIgnoreCase.Equals(previous.InstanceId, current.InstanceId) &&
        StringComparer.OrdinalIgnoreCase.Equals(previous.DevicePath, current.DevicePath) &&
        StringComparer.Ordinal.Equals(previous.Error, current.Error);

}
