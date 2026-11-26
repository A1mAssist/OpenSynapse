using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using OpenSynapse.App.Runtime;
using OpenSynapse.Core.Audio;
using OpenSynapse.Core.Diagnostics;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Displays;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Input;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Lifecycle;
using OpenSynapse.Windows.Protocols;
using OpenSynapse.Windows.Devices;
using static OpenSynapse.App.ViewModels.DeviceUiCatalog;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly IRazerDeviceTelemetryReader _deviceTelemetryReader;
    private readonly ProfileStore _profileStore;
    private readonly MachineSettingsStore _machineSettingsStore;
    private readonly IPowerSourceProvider _powerSourceProvider;
    private readonly LocalDiagnosticLog _diagnosticLog;
    private HashSet<int>? _internalDisplayRefreshRateCycleHertz;
    private IReadOnlyList<BladePerformanceMode>? _legacyPerformanceCycleModes;
    private IReadOnlyList<int>? _legacyRefreshRateCycleHertz;
    private readonly SemaphoreSlim _deviceOperationGate = new(1, 1);
    private ProfileDocument _profile = ProfileDocument.CreateDefault();
    private MachineSettings _machineSettings = new();
    private string _deviceTelemetryTimeText = AppStrings.Text("Text_CF8B06AA");
    private string _deviceErrorText = string.Empty;
    private string _deviceQueryErrorText = string.Empty;
    private string _deviceOperationErrorText = string.Empty;
    private string _performanceErrorText = string.Empty;
    private string _displayErrorText = string.Empty;
    private string _errorText = string.Empty;
    private bool _isBusy;
    private IReadOnlyList<DeviceDescriptor> _deviceDescriptors = Array.Empty<DeviceDescriptor>();
    private RazerDeviceTelemetry? _lastDeviceTelemetry;
    private int _disposed;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue? _dispatcher =
        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

    public MainViewModel(
        IDeviceDiscovery discovery,
        IRazerDeviceTelemetryReader deviceTelemetryReader,
        IPerformanceMonitor performanceMonitor,
        ProfileStore? profileStore = null,
        IPowerSourceProvider? powerSourceProvider = null,
        IActiveApplicationProvider? activeApplicationProvider = null,
        LocalDiagnosticLog? diagnosticLog = null,
        IInternalDisplayController? internalDisplayController = null,
        IBladeLightingController? bladeLightingController = null,
        WindowsStartupManager? startupManager = null,
        string? executablePath = null,
        IReadOnlyList<string>? startupDiagnostics = null,
        WindowsTouchpadController? touchpadController = null,
        RazerHidDeviceService? razerHidDeviceService = null,
        RazerHidSpecialLightingService? razerHidSpecialLightingService = null,
        RazerFirmwareUpdateClient? firmwareUpdateClient = null,
        IThxAudioController? thxAudioController = null,
        bool thxAudioSupported = false,
        IMacroInputSink? macroInputSink = null,
        MachineSettingsStore? machineSettingsStore = null,
        bool liteMode = false,
        Action<bool, bool>? persistStartup = null)
    {
        ProfileSettingsRegistration.EnsureRegistered();
        IsLiteMode = liteMode;
        _deviceTelemetryReader = deviceTelemetryReader;
        _profileStore = profileStore ?? new ProfileStore();
        _machineSettingsStore = machineSettingsStore ?? new MachineSettingsStore();
        _powerSourceProvider = powerSourceProvider ?? UnknownPowerSourceProvider.Instance;
        _diagnosticLog = diagnosticLog ?? new LocalDiagnosticLog();
        CoolingPad = new RazerCoolingPadViewModel(
            cancellationToken => RazerCoolingPadService.ConnectAsync(
                message => _diagnosticLog.TryWrite("cooling-pad", message), cancellationToken),
            performanceMonitor,
            () => _profile,
            SaveProfileAsync,
            () => _lastDeviceTelemetry?.BladePerformanceMode,
            () => (ushort)(_deviceDescriptors.FirstOrDefault(device =>
                DeviceProtocolFamilies.IsBlade(device.ProtocolFamily))?.ProductId ?? 0));
        Kiyo = new RazerKiyoViewModel(
            message => _diagnosticLog.TryWrite("kiyo", message),
            () => _profile,
            SaveProfileAsync);
        Telemetry = new TelemetryWorkspace(
            performanceMonitor,
            _powerSourceProvider,
            () => Refresh!.IsDisplayAvailable,
            () => CurrentPowerSource,
            error => SetPerformanceError(error));
        Firmware = new FirmwareWorkspace(
            firmwareUpdateClient ?? new RazerFirmwareUpdateClient(),
            () => _deviceDescriptors);
        Macros = new MacroWorkspace(() => GetActiveProfile(), SaveProfileAsync, _diagnosticLog, macroInputSink);
        Viper = new ViperWorkspace(
            deviceTelemetryReader,
            () => _profile,
            document => _profile = document,
            SaveProfileAsync,
            () => _deviceDescriptors,
            () => CurrentPowerSource,
            () => Devices.FirstOrDefault(device => device.ProtocolFamily == DeviceProtocolFamilies.Viper),
            RunDeviceOperationAsync,
            reading => Telemetry.NotifyBatteryObserved(reading),
            () => Macros.Items.Select(macro => macro.Definition).ToArray(),
            Macros.TryApplyMacroBindingAsync,
            Macros.TryClearMacroTriggerAsync);
        Blade = new BladeWorkspace(
            deviceTelemetryReader,
            bladeLightingController,
            touchpadController,
            _diagnosticLog,
            () => _profile,
            document => _profile = document,
            SaveProfileAsync,
            () => _machineSettings,
            SaveMachineSettingsAsync,
            RunDeviceOperationAsync,
            () => _deviceDescriptors,
            () => CurrentPowerSourceEvidence,
            () => _lastDeviceTelemetry,
            () => _internalDisplayRefreshRateCycleHertz,
            () => Refresh!.IsDisplayAvailable,
            () => Refresh!.IsBrightnessRestorePending,
            () => Volatile.Read(ref _disposed) != 0,
            SetDeviceOperationError,
            () => Refresh!.RequestDeviceRefresh(),
            rate => InternalDisplayRefreshRateChangedByUser?.Invoke(rate),
            Firmware.ApplyMcuVersion,
            () => Display!,
            () => Fan!,
            () => Lighting!,
            () => Devices.FirstOrDefault(device => DeviceProtocolFamilies.IsBlade(device.ProtocolFamily)),
            GetDeviceSerialAsync,
            () => CoolingPad);
        RazerDrivers = new RazerDriverWorkspace(_diagnosticLog);
        Display = new DisplayWorkspace(
            internalDisplayController,
            () => Blade.IsSelectedRefreshRatePowerActive,
            () => Blade.EditableRefreshRateProfile,
            SaveProfileAsync,
            RunDeviceOperation,
            SetDisplayError,
            () => _profile,
            () => _deviceDescriptors,
            _diagnosticLog,
            () => Volatile.Read(ref _disposed) != 0);
        Display.AttachBrightnessProfile(() => Blade.EditablePlatformProfile);
        Profiles = new ProfilesWorkspace(
            _profileStore,
            () => _profile,
            document => _profile = document,
            GetActiveProfile,
            SaveProfileAsync,
            Macros,
            ApplyProfileShortcuts,
            () => Blade.PerformanceModes,
            () => Display.InternalDisplayRefreshRates,
            () => _legacyPerformanceCycleModes,
            () => _legacyRefreshRateCycleHertz,
            startupManager,
            executablePath,
            _deviceOperationGate,
            busy => IsBusy = busy,
            SetDeviceOperationError,
            ReportApplicationError,
            () => Refresh!.RequestProfileApply(),
            persistStartup: persistStartup);
        Fan = new FanWorkspace(
            deviceTelemetryReader,
            performanceMonitor,
            () => _profile,
            document => _profile = document,
            () => Blade.EditablePerformanceProfile,
            SaveProfileAsync,
            Blade.RefreshPerformanceEditor,
            () => _deviceDescriptors,
            () => CurrentPowerSource,
            () => Blade.IsSelectedPerformancePowerActive,
            () => Blade.SelectedPerformancePowerState,
            Blade.State,
            () => Blade.PerformanceModeIndex,
            () => Blade.CanSetFan,
            mode => Blade.SetPerformanceMode(mode, confirm: false),
            Blade.DisableMaxFanForFanControlAsync,
            _diagnosticLog,
            RunDeviceOperation,
            () => Refresh!.RequestDeviceRefresh());
        Lighting = new LightingWorkspace(
            bladeLightingController,
            _deviceTelemetryReader,
            _diagnosticLog,
            () => _deviceDescriptors,
            () => _profile,
            () => CurrentPowerSource,
            () => Blade.LightingDialect,
            available => Refresh!.SetDisplayAvailableFlag(available),
            () => Refresh!.IsDisplayAvailable,
            _deviceOperationGate,
            () => Refresh!.RequestDeviceRefresh(),
            SetDeviceOperationError,
            () => Fan.StopAsync("suspend"),
            SetRazerHidChromaEnabled,
            Blade.State);
        RazerHid = new RazerHidWorkspace(
            razerHidDeviceService,
            razerHidSpecialLightingService,
            () => CurrentPowerSource,
            () => _profile,
            GetActiveProfile,
            SaveProfileAsync,
            _diagnosticLog,
            () => Lighting.IsChromaIntegrationEnabled,
            () => Macros.Items.Select(macro => macro.Definition).ToArray(),
            Macros.TryApplyMacroBindingAsync,
            Macros.TryClearMacroTriggerAsync,
            () => _machineSettings,
            SaveMachineSettingsAsync,
            ct => Refresh!.RefreshDevicesAsync(ct),
            RefreshDeviceOverviewVisibility,
            SetDeviceOperationError,
            DispatcherQueue.GetForCurrentThread());
        Macros.SoftwareBindingRestorer = RazerHid.RestoreSoftwareMacroBindingAsync;
        Macros.SoftwareTriggerBinder = RazerHid.BindSoftwareMacroTriggerAsync;
        Macros.TriggerChanged += (_, _) => RazerHid.InvalidateMappings();
        Macros.SetUnhandledRazerKeyHandler(
            input => _ = RazerHid.HandleUnhandledRazerKeyAsync(input));
        Macros.SetTurboResolver(RazerHid.ResolveTurboBinding);
        Macros.MacrosChanged += () =>
        {
            Macros.StopAllTurbo();
            _ = RazerHid.SyncPrivateKeyDriverModeAsync();
        };
        Macros.MacrosChanged += OnLaylaMacrosChanged;
        // The hook runs on its own thread; the sync must not hold up a mouse move.
        Macros.PointerActivity += () => _ = Task.Run(() => RazerHid.SyncPrivateKeyDriverModeAsync());
        Accessories = new UsbAccessoriesWorkspace(
            _diagnosticLog, RazerHid, Macros, () => _profile, SaveProfileAsync,
            () =>
            {
                RefreshDeviceSelectorItems();
                RefreshDeviceOverviewVisibility();
            },
            performanceMonitor);
        Accessories.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Accessories));
        Diagnostics = new DiagnosticsWorkspace(
            RazerHid,
            startupDiagnostics?.ToArray() ?? Array.Empty<string>(),
            () => Refresh!.LastRefreshText,
            () => DeviceTelemetryTimeText,
            ReadAccessoryDiagnostics,
            () => Devices);
        Accessories.ForgetAccessoryDiagnostic = Diagnostics.ForgetAccessory;
        Refresh = new RefreshWorkspace(
            discovery,
            deviceTelemetryReader,
            _profileStore,
            _powerSourceProvider,
            activeApplicationProvider ?? UnknownActiveApplicationProvider.Instance,
            _diagnosticLog,
            bladeLightingController,
            _deviceOperationGate,
            () => _profile,
            document => _profile = document,
            () => _deviceDescriptors,
            descriptors => _deviceDescriptors = descriptors,
            () => CurrentPowerSource,
            GetDeviceSerialAsync,
            () => _deviceSerials.Clear(),
            RefreshAttachedDevicesAsync,
            Accessories.PollHeadsetPowerAsync,
            Blade,
            Display,
            Fan,
            Lighting,
            RazerHid,
            Viper,
            Telemetry,
            Profiles,
            Firmware,
            Devices,
            RefreshDeviceSelectorItems,
            RefreshDeviceOverviewVisibility,
            SetDeviceQueryError,
            SetDeviceOperationError,
            ResetDeviceTelemetry,
            ApplyDeviceTelemetry,
            Diagnostics.Rebuild,
            () => Diagnostics.AccessoryErrorCount,
            () =>
            {
                BlackSharkDevices.Clear();
                _selectedBlackSharkDevice = null;
                SelectedBlackShark = null;
            },
            OnPropertyChanged,
            ApplyAllBleProfilesAsync,
            (power, token) => AccessoryProfileReplay.ApplyAsync(this, _diagnosticLog, power, token),
            liteMode: liteMode);
        Refresh.StudioLightingActive = () => StudioLightingActive?.Invoke() == true || ChromaLightingActive?.Invoke() == true;
        Lighting.StudioLightingActive = () => StudioLightingActive?.Invoke() == true || ChromaLightingActive?.Invoke() == true;
        RazerHid.StudioLightingActive = () => StudioLightingActive?.Invoke() == true || ChromaLightingActive?.Invoke() == true;
        FullscreenPolling = new FullscreenPollingRateService(
            () => _profile, () => CurrentPowerSource, RazerHid, Accessories, Refresh, _diagnosticLog);
        InitializeDeviceHome(activeApplicationProvider);
        Tray = new TrayWorkspace(
            () => Devices, RazerHid, Accessories,
            () => BlackSharkDevices, () => BleDevices, () => AwBleHeadsetDevices,
            () => ApBleHeadsetDevices);
        CpuCurve = new CpuCurveWorkspace(
            (label, operation, token) => RunDeviceOperationAsync(label, operation, token),
            () => Blade.EditablePlatformProfile,
            SaveProfileAsync);
        CpuPower = new CpuPowerLimitWorkspace(
            () => IsBusy,
            () => Blade.IsCustomMode,
            () => Blade.PerformanceModeIndex >= 0,
            RunDeviceOperation,
            () => Blade.EditablePlatformProfile,
            SaveProfileAsync);
        Blade.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is null or nameof(BladeWorkspace.PerformanceModeIndex))
                CpuPower.NotifyModeChanged();
        };
        Blade.PerformanceModeChangedByUser += mode =>
        {
            if (mode == BladePerformanceMode.Custom)
            {
                // The EC re-applies its own wall on the mode write; restore the
                // watt preset the profile names once the switch is confirmed.
                _ = CpuPower.ReplayCpuPowerLimitAsync(CancellationToken.None);
            }
        };
        ThxAudio = new ThxAudioViewModel(thxAudioController, thxAudioSupported);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    internal event Action<int>? InternalDisplayRefreshRateChangedByUser;
    internal IReadOnlySet<int>? InternalDisplayRefreshRateCycleHertz =>
        _internalDisplayRefreshRateCycleHertz;

    public ObservableCollection<DeviceRowViewModel> Devices { get; } = new();
    public ObservableCollection<DeviceSelectorItemViewModel> DeviceSelectorItems { get; } = new();
    public bool IsLiteMode { get; }
    public RazerCoolingPadViewModel CoolingPad { get; }
    public RazerKiyoViewModel Kiyo { get; }
    public FirmwareWorkspace Firmware { get; }
    public ViperWorkspace Viper { get; }
    public RefreshWorkspace Refresh { get; }
    public BladeWorkspace Blade { get; }
    public CpuCurveWorkspace CpuCurve { get; }
    public CpuPowerLimitWorkspace CpuPower { get; }
    public UsbAccessoriesWorkspace Accessories { get; }
    public ThxAudioViewModel ThxAudio { get; }
    public MacroWorkspace Macros { get; }
    public RazerDriverWorkspace RazerDrivers { get; }
    public RazerHidWorkspace RazerHid { get; }
    internal FullscreenPollingRateService FullscreenPolling { get; }
    public TrayWorkspace Tray { get; }
    public ProfilesWorkspace Profiles { get; }
    public TelemetryWorkspace Telemetry { get; }
    public FanWorkspace Fan { get; }
    public DisplayWorkspace Display { get; }
    public LightingWorkspace Lighting { get; }
    public DiagnosticsWorkspace Diagnostics { get; }

    public string DeviceTelemetryTimeText { get => _deviceTelemetryTimeText; private set => SetField(ref _deviceTelemetryTimeText, value); }

    public string DeviceErrorText
    {
        get => _deviceErrorText;
        private set
        {
            if (SetField(ref _deviceErrorText, value))
            {
                OnPropertyChanged(nameof(HasDeviceError));
                UpdateErrorText();
            }
        }
    }

    public bool HasDeviceError => !string.IsNullOrWhiteSpace(DeviceErrorText);

    public string ErrorText
    {
        get => _errorText;
        private set
        {
            if (SetField(ref _errorText, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value)) CpuPower?.NotifyBusyChanged();
        }
    }

    public bool CanRefresh => true;

    internal IReadOnlyList<DeviceDescriptor> CurrentDeviceDescriptors => _deviceDescriptors;

    internal Func<Task>? SuspendStudioLighting { get; set; }

    internal Task SuspendStudioLightingAsync() =>
        RunLightingOnDispatcherAsync(() => SuspendStudioLighting?.Invoke() ?? Task.CompletedTask);

    internal Func<Task>? SuspendStudioForChroma { get; set; }
    internal Func<Task>? ResumeStudioAfterChroma { get; set; }
    internal Action? CancelStudioResumeAfterChroma { get; set; }
    internal Func<bool>? ChromaLightingActive { get; set; }

    internal Task SuspendLightingForChromaAsync() => RunLightingOnDispatcherAsync(async () =>
    {
        if (SuspendStudioForChroma is { } suspend) await suspend();
        await RazerHid.SuspendLightingForChromaAsync();
    });

    internal Task ResumeStudioAfterChromaAsync() => RunLightingOnDispatcherAsync(() =>
        ResumeStudioAfterChroma?.Invoke() ?? Task.CompletedTask);

    internal Task CancelStudioResumeAfterChromaAsync() => RunLightingOnDispatcherAsync(() =>
    {
        CancelStudioResumeAfterChroma?.Invoke();
        return Task.CompletedTask;
    });

    internal Task RunLightingOnDispatcherAsync(Func<Task> action)
    {
        if (_dispatcher is null || _dispatcher.HasThreadAccess) return action();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcher.TryEnqueue(async () =>
            {
                try { await action(); completion.TrySetResult(); }
                catch (Exception error) { completion.TrySetException(error); }
            }))
            completion.TrySetException(new InvalidOperationException("The lighting dispatcher is unavailable."));
        return completion.Task;
    }

    /// <summary>True while the Lighting Studio effect is the active lighting surface.</summary>
    internal Func<bool>? StudioLightingActive { get; set; }

    // Shared hand-back path for Chroma hosts and Lighting Studio.
    internal Task RestoreLightingAfterExternalAsync() => RunLightingOnDispatcherAsync(async () =>
    {
        await Lighting.RestoreBladeLightingAfterExternalAsync();
        await RazerHid.RestoreLightingAfterExternalAsync();
        await Accessories.RestoreLightingAfterExternalAsync(
            CurrentPowerSource, CancellationToken.None);
    });

    /// <summary>Called after Studio has stopped writing the named owned instances.</summary>
    internal async Task RestoreStudioLightingAsync(IReadOnlyCollection<string> instanceIds)
    {
        ArgumentNullException.ThrowIfNull(instanceIds);
        var failures = new List<Exception>();
        foreach (var instanceId in instanceIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
                if (CurrentDeviceDescriptors.Any(device =>
                        DeviceProtocolFamilies.IsBlade(device.ProtocolFamily) &&
                        StringComparer.OrdinalIgnoreCase.Equals(device.Id, instanceId)))
                {
                    await Lighting.RestoreBladeLightingAfterExternalAsync(instanceId).ConfigureAwait(true);
                }
                else if (RazerHid.CurrentConnections.Any(connection =>
                             StringComparer.OrdinalIgnoreCase.Equals(connection.InstanceId, instanceId)))
                {
                    await RazerHid.RestoreLightingAfterExternalAsync(
                        instanceIds: [instanceId]).ConfigureAwait(true);
                }
                else
                {
                    await Accessories.RestoreLightingAfterExternalAsync(
                        CurrentPowerSource, CancellationToken.None, [instanceId]).ConfigureAwait(true);
                }
            }
            catch (Exception exception)
            {
                failures.Add(new InvalidOperationException(
                    $"Lighting restore failed for {instanceId}: {exception.Message}", exception));
            }
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                string.Join(Environment.NewLine, failures.Select(failure => failure.Message)),
                new AggregateException(failures));
        }
    }

    public string EmptyStateText => ConnectedDevices.Count == 0
        ? AppStrings.Text("Text_76BEF6E0")
        : string.Empty;

    public Visibility EmptyStateVisibility => ConnectedDevices.Count == 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string ActivePowerSourceText => AppStrings.FormatText(
        "ActivePowerSource",
        FormatPowerSource(CurrentPowerSource));

    private static string FormatPowerSource(PowerSourceKind source) => source switch
    {
        PowerSourceKind.FullPowerAdapter => AppStrings.Text("BladeLightingPowerPluggedIn"),
        PowerSourceKind.LowPowerAdapter => AppStrings.Text("BladeLightingPowerLowAdapter"),
        PowerSourceKind.Battery => AppStrings.Text("BladeLightingPowerBattery"),
        _ => "--",
    };

    /// <summary>Chroma ownership also gates the selected RazerHid device's software lighting.</summary>
    private void SetRazerHidChromaEnabled(bool enabled) =>
        RazerHid.SelectedDevice?.SetChromaIntegrationEnabled(enabled);

    public void RefreshLocalization()
    {
        RefreshLocalizationCore();
    }

    private PowerSourceKind CurrentPowerSource =>
        PowerSourceResolver.Resolve(
            _powerSourceProvider.IsPluggedIn,
            _lastDeviceTelemetry?.BladeAdapterPower);

    private PowerSourceResolver.PowerSourceEvidence CurrentPowerSourceEvidence =>
        PowerSourceResolver.ResolveEvidence(
            _powerSourceProvider.IsPluggedIn,
            _lastDeviceTelemetry?.BladeAdapterPower);

    internal async Task InitializeProfileAsync(CancellationToken cancellationToken = default)
    {
        await Profiles.LoadAsync(cancellationToken);
        await InitializeDeviceHomeAsync(cancellationToken);
        _machineSettings = await _machineSettingsStore.LoadAsync(cancellationToken);
        Macros.InitializeMacroRuntime();
        Firmware.RefreshVersions();
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
        _internalDisplayRefreshRateCycleHertz = refreshRates?.ToHashSet();
        Blade.ApplyProfileShortcuts(cycleModes, _profile.Global.Blade);
        Macros.RefreshMacroState();
        OnPropertyChanged(nameof(InternalDisplayRefreshRateCycleHertz));
    }

    // False means a reported persistence failure; cancellation and unexpected errors propagate.
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

    private async Task<bool> SaveMachineSettingsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _machineSettingsStore.SaveAsync(_machineSettings, cancellationToken);
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

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

}
