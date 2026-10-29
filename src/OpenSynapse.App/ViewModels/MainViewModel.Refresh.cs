using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel
{
    private readonly Dictionary<string, string> _deviceSerials = new(StringComparer.OrdinalIgnoreCase);
    private string? _bladeSerial;
    private string? _viperSerial;

    public string? BladeSerial { get => _bladeSerial; private set => SetField(ref _bladeSerial, value); }
    public string? ViperSerial { get => _viperSerial; private set => SetField(ref _viperSerial, value); }
    public Visibility BladeSerialVisibility => string.IsNullOrWhiteSpace(BladeSerial) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ViperSerialVisibility => string.IsNullOrWhiteSpace(ViperSerial) ? Visibility.Collapsed : Visibility.Visible;

    private async Task<string?> GetDeviceSerialAsync(DeviceDescriptor? device, CancellationToken cancellationToken)
    {
        if (device is not { Access: DeviceAccessState.Available }) return null;
        if (_deviceSerials.TryGetValue(device.Id, out var serial)) return serial;
        serial = await WindowsHidDiscovery.TryReadSerialNumberAsync(device.Id, cancellationToken);
        if (!string.IsNullOrWhiteSpace(serial)) _deviceSerials[device.Id] = serial;
        return serial;
    }

    private async Task RefreshCoolingPadPresenceAsync(CancellationToken cancellationToken)
    {
        var connected = await RazerCoolingPad.IsConnectedAsync(cancellationToken);
        if (_bladeCoolingPadConnected == connected)
        {
            return;
        }

        _bladeCoolingPadConnected = connected;
        OnPropertyChanged(nameof(HyperboostOptionVisibility));
        OnPropertyChanged(nameof(HyperboostNeedsCoolingPadWarning));
        OnPropertyChanged(nameof(BladePerformanceModeOptions));
    }

    /// <summary>
    /// Reads the device telemetry again so the live readouts on the device page keep up.
    /// This is a read-only path: it never pushes a stored profile back to the hardware,
    /// so it cannot pin the fans or rewrite a setting the user did not just change.
    /// </summary>
    private async Task RefreshDeviceReadoutsAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _deviceReadoutSamplingEnabled) == 0 ||
            !await TryEnterOperationAsync(cancellationToken))
        {
            return;
        }

        try
        {
            if (OpenRazer.SelectedDevice is { } openRazerDevice)
            {
                await openRazerDevice.RefreshBatteryStateAsync(cancellationToken);
            }
            if (_deviceDescriptors.Count == 0)
            {
                return;
            }
            var telemetry = await _deviceTelemetryReader.ReadAsync(_deviceDescriptors, cancellationToken);
            ApplyDeviceTelemetry(telemetry);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedRuntimeException(exception))
        {
            _diagnosticLog.TryWrite("device-readout", $"readout refresh failed: {exception.Message}");
        }
        finally
        {
            _deviceOperationGate.Release();
        }
    }

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
        var powerState = CurrentPowerSource;
        var powerChanged = _lastPowerState != powerState;
        applyDisplayProfile |= powerChanged;
        if (powerChanged)
        {
            await Telemetry.RefreshBatteryHealthAsync(cancellationToken);
        }
        try
        {
            var snapshot = knownSnapshot ?? await _discovery.DiscoverAsync(cancellationToken);
            await OpenRazer.RefreshAsync(cancellationToken);
            await RefreshBlackSharkAsync(cancellationToken);
            await RefreshHeadsetAudioAsync(cancellationToken);
            await RefreshKeyboardsAsync(cancellationToken);
            await RefreshGamepadsAsync(cancellationToken);
            await RefreshChromaAsync(cancellationToken);
            await RefreshBleAsync(cancellationToken);
            var nextFingerprint = CreateDeviceFingerprint(snapshot);
            if (!StringComparer.Ordinal.Equals(_deviceFingerprint, nextFingerprint))
            {
                ResetDeviceTelemetry();
                _deviceSerials.Clear();
            }
            _deviceFingerprint = nextFingerprint;
            _deviceDescriptors = snapshot.Devices;

            var blade = snapshot.Devices.FirstOrDefault(device =>
                DeviceProtocolFamilies.IsBlade(device.ProtocolFamily));
            var viper = snapshot.Devices.FirstOrDefault(device => device.ProtocolFamily == DeviceProtocolFamilies.Viper);
            SetBladeControlDevicePath(
                blade is
                {
                    Access: DeviceAccessState.Available,
                    FeatureReportByteLength: RazerFeatureReport.Length,
                }
                    ? blade.Id
                    : null,
                blade?.ProductId ?? 0);
            await RefreshCoolingPadPresenceAsync(cancellationToken);
            BladeDeviceName = blade?.Name ?? "Razer Blade";
            if (blade is not null)
            {
                ProbeBladeGpuClockOffset();
            }
            if (_blade._bladeProductId != (blade?.ProductId ?? 0))
            {
                _blade._bladeProductId = blade?.ProductId ?? 0;
                RefreshBladeModelCapabilityUi();
            }
            ViperDeviceName = viper?.Name ?? "Razer Viper";
            BladeSerial = await GetDeviceSerialAsync(blade, cancellationToken);
            ViperSerial = await GetDeviceSerialAsync(viper, cancellationToken);
            OnPropertyChanged(nameof(BladeSerialVisibility));
            OnPropertyChanged(nameof(ViperSerialVisibility));
            BladeStatusText = FormatDeviceStatus(blade);
            ViperStatusText = FormatDeviceStatus(viper);

            var telemetry = await _deviceTelemetryReader.ReadAsync(snapshot.Devices, cancellationToken);
            ApplyDeviceTelemetry(telemetry);
            var observedPowerState = PowerSourceResolver.Resolve(
                _powerSourceProvider.IsPluggedIn,
                telemetry.BladeAdapterPower);
            var observedPowerChanged = _lastPowerState != observedPowerState;
            if (observedPowerChanged)
            {
                powerState = observedPowerState;
                powerChanged = true;
                applyDisplayProfile = true;
                await Telemetry.RefreshBatteryHealthAsync(cancellationToken);
            }
            _lastPowerState = powerState;
            OnPropertyChanged(nameof(ActivePowerSourceText));
            Display.RefreshInternalDisplay(powerState, applyDisplayProfile);
            if (applyDisplayProfile)
            {
                await OpenRazer.RestoreLightingAfterExternalAsync(cancellationToken);
                OpenRazer.SelectedDevice?.ApplyConfiguredLightingToEditor();
            }
            var profileApply = await ApplyLoadedProfileAsync(
                telemetry,
                powerState,
                cancellationToken);
            if (profileApply is { AppliedCount: > 0 })
            {
                telemetry = await _deviceTelemetryReader.ReadAsync(snapshot.Devices, cancellationToken);
                ApplyDeviceTelemetry(telemetry);
            }
            RefreshBladeLightingEditor();
            RefreshBladePerformanceEditor();
            RefreshBladeLogoEditor();
            var viperAvailable = viper is not null &&
                (telemetry.CapabilitySummaries?.GetValueOrDefault(viper.Id)
                    ?? DeviceCapabilitySummaryCalculator.Calculate(viper, telemetry)).Available > 0;
            var bladeProfileBlocked = profileApply?.Errors.Any(error =>
                error.StartsWith("Blade", StringComparison.OrdinalIgnoreCase)) == true;
            var fanApply = bladeProfileBlocked
                ? new BladeFanProfileApplyResult(
                    await Fan.StopAsync("profile-error"),
                    Changed: true)
                : await Fan.ApplyProfileAsync(blade, powerState, cancellationToken, writeStoredTarget: false);
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
                : await Lighting.ApplyLoadedProfileAsync(blade, powerState, cancellationToken);
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
            RefreshDeviceSelectorItems();
            Devices.Clear();
            foreach (var device in visibleDevices
                .OrderBy(device => DeviceUiCatalog.DeviceCategoryOrder(device.Category))
                .ThenBy(device => device.Name, StringComparer.OrdinalIgnoreCase))
            {
                Devices.Add(new DeviceRowViewModel(device, telemetry));
            }
            RefreshDeviceOverviewVisibility();

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

            var openRazerErrorCount = OpenRazer.ErrorCount;

            if (Volatile.Read(ref _displayAvailable) != 0 &&
                Interlocked.Exchange(ref _displayBrightnessRestorePending, 0) != 0)
            {
                RefreshBladeLightingEditor();
            }

            if (Volatile.Read(ref _diagnosticsRebuildEnabled) != 0)
            {
                RebuildDiagnostics(snapshot with { Devices = visibleDevices }, telemetry, errors);
            }
            var totalErrorCount = errors.Count + openRazerErrorCount;
            SetDeviceQueryError(totalErrorCount == 0
                ? string.Empty
                : AppStrings.FormatText("HardwareQueryFailureCount",
                    totalErrorCount));
            LastDeviceRefreshText = AppStrings.FormatText("DeviceScanTime",
                snapshot.CapturedAt.ToLocalTime());
            OnPropertyChanged(nameof(EmptyStateText));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedRuntimeException(exception))
        {
            var hadBlade = _deviceDescriptors.Any(device => DeviceProtocolFamilies.IsBlade(device.ProtocolFamily));
            var hadViper = _deviceDescriptors.Any(device => device.ProtocolFamily == DeviceProtocolFamilies.Viper);
            _deviceDescriptors = Array.Empty<DeviceDescriptor>();
            BlackSharkDevices.Clear();
            _selectedBlackSharkDevice = null;
            SelectedBlackShark = null;
            BladeSerial = null;
            ViperSerial = null;
            OnPropertyChanged(nameof(BladeSerialVisibility));
            OnPropertyChanged(nameof(ViperSerialVisibility));
            ViperDeviceVisibility = Visibility.Collapsed;
            RefreshDeviceSelectorItems();
            if (_bladeLightingController is not null && Lighting.LightingDevicePath.Length > 0)
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
            _ = await Fan.StopAsync("refresh-failure");
            SetBladeControlDevicePath(null, 0);
            Lighting.ShadowFingerprint = string.Empty;
            Lighting.LightingDevicePath = string.Empty;
            _lastPowerState = powerState;
            OnPropertyChanged(nameof(ActivePowerSourceText));
            Display.RefreshInternalDisplay(powerState, applyDisplayProfile);
            BladeStatusText = hadBlade ? AppStrings.Text("Text_A70924BE") : AppStrings.Text("Text_DB0974DC");
            ViperStatusText = hadViper ? AppStrings.Text("Text_A70924BE") : AppStrings.Text("Text_DB0974DC");
            SetDeviceQueryError(exception.Message);
            LastDeviceRefreshText = AppStrings.Text("Text_F9E7EDEF");
            OnPropertyChanged(nameof(EmptyStateText));
        }
        finally
        {
            _deviceOperationGate.Release();
            OnPropertyChanged(nameof(CanCheckFirmwareUpdate));
        }
    }

    /// <summary>
    /// The profile changed, so everything that follows the active profile is refreshed here:
    /// the shortcut cycle, the Blade editors, and the macro bindings. The profile page has
    /// already rebuilt its own lists before calling this.
    /// </summary>
    private void ApplyProfileShortcuts(
        HashSet<BladePerformanceMode> cycleModes,
        IReadOnlyList<int>? refreshRates)
    {
        _bladePerformanceCycleModes = cycleModes;
        _internalDisplayRefreshRateCycleHertz = refreshRates?.ToHashSet();
        var snapTap = _profile.Global.Blade.SnapTapEnabled == true;
        var mappingPreset = _profile.Global.Blade.MappingPreset ??
            BladeProfileSettings.Product710DefaultMappingPreset;
        var bladeInputChanged = snapTap != _activeSnapTapEnabled ||
            !StringComparer.Ordinal.Equals(mappingPreset, _activeBladeMappingPreset);
        _activeSnapTapEnabled = snapTap;
        _activeBladeMappingPreset = mappingPreset;
        RefreshBladeLightingEditor();
        RefreshBladePerformanceEditor();
        Macros.RefreshMacroState();
        OnPropertyChanged(nameof(BladePerformanceCycleModes));
        OnPropertyChanged(nameof(InternalDisplayRefreshRateCycleHertz));
        if (bladeInputChanged)
        {
            BladeInputProfileChanged?.Invoke();
        }
    }

    private async Task<bool> SaveProfileAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _profileStore.SaveAsync(_profile, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SetDeviceOperationError(AppStrings.FormatText("ProfileSaveError", exception.Message));
            return false;
        }
    }

    private ProfileDefinition GetActiveProfile() =>
        _profile.Profiles.TryGetValue(_profile.ActiveProfileName, out var profile)
            ? profile
            : throw new InvalidOperationException("The active profile is missing from the profile catalog.");

    private async Task<ProfileApplyResult?> ApplyLoadedProfileAsync(
        RazerDeviceTelemetry telemetry,
        PowerSourceKind powerState,
        CancellationToken cancellationToken)
    {
        if (_deviceDescriptors.Count == 0)
        {
            return null;
        }

        var result = await _profileApplier.ApplyAsync(
            _profile,
            _deviceDescriptors,
            telemetry,
            _deviceTelemetryReader,
            powerState,
            cancellationToken,
            applyKeyboardBrightness: Volatile.Read(ref _displayAvailable) != 0,
            coolingPadConnected: await RazerCoolingPad.IsConnectedAsync(cancellationToken),
            padlessHyperBoostAccepted: _padlessHyperBoostAccepted);
        return result;
    }

    private async Task<string?> ApplyLoadedViperMappingProfileAsync(
        DeviceDescriptor? viper,
        PowerSourceKind powerState,
        CancellationToken cancellationToken)
    {
        if (viper is null || viper.Access != DeviceAccessState.Available)
        {
            _viper._viperMappingProfileFingerprint = string.Empty;
            return null;
        }

        var profile = ProfileResolver.Resolve(_profile, viper, powerState).Viper;
        var fingerprint = CreateViperMappingFingerprint(viper.Id, profile.ButtonAssignments);
        if (StringComparer.Ordinal.Equals(_viper._viperMappingProfileFingerprint, fingerprint))
        {
            return null;
        }

        try
        {
            IReadOnlyList<ViperButtonAssignment> actual;
            if (profile.ButtonAssignments is null)
            {
                actual = await _deviceTelemetryReader.ReadViperButtonAssignmentsAsync(
                    _deviceDescriptors, cancellationToken);
                var previous = _profile.Global.Viper.ButtonAssignments;
                _profile.Global.Viper.ButtonAssignments = actual.Select(ToProfileAssignment).ToList();
                if (!await SaveProfileAsync(cancellationToken))
                {
                    _profile.Global.Viper.ButtonAssignments = previous;
                    return AppStrings.Text("ViperMappingReadProfileSaveFailed");
                }
                fingerprint = CreateViperMappingFingerprint(
                    viper.Id,
                    _profile.Global.Viper.ButtonAssignments);
            }
            else
            {
                actual = await _deviceTelemetryReader.SetViperButtonAssignmentsAsync(
                    _deviceDescriptors,
                    profile.ButtonAssignments.Select(ToDeviceAssignment).ToArray(),
                    cancellationToken);
            }

            SetViperButtonAssignments(actual);
            _viper._viperMappingProfileFingerprint = fingerprint;
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedRuntimeException(exception))
        {
            _viper._viperMappingProfileFingerprint = string.Empty;
            return AppStrings.FormatText("ViperMappingProfileError", exception.Message);
        }
    }

    private string CreateViperMappingFingerprint(
        string devicePath,
        IReadOnlyList<ViperButtonAssignmentProfile>? assignments) =>
        string.Join('|',
            _profile.ActiveProfileName,
            devicePath,
            assignments is null
                ? "unmanaged"
                : string.Join(';', assignments
                    .OrderBy(item => item.ButtonId)
                    .ThenBy(item => item.Layer)
                    .Select(item => $"{item.ProfileId:X2}:{item.ButtonId:X2}:{(byte)item.Layer:X2}:{(byte)item.Function:X2}:{Convert.ToHexString(item.FunctionData.ToArray())}")));

}

