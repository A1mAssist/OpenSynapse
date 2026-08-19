using Microsoft.UI.Xaml;
using System.Text.Json;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Diagnostics;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Displays;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Lifecycle;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;
using OpenSynapse.Windows.Sensors;

namespace OpenSynapse.App;

public partial class App : Application
{
    private static readonly TimeSpan AudioMuteRetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ExitCleanupTimeout = TimeSpan.FromSeconds(12);
    private static readonly string BladeMappingSessionMarkerPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenSynapse",
        "blade-mapping-session.json");
    private MainWindow? _window;
    private SingleInstanceGuard? _singleInstanceGuard;
    private WindowsTrayIcon? _trayIcon;
    private TrayMenuWindow? _trayMenuWindow;
    private WindowsPerformanceMonitor? _performanceMonitor;
    private BladeLightingController? _bladeLightingController;
    private readonly BladeSoftwareModeCoordinator _bladeModeCoordinator = new();
    private readonly SemaphoreSlim _audioMuteRuntimeGate = new(1, 1);
    private readonly WindowsDisplayBrightnessController _displayBrightnessController = new();
    private BladeAudioMuteRuntime? _audioMuteRuntime;
    private BladeMappingEngineNativeRuntime? _bladeMappingRuntime;
    private IRazerFeatureTransport? _razerTransport;
    private MainViewModel? _audioMuteViewModel;
    private int _audioMuteGeneration;
    private int _closing;
    private int _emergencyMappingCleanupStarted;
    private long _lastUiHeartbeatTicks;
    private CancellationTokenSource? _mappingWatchdogCancellation;
    private Thread? _mappingWatchdogThread;
    private CancellationTokenSource? _activationCancellation;
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

        try
        {
            if (Environment.ProcessPath is string executablePath &&
                WindowsGpuPreference.EnsureMinimumPower(executablePath))
            {
                _diagnosticLog.TryWrite(
                    "gpu-preference",
                    "Registered Windows minimum-power GPU preference; effective on next launch.");
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            InvalidOperationException or System.Security.SecurityException)
        {
            _diagnosticLog.TryWrite("gpu-preference", $"registration failed: {exception}");
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
        if (!SingleInstanceGuard.TryAcquire(@"Local\OpenSynapse", out _singleInstanceGuard))
        {
            Environment.Exit(0);
            return;
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
        _diagnosticLog.TryWrite(
            "audio-mute-sync",
            "Blade speaker/microphone mute synchronization enabled behind MappingEngine.");
        _diagnosticLog.TryWrite("application", "OpenSynapse started.");
        var window = new MainWindow(viewModel);
        _window = window;
        StartMappingWatchdog(window);
        _window.Closed += async (_, _) =>
        {
            Interlocked.Exchange(ref _closing, 1);
            Interlocked.Increment(ref _audioMuteGeneration);
            StopMappingWatchdog();
            if (_audioMuteViewModel is not null)
            {
                _audioMuteViewModel.BladeControlDevicePathChanged -= OnBladeControlDevicePathChanged;
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
        };
        _window.Activate();

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
            viewModel.ReportApplicationError(AppStrings.Format("TrayInitializationError", "托盘初始化：{0}", exception.Message));
        }

        _activationCancellation = new CancellationTokenSource();
        _ = ObserveActivationRequestsAsync(_activationCancellation.Token);
    }

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
        var generation = Interlocked.Increment(ref _audioMuteGeneration);
        _ = SwitchBladeAudioMuteRuntimeAsync(devicePath, generation);
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

            BladeMappingEngineNativeRuntime? mappingRuntime = null;
            BladeAudioMuteRuntime? audioRuntime = null;
            try
            {
                var deviceInfoJson = BladeMappingEngineProtocol.CreateBladeMediaMappingDeviceInfoJson();
                var (storageKey, storageValueJson) = LoadInstalledBladeMappingStorage();
                var staleDeviceInfoJson = ReadBladeMappingSessionMarker();
                if (staleDeviceInfoJson is not null)
                {
                    await using var recoveryRuntime = CreateInstalledMappingEngineRuntime();
                    if (!await recoveryRuntime.TryRecoverStaleSessionAsync(staleDeviceInfoJson)
                            .ConfigureAwait(false))
                    {
                        throw new InvalidOperationException(AppStrings.Get(
                            "上次异常退出留下的 Blade Fn 会话未能清理，已拒绝重新进入 Driver Mode。"));
                    }

                    DeleteBladeMappingSessionMarker();
                    _diagnosticLog.TryWrite("blade-fn", "Recovered stale MappingEngine session.");
                }

                mappingRuntime = CreateInstalledMappingEngineRuntime();
                mappingRuntime.UnsupportedMappingReceived += mappingEvent =>
                    OnBladeUnsupportedMappingReceived(mappingEvent, generation);
                mappingRuntime.InputNotificationReceived += inputEvent =>
                    OnBladeInputNotificationReceived(inputEvent, generation);
                WriteBladeMappingSessionMarker(deviceInfoJson);
                await mappingRuntime.StartAsync(
                    deviceInfoJson,
                    storageKey,
                    storageValueJson).ConfigureAwait(false);
                _bladeMappingRuntime = mappingRuntime;
                if (Volatile.Read(ref _closing) != 0 ||
                    generation != Volatile.Read(ref _audioMuteGeneration))
                {
                    throw new OperationCanceledException(AppStrings.Get(
                        "Blade MappingEngine 在音频同步完成前已被安全停止。"));
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

                mappingRuntime = null;
                _audioMuteRuntime = audioRuntime;
                audioRuntime = null;
                _diagnosticLog.TryWrite(
                    "audio-mute-sync",
                    "Blade MappingEngine and endpoint synchronization started.");
            }
            catch
            {
                if (audioRuntime is not null)
                {
                    await audioRuntime.DisposeAsync().ConfigureAwait(false);
                }
                if (mappingRuntime is not null)
                {
                    if (ReferenceEquals(_bladeMappingRuntime, mappingRuntime))
                    {
                        _bladeMappingRuntime = null;
                    }

                    await mappingRuntime.DisposeAsync().ConfigureAwait(false);
                    DeleteBladeMappingSessionMarker();
                }
                throw;
            }
        }
        catch (Exception exception)
        {
            _diagnosticLog.TryWrite("audio-mute-sync", $"switch failed: {exception}");
            if (exception is not FileNotFoundException &&
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

    private async Task DisposeBladeAudioStackAsync()
    {
        var audioRuntime = _audioMuteRuntime;
        _audioMuteRuntime = null;
        var mappingRuntime = _bladeMappingRuntime;
        _bladeMappingRuntime = null;
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
            if (mappingRuntime is not null)
            {
                await mappingRuntime.DisposeAsync().ConfigureAwait(false);
                DeleteBladeMappingSessionMarker();
            }
        }
        catch (Exception mappingError)
        {
            throw audioError is null
                ? mappingError
                : new AggregateException(audioError, mappingError);
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
                    "Emergency MappingEngine cleanup timed out; stale-session recovery remains armed.");
            }
        }
        catch (Exception exception)
        {
            _diagnosticLog.TryWrite("blade-fn", $"Emergency MappingEngine cleanup failed: {exception}");
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
                        Volatile.Read(ref _bladeMappingRuntime) is not null)
                    {
                        _diagnosticLog.TryWrite(
                            "blade-fn",
                            "UI thread heartbeat stopped; emergency MappingEngine cleanup started.");
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
        var mappingRuntime = Interlocked.Exchange(ref _bladeMappingRuntime, null);
        if (mappingRuntime is null)
        {
            return;
        }

        await mappingRuntime.DisposeAsync().ConfigureAwait(false);
        DeleteBladeMappingSessionMarker();
    }

    private void OnBladeUnsupportedMappingReceived(
        BladeUnsupportedMappingEvent mappingEvent,
        int generation)
    {
        var dispatcher = _window?.DispatcherQueue;
        if (dispatcher is null ||
            Volatile.Read(ref _closing) != 0 ||
            generation != Volatile.Read(ref _audioMuteGeneration))
        {
            return;
        }

        dispatcher.TryEnqueue(() => _ = HandleBladeUnsupportedMappingAsync(mappingEvent, generation));
    }

    private void OnBladeInputNotificationReceived(
        BladeInputNotificationEvent inputEvent,
        int generation)
    {
        if (!BladeMappingEngineProtocol.IsGameModeToggleInput(inputEvent.InputJson))
        {
            return;
        }

        var dispatcher = _window?.DispatcherQueue;
        if (dispatcher is null ||
            Volatile.Read(ref _closing) != 0 ||
            generation != Volatile.Read(ref _audioMuteGeneration))
        {
            return;
        }

        dispatcher.TryEnqueue(() => _ = HandleBladeGameModeInputAsync(generation));
    }

    private async Task HandleBladeGameModeInputAsync(int generation)
    {
        var viewModel = _audioMuteViewModel;
        if (viewModel is null ||
            Volatile.Read(ref _closing) != 0 ||
            generation != Volatile.Read(ref _audioMuteGeneration))
        {
            return;
        }

        await viewModel.ToggleBladeGamingModeAsync().ConfigureAwait(true);
    }

    private async Task HandleBladeUnsupportedMappingAsync(
        BladeUnsupportedMappingEvent mappingEvent,
        int generation)
    {
        var viewModel = _audioMuteViewModel;
        if (viewModel is null ||
            Volatile.Read(ref _closing) != 0 ||
            generation != Volatile.Read(ref _audioMuteGeneration))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(mappingEvent.OutputJson);
            var output = document.RootElement;
            if (output.ValueKind != JsonValueKind.Object ||
                !output.TryGetProperty("type", out var typeValue) ||
                typeValue.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException(AppStrings.Get("MappingEngine unsupported output 缺少 type。"));
            }

            var type = typeValue.GetString();
            switch (type)
            {
                case "gameMode" when _bladeMappingRuntime?.InputNotificationRegistered != true:
                    await viewModel.ToggleBladeGamingModeAsync().ConfigureAwait(true);
                    break;
                case "bladeTrackpad":
                    await viewModel.ToggleBladeTouchpadAsync().ConfigureAwait(true);
                    break;
                case "bladePerformance":
                    await viewModel.CycleBladePerformanceModeAsync().ConfigureAwait(true);
                    break;
                case "screenRefresh":
                    await viewModel.CycleInternalDisplayRefreshRateAsync().ConfigureAwait(true);
                    break;
                case "display" when
                    output.TryGetProperty("id", out var displayId) &&
                    displayId.ValueKind == JsonValueKind.String:
                    switch (displayId.GetString())
                    {
                        case "driverBrightnessDown":
                            await _displayBrightnessController.StepAsync(false).ConfigureAwait(true);
                            break;
                        case "driverBrightnessUp":
                            await _displayBrightnessController.StepAsync(true).ConfigureAwait(true);
                            break;
                        case "driverBrightnessStop":
                            break;
                        default:
                            throw new InvalidDataException(AppStrings.Get("MappingEngine display output id 无效。"));
                    }
                    break;
                case "bladeBattery":
                    await viewModel.ToggleBladeOneTimeFullChargeAsync().ConfigureAwait(true);
                    break;
                case "backlight" when
                    output.TryGetProperty("flag", out var flag) &&
                    flag.TryGetInt32(out var flagValue) &&
                    flagValue == 0 &&
                    output.TryGetProperty("name", out var name) &&
                    name.ValueKind == JsonValueKind.String:
                    await viewModel.StepBladeBrightnessAsync(
                        name.GetString() == "BrightnessUp").ConfigureAwait(true);
                    break;
            }
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidDataException or InvalidOperationException or
            IOException or ArgumentOutOfRangeException or AggregateException or
            System.Runtime.InteropServices.COMException)
        {
            _diagnosticLog.TryWrite("blade-fn", $"unsupported mapping failed: {exception}");
            viewModel.ReportApplicationError(AppStrings.Format("FnKeyError", "Fn 组合键：{0}", exception.Message));
        }
    }

    private static BladeMappingEngineNativeRuntime CreateInstalledMappingEngineRuntime()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Razer",
            "RazerAppEngine");
        if (Directory.Exists(root))
        {
            foreach (var directory in Directory.EnumerateDirectories(root, "app-*")
                         .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var candidate = Path.Combine(directory, "CommonDLL", "mapping_engine.dll");
                if (!File.Exists(candidate))
                {
                    continue;
                }

                try
                {
                    return BladeMappingEngineNativeRuntime.CreateVerified(candidate);
                }
                catch (InvalidOperationException)
                {
                    // Try another installed version; only the verified ABI is allowed.
                }
            }
        }

        throw new FileNotFoundException(
            AppStrings.Get("未找到已安装的已验证 Razer MappingEngine；Fn 和静音灯同步保持禁用。"),
            "mapping_engine.dll");
    }

    private static (string StorageKey, string StorageValueJson) LoadInstalledBladeMappingStorage()
    {
        const string marker = "device local storage data ";
        var containerId = BladeMappingEngineProtocol.DefaultBladeContainerId;
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Razer",
            "RazerAppEngine",
            "User Data",
            "Logs");
        if (Directory.Exists(logDirectory))
        {
            foreach (var path in Directory.EnumerateFiles(logDirectory, "products_710_ui*.log")
                         .OrderByDescending(File.GetLastWriteTimeUtc))
            {
                string? candidate = null;
                try
                {
                    foreach (var line in File.ReadLines(path))
                    {
                        var markerIndex = line.IndexOf(marker, StringComparison.Ordinal);
                        if (markerIndex >= 0)
                        {
                            var value = line[(markerIndex + marker.Length)..];
                            try
                            {
                                BladeMappingEngineProtocol.ValidateCompleteProduct710Storage(value);
                                candidate = value;
                            }
                            catch (ArgumentException)
                            {
                                // Continue to a later valid record in this or another rotated log.
                            }
                        }
                    }
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    return ($"synapse_{BladeMappingEngineProtocol.BladeProduct710Id}_{containerId}", candidate);
                }
            }
        }

        var bundled = Path.Combine(
            AppContext.BaseDirectory,
            "Native",
            "Razer",
            "Product710Mapping.json");
        if (File.Exists(bundled))
        {
            var candidate = File.ReadAllText(bundled);
            BladeMappingEngineProtocol.ValidateCompleteProduct710Storage(candidate);
            return ($"synapse_{BladeMappingEngineProtocol.BladeProduct710Id}_{containerId}", candidate);
        }

        throw new FileNotFoundException(AppStrings.Get(
            "没有找到包含完整 64 条 Product 710 默认映射的官方存储；为避免破坏 Fn 组合键，拒绝进入 Driver Mode。"));
    }

    private static string? ReadBladeMappingSessionMarker()
    {
        try
        {
            if (!File.Exists(BladeMappingSessionMarkerPath))
            {
                return null;
            }

            var value = File.ReadAllText(BladeMappingSessionMarkerPath);
            using var document = JsonDocument.Parse(value);
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? value
                : null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _ = exception;
            return null;
        }
    }

    private static void WriteBladeMappingSessionMarker(string deviceInfoJson)
    {
        var directory = Path.GetDirectoryName(BladeMappingSessionMarkerPath);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = $"{BladeMappingSessionMarkerPath}.{Environment.ProcessId}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, deviceInfoJson);
            File.Move(temporaryPath, BladeMappingSessionMarkerPath, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
                // The next startup can ignore an orphaned temporary marker.
            }
        }
    }

    private void DeleteBladeMappingSessionMarker()
    {
        try
        {
            File.Delete(BladeMappingSessionMarkerPath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            _diagnosticLog.TryWrite("blade-fn", $"Session marker cleanup failed: {exception.Message}");
        }
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
