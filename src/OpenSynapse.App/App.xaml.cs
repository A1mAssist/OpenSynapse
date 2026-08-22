using Microsoft.UI.Xaml;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using OpenSynapse.App.Runtime;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Diagnostics;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Displays;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Lifecycle;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;
using OpenSynapse.Windows.Sensors;
using Velopack;

namespace OpenSynapse.App;

public partial class App : Application
{
    private static readonly TimeSpan AudioMuteRetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ExitCleanupTimeout = TimeSpan.FromSeconds(12);
    private MainWindow? _window;
    private SingleInstanceGuard? _singleInstanceGuard;
    private WindowsTrayIcon? _trayIcon;
    private TrayMenuWindow? _trayMenuWindow;
    private WindowsPerformanceMonitor? _performanceMonitor;
    private BladeLightingController? _bladeLightingController;
    private readonly BladeSoftwareModeCoordinator _bladeModeCoordinator = new();
    private readonly SemaphoreSlim _audioMuteRuntimeGate = new(1, 1);
    private BladeAudioMuteRuntime? _audioMuteRuntime;
    private BladeFnRuntime? _bladeFnRuntime;
    private IRazerFeatureTransport? _razerTransport;
    private MainViewModel? _audioMuteViewModel;
    private string? _activeBladeControlDevicePath;
    private int _audioMuteGeneration;
    private int _closing;
    private int _emergencyMappingCleanupStarted;
    private long _lastUiHeartbeatTicks;
    private CancellationTokenSource? _mappingWatchdogCancellation;
    private Thread? _mappingWatchdogThread;
    private CancellationTokenSource? _activationCancellation;
    private Task? _shutdownTask;
    private bool _appNotificationsRegistered;
    private AppBehaviorSettings _behaviorSettings = new();
    private readonly LocalDiagnosticLog _diagnosticLog = new();

    public App()
    {
        try
        {
            AppLanguageSettings.ApplySaved();
        }
        catch (Exception exception)
        {
            _diagnosticLog.TryWrite("language", $"applying saved language failed: {exception}");
        }

        InitializeComponent();
        UnhandledException += (_, args) =>
        {
            _diagnosticLog.TryWrite("unhandled", args.Exception.ToString());
            EmergencyStopBladeMapping();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                _diagnosticLog.TryWrite("unhandled", exception.ToString());
            }

            EmergencyStopBladeMapping();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => EmergencyStopBladeMapping();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppStrings.Enable();
        var silentLaunch = Environment.GetCommandLineArgs()
            .Skip(1)
            .Any(argument => StringComparer.OrdinalIgnoreCase.Equals(argument, "--silent"));
        if (!SingleInstanceGuard.TryAcquire(
                @"Local\OpenSynapse",
                out _singleInstanceGuard,
                activateExisting: !silentLaunch))
        {
            Environment.Exit(0);
            return;
        }

        _behaviorSettings = AppBehaviorSettings.Load();
        RegisterAppNotifications();

        try
        {
            if (Environment.ProcessPath is string executablePath &&
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

        _performanceMonitor = new WindowsPerformanceMonitor();
        var razerTransport = new RazerFeatureTransport();
        _razerTransport = razerTransport;
        var registryLoad = RazerDeviceRegistry.Load();
        foreach (var error in registryLoad.Errors)
        {
            _diagnosticLog.TryWrite("device-manifest", error);
        }
        _bladeLightingController = new BladeLightingController(
            razerTransport,
            registryLoad.Registry,
            _bladeModeCoordinator);
        var viewModel = new MainViewModel(
            new WindowsHidDiscovery(registryLoad.Registry),
            new RazerDeviceTelemetryReader(razerTransport, registryLoad.Registry),
            _performanceMonitor,
            new ProfileStore(),
            new WindowsPowerSourceProvider(),
            new WindowsActiveApplicationProvider(),
            _diagnosticLog,
            new WindowsInternalDisplayController(),
            _bladeLightingController,
            new WindowsStartupManager(),
            Environment.ProcessPath,
            registryLoad.Errors,
            new WindowsTouchpadController());
        _audioMuteViewModel = viewModel;
        viewModel.BladeControlDevicePathChanged += OnBladeControlDevicePathChanged;
        viewModel.BladePerformanceModeChangedByUser += OnBladePerformanceModeChangedByUser;
        viewModel.BladeGamingModeChangedByUser += OnBladeGamingModeChangedByUser;
        viewModel.BladeInputProfileChanged += OnBladeInputProfileChanged;
        viewModel.SetLegacyShortcutCycleDefaults(
            _behaviorSettings.PerformanceCycleModes,
            _behaviorSettings.RefreshRateCycleHertz);
        _diagnosticLog.TryWrite(
            "audio-mute-sync",
            "Blade Fn and speaker/microphone mute synchronization enabled.");
        _diagnosticLog.TryWrite("application", "OpenSynapse started.");
        var window = new MainWindow(viewModel, _behaviorSettings, silentLaunch);
        RegisterMainWindow(window, viewModel);
        StartMappingWatchdog(window);
        InitializeTray(window, viewModel);
        if (silentLaunch)
        {
            window.AppWindow.IsShownInSwitchers = false;
        }
        window.Activate();

        _activationCancellation = new CancellationTokenSource();
        _ = ObserveActivationRequestsAsync(_activationCancellation.Token);
    }

    private void RegisterMainWindow(MainWindow window, MainViewModel viewModel)
    {
        _window = window;
        window.Closed += async (_, _) => await CloseApplicationAsync(window, viewModel);
    }

    private async Task CloseApplicationAsync(MainWindow window, MainViewModel viewModel)
    {
        if (!ReferenceEquals(_window, window))
        {
            return;
        }

        await (_shutdownTask ??= ShutdownApplicationAsync(viewModel));
    }

    internal async Task ApplyUpdateAndRestartAsync(UpdateManager updateManager, VelopackAsset release)
    {
        if (_audioMuteViewModel is null)
        {
            throw new InvalidOperationException("OpenSynapse is not ready to restart for an update.");
        }

        await (_shutdownTask ??= ShutdownApplicationAsync(_audioMuteViewModel));
        updateManager.ApplyUpdatesAndRestart(release);
    }

    private async Task ShutdownApplicationAsync(MainViewModel viewModel)
    {
        Interlocked.Exchange(ref _closing, 1);
        Interlocked.Increment(ref _audioMuteGeneration);
        StopMappingWatchdog();
        if (_audioMuteViewModel is not null)
        {
            _audioMuteViewModel.BladeControlDevicePathChanged -= OnBladeControlDevicePathChanged;
            _audioMuteViewModel.BladePerformanceModeChangedByUser -= OnBladePerformanceModeChangedByUser;
            _audioMuteViewModel.BladeGamingModeChangedByUser -= OnBladeGamingModeChangedByUser;
            _audioMuteViewModel.BladeInputProfileChanged -= OnBladeInputProfileChanged;
            _audioMuteViewModel = null;
        }
        try
        {
            await DisposeHardwareForExitAsync(viewModel).WaitAsync(ExitCleanupTimeout);
        }
        catch (TimeoutException)
        {
            _diagnosticLog.TryWrite(
                "application",
                "Exit cleanup timed out; stale-session recovery remains armed.");
        }
        _diagnosticLog.TryWrite("application", "OpenSynapse stopped.");
        _trayIcon?.Dispose();
        _trayIcon = null;
        _trayMenuWindow?.CloseMenuHost();
        _trayMenuWindow = null;
        _performanceMonitor?.Dispose();
        _performanceMonitor = null;
        _activationCancellation?.Cancel();
        _activationCancellation?.Dispose();
        _activationCancellation = null;
        _singleInstanceGuard?.Dispose();
        _singleInstanceGuard = null;
        _razerTransport = null;
        if (_appNotificationsRegistered)
        {
            try
            {
                AppNotificationManager.Default.Unregister();
            }
            catch (Exception exception)
            {
                _diagnosticLog.TryWrite("notifications", $"unregistration failed: {exception}");
            }
            _appNotificationsRegistered = false;
        }
    }

    private void RegisterAppNotifications()
    {
        try
        {
            AppNotificationManager.Default.Register();
            _appNotificationsRegistered = true;
        }
        catch (Exception exception)
        {
            _diagnosticLog.TryWrite("notifications", $"registration failed: {exception}");
        }
    }

    private void OnBladePerformanceModeChangedByUser(OpenSynapse.Core.Devices.BladePerformanceMode mode)
    {
        var modeText = mode switch
        {
            OpenSynapse.Core.Devices.BladePerformanceMode.Balanced => AppStrings.Get("平衡"),
            OpenSynapse.Core.Devices.BladePerformanceMode.Performance => AppStrings.Get("性能"),
            OpenSynapse.Core.Devices.BladePerformanceMode.Custom => AppStrings.Get("自定义"),
            OpenSynapse.Core.Devices.BladePerformanceMode.Silent => AppStrings.Get("静音"),
            OpenSynapse.Core.Devices.BladePerformanceMode.Hyperboost => "HyperBoost",
            _ => mode.ToString(),
        };
        ShowModeNotification(
            AppStrings.Text("性能模式已切换"),
            AppStrings.FormatText("PerformanceModeNotification", modeText));
    }

    private void OnBladeGamingModeChangedByUser(bool enabled) => ShowModeNotification(
        AppStrings.Text("游戏模式已切换"),
        enabled
            ? AppStrings.Text("游戏模式已启用")
            : AppStrings.Text("游戏模式已关闭"));

    private void ShowModeNotification(string title, string body)
    {
        if (!_behaviorSettings.ModeChangeNotificationsEnabled || !_appNotificationsRegistered)
        {
            return;
        }

        try
        {
            var notification = new AppNotificationBuilder()
                .AddText(title)
                .AddText(body)
                .BuildNotification();
            notification.Tag = "blade-mode";
            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception exception)
        {
            _diagnosticLog.TryWrite("notifications", $"show failed: {exception}");
        }
    }

    private void InitializeTray(MainWindow window, MainViewModel viewModel)
    {
        _trayIcon?.Dispose();
        _trayIcon = null;
        _trayMenuWindow?.CloseMenuHost();
        _trayMenuWindow = null;
        try
        {
            var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "OpenSynapse.ico");
            _trayIcon = new WindowsTrayIcon(windowHandle, "OpenSynapse", iconPath);
            _trayMenuWindow = new TrayMenuWindow(viewModel);
            _trayIcon.ShowRequested += window.RequestActivation;
            _trayIcon.MenuRequested += _trayMenuWindow.ShowAt;
            _trayMenuWindow.ShowRequested += window.RequestActivation;
            _trayMenuWindow.NavigationRequested += window.RequestNavigation;
            _trayMenuWindow.StartupChangeRequested += window.RequestStartupChange;
            _trayMenuWindow.ExitRequested += window.RequestExit;
            _trayIcon.Unavailable += () =>
            {
                viewModel.ReportApplicationError(AppStrings.Get("托盘图标恢复失败，已切换为普通窗口关闭模式。"));
                window.DisableTrayLifecycle();
            };
            window.EnableTrayLifecycle();
        }
        catch (Exception exception)
        {
            _trayIcon?.Dispose();
            _trayIcon = null;
            _trayMenuWindow?.CloseMenuHost();
            _trayMenuWindow = null;
            viewModel.ReportApplicationError(AppStrings.FormatText("TrayInitializationError", exception.Message));
        }
    }

    internal void RefreshTrayLocalization() => _trayMenuWindow?.RefreshLocalization();

    private async Task DisposeHardwareForExitAsync(MainViewModel viewModel)
    {
        try
        {
            await viewModel.DisposeAsync();
        }
        catch (Exception exception)
        {
            _diagnosticLog.TryWrite("application", $"退出前恢复风扇失败：{exception}");
        }

        var lightingController = _bladeLightingController;
        _bladeLightingController = null;
        if (lightingController is not null)
        {
            try
            {
                await lightingController.DisposeAsync();
            }
            catch (Exception exception)
            {
                _diagnosticLog.TryWrite("keyboard-lighting", $"restore failed: {exception}");
            }
        }

        await _audioMuteRuntimeGate.WaitAsync();
        try
        {
            await DisposeBladeAudioStackAsync();
        }
        catch (Exception exception)
        {
            _diagnosticLog.TryWrite("audio-mute-sync", $"stop failed: {exception}");
        }
        finally
        {
            _audioMuteRuntimeGate.Release();
        }
    }

    private void OnBladeControlDevicePathChanged(string? devicePath)
    {
        _activeBladeControlDevicePath = devicePath;
        var generation = Interlocked.Increment(ref _audioMuteGeneration);
        _ = SwitchBladeAudioMuteRuntimeAsync(devicePath, generation);
    }

    private void OnBladeInputProfileChanged()
    {
        var generation = Interlocked.Increment(ref _audioMuteGeneration);
        _ = SwitchBladeAudioMuteRuntimeAsync(_activeBladeControlDevicePath, generation);
    }

    private async Task SwitchBladeAudioMuteRuntimeAsync(string? devicePath, int generation)
    {
        await _audioMuteRuntimeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _closing) != 0 ||
                generation != Volatile.Read(ref _audioMuteGeneration))
            {
                return;
            }

            await DisposeBladeAudioStackAsync().ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(devicePath) || _razerTransport is null)
            {
                return;
            }

            BladeFnRuntime? fnRuntime = null;
            BladeAudioMuteRuntime? audioRuntime = null;
            try
            {
                fnRuntime = new BladeFnRuntime(
                    _razerTransport,
                    devicePath,
                    _bladeModeCoordinator,
                    (action, token) => ExecuteBladeFnLeafAsync(action, generation, token),
                    _audioMuteViewModel?.ActiveBladeMappingPreset ??
                        OpenSynapse.Core.Profiles.BladeProfileSettings.Product710DefaultMappingPreset,
                    _audioMuteViewModel?.ActiveSnapTapEnabled == true,
                    enabled => PersistBladeSnapTap(enabled, generation));
                await fnRuntime.StartAsync().ConfigureAwait(false);
                _bladeFnRuntime = fnRuntime;
                if (Volatile.Read(ref _closing) != 0 ||
                    generation != Volatile.Read(ref _audioMuteGeneration))
                {
                    throw new OperationCanceledException(AppStrings.Text(
                        "BladeFnRuntimeStoppedBeforeAudioSync"));
                }

                audioRuntime = new BladeAudioMuteRuntime(
                    _razerTransport,
                    devicePath,
                    _bladeModeCoordinator);
                audioRuntime.Synchronized += state => _diagnosticLog.TryWrite(
                    "audio-mute-sync",
                    $"{state.Target} indicator synchronized: muted={state.Muted}.");
                audioRuntime.SynchronizationFailed += exception => _diagnosticLog.TryWrite(
                    "audio-mute-sync",
                    $"audio indicator synchronization failed: {exception}");
                await audioRuntime.StartAsync().ConfigureAwait(false);

                fnRuntime = null;
                _audioMuteRuntime = audioRuntime;
                audioRuntime = null;
                _diagnosticLog.TryWrite(
                    "audio-mute-sync",
                    "Blade Fn runtime and endpoint synchronization started without MappingEngine/AppEngine.");
                _ = ObserveBladeFnRuntimeAsync(_bladeFnRuntime, devicePath, generation);
            }
            catch
            {
                if (audioRuntime is not null)
                {
                    await audioRuntime.DisposeAsync().ConfigureAwait(false);
                }
                if (fnRuntime is not null)
                {
                    if (ReferenceEquals(_bladeFnRuntime, fnRuntime))
                    {
                        _bladeFnRuntime = null;
                    }

                    await fnRuntime.DisposeAsync().ConfigureAwait(false);
                }
                throw;
            }
        }
        catch (Exception exception)
        {
            _diagnosticLog.TryWrite("audio-mute-sync", $"switch failed: {exception}");
            ReportBladeFnFailure(exception);
            if (exception is not FileNotFoundException &&
                exception is not AggregateException &&
                !string.IsNullOrWhiteSpace(devicePath) &&
                Volatile.Read(ref _closing) == 0 &&
                generation == Volatile.Read(ref _audioMuteGeneration))
            {
                _ = RetryBladeAudioMuteRuntimeAsync(devicePath, generation);
            }
        }
        finally
        {
            _audioMuteRuntimeGate.Release();
        }
    }

    private void PersistBladeSnapTap(bool enabled, int generation)
    {
        var dispatcher = _window?.DispatcherQueue;
        var viewModel = _audioMuteViewModel;
        if (dispatcher is null || viewModel is null)
        {
            return;
        }

        dispatcher.TryEnqueue(async () =>
        {
            if (generation == Volatile.Read(ref _audioMuteGeneration))
            {
                await viewModel.SetBladeSnapTapEnabledAsync(enabled);
            }
        });
    }

    private async Task DisposeBladeAudioStackAsync()
    {
        var audioRuntime = _audioMuteRuntime;
        _audioMuteRuntime = null;
        var fnRuntime = _bladeFnRuntime;
        _bladeFnRuntime = null;
        Exception? audioError = null;
        try
        {
            if (audioRuntime is not null)
            {
                await audioRuntime.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            audioError = exception;
        }

        try
        {
            if (fnRuntime is not null)
            {
                await fnRuntime.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (Exception fnError)
        {
            throw audioError is null
                ? fnError
                : new AggregateException(audioError, fnError);
        }

        if (audioError is not null)
        {
            throw audioError;
        }
    }

    private void EmergencyStopBladeMapping()
    {
        if (Interlocked.Exchange(ref _emergencyMappingCleanupStarted, 1) != 0)
        {
            return;
        }

        Interlocked.Exchange(ref _closing, 1);
        Interlocked.Increment(ref _audioMuteGeneration);
        try
        {
            var cleanup = StopBladeMappingForExitAsync();
            if (!cleanup.Wait(ExitCleanupTimeout))
            {
                _diagnosticLog.TryWrite(
                    "blade-fn",
                    "Emergency Blade Fn cleanup timed out; RecoveryHost remains armed.");
            }
        }
        catch (Exception exception)
        {
            _diagnosticLog.TryWrite("blade-fn", $"Emergency Blade Fn cleanup failed: {exception}");
        }
    }

    private void StartMappingWatchdog(MainWindow window)
    {
        var dispatcher = window.DispatcherQueue;
        Interlocked.Exchange(ref _lastUiHeartbeatTicks, Environment.TickCount64);
        var watchdogCancellation = new CancellationTokenSource();
        _mappingWatchdogCancellation = watchdogCancellation;
        _mappingWatchdogThread = new Thread(() =>
        {
            var cancellation = watchdogCancellation.Token;
            try
            {
                while (!cancellation.WaitHandle.WaitOne(TimeSpan.FromSeconds(2)))
                {
                    if (Volatile.Read(ref _closing) != 0)
                    {
                        return;
                    }

                    dispatcher.TryEnqueue(() =>
                        Interlocked.Exchange(ref _lastUiHeartbeatTicks, Environment.TickCount64));

                    var heartbeatAge = Environment.TickCount64 -
                        Interlocked.Read(ref _lastUiHeartbeatTicks);
                    if (heartbeatAge > TimeSpan.FromSeconds(12).TotalMilliseconds &&
                        Volatile.Read(ref _bladeFnRuntime) is not null)
                    {
                        _diagnosticLog.TryWrite(
                            "blade-fn",
                            "UI thread heartbeat stopped; emergency Blade Fn cleanup started.");
                        EmergencyStopBladeMapping();
                        return;
                    }
                }
            }
            finally
            {
                watchdogCancellation.Dispose();
            }
        })
        {
            IsBackground = true,
            Name = "OpenSynapse Blade Fn watchdog",
        };
        _mappingWatchdogThread.Start();
    }

    private void StopMappingWatchdog()
    {
        var cancellation = Interlocked.Exchange(ref _mappingWatchdogCancellation, null);
        cancellation?.Cancel();
        _mappingWatchdogThread = null;
    }

    private async Task StopBladeMappingForExitAsync()
    {
        await _audioMuteRuntimeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await DisposeBladeAudioStackAsync().ConfigureAwait(false);
        }
        finally
        {
            _audioMuteRuntimeGate.Release();
        }
    }

    private ValueTask ExecuteBladeFnLeafAsync(
        BladeMappingAction action,
        int generation,
        CancellationToken cancellationToken)
    {
        var dispatcher = _window?.DispatcherQueue
            ?? throw new InvalidOperationException("Blade Fn UI dispatcher is unavailable.");
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcher.TryEnqueue(async () =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await ExecuteBladeFnLeafOnUiThreadAsync(
                        action,
                        generation,
                        cancellationToken).ConfigureAwait(true);
                    completion.TrySetResult();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled(cancellationToken);
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            }))
        {
            throw new InvalidOperationException("Blade Fn action could not enter the UI dispatcher.");
        }

        return new ValueTask(completion.Task);
    }

    private async Task ExecuteBladeFnLeafOnUiThreadAsync(
        BladeMappingAction action,
        int generation,
        CancellationToken cancellationToken)
    {
        var viewModel = _audioMuteViewModel;
        if (viewModel is null ||
            Volatile.Read(ref _closing) != 0 ||
            generation != Volatile.Read(ref _audioMuteGeneration))
        {
            throw new OperationCanceledException("Blade Fn session is no longer current.");
        }

        switch (action)
        {
            case BladeCommandMappingAction
            {
                CommandKind: BladeMappingOutputKind.GameMode,
                Command: BladeMappingCommand.Toggle,
            }:
                await viewModel.ToggleBladeGamingModeAsync(cancellationToken).ConfigureAwait(true);
                break;
            case BladeCommandMappingAction
            {
                CommandKind: BladeMappingOutputKind.BladePerformance,
                Command: BladeMappingCommand.NextPerformanceMode,
            }:
                await viewModel.CycleBladePerformanceModeAsync(cancellationToken).ConfigureAwait(true);
                break;
            case BladeCommandMappingAction
            {
                CommandKind: BladeMappingOutputKind.BladeTrackpad,
                Command: BladeMappingCommand.Toggle,
            }:
                await viewModel.ToggleBladeTouchpadAsync(cancellationToken).ConfigureAwait(true);
                break;
            case BladeCommandMappingAction
            {
                CommandKind: BladeMappingOutputKind.BladeBattery,
                Command: BladeMappingCommand.Toggle,
            }:
                await viewModel.ToggleBladeOneTimeFullChargeAsync(cancellationToken).ConfigureAwait(true);
                break;
            case BladeCommandMappingAction
            {
                CommandKind: BladeMappingOutputKind.ScreenRefresh,
                Command: BladeMappingCommand.NextRefreshRate,
            }:
                await viewModel.CycleInternalDisplayRefreshRateAsync(cancellationToken).ConfigureAwait(true);
                break;
            case BladeBacklightMappingAction
            {
                IsDown: true,
                Command: BladeMappingCommand.BrightnessDown,
            }:
                await viewModel.StepBladeBrightnessAsync(false, cancellationToken).ConfigureAwait(true);
                break;
            case BladeBacklightMappingAction
            {
                IsDown: true,
                Command: BladeMappingCommand.BrightnessUp,
            }:
                await viewModel.StepBladeBrightnessAsync(true, cancellationToken).ConfigureAwait(true);
                break;
            case BladeBacklightMappingAction { IsDown: false }:
                break;
            case BladeAudioMappingAction
            {
                Command: BladeMappingCommand.Microphone,
                Mute: 2,
            }:
                await Task.Run(
                    WindowsCoreAudioMuteEventSource.ToggleDefaultCaptureMute,
                    cancellationToken).ConfigureAwait(true);
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported Blade Fn leaf action: {action}.");
        }
    }

    private async Task ObserveBladeFnRuntimeAsync(
        BladeFnRuntime? runtime,
        string devicePath,
        int generation)
    {
        if (runtime is null)
        {
            return;
        }

        try
        {
            await runtime.Completion.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (Volatile.Read(ref _closing) != 0 ||
                generation != Volatile.Read(ref _audioMuteGeneration))
            {
                return;
            }

            _diagnosticLog.TryWrite("blade-fn", $"runtime failed closed: {exception}");
            var cleanupSucceeded = true;
            await _audioMuteRuntimeGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (ReferenceEquals(_bladeFnRuntime, runtime))
                {
                    try
                    {
                        await DisposeBladeAudioStackAsync().ConfigureAwait(false);
                    }
                    catch (Exception cleanupError)
                    {
                        cleanupSucceeded = false;
                        _diagnosticLog.TryWrite("blade-fn", $"fault cleanup failed: {cleanupError}");
                        ReportBladeFnFailure(cleanupError);
                    }
                }
            }
            finally
            {
                _audioMuteRuntimeGate.Release();
            }

            if (Volatile.Read(ref _closing) == 0 &&
                cleanupSucceeded &&
                generation == Volatile.Read(ref _audioMuteGeneration))
            {
                _ = RetryBladeAudioMuteRuntimeAsync(devicePath, generation);
            }
        }
    }

    private void ReportBladeFnFailure(Exception exception)
    {
        var dispatcher = _window?.DispatcherQueue;
        var viewModel = _audioMuteViewModel;
        if (dispatcher is null || viewModel is null)
        {
            return;
        }

        dispatcher.TryEnqueue(() => viewModel.ReportApplicationError(
            AppStrings.FormatText("FnKeyError", exception.Message)));
    }

    private async Task RetryBladeAudioMuteRuntimeAsync(string devicePath, int generation)
    {
        await Task.Delay(AudioMuteRetryDelay).ConfigureAwait(false);
        if (Volatile.Read(ref _closing) == 0 &&
            generation == Volatile.Read(ref _audioMuteGeneration))
        {
            await SwitchBladeAudioMuteRuntimeAsync(devicePath, generation).ConfigureAwait(false);
        }
    }

    private async Task ObserveActivationRequestsAsync(CancellationToken cancellationToken)
    {
        var guard = _singleInstanceGuard;
        if (guard is null)
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            var requested = await Task.Run(
                () => guard.WaitForActivation(cancellationToken),
                CancellationToken.None);
            if (!requested)
            {
                return;
            }

            _window?.RequestActivation();
        }
    }
}
