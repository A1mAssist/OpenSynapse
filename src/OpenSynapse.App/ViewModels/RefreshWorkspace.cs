using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Diagnostics;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed class RefreshWorkspace : INotifyPropertyChanged
{
    private readonly IDeviceDiscovery _discovery;
    private readonly IRazerDeviceTelemetryReader _deviceTelemetryReader;
    private readonly ProfileStore _profileStore;
    private readonly IPowerSourceProvider _powerSourceProvider;
    private readonly IActiveApplicationProvider _activeApplicationProvider;
    private readonly LocalDiagnosticLog _diagnosticLog;
    private readonly IBladeLightingController? _bladeLightingController;
    private readonly SemaphoreSlim _deviceOperationGate;
    private readonly Func<ProfileDocument> _getProfile;
    private readonly Action<ProfileDocument> _setProfile;
    private readonly Func<IReadOnlyList<DeviceDescriptor>> _deviceDescriptors;
    private readonly Action<IReadOnlyList<DeviceDescriptor>> _setDeviceDescriptors;
    private readonly Func<PowerSourceKind> _currentPowerSource;
    private readonly Func<DeviceDescriptor?, CancellationToken, Task<string?>> _readSerialAsync;
    private readonly Action _clearSerials;
    private readonly Func<CancellationToken, Task> _refreshAccessoriesAsync;
    private readonly BladeWorkspace _blade;
    private readonly DisplayWorkspace _display;
    private readonly FanWorkspace _fan;
    private readonly LightingWorkspace _lighting;
    private readonly OpenRazerWorkspace _openRazer;
    private readonly ViperWorkspace _viper;
    private readonly TelemetryWorkspace _telemetry;
    private readonly ProfilesWorkspace _profiles;
    private readonly FirmwareWorkspace _firmware;
    private readonly ObservableCollection<DeviceRowViewModel> _devices;
    private readonly Action _refreshDeviceSelector;
    private readonly Action _refreshDeviceOverview;
    private readonly Action<string> _setDeviceQueryError;
    private readonly Action<string> _setDeviceOperationError;
    private readonly Action _resetDeviceTelemetry;
    private readonly Action<RazerDeviceTelemetry> _applyDeviceTelemetry;
    private readonly Action<DeviceSnapshot, RazerDeviceTelemetry, IReadOnlyList<string>> _rebuildDiagnostics;
    private readonly Action _resetBlackShark;
    private readonly Action<string> _notifyPropertyChanged;
    private readonly VerifiedProfileApplier _profileApplier = new();
    private readonly SemaphoreSlim _deviceWatchSignal = new(0, 1);
    private ApplicationProfileSwitcher _applicationProfileSwitcher = new();
    private PowerSourceKind? _lastPowerState;
    private string _deviceFingerprint = string.Empty;
    private string _lastRefreshText = AppStrings.Text("Text_E19E170A");
    private string? _lastObservedForegroundExecutablePath;
    private string? _lastObservedApplicationBindings;
    private string? _lastObservedActiveProfileName;
    private int _deviceRefreshRequested;
    private int _displayProfileApplyRequested;
    private int _deviceReadoutSamplingEnabled = 1;
    private int _diagnosticsRebuildEnabled = 1;
    private int _deviceWatchActive = 1;
    private int _displayAvailable = 1;
    private int _displayBrightnessRestorePending;

    internal RefreshWorkspace(
        IDeviceDiscovery discovery,
        IRazerDeviceTelemetryReader deviceTelemetryReader,
        ProfileStore profileStore,
        IPowerSourceProvider powerSourceProvider,
        IActiveApplicationProvider activeApplicationProvider,
        LocalDiagnosticLog diagnosticLog,
        IBladeLightingController? bladeLightingController,
        SemaphoreSlim deviceOperationGate,
        Func<ProfileDocument> getProfile,
        Action<ProfileDocument> setProfile,
        Func<IReadOnlyList<DeviceDescriptor>> deviceDescriptors,
        Action<IReadOnlyList<DeviceDescriptor>> setDeviceDescriptors,
        Func<PowerSourceKind> currentPowerSource,
        Func<DeviceDescriptor?, CancellationToken, Task<string?>> readSerialAsync,
        Action clearSerials,
        Func<CancellationToken, Task> refreshAccessoriesAsync,
        BladeWorkspace blade,
        DisplayWorkspace display,
        FanWorkspace fan,
        LightingWorkspace lighting,
        OpenRazerWorkspace openRazer,
        ViperWorkspace viper,
        TelemetryWorkspace telemetry,
        ProfilesWorkspace profiles,
        FirmwareWorkspace firmware,
        ObservableCollection<DeviceRowViewModel> devices,
        Action refreshDeviceSelector,
        Action refreshDeviceOverview,
        Action<string> setDeviceQueryError,
        Action<string> setDeviceOperationError,
        Action resetDeviceTelemetry,
        Action<RazerDeviceTelemetry> applyDeviceTelemetry,
        Action<DeviceSnapshot, RazerDeviceTelemetry, IReadOnlyList<string>> rebuildDiagnostics,
        Action resetBlackShark,
        Action<string> notifyPropertyChanged)
    {
        _discovery = discovery;
        _deviceTelemetryReader = deviceTelemetryReader;
        _profileStore = profileStore;
        _powerSourceProvider = powerSourceProvider;
        _activeApplicationProvider = activeApplicationProvider;
        _diagnosticLog = diagnosticLog;
        _bladeLightingController = bladeLightingController;
        _deviceOperationGate = deviceOperationGate;
        _getProfile = getProfile;
        _setProfile = setProfile;
        _deviceDescriptors = deviceDescriptors;
        _setDeviceDescriptors = setDeviceDescriptors;
        _currentPowerSource = currentPowerSource;
        _readSerialAsync = readSerialAsync;
        _clearSerials = clearSerials;
        _refreshAccessoriesAsync = refreshAccessoriesAsync;
        _blade = blade;
        _display = display;
        _fan = fan;
        _lighting = lighting;
        _openRazer = openRazer;
        _viper = viper;
        _telemetry = telemetry;
        _profiles = profiles;
        _firmware = firmware;
        _devices = devices;
        _refreshDeviceSelector = refreshDeviceSelector;
        _refreshDeviceOverview = refreshDeviceOverview;
        _setDeviceQueryError = setDeviceQueryError;
        _setDeviceOperationError = setDeviceOperationError;
        _resetDeviceTelemetry = resetDeviceTelemetry;
        _applyDeviceTelemetry = applyDeviceTelemetry;
        _rebuildDiagnostics = rebuildDiagnostics;
        _resetBlackShark = resetBlackShark;
        _notifyPropertyChanged = notifyPropertyChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string LastRefreshText
    {
        get => _lastRefreshText;
        private set
        {
            if (EqualityComparer<string>.Default.Equals(_lastRefreshText, value))
            {
                return;
            }

            _lastRefreshText = value;
            OnPropertyChanged();
        }
    }

    public void RequestDeviceRefresh()
    {
        Interlocked.Exchange(ref _deviceRefreshRequested, 1);
        try
        {
            _deviceWatchSignal.Release();
        }
        catch (SemaphoreFullException)
        {
            // One pending signal is enough; the refresh flag retains the request.
        }
    }

    internal void SetDeviceWatchActive(bool active)
    {
        Volatile.Write(ref _deviceWatchActive, active ? 1 : 0);
        if (active)
        {
            try
            {
                _deviceWatchSignal.Release();
            }
            catch (SemaphoreFullException)
            {
            }
        }
    }

    internal void SetDeviceReadoutSamplingEnabled(bool enabled) =>
        Interlocked.Exchange(ref _deviceReadoutSamplingEnabled, enabled ? 1 : 0);

    internal void SetDiagnosticsRebuildEnabled(bool enabled) =>
        Interlocked.Exchange(ref _diagnosticsRebuildEnabled, enabled ? 1 : 0);

    internal void RequestProfileApply()
    {
        Interlocked.Exchange(ref _displayProfileApplyRequested, 1);
        RequestDeviceRefresh();
    }

    internal bool IsDisplayAvailable => Volatile.Read(ref _displayAvailable) != 0;

    internal bool IsBrightnessRestorePending => Volatile.Read(ref _displayBrightnessRestorePending) != 0;

    internal void SetDisplayAvailableFlag(bool available)
    {
        if (Interlocked.Exchange(ref _displayAvailable, available ? 1 : 0) !=
            (available ? 1 : 0))
        {
            _telemetry.NotifySamplingStateChanged();
        }
        Interlocked.Exchange(ref _displayBrightnessRestorePending, 1);
        if (!available)
        {
            _blade.OnDisplayAvailabilityChanged(false);
        }
    }

    public Task RefreshDevicesAsync(CancellationToken cancellationToken = default) =>
        RefreshDevicesCoreAsync(null, cancellationToken);

    public async Task RunDeviceWatchLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                var interval = Volatile.Read(ref _deviceWatchActive) != 0 &&
                               IsDisplayAvailable
                    ? TimeSpan.FromSeconds(3)
                    : Profile.ApplicationBindings.Count > 0
                        ? TimeSpan.FromSeconds(10)
                        : Timeout.InfiniteTimeSpan;
                await _deviceWatchSignal.WaitAsync(interval, cancellationToken);
                try
                {
                    var powerState = _currentPowerSource();
                    var executablePath = Profile.ApplicationBindings.Count == 0
                        ? null
                        : _activeApplicationProvider.ExecutablePath;
                    var applicationBindings = Profile.ApplicationBindings.Count == 0
                        ? string.Empty
                        : string.Join(
                            '\n',
                            Profile.ApplicationBindings
                                .OrderBy(binding => binding.Key, StringComparer.OrdinalIgnoreCase)
                                .Select(binding => $"{binding.Key}\0{binding.Value}"));
                    var profileChanged = false;
                    if (_lastObservedApplicationBindings is null ||
                        !StringComparer.OrdinalIgnoreCase.Equals(
                            _lastObservedForegroundExecutablePath, executablePath) ||
                        !StringComparer.Ordinal.Equals(
                            _lastObservedApplicationBindings, applicationBindings) ||
                        !StringComparer.OrdinalIgnoreCase.Equals(
                            _lastObservedActiveProfileName, Profile.ActiveProfileName))
                    {
                        var previousProfile = Profile.Clone();
                        var previousProfileSwitcher = _applicationProfileSwitcher.Clone();
                        profileChanged = _applicationProfileSwitcher.Update(Profile, executablePath);
                        _lastObservedForegroundExecutablePath = executablePath;
                        _lastObservedApplicationBindings = applicationBindings;
                        _lastObservedActiveProfileName = Profile.ActiveProfileName;
                        if (profileChanged)
                        {
                            _profiles.RefreshProfileState();
                            try
                            {
                                await _profileStore.SaveAsync(Profile, cancellationToken);
                                _profiles.ProfileStatusText = AppStrings.FormatText("ProfileAutoSwitched", _profiles.ActiveProfileName);
                            }
                            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                            {
                                throw;
                            }
                            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                            {
                                _setProfile(previousProfile);
                                _applicationProfileSwitcher = previousProfileSwitcher;
                                _lastObservedApplicationBindings = null;
                                _profiles.RefreshProfileState();
                                _setDeviceOperationError(AppStrings.FormatText("AutomaticProfileSaveError",
                                    exception.Message));
                                profileChanged = false;
                            }
                        }
                    }

                    // Discovery is event-driven: startup, device notifications, resume,
                    // and explicit refresh requests set this flag. A request arriving
                    // during a scan remains queued for the next loop iteration.
                    var refreshRequested = Interlocked.Exchange(ref _deviceRefreshRequested, 0) != 0;
                    if (!IsDisplayAvailable)
                    {
                        if (refreshRequested || profileChanged)
                        {
                            Interlocked.Exchange(ref _deviceRefreshRequested, 1);
                        }
                        continue;
                    }

                    var powerChanged = _lastPowerState != powerState;
                    var displayProfileRequested =
                        Interlocked.Exchange(ref _displayProfileApplyRequested, 0) != 0;
                    if (refreshRequested || powerChanged || profileChanged || displayProfileRequested)
                    {
                        var snapshot = refreshRequested
                            ? await _discovery.DiscoverAsync(cancellationToken)
                            : new DeviceSnapshot(ActiveDescriptors, DateTimeOffset.UtcNow);
                        await RefreshDevicesCoreAsync(
                            snapshot,
                            cancellationToken,
                            applyDisplayProfile: powerChanged || profileChanged || displayProfileRequested);
                    }
                    else
                    {
                        // Nothing asked for a rescan, but the readouts on the device page are
                        // live numbers such as fan speed, so they still need a periodic read.
                        // This path only reads; it never writes a setting back to the device.
                        await RefreshDeviceReadoutsAsync(cancellationToken);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (IsExpectedRuntimeException(exception))
                {
                    _setDeviceQueryError(AppStrings.FormatText("DeviceWatchError", exception.Message));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
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
            if (_openRazer.SelectedDevice is { } openRazerDevice)
            {
                await openRazerDevice.RefreshBatteryStateAsync(cancellationToken);
            }
            if (ActiveDescriptors.Count == 0)
            {
                return;
            }
            var telemetry = await _deviceTelemetryReader.ReadAsync(ActiveDescriptors, cancellationToken);
            _applyDeviceTelemetry(telemetry);
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
        _setDeviceQueryError(string.Empty);
        _setDeviceOperationError(string.Empty);
        var powerState = _currentPowerSource();
        var powerChanged = _lastPowerState != powerState;
        applyDisplayProfile |= powerChanged;
        if (powerChanged)
        {
            await _telemetry.RefreshBatteryHealthAsync(cancellationToken);
        }
        try
        {
            var snapshot = knownSnapshot ?? await _discovery.DiscoverAsync(cancellationToken);
            await _openRazer.RefreshAsync(cancellationToken);
            await _refreshAccessoriesAsync(cancellationToken);
            var nextFingerprint = CreateDeviceFingerprint(snapshot);
            if (!StringComparer.Ordinal.Equals(_deviceFingerprint, nextFingerprint))
            {
                _resetDeviceTelemetry();
                _clearSerials();
            }
            _deviceFingerprint = nextFingerprint;
            _setDeviceDescriptors(snapshot.Devices);

            var blade = snapshot.Devices.FirstOrDefault(device =>
                DeviceProtocolFamilies.IsBlade(device.ProtocolFamily));
            var viper = snapshot.Devices.FirstOrDefault(device => device.ProtocolFamily == DeviceProtocolFamilies.Viper);
            await _blade.RefreshCoolingPadPresenceAsync(cancellationToken);
            _blade.UpdateDevice(blade, FormatDeviceStatus(blade));
            _blade.SetSerial(await _readSerialAsync(blade, cancellationToken));
            _viper.SetDevice(
                viper,
                await _readSerialAsync(viper, cancellationToken),
                FormatDeviceStatus(viper));

            var telemetry = await _deviceTelemetryReader.ReadAsync(snapshot.Devices, cancellationToken);
            _applyDeviceTelemetry(telemetry);
            var observedPowerState = PowerSourceResolver.Resolve(
                _powerSourceProvider.IsPluggedIn,
                telemetry.BladeAdapterPower);
            var observedPowerChanged = _lastPowerState != observedPowerState;
            if (observedPowerChanged)
            {
                powerState = observedPowerState;
                powerChanged = true;
                applyDisplayProfile = true;
                await _telemetry.RefreshBatteryHealthAsync(cancellationToken);
            }
            _lastPowerState = powerState;
            _notifyPropertyChanged(nameof(MainViewModel.ActivePowerSourceText));
            _display.RefreshInternalDisplay(powerState, applyDisplayProfile);
            if (applyDisplayProfile)
            {
                await _openRazer.RestoreLightingAfterExternalAsync(cancellationToken);
                _openRazer.SelectedDevice?.ApplyConfiguredLightingToEditor();
            }
            var profileApply = await ApplyLoadedProfileAsync(
                telemetry,
                powerState,
                cancellationToken);
            if (profileApply is { AppliedCount: > 0 })
            {
                telemetry = await _deviceTelemetryReader.ReadAsync(snapshot.Devices, cancellationToken);
                _applyDeviceTelemetry(telemetry);
            }
            _blade.RefreshLightingEditor();
            _blade.RefreshPerformanceEditor();
            _blade.RefreshLogoEditor();
            var viperAvailable = viper is not null &&
                (telemetry.CapabilitySummaries?.GetValueOrDefault(viper.Id)
                    ?? DeviceCapabilitySummaryCalculator.Calculate(viper, telemetry)).Available > 0;
            var bladeProfileBlocked = profileApply?.Errors.Any(error =>
                error.StartsWith("Blade", StringComparison.OrdinalIgnoreCase)) == true;
            var fanApply = bladeProfileBlocked
                ? new BladeFanProfileApplyResult(
                    await _fan.StopAsync("profile-error"),
                    Changed: true)
                : await _fan.ApplyProfileAsync(blade, powerState, cancellationToken, writeStoredTarget: false);
            var viperMappingError = await _viper.ApplyProfileMappingAsync(
                viperAvailable ? viper : null, powerState, cancellationToken);
            if (fanApply.Changed && blade is { Access: DeviceAccessState.Available })
            {
                telemetry = await _deviceTelemetryReader.ReadAsync(snapshot.Devices, cancellationToken);
                _applyDeviceTelemetry(telemetry);
            }
            var lightingError = profileApply?.Errors.Any(error =>
                    error.StartsWith("Blade", StringComparison.OrdinalIgnoreCase)) == true
                ? null
                : await _lighting.ApplyLoadedProfileAsync(blade, powerState, cancellationToken);
            var profileOperationErrors = profileApply?.Errors
                .Where(error => viperAvailable ||
                    !error.StartsWith("Viper", StringComparison.OrdinalIgnoreCase))
                .ToArray() ?? [];
            var profileOperationError = profileOperationErrors.Length == 0
                ? string.Empty
                : AppStrings.FormatText("ProfileApplyError",
                    string.Join("; ", profileOperationErrors));
            _setDeviceOperationError(string.Join(
                Environment.NewLine,
                new[]
                {
                    profileOperationError,
                    viperAvailable ? viperMappingError : null,
                }.Where(error => !string.IsNullOrWhiteSpace(error))));
            var visibleDevices = viperAvailable
                ? snapshot.Devices
                : snapshot.Devices.Where(device => device.ProtocolFamily != DeviceProtocolFamilies.Viper).ToArray();
            _setDeviceDescriptors(visibleDevices);
            _viper.SetVisibility(viperAvailable);
            if (!viperAvailable)
            {
                _viper.ResetTelemetry();
            }
            _refreshDeviceSelector();
            _devices.Clear();
            foreach (var device in visibleDevices
                .OrderBy(device => DeviceUiCatalog.DeviceCategoryOrder(device.Category))
                .ThenBy(device => device.Name, StringComparer.OrdinalIgnoreCase))
            {
                _devices.Add(new DeviceRowViewModel(device, telemetry));
            }
            _refreshDeviceOverview();

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

            var openRazerErrorCount = _openRazer.ErrorCount;

            if (IsDisplayAvailable &&
                Interlocked.Exchange(ref _displayBrightnessRestorePending, 0) != 0)
            {
                _blade.RefreshLightingEditor();
            }

            if (Volatile.Read(ref _diagnosticsRebuildEnabled) != 0)
            {
                _rebuildDiagnostics(snapshot with { Devices = visibleDevices }, telemetry, errors);
            }
            var totalErrorCount = errors.Count + openRazerErrorCount;
            _setDeviceQueryError(totalErrorCount == 0
                ? string.Empty
                : AppStrings.FormatText("HardwareQueryFailureCount",
                    totalErrorCount));
            LastRefreshText = AppStrings.FormatText("DeviceScanTime",
                snapshot.CapturedAt.ToLocalTime());
            _notifyPropertyChanged(nameof(MainViewModel.EmptyStateText));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedRuntimeException(exception))
        {
            var hadBlade = ActiveDescriptors.Any(device => DeviceProtocolFamilies.IsBlade(device.ProtocolFamily));
            var hadViper = ActiveDescriptors.Any(device => device.ProtocolFamily == DeviceProtocolFamilies.Viper);
            _setDeviceDescriptors(Array.Empty<DeviceDescriptor>());
            _resetBlackShark();
            _blade.SetSerial(null);
            _viper.SetUnavailable(hadViper);
            _refreshDeviceSelector();
            if (_bladeLightingController is not null && _lighting.LightingDevicePath.Length > 0)
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
            _ = await _fan.StopAsync("refresh-failure");
            _blade.SetControlDevice(null);
            _lighting.ShadowFingerprint = string.Empty;
            _lighting.LightingDevicePath = string.Empty;
            _lastPowerState = powerState;
            _notifyPropertyChanged(nameof(MainViewModel.ActivePowerSourceText));
            _display.RefreshInternalDisplay(powerState, applyDisplayProfile);
            _blade.SetStatus(hadBlade ? AppStrings.Text("Text_A70924BE") : AppStrings.Text("Text_DB0974DC"));
            _setDeviceQueryError(exception.Message);
            LastRefreshText = AppStrings.Text("Text_F9E7EDEF");
            _notifyPropertyChanged(nameof(MainViewModel.EmptyStateText));
        }
        finally
        {
            _deviceOperationGate.Release();
            _firmware.NotifyDeviceStateChanged();
        }
    }

    private async Task<ProfileApplyResult?> ApplyLoadedProfileAsync(
        RazerDeviceTelemetry telemetry,
        PowerSourceKind powerState,
        CancellationToken cancellationToken)
    {
        if (ActiveDescriptors.Count == 0)
        {
            return null;
        }

        return await _profileApplier.ApplyAsync(
            Profile,
            ActiveDescriptors,
            telemetry,
            _deviceTelemetryReader,
            powerState,
            cancellationToken,
            applyKeyboardBrightness: IsDisplayAvailable,
            coolingPadConnected: _blade.CoolingPadConnected,
            padlessHyperBoostAccepted: _blade.PadlessHyperBoostAccepted);
    }

    private async Task<bool> TryEnterOperationAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _deviceOperationGate.WaitAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static string FormatDeviceStatus(DeviceDescriptor? device) => device switch
    {
        null => AppStrings.Text("Text_DB0974DC"),
        { Access: DeviceAccessState.Available, Capability: DeviceCapabilityState.PendingValidation } => AppStrings.Text("Text_95979F99"),
        _ => AppStrings.Text("Text_A14B88EA"),
    };

    private static string CreateDeviceFingerprint(DeviceSnapshot snapshot) =>
        string.Join(
            "|",
            snapshot.Devices
                .OrderBy(device => device.ProductId)
                .ThenBy(device => device.Id, StringComparer.OrdinalIgnoreCase)
                .Select(device => $"{device.ProductId:X4}:{device.Id}:{device.Access}:{device.Capability}")) +
        $"|{snapshot.ErrorMessage}";

    private static bool IsExpectedRuntimeException(Exception exception) =>
        exception is Win32Exception or IOException or UnauthorizedAccessException or
        InvalidOperationException or NotSupportedException;

    private ProfileDocument Profile => _getProfile();

    private IReadOnlyList<DeviceDescriptor> ActiveDescriptors => _deviceDescriptors();

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
