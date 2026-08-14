using Microsoft.UI.Xaml;
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
    private MainWindow? _window;
    private SingleInstanceGuard? _singleInstanceGuard;
    private WindowsTrayIcon? _trayIcon;
    private WindowsPerformanceMonitor? _performanceMonitor;
    private BladeLightingController? _bladeLightingController;
    private CancellationTokenSource? _activationCancellation;
    private readonly LocalDiagnosticLog _diagnosticLog = new();

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, args) =>
            _diagnosticLog.TryWrite("unhandled", args.Exception.ToString());
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (!SingleInstanceGuard.TryAcquire(@"Local\OpenSynapse", out _singleInstanceGuard))
        {
            Environment.Exit(0);
            return;
        }

        _performanceMonitor = new WindowsPerformanceMonitor();
        var razerTransport = new RazerFeatureTransport();
        var registryLoad = RazerDeviceRegistry.Load();
        foreach (var error in registryLoad.Errors)
        {
            _diagnosticLog.TryWrite("device-manifest", error);
        }
        _bladeLightingController = new BladeLightingController(razerTransport, registryLoad.Registry);
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
            registryLoad.Errors);
        _diagnosticLog.TryWrite("application", "OpenSynapse started.");
        var window = new MainWindow(viewModel);
        _window = window;
        _window.Closed += (_, _) =>
        {
            _diagnosticLog.TryWrite("application", "OpenSynapse stopped.");
            if (_bladeLightingController is not null)
            {
                try
                {
                    _bladeLightingController.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                catch (Exception exception)
                {
                    _diagnosticLog.TryWrite("keyboard-lighting", $"restore failed: {exception}");
                }
                _bladeLightingController = null;
            }
            _trayIcon?.Dispose();
            _trayIcon = null;
            _performanceMonitor?.Dispose();
            _performanceMonitor = null;
            _activationCancellation?.Cancel();
            _activationCancellation?.Dispose();
            _activationCancellation = null;
            _singleInstanceGuard?.Dispose();
            _singleInstanceGuard = null;
        };
        _window.Activate();

        try
        {
            var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "OpenSynapse.ico");
            _trayIcon = new WindowsTrayIcon(windowHandle, "OpenSynapse", iconPath);
            _trayIcon.ShowRequested += window.RequestActivation;
            _trayIcon.ExitRequested += window.RequestExit;
            _trayIcon.Unavailable += () =>
            {
                viewModel.ReportApplicationError("托盘图标恢复失败，已切换为普通窗口关闭模式。");
                window.DisableTrayLifecycle();
            };
            window.EnableTrayLifecycle();
        }
        catch (Exception exception)
        {
            viewModel.ReportApplicationError($"托盘初始化：{exception.Message}");
        }

        _activationCancellation = new CancellationTokenSource();
        _ = ObserveActivationRequestsAsync(_activationCancellation.Token);
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
