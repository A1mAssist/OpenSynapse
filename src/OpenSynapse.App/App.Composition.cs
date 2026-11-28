using Microsoft.UI.Xaml;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using OpenSynapse.App.Runtime;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Diagnostics;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Storage;
using OpenSynapse.Windows.Audio;
using OpenSynapse.Windows.Displays;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Input;
using OpenSynapse.Windows.Lifecycle;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;
using OpenSynapse.Windows.Sensors;
using Velopack;
using Velopack.Locators;
namespace OpenSynapse.App;

public partial class App
{
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppStrings.Enable();
#if DEBUG
        if (TryLaunchKeypadMappingQa()) return;
        if (TryLaunchSettingAutoApplyQa()) return;
        if (TryLaunchNavigationPaneQa()) return;
        if (TryLaunchStudioScopeQa()) return;
        if (TryLaunchSurfaceCalibrationQa()) return;
#endif
        var silentLaunch = Environment.GetCommandLineArgs()
            .Skip(1)
            .Any(argument => StringComparer.OrdinalIgnoreCase.Equals(argument, "--silent"));
        var isElevated = ProcessIntegrityLevel.IsElevated(ProcessIntegrityLevel.Current());
        // An elevated launch must replace the tray instance; otherwise it exits before device discovery.
        // UI automation uses a separate mutex so it never replaces the user tray instance.
        var instanceName = Environment.GetEnvironmentVariable("OPENSSYNAPSE_UI_TEST_INSTANCE") is { Length: > 0 } testInstance
            ? $@"Local\OpenSynapse.QA.{testInstance}"
            : ElevatedMacroProtocol.HostInstanceName;
        var replaceExisting = isElevated && !silentLaunch &&
            !SingleInstanceGuard.IsExistingElevated(instanceName);
        bool acquired;
        try
        {
            acquired = SingleInstanceGuard.TryAcquire(
                instanceName,
                out _singleInstanceGuard,
                activateExisting: !silentLaunch,
                requestExistingShutdown: replaceExisting);
        }
        catch (ExistingInstanceNotTakeoverableException)
        {
            ShowLegacyInstanceMessage();
            Environment.Exit(0);
            return;
        }

        MarkElevatedIfNeeded(isElevated);

        if (!acquired)
        {
            Environment.Exit(0);
            return;
        }

        LoadBehaviorSettings();
        _startupArguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
        var liteMode = _behaviorSettings.LiteModeEnabled;
        LogLiteModeState(liteMode);
        EnsureStartupIntent();

        _lowBatteryNotifications = new LowBatteryNotificationTracker(_behaviorSettings.LowBatteryNotifiedLevels);
        RegisterAppNotifications();

        try
        {
            if (!liteMode &&
                Environment.ProcessPath is string executablePath &&
                WindowsGpuPreference.EnsureMinimumPower(executablePath))
            {
                _diagnosticLog.TryWrite(
                    "gpu-preference",
                    "Cleaned stale entries and registered the Windows minimum-power GPU preference.");
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            InvalidOperationException or System.Security.SecurityException)
        {
            _diagnosticLog.TryWrite("gpu-preference", $"registration failed: {exception}");
        }

        _performanceMonitor = new WindowsPerformanceMonitor(
            OpenSynapseServiceClient.TryReadCpuTemperatureAsync);
        var razerTransport = new RazerFeatureTransport(_diagnosticLog);
        _razerTransport = razerTransport;
        var registryLoad = RazerDeviceRegistry.Load();
        foreach (var error in registryLoad.Errors)
        {
            _diagnosticLog.TryWrite("device-manifest", error);
        }
        if (LiteModePolicy.ShouldCreateBladeLighting(liteMode))
        {
            _bladeLightingController = new BladeLightingController(
                razerTransport,
                registryLoad.Registry,
                _bladeModeCoordinator);
            _bladeLightingController.MappingInputObserved += input =>
                _window?.ForwardBladeMappingInput(input);
        }
        _thxAudioController = new WindowsThxAudioController();
        _elevatedMacroClient = new ElevatedMacroClient();
        _elevatedMacroSink = new ElevatedMacroInputSink(_elevatedMacroClient);
        _razerFilterMacroSink = new RazerFilterMacroInputSink(
            _elevatedMacroSink,
            message => _diagnosticLog.TryWrite("macro", message));
        // Share the Razer transport so RazerHid requests and telemetry serialize on one gate.
        _razerHidDeviceService = new RazerHidDeviceService(
            RazerHidDeviceCatalog.BuiltIn, razerTransport, _diagnosticLog);
        _razerHidDeviceService.BatteryObserved += OnPeripheralBatteryObserved;
        var viewModel = new MainViewModel(
            new WindowsHidDiscovery(
                registryLoad.Registry,
                message => _diagnosticLog.TryWrite("razer-hid", message)),
            new RazerDeviceTelemetryReader(razerTransport, registryLoad.Registry,
                () => _behaviorSettings.BladePerformanceCompatibilityEnabled),
            _performanceMonitor,
            new ProfileStore(),
            new WindowsPowerSourceProvider(),
            new WindowsActiveApplicationProvider(),
            _diagnosticLog,
            liteMode ? null : new WindowsInternalDisplayController(),
            _bladeLightingController,
            new WindowsStartupManager(),
            Environment.ProcessPath,
            registryLoad.Errors,
            liteMode ? null : new WindowsTouchpadController(),
            _razerHidDeviceService,
            new RazerHidSpecialLightingService(
                message => _diagnosticLog.TryWrite("kraken-lighting", message)),
            thxAudioController: _thxAudioController,
            thxAudioSupported: WindowsThxAudioController.IsSupportedModel(),
            macroInputSink: _razerFilterMacroSink,
            liteMode: liteMode,
            persistStartup: SaveStartupIntent);
        _ = EnsureMacroAgentAsync();
        _audioMuteViewModel = viewModel;
        StartBladeAudioMuteWatchdog();
        viewModel.Blade.ControlDeviceChanged += OnBladeControlDeviceChanged;
        viewModel.Blade.PerformanceModeChangedByUser += OnBladePerformanceModeChangedByUser;
        viewModel.Blade.GamingModeChangedByUser += OnBladeGamingModeChangedByUser;
        viewModel.Blade.GamingModeStateChanged += OnBladeGamingModeStateChanged;
        viewModel.Blade.TouchpadChangedByUser += OnBladeTouchpadChangedByUser;
        viewModel.Blade.OneTimeFullChargeChangedByUser += OnBladeOneTimeFullChargeChangedByUser;
        viewModel.InternalDisplayRefreshRateChangedByUser += OnInternalDisplayRefreshRateChangedByUser;
        viewModel.Blade.InputProfileChanged += OnBladeInputProfileChanged;
        viewModel.Telemetry.BatteryObserved += OnPeripheralBatteryObserved;
        viewModel.Macros.MacroContinuousPlaybackStarted += OnMacroContinuousPlaybackStarted;
        viewModel.SetLegacyShortcutCycleDefaults(
            _behaviorSettings.PerformanceCycleModes,
            _behaviorSettings.RefreshRateCycleHertz);
        _diagnosticLog.TryWrite(
            "audio-mute-sync",
            "Blade Fn and speaker/microphone mute synchronization enabled.");
        _diagnosticLog.TryWrite("diagnostic", "Razer device discovery diagnostics enabled.");
        _diagnosticLog.TryWrite("application", "OpenSynapse started.");
        var window = new MainWindow(
            viewModel,
            _behaviorSettings,
            silentLaunch,
            SetChromaRestEnabledAsync,
            GetChromaRestSnapshot,
            SetBladeIndicatorDisplayAvailableAsync,
            SetChromaSourceAllowedAsync, _bladeLightingController, SetTaskbarStatusEnabled,
            message => _diagnosticLog.TryWrite("power-notification", message), RefreshTaskbarStatus);
        RegisterMainWindow(window, viewModel);
        StartChromaHosts(viewModel);
        InitializeTray(window, viewModel);
        _taskbarStatusPresenter = new TaskbarStatusPresenter(
            viewModel, _behaviorSettings, _diagnosticLog, window.RequestActivation);
        if (silentLaunch)
        {
            window.AppWindow.IsShownInSwitchers = false;
            // Activate() composites a frame before OnActivated can hide the window,
            // which is the window users see flash at sign-in. Boot the runtime with
            // the window still hidden; the tray opens it normally on first use.
            window.StartHiddenLaunch();
        }
        else
        {
            window.Activate();
        }
        if (!liteMode)
        {
            window.RestorePerformanceOverlay();
        }

        _activationCancellation = new CancellationTokenSource();
        _ = ObserveActivationRequestsAsync(_activationCancellation.Token);
    }

    /// <summary>
    /// Brings the elevated macro agent up.
    ///
    /// The scheduled task has to run at the highest integrity level, so the
    /// machine-wide service creates it on the app's behalf. After that the
    /// agent runs in the user's session and no further prompt appears.
    /// </summary>
    private async Task EnsureMacroAgentAsync()
    {
        if (_elevatedMacroSink is null)
        {
            return;
        }

        try
        {
            if (!await OpenSynapseServiceClient.TryEnsureMacroAgentAsync()
                    .ConfigureAwait(false))
            {
                _diagnosticLog.TryWrite(
                    "macro",
                    "The elevated macro agent is unavailable; macros use the app's own input path.");
                return;
            }

            if (!await _elevatedMacroSink.TryEnableAsync().ConfigureAwait(false))
            {
                _diagnosticLog.TryWrite(
                    "macro",
                    "The elevated macro agent did not answer; macros use the app's own input path.");
                return;
            }

            _diagnosticLog.TryWrite("macro", "The elevated macro agent is active.");
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException or
                UnauthorizedAccessException or TimeoutException or
                System.ServiceProcess.TimeoutException)
        {
            _diagnosticLog.TryWrite(
                "macro",
                $"The elevated macro agent could not be started: {exception.Message}");
        }
    }

    private void RegisterMainWindow(MainWindow window, MainViewModel viewModel)
    {
        _window = window;
        MemoryTrace.Start(_diagnosticLog);
        window.Closed += async (_, _) => await CloseApplicationAsync(window, viewModel);
    }
}
