using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
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
using Windows.UI;
using static OpenSynapse.App.ViewModels.DeviceUiCatalog;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly IDeviceDiscovery _discovery;
    private readonly IRazerDeviceTelemetryReader _deviceTelemetryReader;
    private readonly RazerFirmwareUpdateClient _firmwareUpdateClient;
    private readonly ProfileStore _profileStore;
    private readonly IPowerSourceProvider _powerSourceProvider;
    private readonly IActiveApplicationProvider _activeApplicationProvider;
    private readonly LocalDiagnosticLog _diagnosticLog;
    private readonly ViperViewModel _viper = new();
    private HashSet<int>? _internalDisplayRefreshRateCycleHertz;
    private IReadOnlyList<BladePerformanceMode>? _legacyPerformanceCycleModes;
    private IReadOnlyList<int>? _legacyRefreshRateCycleHertz;
    private readonly IBladeLightingController? _bladeLightingController;
    private readonly IReadOnlyList<string> _startupDiagnostics;
    private readonly VerifiedProfileApplier _profileApplier = new();
    private readonly SemaphoreSlim _deviceOperationGate = new(1, 1);
    private readonly SemaphoreSlim _deviceWatchSignal = new(0, 1);
    private ApplicationProfileSwitcher _applicationProfileSwitcher = new();
    private ProfileDocument _profile = ProfileDocument.CreateDefault();
    private PowerSourceKind? _lastPowerState;
    private string _lastDeviceRefreshText = AppStrings.Text("Text_E19E170A");
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
    private string _deviceFingerprint = string.Empty;
    private int _deviceRefreshRequested;
    private int _displayProfileApplyRequested;
    private int _deviceReadoutSamplingEnabled = 1;
    private int _diagnosticsRebuildEnabled = 1;
    private int _deviceWatchActive = 1;
    private int _displayAvailable = 1;
    private int _displayBrightnessRestorePending;
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
        OpenRazerSpecialLightingService? openRazerSpecialLightingService = null,
        RazerFirmwareUpdateClient? firmwareUpdateClient = null,
        IThxAudioController? thxAudioController = null,
        bool thxAudioSupported = false,
        IMacroInputSink? macroInputSink = null)
    {
        _discovery = discovery;
        _deviceTelemetryReader = deviceTelemetryReader;
        _firmwareUpdateClient = firmwareUpdateClient ?? new RazerFirmwareUpdateClient();
        _profileStore = profileStore ?? new ProfileStore();
        _powerSourceProvider = powerSourceProvider ?? UnknownPowerSourceProvider.Instance;
        _activeApplicationProvider = activeApplicationProvider ?? UnknownActiveApplicationProvider.Instance;
        _diagnosticLog = diagnosticLog ?? new LocalDiagnosticLog();
        _bladeLightingController = bladeLightingController;
        Blade = new BladeWorkspace(
            deviceTelemetryReader,
            bladeLightingController,
            touchpadController,
            _diagnosticLog,
            () => _profile,
            document => _profile = document,
            SaveProfileAsync,
            RunDeviceOperationAsync,
            () => _deviceDescriptors,
            () => CurrentPowerSource,
            () => _lastDeviceTelemetry,
            () => _internalDisplayRefreshRateCycleHertz,
            () => Volatile.Read(ref _displayAvailable) != 0,
            () => Volatile.Read(ref _displayBrightnessRestorePending) != 0,
            () => Volatile.Read(ref _disposed) != 0,
            SetDeviceOperationError,
            RequestDeviceRefresh,
            rate => InternalDisplayRefreshRateChangedByUser?.Invoke(rate),
            ApplyFirmwareMcuVersion,
            () => Display!,
            () => Fan!,
            () => Lighting!,
            () => Devices.FirstOrDefault(device => DeviceProtocolFamilies.IsBlade(device.ProtocolFamily)),
            GetDeviceSerialAsync);
        Macros = new MacroWorkspace(() => GetActiveProfile(), SaveProfileAsync, _diagnosticLog, macroInputSink);
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
            RequestProfileApply);
        Telemetry = new TelemetryWorkspace(
            performanceMonitor,
            _powerSourceProvider,
            () => Volatile.Read(ref _displayAvailable) != 0,
            () => CurrentPowerSource,
            error => SetPerformanceError(error));
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
            RequestDeviceRefresh);
        Lighting = new LightingWorkspace(
            _bladeLightingController,
            _deviceTelemetryReader,
            _diagnosticLog,
            () => _deviceDescriptors,
            () => _profile,
            () => CurrentPowerSource,
            () => Blade.UsesFirmwareKeyboardLighting,
            SetDisplayAvailableFlag,
            () => Volatile.Read(ref _displayAvailable) != 0,
            _deviceOperationGate,
            RequestDeviceRefresh,
            SetDeviceOperationError,
            () => Fan.StopAsync("suspend"),
            SetOpenRazerChromaEnabled,
            Blade.State);
        OpenRazer = new OpenRazerWorkspace(
            openRazerDeviceService,
            openRazerSpecialLightingService,
            () => CurrentPowerSource,
            () => _profile,
            GetActiveProfile,
            SaveProfileAsync,
            _diagnosticLog,
            () => Lighting.IsChromaIntegrationEnabled,
            () => Macros.Items.Select(macro => macro.Definition).ToArray(),
            RefreshDevicesAsync,
            RefreshDeviceOverviewVisibility,
            SetDeviceOperationError,
            DispatcherQueue.GetForCurrentThread());
        _startupDiagnostics = startupDiagnostics?.ToArray() ?? Array.Empty<string>();
        ThxAudio = new ThxAudioViewModel(thxAudioController, thxAudioSupported);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    internal event Action<int>? InternalDisplayRefreshRateChangedByUser;
    internal IReadOnlySet<int>? InternalDisplayRefreshRateCycleHertz =>
        _internalDisplayRefreshRateCycleHertz;

    public ObservableCollection<DeviceRowViewModel> Devices { get; } = new();
    public ObservableCollection<DeviceSelectorItemViewModel> DeviceSelectorItems { get; } = new();
    public BladeWorkspace Blade { get; }
    public ThxAudioViewModel ThxAudio { get; }
    public MacroWorkspace Macros { get; }
    public OpenRazerWorkspace OpenRazer { get; }
    public ProfilesWorkspace Profiles { get; }
    public TelemetryWorkspace Telemetry { get; }
    public FanWorkspace Fan { get; }
    public DisplayWorkspace Display { get; }
    public LightingWorkspace Lighting { get; }
    public ObservableCollection<DiagnosticRowViewModel> Diagnostics { get; } = new();

    public string LastDeviceRefreshText
    {
        get => _lastDeviceRefreshText;
        private set => SetField(ref _lastDeviceRefreshText, value);
    }

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
        private set => SetField(ref _isBusy, value);
    }

    public bool CanRefresh => true;

    internal IReadOnlyList<DeviceDescriptor> CurrentDeviceDescriptors => _deviceDescriptors;

    public string EmptyStateText => ConnectedDevices.Count == 0
        ? AppStrings.Text("Text_76BEF6E0")
        : string.Empty;

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

    public string CreateDiagnosticsReport()
    {
        var report = new StringBuilder();
        report.AppendLine(AppStrings.Text("DiagnosticsReportTitle"));
        report.AppendLine(AppStrings.FormatText("DiagnosticsReportDeviceRefresh", LastDeviceRefreshText));
        report.AppendLine(AppStrings.FormatText("DiagnosticsReportDeviceTelemetry", DeviceTelemetryTimeText));
        report.AppendLine();
        foreach (var row in Diagnostics)
        {
            AppendDiagnostic(report, row, 0);
        }

        return report.ToString().TrimEnd();
    }

    private static void AppendDiagnostic(
        StringBuilder report,
        DiagnosticRowViewModel row,
        int depth)
    {
        report.Append(' ', depth * 2)
            .Append(row.Device)
            .Append(" | ")
            .Append(row.Capability)
            .Append(" | ")
            .Append(row.Status)
            .Append(" | ")
            .AppendLine(row.Detail);
        foreach (var protocol in row.ProtocolDetails)
        {
            report.Append(' ', (depth + 1) * 2)
                .Append(protocol.Capability)
                .Append(" | ")
                .Append(protocol.Status)
                .Append(" | ")
                .AppendLine(protocol.Detail);
        }
        foreach (var issue in row.Issues)
        {
            AppendDiagnostic(report, issue, depth + 1);
        }
    }

    /// <summary>
    /// Names the capabilities that actually failed. The generic "writes are paused" text
    /// alone leaves a device owner with nothing to act on.
    /// </summary>
    private static string DescribeFailedReads(OpenRazerDeviceViewModel device)
    {
        var failures = device.BasicErrors;
        return failures.Count == 0
            ? device.ErrorText
            : AppStrings.FormatText("OpenRazerFailedReadsDetail",
                string.Join("; ", failures.Select(read =>
                    $"{AppStrings.Text("OpenRazerCapability" + read.Key)}: {read.Value}")));
    }

    /// <summary>
    /// Enables the periodic read-only telemetry poll. It is only worth running while the
    /// device page is open, because that is where the live readouts are shown.
    /// </summary>
    internal void SetDeviceReadoutSamplingEnabled(bool enabled) =>
        Interlocked.Exchange(ref _deviceReadoutSamplingEnabled, enabled ? 1 : 0);

    /// <summary>
    /// Rebuilding the diagnostics collection clears it and re-adds every row, which resets the
    /// bound list. Doing that on every telemetry poll stalled whichever page the user was on,
    /// so the rebuild is deferred until the diagnostics page is shown again.
    /// </summary>
    internal void SetDiagnosticsRebuildEnabled(bool enabled) =>
        Interlocked.Exchange(ref _diagnosticsRebuildEnabled, enabled ? 1 : 0);

    private void RequestProfileApply()
    {
        Interlocked.Exchange(ref _displayProfileApplyRequested, 1);
        RequestDeviceRefresh();
    }

    /// <summary>
    /// The window became visible or hidden. Wakes the samplers when the state actually
    /// changed and asks the next refresh to restore the brightness the user last set.
    /// </summary>
    private void SetDisplayAvailableFlag(bool available)
    {
        if (Interlocked.Exchange(ref _displayAvailable, available ? 1 : 0) !=
            (available ? 1 : 0))
        {
            Telemetry.NotifySamplingStateChanged();
        }
        Interlocked.Exchange(ref _displayBrightnessRestorePending, 1);
        if (!available)
        {
            Blade.OnDisplayAvailabilityChanged(false);
        }
    }

    /// <summary>Chroma ownership also gates the selected OpenRazer device's software lighting.</summary>
    private void SetOpenRazerChromaEnabled(bool enabled) =>
        OpenRazer.SelectedDevice?.SetChromaIntegrationEnabled(enabled);

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
        OpenRazer.RefreshLocalization();
        foreach (var row in BlackSharkDevices)
        {
            row.RefreshLocalization();
        }
        SelectedBlackShark?.RefreshLocalization();
        foreach (var row in Diagnostics)
        {
            row.RefreshLocalization();
        }
        foreach (var row in ViperButtonAssignments)
        {
            row.RefreshLocalization();
        }

        Telemetry.RefreshLocalization();

        OnPropertyChanged(string.Empty);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await Profiles.LoadAsync(cancellationToken);
        await Telemetry.RefreshBatteryHealthAsync(cancellationToken);
        await RefreshDevicesAsync(cancellationToken);
        RefreshFirmwareVersions();
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

        await OpenRazer.DisposeAsync().ConfigureAwait(false);
        Macros.Dispose();
        _hue?.Dispose();

        SelectedBlackShark = null;
        _selectedBlackSharkDevice = null;
        BlackSharkDevices.Clear();

        if (SelectedHeadsetAudio is { } headsetAudio)
        {
            await headsetAudio.DisposeAsync().ConfigureAwait(false);
            SelectedHeadsetAudio = null;
        }

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
        Macros.InitializeMacroRuntime();
        RefreshFirmwareVersions();
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

    private void RebuildDiagnostics(
        DeviceSnapshot snapshot,
        RazerDeviceTelemetry telemetry,
        IReadOnlyList<string> errors)
    {
        Diagnostics.Clear();
        var groups = new Dictionary<string, DiagnosticRowViewModel>(StringComparer.OrdinalIgnoreCase);
        var errorBrush = new SolidColorBrush(Color.FromArgb(255, 255, 107, 107));
        var bladeName = snapshot.Devices.FirstOrDefault(device => DeviceProtocolFamilies.IsBlade(device.ProtocolFamily))?.Name
            ?? "Razer Blade";
        var viperName = snapshot.Devices.FirstOrDefault(device => device.ProtocolFamily == DeviceProtocolFamilies.Viper)?.Name
            ?? "Razer Viper";
        foreach (var device in snapshot.Devices)
        {
            var row = new DeviceRowViewModel(device, telemetry);
            var group = new DiagnosticRowViewModel(
                row.Name,
                AppStrings.Text("Text_5EAB9F51"),
                row.Capability,
                $"{row.Access} · {row.ReportInfo}",
                row.StatusBrush);
            foreach (var detail in row.ProtocolDetails)
            {
                group.AddProtocolDetail(detail);
            }
            Diagnostics.Add(group);
            groups.TryAdd(row.Name, group);
        }

        foreach (var row in OpenRazer.Devices)
        {
            var group = new DiagnosticRowViewModel(
                row.Name,
                AppStrings.Text("Text_5EAB9F51"),
                row.Status,
                row.Identity,
                row.StatusBrush);
            Diagnostics.Add(group);
            var details = OpenRazer.SelectedDevice?.InstanceId == row.Connection.InstanceId
                ? OpenRazer.SelectedDevice.ProtocolDetails
                : OpenRazerDeviceViewModel.BuildProtocolDetails(row.Connection);
            foreach (var detail in details) group.AddProtocolDetail(detail);
            if (row.HasError)
            {
                group.AddIssue(new DiagnosticRowViewModel(row.Name, group.Capability,
                    AppStrings.Text("Text_6027BEB0"), row.Error, errorBrush));
            }
            if (OpenRazer.SelectedDevice?.InstanceId == row.Connection.InstanceId &&
                OpenRazer.SelectedDevice.HasError &&
                !StringComparer.Ordinal.Equals(OpenRazer.SelectedDevice.ErrorText, row.Error))
            {
                group.AddIssue(new DiagnosticRowViewModel(row.Name, group.Capability,
                    AppStrings.Text("Text_6027BEB0"), DescribeFailedReads(OpenRazer.SelectedDevice), errorBrush));
            }
        }

        foreach (var row in OpenRazer.KrakenDevices)
        {
            var group = new DiagnosticRowViewModel(
                row.Name,
                AppStrings.Text("Text_5EAB9F51"),
                row.Status,
                row.Identity,
                row.StatusBrush);
            Diagnostics.Add(group);
            if (!string.IsNullOrWhiteSpace(row.Error))
            {
                group.AddIssue(new DiagnosticRowViewModel(row.Name, group.Capability,
                    AppStrings.Text("Text_6027BEB0"), row.Error, errorBrush));
            }
        }

        foreach (var error in errors)
        {
            var separator = FindDiagnosticSeparator(error);
            var rawCapability = separator > 0 ? error[..separator] : string.Empty;
            var (capability, deviceName) = LocalizeTelemetryError(rawCapability, bladeName, viperName);
            var profilePrefix = AppStrings.FormatText("ProfileApplyError", string.Empty);
            var profileDetail = error.StartsWith(profilePrefix, StringComparison.Ordinal)
                ? error[profilePrefix.Length..].TrimStart()
                : string.Empty;
            if (profileDetail.StartsWith("Blade", StringComparison.OrdinalIgnoreCase) ||
                error.StartsWith(AppStrings.FormatText("LightingError", string.Empty), StringComparison.Ordinal) ||
                error.StartsWith(AppStrings.FormatText("FanControlError", string.Empty), StringComparison.Ordinal))
            {
                deviceName = bladeName;
                capability = rawCapability;
            }
            else if (profileDetail.StartsWith("Viper", StringComparison.OrdinalIgnoreCase) ||
                error.StartsWith(AppStrings.FormatText("ViperMappingProfileError", string.Empty), StringComparison.Ordinal) ||
                error == AppStrings.Text("ViperMappingReadProfileSaveFailed"))
            {
                deviceName = viperName;
                capability = string.IsNullOrEmpty(rawCapability)
                    ? AppStrings.Text("Text_23A7CCBC")
                    : rawCapability;
            }
            var detail = LocalizeDiagnosticDetail(separator > 0 ? error[(separator + 1)..] : error);
            if (!groups.TryGetValue(deviceName, out var group))
            {
                group = new DiagnosticRowViewModel(deviceName, AppStrings.Text("Text_DA035EAB"),
                    AppStrings.Text("Text_6027BEB0"), string.Empty, errorBrush);
                Diagnostics.Add(group);
                groups.Add(deviceName, group);
            }
            group.AddIssue(new DiagnosticRowViewModel(deviceName, capability,
                AppStrings.Text("Text_6027BEB0"), detail, errorBrush));
        }

        DiagnosticRowViewModel? startupGroup = null;
        foreach (var error in _startupDiagnostics)
        {
            var separator = FindDiagnosticSeparator(error);
            var source = separator > 0 ? error[..separator] : AppStrings.Text("Text_23A7CCBC");
            var detail = separator > 0 ? error[(separator + 1)..] : error;
            if (startupGroup is null)
            {
                startupGroup = new DiagnosticRowViewModel(AppStrings.Text("Text_6634ED40"),
                    AppStrings.Text("Text_23A7CCBC"), AppStrings.Text("Text_4B4189B0"),
                    string.Empty, errorBrush);
                Diagnostics.Add(startupGroup);
            }
            startupGroup.AddIssue(new DiagnosticRowViewModel(startupGroup.Device, source,
                AppStrings.Text("Text_4B4189B0"), detail, errorBrush));
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

    private static (string Capability, string Device) LocalizeTelemetryError(
        string rawCapability,
        string bladeName,
        string viperName)
    {
        var mapping = rawCapability switch
        {
            "Keyboard brightness" => ("Text_5F0C27DB", bladeName),
            "Performance and fan state" => ("DiagnosticPerformanceFan", bladeName),
            "Current CPU fan speed" => ("Text_5FCEEE13", bladeName),
            "Current GPU fan speed" => ("Text_737DBF87", bladeName),
            "CPU advanced fan mode" => ("Text_BE0A43ED", bladeName),
            "GPU advanced fan mode" => ("Text_37B37761", bladeName),
            "CPU/GPU Boost" => ("Text_1DE2FEA3", bladeName),
            "Charge limit" => ("Text_DEE979FD", bladeName),
            "Power Mode Control" => ("DiagnosticPowerModeControl", bladeName),
            "Startup animation" => ("Text_AA91A1FF", bladeName),
            "Native display mode" => ("Text_24A8C247", bladeName),
            "SKU hardware configuration" => ("Text_9D1351CB", bladeName),
            "Blade Logo" => ("DiagnosticBladeLogo", bladeName),
            "Mouse battery" => ("Text_8B2E15F8", viperName),
            "Mouse polling rate" => ("DiagnosticMousePollingRate", viperName),
            "Mouse DPI" => ("Text_25083B1F", viperName),
            "Mouse idle timeout" => ("DiagnosticMouseIdleTimeout", viperName),
            "Mouse DPI stages" => ("Text_6D7EF7B5", viperName),
            "Mouse low-battery threshold" => ("Text_B98036FA", viperName),
            _ => ("Text_DA035EAB", "Windows HID"),
        };
        return (AppStrings.Text(mapping.Item1), mapping.Item2);
    }

    private static string LocalizeDiagnosticDetail(string detail) =>
        detail.StartsWith("The device timed out", StringComparison.OrdinalIgnoreCase)
            ? AppStrings.Text("DiagnosticDeviceTimedOut")
            : detail;

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

}
