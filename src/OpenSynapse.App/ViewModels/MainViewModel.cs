using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
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
        RazerHidDeviceService? openRazerDeviceService = null,
        RazerHidSpecialLightingService? openRazerSpecialLightingService = null,
        RazerFirmwareUpdateClient? firmwareUpdateClient = null,
        IThxAudioController? thxAudioController = null,
        bool thxAudioSupported = false,
        IMacroInputSink? macroInputSink = null,
        MachineSettingsStore? machineSettingsStore = null)
    {
        ProfileSettingsRegistration.EnsureRegistered();
        _deviceTelemetryReader = deviceTelemetryReader;
        _profileStore = profileStore ?? new ProfileStore();
        _machineSettingsStore = machineSettingsStore ?? new MachineSettingsStore();
        _powerSourceProvider = powerSourceProvider ?? UnknownPowerSourceProvider.Instance;
        _diagnosticLog = diagnosticLog ?? new LocalDiagnosticLog();
        CoolingPad = new RazerCoolingPadViewModel(
            message => _diagnosticLog.TryWrite("cooling-pad", message),
            performanceMonitor,
            () => _profile,
            SaveProfileAsync);
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
        Viper = new ViperWorkspace(
            deviceTelemetryReader,
            () => _profile,
            document => _profile = document,
            SaveProfileAsync,
            () => _deviceDescriptors,
            () => CurrentPowerSource,
            () => Devices.FirstOrDefault(device => device.ProtocolFamily == DeviceProtocolFamilies.Viper),
            RunDeviceOperationAsync,
            reading => Telemetry.NotifyBatteryObserved(reading));
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
            () => CurrentPowerSource,
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
            GetDeviceSerialAsync);
        Macros = new MacroWorkspace(() => GetActiveProfile(), SaveProfileAsync, _diagnosticLog, macroInputSink);
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
            () => Refresh!.RequestProfileApply());
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
            openRazerDeviceService,
            openRazerSpecialLightingService,
            () => CurrentPowerSource,
            () => _profile,
            GetActiveProfile,
            SaveProfileAsync,
            _diagnosticLog,
            () => Lighting.IsChromaIntegrationEnabled,
            () => Macros.Items.Select(macro => macro.Definition).ToArray(),
            Macros.TryBindMacroTriggerAsync,
            () => _machineSettings,
            SaveMachineSettingsAsync,
            ct => Refresh!.RefreshDevicesAsync(ct),
            RefreshDeviceOverviewVisibility,
            SetDeviceOperationError,
            DispatcherQueue.GetForCurrentThread());
        Accessories = new UsbAccessoriesWorkspace(
            _diagnosticLog, RazerHid, Macros, () => _profile, SaveProfileAsync);
        Accessories.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Accessories));
        Diagnostics = new DiagnosticsWorkspace(
            RazerHid,
            startupDiagnostics?.ToArray() ?? Array.Empty<string>(),
            () => Refresh!.LastRefreshText,
            () => DeviceTelemetryTimeText,
            ReadAccessoryDiagnostics,
            () => Devices);
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
            (power, token) => SelectedBle?.ApplyPersistedProfileAsync(power, token) ?? Task.CompletedTask,
            (power, token) => AccessoryProfileReplay.ApplyAsync(this, _diagnosticLog, power, token));
        Tray = new TrayWorkspace(() => Devices, RazerHid);
        CpuCurve = new CpuCurveWorkspace(
            (label, operation, token) => RunDeviceOperationAsync(label, operation, token),
            () => Blade.EditablePlatformProfile,
            SaveProfileAsync);
        CpuPower = new CpuPowerLimitWorkspace(
            () => IsBusy,
            RunDeviceOperation,
            () => Blade.EditablePlatformProfile,
            SaveProfileAsync);
        ThxAudio = new ThxAudioViewModel(thxAudioController, thxAudioSupported);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    internal event Action<int>? InternalDisplayRefreshRateChangedByUser;
    internal IReadOnlySet<int>? InternalDisplayRefreshRateCycleHertz =>
        _internalDisplayRefreshRateCycleHertz;

    public ObservableCollection<DeviceRowViewModel> Devices { get; } = new();
    public ObservableCollection<DeviceSelectorItemViewModel> DeviceSelectorItems { get; } = new();
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

    // Shared hand-back path for Chroma hosts and Lighting Studio.
    internal async Task RestoreLightingAfterExternalAsync()
    {
        await Lighting.RestoreBladeLightingAfterExternalAsync().ConfigureAwait(false);
        await RazerHid.RestoreLightingAfterExternalAsync().ConfigureAwait(false);
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
        Profiles.ProfileStatusText = AppStrings.FormatText("ProfileLoaded",
            Profiles.ActiveProfileName);

        if (_lastDeviceTelemetry is { } telemetry)
        {
            ApplyDeviceTelemetry(telemetry);
        }
        Blade.RefreshLocalization();

        foreach (var row in Devices)
        {
            row.RefreshLocalization();
        }
        RazerHid.RefreshLocalization();
        foreach (var row in BlackSharkDevices)
        {
            row.RefreshLocalization();
        }
        SelectedBlackShark?.RefreshLocalization();
        Accessories.SelectedGamepad?.RefreshLocalization();
        CoolingPad.RefreshLocalization();
        Kiyo.RefreshLocalization();
        foreach (var row in Diagnostics.Rows)
        {
            row.RefreshLocalization();
        }
        Viper.RefreshLocalization();

        Telemetry.RefreshLocalization();

        OnPropertyChanged(string.Empty);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await Profiles.LoadAsync(cancellationToken);
        await Telemetry.RefreshBatteryHealthAsync(cancellationToken);
        await Refresh.RefreshDevicesAsync(cancellationToken);
        Firmware.RefreshVersions();
        await Blade.InitializeAsync(cancellationToken);
        if (Telemetry.IsSamplingEnabled)
        {
            await Telemetry.RefreshPerformanceAsync(cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await RazerHid.DisposeAsync().ConfigureAwait(false);
        Macros.Dispose();
        _hue?.Dispose();
        CoolingPad.Dispose();
        Kiyo.Dispose();

        SelectedBlackShark = null;

        if (SelectedBle is { } ble)
        {
            await ble.DisposeAsync().ConfigureAwait(false);
            SelectedBle = null;
        }

        if (SelectedAwBleHeadset is { } awBleHeadset)
        {
            await awBleHeadset.DisposeAsync().ConfigureAwait(false);
            SelectedAwBleHeadset = null;
        }
        _selectedBlackSharkDevice = null;
        BlackSharkDevices.Clear();

        await Accessories.DisposeAsync().ConfigureAwait(false);

        await Blade.DisposeAsync().ConfigureAwait(false);
        _ = await Fan.StopAsync("application-exit").ConfigureAwait(false);
        await Fan.DisposeAsync().ConfigureAwait(false);
    }

    private PowerSourceKind CurrentPowerSource =>
        PowerSourceResolver.Resolve(
            _powerSourceProvider.IsPluggedIn,
            _lastDeviceTelemetry?.BladeAdapterPower);

    internal async Task InitializeProfileAsync(CancellationToken cancellationToken = default)
    {
        await Profiles.LoadAsync(cancellationToken);
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

    private static string FormatOperationException(Exception exception)
    {
        var exceptions = exception is AggregateException aggregate
            ? aggregate.Flatten().InnerExceptions
            : [exception];
        return string.Join(
            "; ",
            exceptions
                .Select(error => error.Message)
                .Where(message => !string.IsNullOrWhiteSpace(message)));
    }

    private static string FormatDeviceStatus(DeviceDescriptor? device) => device switch
    {
        null => AppStrings.Text("Text_DB0974DC"),
        { Access: DeviceAccessState.Available, Capability: DeviceCapabilityState.PendingValidation } => AppStrings.Text("Text_95979F99"),
        _ => AppStrings.Text("Text_A14B88EA"),
    };

    private static string FormatDuration(int seconds) => seconds switch
    {
        < 60 => AppStrings.FormatText("DurationSeconds", seconds),
        _ when seconds % 60 == 0 => AppStrings.FormatText("DurationMinutes", seconds / 60),
        _ => AppStrings.FormatText("DurationMinutesSeconds", seconds / 60, seconds % 60),
    };

    private static string FormatOptionalState(bool? value) => value switch
    {
        true => AppStrings.Text("Text_F55AD712"),
        false => AppStrings.Text("Text_7E3B0F3C"),
        null => "--",
    };

    private static string FormatState(bool value) => AppStrings.Text(value ? "Text_2AA0915E" : "Text_2351D059");

    private void UpdateErrorText()
    {
        ErrorText = string.Join(
            Environment.NewLine,
            new[] { DeviceErrorText, _displayErrorText, _performanceErrorText }.Where(error => !string.IsNullOrWhiteSpace(error)));
    }

    private void SetDisplayError(string error)
    {
        LogChangedError("display", _displayErrorText, error);
        _displayErrorText = error;
        UpdateErrorText();
    }

    private void SetDeviceQueryError(string error)
    {
        LogChangedError("device-query", _deviceQueryErrorText, error);
        _deviceQueryErrorText = error;
        UpdateDeviceErrorText();
    }

    private void SetDeviceOperationError(string error)
    {
        LogChangedError("device-operation", _deviceOperationErrorText, error);
        _deviceOperationErrorText = error;
        UpdateDeviceErrorText();
    }

    private void UpdateDeviceErrorText()
    {
        DeviceErrorText = string.Join(
            Environment.NewLine,
            new[] { _deviceQueryErrorText, _deviceOperationErrorText }.Where(error => !string.IsNullOrWhiteSpace(error)));
    }

    public void ReportApplicationError(string message)
    {
        LogChangedError("application", _performanceErrorText, message);
        _performanceErrorText = message;
        UpdateErrorText();
    }

    private void SetPerformanceError(string error)
    {
        LogChangedError("performance", _performanceErrorText, error);
        _performanceErrorText = error;
        UpdateErrorText();
    }

    private void LogChangedError(string source, string previous, string current)
    {
        if (!string.IsNullOrWhiteSpace(current) && !StringComparer.Ordinal.Equals(previous, current))
        {
            _diagnosticLog.TryWrite(source, current);
        }
    }

    private static bool IsExpectedRuntimeException(Exception exception) =>
        exception is Win32Exception or IOException or UnauthorizedAccessException or
        InvalidOperationException or NotSupportedException or COMException;

    private static int FindDiagnosticSeparator(string value)
    {
        var ascii = value.IndexOf(':');
        var fullWidth = value.IndexOf('\uFF1A');
        return ascii < 0 ? fullWidth : fullWidth < 0 ? ascii : Math.Min(ascii, fullWidth);
    }

    private static bool IsExpectedFanException(Exception exception) =>
        IsExpectedRuntimeException(exception) ||
        exception is ArgumentException or AggregateException or ObjectDisposedException;

    private static string FormatRawByte(byte? value) => value is byte raw ? $"0x{raw:X2} ({raw})" : AppStrings.Text("Text_D0003708");

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
