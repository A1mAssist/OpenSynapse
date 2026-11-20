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

public partial class App : Application
{
    private static readonly TimeSpan AudioMuteRetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ExitCleanupTimeout = TimeSpan.FromSeconds(12);
    // Cover the ~2.1 s device-rescan handoff without leaving software mode behind.
    private static readonly TimeSpan BladeSoftwareModeReleaseGrace = TimeSpan.FromSeconds(3);
    private MainWindow? _window;
    private SingleInstanceGuard? _singleInstanceGuard;
    private ElevatedMacroClient? _elevatedMacroClient;
    private ElevatedMacroInputSink? _elevatedMacroSink;
    private RazerFilterMacroInputSink? _razerFilterMacroSink;
    private WindowsTrayIcon? _trayIcon;
    private TrayMenuWindow? _trayMenuWindow;
    private TaskbarStatusPresenter? _taskbarStatusPresenter;
    private WindowsPerformanceMonitor? _performanceMonitor;
    private BladeLightingController? _bladeLightingController;
    private WindowsThxAudioController? _thxAudioController;
    private readonly BladeSoftwareModeCoordinator _bladeModeCoordinator;
    private readonly SemaphoreSlim _audioMuteRuntimeGate = new(1, 1);
    private BladeAudioMuteRuntime? _audioMuteRuntime;
    private BladeFnRuntime? _bladeFnRuntime;
    private IRazerFeatureTransport? _razerTransport;
    private MainViewModel? _audioMuteViewModel;
    private string? _activeBladeControlDevicePath;
    private ushort _activeBladeProductId;
    private int _audioMuteGeneration;
    private int _displayAvailable = 1;
    private int _closing;
    private int _emergencyLightingCleanupStarted;
    private int _emergencyMappingCleanupStarted;
    private CancellationTokenSource? _activationCancellation;
    private Task? _shutdownTask;
    private readonly object _shutdownGate = new();
    private bool _appNotificationsRegistered;
    private AppBehaviorSettings _behaviorSettings = new();
    private LowBatteryNotificationTracker? _lowBatteryNotifications;
    private RazerHidDeviceService? _razerHidDeviceService;
    private readonly LocalDiagnosticLog _diagnosticLog = new();

    public App()
    {
        _bladeModeCoordinator = new BladeSoftwareModeCoordinator(
            message => _diagnosticLog.TryWrite("blade-mode", message),
            BladeSoftwareModeReleaseGrace);
        try
        {
            AppLanguageSettings.ApplySaved();
        }
        catch (Exception exception)
        {
            _diagnosticLog.TryWrite("language", $"applying saved language failed: {exception}");
        }

        InitializeComponent();
        ConfigureDiagnostics();
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            EmergencyStopBladeLighting();
            EmergencyStopBladeMapping();
        };
    }
}
