using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.Core.Diagnostics;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Displays;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Lifecycle;
using OpenSynapse.Windows.Protocols;
using OpenSynapse.Windows.Devices;
using Windows.UI;
using static OpenSynapse.App.ViewModels.DeviceUiCatalog;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private static readonly TimeSpan BladeBrightnessVerificationDelay =
        TimeSpan.FromMilliseconds(150);

    private readonly IDeviceDiscovery _discovery;
    private readonly IRazerDeviceTelemetryReader _deviceTelemetryReader;
    private readonly IPerformanceMonitor _performanceMonitor;
    private readonly ProfileStore _profileStore;
    private readonly IPowerSourceProvider _powerSourceProvider;
    private readonly IActiveApplicationProvider _activeApplicationProvider;
    private readonly LocalDiagnosticLog _diagnosticLog;
    private readonly SystemTelemetryViewModel _systemTelemetry = new();
    private readonly BladeViewModel _blade = new();
    private readonly ViperViewModel _viper = new();
    private HashSet<BladePerformanceMode> _bladePerformanceCycleModes =
        [.. BladePerformanceModes];
    private HashSet<int>? _internalDisplayRefreshRateCycleHertz;
    private IReadOnlyList<BladePerformanceMode>? _legacyPerformanceCycleModes;
    private IReadOnlyList<int>? _legacyRefreshRateCycleHertz;
    private string _activeBladeMappingPreset = BladeProfileSettings.Product710DefaultMappingPreset;
    private bool _activeSnapTapEnabled;
    private readonly IInternalDisplayController? _internalDisplayController;
    private readonly IBladeLightingController? _bladeLightingController;
    private readonly WindowsStartupManager? _startupManager;
    private readonly WindowsTouchpadController? _touchpadController;
    private readonly OpenRazerDeviceService? _openRazerDeviceService;
    private readonly OpenRazerSpecialLightingService? _openRazerSpecialLightingService;
    private CancellationTokenSource? _openRazerSelectionCancellation;
    private OpenRazerDeviceViewModel? _selectedOpenRazerDevice;
    private OpenRazerKrakenViewModel? _selectedOpenRazerKraken;
    private readonly string? _executablePath;
    private readonly IReadOnlyList<string> _startupDiagnostics;
    private readonly VerifiedProfileApplier _profileApplier = new();
    private readonly BladeFanCurveRuntime _bladeFanRuntime;
    private readonly SemaphoreSlim _deviceOperationGate = new(1, 1);
    private readonly SemaphoreSlim _deviceWatchSignal = new(0, 1);
    private readonly SemaphoreSlim _performanceSamplingSignal = new(0, 1);
    private readonly object _bladeBrightnessGate = new();
    private Task _bladeBrightnessWriter = Task.CompletedTask;
    private byte? _desiredBladeBrightness;
    private bool _bladeBrightnessWriterActive;
    private Task _bladeBrightnessVerification = Task.CompletedTask;
    private long _bladeBrightnessVerificationGeneration;
    private ApplicationProfileSwitcher _applicationProfileSwitcher = new();
    private ProfileDocument _profile = ProfileDocument.CreateDefault();
    private bool? _lastPowerState;
    private string _lastDeviceRefreshText = AppStrings.Text("Text_E19E170A");
    private string _deviceTelemetryTimeText = AppStrings.Text("Text_CF8B06AA");
    private string _deviceErrorText = string.Empty;
    private string _deviceQueryErrorText = string.Empty;
    private string _deviceOperationErrorText = string.Empty;
    private string _performanceErrorText = string.Empty;
    private string _displayErrorText = string.Empty;
    private string _profileStatusText = AppStrings.Text("Text_A9AE645B");
    private string _activeProfileName = ProfileCatalog.DefaultProfileName;
    private string _profileNameInput = string.Empty;
    private bool _isStartupEnabled;
    private bool _isSilentStartupEnabled;
    private string _errorText = string.Empty;
    private bool _isBusy;
    private IReadOnlyList<DeviceDescriptor> _deviceDescriptors = Array.Empty<DeviceDescriptor>();
    private RazerDeviceTelemetry? _lastDeviceTelemetry;
    private string _deviceFingerprint = string.Empty;
    private string _lightingShadowFingerprint = string.Empty;
    private string _bladeLightingDevicePath = string.Empty;
    private string _bladeFanControlFingerprint = string.Empty;
    private Task? _bladeFanControlCompletion;
    private string? _bladeControlDevicePath;
    private DateTimeOffset _nextFullDeviceRefresh = DateTimeOffset.MinValue;
    private int _bladeLightingPowerProfileIndex;
    private int _bladePerformancePowerProfileIndex;
    private int _bladeRefreshRatePowerProfileIndex;
    private int _deviceRefreshRequested;
    private int _displayProfileApplyRequested;
    private int _performanceSamplingEnabled = 1;
    private int _deviceWatchActive = 1;
    private int _displayAvailable = 1;
    private int _displayBrightnessRestorePending;
    private BladeLogoMode? _displaySuspendedLogoMode;
    private string _internalDisplayResolutionText = "--";
    private string _internalDisplayRefreshRateText = "--";
    private IReadOnlyList<int> _internalDisplayRefreshRates = Array.Empty<int>();
    private int _internalDisplayRefreshRateHertz;
    private int _confirmedInternalDisplayRefreshRateHertz;
    private bool _canSetInternalDisplayRefreshRate;
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
        OpenRazerDeviceService? openRazerDeviceService = null,
        OpenRazerSpecialLightingService? openRazerSpecialLightingService = null)
    {
        _discovery = discovery;
        _deviceTelemetryReader = deviceTelemetryReader;
        _performanceMonitor = performanceMonitor;
        _profileStore = profileStore ?? new ProfileStore();
        _powerSourceProvider = powerSourceProvider ?? UnknownPowerSourceProvider.Instance;
        _activeApplicationProvider = activeApplicationProvider ?? UnknownActiveApplicationProvider.Instance;
        _diagnosticLog = diagnosticLog ?? new LocalDiagnosticLog();
        _internalDisplayController = internalDisplayController;
        _bladeLightingController = bladeLightingController;
        _startupManager = startupManager;
        _touchpadController = touchpadController;
        _openRazerDeviceService = openRazerDeviceService;
        _openRazerSpecialLightingService = openRazerSpecialLightingService;
        _bladeFanRuntime = new BladeFanCurveRuntime(deviceTelemetryReader, performanceMonitor);
        _executablePath = executablePath;
        _startupDiagnostics = startupDiagnostics?.ToArray() ?? Array.Empty<string>();
        _systemTelemetry.PropertyChanged += (_, args) => OnPropertyChanged(args.PropertyName);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    internal event Action<string?>? BladeControlDevicePathChanged;
    internal event Action<BladePerformanceMode>? BladePerformanceModeChangedByUser;
    internal event Action<bool>? BladeGamingModeChangedByUser;
    internal event Action<bool>? BladeTouchpadChangedByUser;
    internal event Action<bool>? BladeOneTimeFullChargeChangedByUser;
    internal event Action<int>? InternalDisplayRefreshRateChangedByUser;
    internal event Action? BladeInputProfileChanged;

    internal IReadOnlySet<BladePerformanceMode> BladePerformanceCycleModes =>
        _bladePerformanceCycleModes;
    internal IReadOnlySet<int>? InternalDisplayRefreshRateCycleHertz =>
        _internalDisplayRefreshRateCycleHertz;
    internal bool ActiveSnapTapEnabled => _activeSnapTapEnabled;
    internal string ActiveBladeMappingPreset => _activeBladeMappingPreset;

    public ObservableCollection<DeviceRowViewModel> Devices { get; } = new();
    public ObservableCollection<OpenRazerDeviceRowViewModel> OpenRazerDevices { get; } = new();
    public ObservableCollection<OpenRazerKrakenDeviceRowViewModel> OpenRazerKrakenDevices { get; } = new();
    public OpenRazerDeviceViewModel? SelectedOpenRazerDevice
    {
        get => _selectedOpenRazerDevice;
        private set => SetField(ref _selectedOpenRazerDevice, value);
    }
    public OpenRazerKrakenViewModel? SelectedOpenRazerKraken
    {
        get => _selectedOpenRazerKraken;
        private set => SetField(ref _selectedOpenRazerKraken, value);
    }
    public ObservableCollection<DiagnosticRowViewModel> Diagnostics { get; } = new();
    public ObservableCollection<string> ProfileNames { get; } = new();
    public ObservableCollection<ApplicationBindingRowViewModel> ApplicationBindings { get; } = new();

    public string LastDeviceRefreshText
    {
        get => _lastDeviceRefreshText;
        private set => SetField(ref _lastDeviceRefreshText, value);
    }

    public string TelemetryTimeText => _systemTelemetry.TelemetryTimeText;

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

    public string ProfileStatusText { get => _profileStatusText; private set => SetField(ref _profileStatusText, value); }
    public string ActiveProfileName { get => _activeProfileName; private set => SetField(ref _activeProfileName, value); }
    public string ProfileNameInput { get => _profileNameInput; set => SetField(ref _profileNameInput, value); }
    public bool CanDeleteProfile => ProfileNames.Count > 1;
    public bool IsStartupEnabled
    {
        get => _isStartupEnabled;
        private set
        {
            if (SetField(ref _isStartupEnabled, value))
            {
                OnPropertyChanged(nameof(CanSetSilentStartup));
            }
        }
    }
    public bool IsSilentStartupEnabled { get => _isSilentStartupEnabled; private set => SetField(ref _isSilentStartupEnabled, value); }
    public bool CanSetStartup => _startupManager is not null && !string.IsNullOrWhiteSpace(_executablePath);
    public bool CanSetSilentStartup => CanSetStartup && IsStartupEnabled;

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    public bool CanRefresh => true;

    internal IReadOnlyList<DeviceDescriptor> CurrentDeviceDescriptors => _deviceDescriptors;

    public string EmptyStateText => Devices.Count == 0
        ? AppStrings.Text("Text_76BEF6E0")
        : string.Empty;

    internal void SetPerformanceSamplingEnabled(bool enabled)
    {
        if (Interlocked.Exchange(ref _performanceSamplingEnabled, enabled ? 1 : 0) !=
            (enabled ? 1 : 0))
        {
            SignalPerformanceSamplingStateChanged();
        }
    }

    private void RequestProfileApply()
    {
        Interlocked.Exchange(ref _displayProfileApplyRequested, 1);
        RequestDeviceRefresh();
    }

    public void RefreshLocalization()
    {
        ProfileStatusText = AppStrings.FormatText("ProfileLoaded",
            ActiveProfileName);

        if (_lastDeviceTelemetry is { } telemetry)
        {
            ApplyDeviceTelemetry(telemetry);
        }

        foreach (var row in Devices)
        {
            row.RefreshLocalization();
        }
        foreach (var row in OpenRazerDevices)
        {
            row.RefreshLocalization();
        }
        foreach (var row in OpenRazerKrakenDevices)
        {
            row.RefreshLocalization();
        }
        SelectedOpenRazerDevice?.RefreshLocalization();
        SelectedOpenRazerKraken?.RefreshLocalization();
        foreach (var row in Diagnostics)
        {
            row.RefreshLocalization();
        }
        foreach (var row in ViperButtonAssignments)
        {
            row.RefreshLocalization();
        }

        _systemTelemetry.RefreshLocalization();

        OnPropertyChanged(string.Empty);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await LoadProfileAsync(cancellationToken);
        await RefreshDevicesAsync(cancellationToken);
        if (_blade._bladeGameModeState is byte gameMode)
        {
            try
            {
                SetBladeGameMode(await _deviceTelemetryReader.SetBladeGameModeAsync(
                    _deviceDescriptors,
                    gameMode != 0,
                    cancellationToken));
            }
            catch (Exception exception) when (IsExpectedRuntimeException(exception))
            {
                _diagnosticLog.TryWrite(
                    "device-operation",
                    $"game mode indicator startup sync failed: {exception}");
                SetDeviceOperationError(AppStrings.FormatText("LabeledError",
                    AppStrings.Text("Text_BF98AC8A"),
                    exception.Message));
            }
        }
        if (Volatile.Read(ref _performanceSamplingEnabled) != 0)
        {
            await RefreshPerformanceAsync(cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _openRazerSelectionCancellation?.Cancel();
        _openRazerSelectionCancellation?.Dispose();

        Task brightnessWriter;
        lock (_bladeBrightnessGate)
        {
            _desiredBladeBrightness = null;
            brightnessWriter = _bladeBrightnessWriter;
        }
        await brightnessWriter.ConfigureAwait(false);
        Interlocked.Increment(ref _bladeBrightnessVerificationGeneration);
        await _bladeBrightnessVerification.ConfigureAwait(false);
        _ = await StopBladeFanControlAsync("application-exit").ConfigureAwait(false);
        try
        {
            await _bladeFanRuntime.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpectedFanException(exception))
        {
            _diagnosticLog.TryWrite("blade-fan", $"dispose restore failed: {exception}");
        }
    }

    private bool? SelectedLightingPowerState => _bladeLightingPowerProfileIndex switch
    {
        1 => true,
        2 => false,
        _ => _powerSourceProvider.IsPluggedIn,
    };

    private bool? SelectedPerformancePowerState => _bladePerformancePowerProfileIndex switch
    {
        1 => true,
        2 => false,
        _ => _powerSourceProvider.IsPluggedIn,
    };

    private bool IsSelectedLightingPowerActive =>
        _bladeLightingPowerProfileIndex == 0 ||
        SelectedLightingPowerState == _powerSourceProvider.IsPluggedIn;

    private bool IsSelectedPerformancePowerActive =>
        _bladePerformancePowerProfileIndex == 0 ||
        SelectedPerformancePowerState == _powerSourceProvider.IsPluggedIn;

    private bool IsSelectedRefreshRatePowerActive =>
        _bladeRefreshRatePowerProfileIndex == 0 ||
        SelectedRefreshRatePowerState == _powerSourceProvider.IsPluggedIn;

    private bool? SelectedRefreshRatePowerState => _bladeRefreshRatePowerProfileIndex switch
    {
        1 => true,
        2 => false,
        _ => _powerSourceProvider.IsPluggedIn,
    };

    private PowerProfileOverrides? SelectedLightingPowerOverrides => _bladeLightingPowerProfileIndex switch
    {
        1 => GetActiveProfile().PluggedIn,
        2 => GetActiveProfile().OnBattery,
        _ when _powerSourceProvider.IsPluggedIn == true => GetActiveProfile().PluggedIn,
        _ when _powerSourceProvider.IsPluggedIn == false => GetActiveProfile().OnBattery,
        _ => null,
    };

    private PowerProfileOverrides? CurrentPowerOverrides => _powerSourceProvider.IsPluggedIn switch
    {
        true => GetActiveProfile().PluggedIn,
        false => GetActiveProfile().OnBattery,
        _ => null,
    };

    private BladeProfileSettings EditableLightingBladeProfile =>
        SelectedLightingPowerOverrides?.Blade ?? GetActiveProfile().Global.Blade;

    private PowerProfileOverrides? SelectedPerformancePowerOverrides => _bladePerformancePowerProfileIndex switch
    {
        1 => GetActiveProfile().PluggedIn,
        2 => GetActiveProfile().OnBattery,
        _ when _powerSourceProvider.IsPluggedIn == true => GetActiveProfile().PluggedIn,
        _ when _powerSourceProvider.IsPluggedIn == false => GetActiveProfile().OnBattery,
        _ => null,
    };

    private PowerProfileOverrides? SelectedRefreshRatePowerOverrides => _bladeRefreshRatePowerProfileIndex switch
    {
        1 => GetActiveProfile().PluggedIn,
        2 => GetActiveProfile().OnBattery,
        _ when _powerSourceProvider.IsPluggedIn == true => GetActiveProfile().PluggedIn,
        _ when _powerSourceProvider.IsPluggedIn == false => GetActiveProfile().OnBattery,
        _ => null,
    };

    private BladeProfileSettings EditablePerformanceBladeProfile =>
        SelectedPerformancePowerOverrides?.Blade ?? GetActiveProfile().Global.Blade;

    private BladeProfileSettings EditableRefreshRateBladeProfile =>
        SelectedRefreshRatePowerOverrides?.Blade ?? GetActiveProfile().Global.Blade;

    private LightingProfile EditableLightingProfile =>
        SelectedLightingPowerOverrides?.Lighting ?? GetActiveProfile().Global.Lighting;

    private void RefreshBladeLightingEditor()
    {
        if (_profile.Profiles.Count == 0)
        {
            return;
        }

        var blade = _deviceDescriptors.FirstOrDefault(device =>
            device.ProtocolFamily == DeviceProtocolFamilies.Blade);
        var powerState = SelectedLightingPowerState;
        var lighting = blade is null
            ? EditableLightingProfile
            : ProfileResolver.Resolve(_profile, blade, powerState).Lighting;
        var effect = BladeLightingProfileCodec.Parse(lighting);
        var lightingIndex = Array.IndexOf(BladeLightingModes, effect.Mode);
        if (lightingIndex >= 0)
        {
            BladeLightingModeIndex = lightingIndex;
            BladeWaveDirectionIndex = Array.IndexOf(BladeWaveDirections, effect.Direction);
            BladeLightingColor = Color.FromArgb(0xFF, effect.Color.Red, effect.Color.Green, effect.Color.Blue);
            BladeLightingSecondColor = Color.FromArgb(
                0xFF, effect.SecondColor.Red, effect.SecondColor.Green, effect.SecondColor.Blue);
            BladeReactiveSpeedIndex = Array.IndexOf(BladeReactiveSpeeds, effect.ReactiveSpeed);
            BladeStarlightSpeedIndex = Array.IndexOf(BladeStarlightSpeeds, effect.StarlightSpeed);
            BladeStarlightColorModeIndex = (int)effect.StarlightColorMode;
        }

        var bladeProfile = blade is null
            ? EditableLightingBladeProfile
            : ProfileResolver.Resolve(_profile, blade, powerState).Blade;
        if (bladeProfile.KeyboardBrightness is byte brightness)
        {
            SetBladeBrightness(brightness, confirm: false);
        }
    }

    private void RefreshBladePerformanceEditor()
    {
        if (_profile.Profiles.Count == 0)
        {
            return;
        }

        var blade = _deviceDescriptors.FirstOrDefault(device =>
            device.ProtocolFamily == DeviceProtocolFamilies.Blade);
        var powerState = SelectedPerformancePowerState;
        var bladeProfile = blade is null
            ? EditablePerformanceBladeProfile
            : ProfileResolver.Resolve(_profile, blade, powerState).Blade;
        if (bladeProfile.PerformanceMode is byte rawPerformanceMode &&
            Enum.IsDefined(typeof(BladePerformanceMode), rawPerformanceMode))
        {
            SetBladePerformanceMode((BladePerformanceMode)rawPerformanceMode, confirm: false);
        }
    }

    internal Task InitializeProfileAsync(CancellationToken cancellationToken = default) =>
        LoadProfileAsync(cancellationToken);

    private void RefreshInternalDisplayRateEditor()
    {
        var configured = EditableRefreshRateBladeProfile.RefreshRateHertz;
        if (configured is int hertz && InternalDisplayRefreshRates.Contains(hertz))
        {
            InternalDisplayRefreshRateHertz = hertz;
        }
        else
        {
            OnPropertyChanged(nameof(InternalDisplayRefreshRateIndex));
        }
    }

    public async Task RunPerformanceLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                while (!IsPerformanceSamplingActive)
                {
                    await _performanceSamplingSignal.WaitAsync(cancellationToken);
                }

                await RefreshPerformanceAsync(cancellationToken);
                await _performanceSamplingSignal.WaitAsync(
                    TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private bool IsPerformanceSamplingActive =>
        Volatile.Read(ref _performanceSamplingEnabled) != 0 &&
        Volatile.Read(ref _displayAvailable) != 0;

    private void SignalPerformanceSamplingStateChanged()
    {
        try
        {
            _performanceSamplingSignal.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

    private async Task ObserveBladeLightingRuntimeAsync(Task completion)
    {
        try
        {
            await completion;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException or AggregateException or ObjectDisposedException)
        {
            if (_bladeLightingController?.RuntimeCompletion == completion)
            {
                _lightingShadowFingerprint = string.Empty;
                _bladeLightingDevicePath = string.Empty;
                SetDeviceOperationError(AppStrings.FormatText("LightingRuntimeError",
                    FormatOperationException(exception)));
            }
        }
    }

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

    private void SetBladeControlDevicePath(string? value)
    {
        if (StringComparer.OrdinalIgnoreCase.Equals(_bladeControlDevicePath, value))
        {
            return;
        }

        _bladeControlDevicePath = value;
        BladeControlDevicePathChanged?.Invoke(value);
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

    private void SetPerformanceUnavailable(string error)
    {
        _systemTelemetry.MarkUnavailable();
        var message = AppStrings.FormatText("PerformanceSamplingError", error);
        LogChangedError("performance", _performanceErrorText, message);
        _performanceErrorText = message;
        UpdateErrorText();
    }

    private void LogChangedError(string source, string previous, string current)
    {
        if (!string.IsNullOrWhiteSpace(current) && !StringComparer.Ordinal.Equals(previous, current))
        {
            _diagnosticLog.TryWrite(source, current);
        }
    }

    private void RebuildDiagnostics(
        DeviceSnapshot snapshot,
        RazerDeviceTelemetry telemetry,
        IReadOnlyList<string> errors)
    {
        Diagnostics.Clear();
        var bladeName = snapshot.Devices.FirstOrDefault(device => device.ProtocolFamily == DeviceProtocolFamilies.Blade)?.Name
            ?? "Razer Blade";
        var viperName = snapshot.Devices.FirstOrDefault(device => device.ProtocolFamily == DeviceProtocolFamilies.Viper)?.Name
            ?? "Razer Viper";
        foreach (var device in snapshot.Devices)
        {
            var row = new DeviceRowViewModel(device, telemetry);
            Diagnostics.Add(new DiagnosticRowViewModel(
                row.Name,
                AppStrings.Text("Text_5EAB9F51"),
                row.Capability,
                $"{row.Access} · {row.ReportInfo}",
                row.StatusBrush));
        }

        foreach (var error in errors)
        {
            var separator = FindDiagnosticSeparator(error);
            var capability = separator > 0 ? error[..separator] : AppStrings.Text("Text_DA035EAB");
            var detail = separator > 0 ? error[(separator + 1)..] : error;
            var device = capability.StartsWith(AppStrings.Text("Text_4B32CEE8"), StringComparison.Ordinal)
                ? viperName
                : string.Equals(capability, AppStrings.Text("Text_DA035EAB"), StringComparison.Ordinal)
                    ? "Windows HID"
                    : bladeName;
            Diagnostics.Add(new DiagnosticRowViewModel(
                device,
                capability,
                AppStrings.Text("Text_6027BEB0"),
                detail,
                new SolidColorBrush(Color.FromArgb(255, 255, 107, 107))));
        }

        foreach (var error in _startupDiagnostics)
        {
            var separator = FindDiagnosticSeparator(error);
            var source = separator > 0 ? error[..separator] : AppStrings.Text("Text_23A7CCBC");
            var detail = separator > 0 ? error[(separator + 1)..] : error;
            Diagnostics.Add(new DiagnosticRowViewModel(
                AppStrings.Text("Text_6634ED40"),
                source,
                AppStrings.Text("Text_4B4189B0"),
                detail,
                new SolidColorBrush(Color.FromArgb(255, 255, 107, 107))));
        }

        if (Diagnostics.Count == 0)
        {
            Diagnostics.Add(new DiagnosticRowViewModel(
                "Windows HID",
                AppStrings.Text("Text_DA035EAB"),
                AppStrings.Text("Text_76BEF6E0"),
                AppStrings.Text("Text_4DC4534C"),
                new SolidColorBrush(Color.FromArgb(255, 255, 181, 71))));
        }
    }

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

    private readonly record struct BladeFanProfileApplyResult(string? Error, bool Changed);
}
