using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.App.Runtime;
using OpenSynapse.App.ViewModels;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Graphics;

namespace OpenSynapse.App;

public sealed partial class MainWindow : Window
{
    private const int MinimumWindowWidth = 1080;
    private const int MinimumWindowHeight = 680;
    private const int ShowWindowRestore = 9;
    private const int MinimumLaunchDurationMilliseconds = 650;
    private readonly MainViewModel _viewModel;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly bool _silentLaunch;
    private readonly Func<bool, Task>? _setBladeIndicatorDisplayAvailable;
    private bool _trayLifecycleEnabled;
    private bool _exitRequested;
    private bool _enforcingMinimumSize;

    internal MainWindow(
        MainViewModel viewModel,
        AppBehaviorSettings behaviorSettings,
        bool silentLaunch = false,
        Func<bool, Task>? setChromaRestEnabled = null,
        Func<ChromaRestSnapshot>? getChromaRestSnapshot = null,
        Func<bool, Task>? setBladeIndicatorDisplayAvailable = null)
    {
        _viewModel = viewModel;
        _behaviorSettings = behaviorSettings;
        _setChromaRestEnabled = setChromaRestEnabled;
        _getChromaRestSnapshot = getChromaRestSnapshot;
        _setBladeIndicatorDisplayAvailable = setBladeIndicatorDisplayAvailable;
        _silentLaunch = silentLaunch;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException(AppStrings.Text("Text_693F327E"));
        _viewModel.SetPerformanceSamplingEnabled(false);
        _viewModel.SetDeviceWatchActive(false);
        InitializeComponent();
        Localized.RefreshTree(RootLayout);
        RootLayout.Loaded += (_, _) => Localized.RefreshTree(RootLayout);
        SelectLanguage(AppLanguageSettings.Current);
        _languageSelectionReady = true;
        InitializeBehaviorSettingsUi();
        _chromaRestStatusTimer = _dispatcherQueue.CreateTimer();
        _chromaRestStatusTimer.Interval = TimeSpan.FromSeconds(1);
        _chromaRestStatusTimer.Tick += OnChromaRestStatusTick;
        InitializeUpdateUi();
        var appIconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "OpenSynapse.ico");
        if (File.Exists(appIconPath))
        {
            AppWindow.SetIcon(appIconPath);
        }
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        RootNavigationView.DataContext = _viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        SystemBackdrop = new MicaBackdrop();
        ApplyDarkTheme();
        ApplyTitleBarColors(launchOverlayActive: true);
        Activated += OnActivated;
        Closed += OnClosed;
        AppWindow.Closing += OnAppWindowClosing;
        AppWindow.Changed += OnAppWindowChanged;
        InitializeDisplayPowerNotification();
        UpdateChromaRestStatusTimer();
    }

    internal void RequestActivation() => _dispatcherQueue.TryEnqueue(RestoreAndActivate);

    internal void EnableTrayLifecycle() => _trayLifecycleEnabled = true;

    internal void DisableTrayLifecycle() => _dispatcherQueue.TryEnqueue(() =>
    {
        _trayLifecycleEnabled = false;
        RestoreAndActivate();
    });

    internal void RequestExit() => _dispatcherQueue.TryEnqueue(async () =>
    {
        if (_exitRequested)
        {
            return;
        }

        _exitRequested = true;
        _introductionPendingAfterLaunch = false;
        IntroductionOverlay.Visibility = Visibility.Collapsed;
        SetIntroductionTarget(null);
        await CloseIntroductionTipAsync();
        _introductionStep = -1;
        Close();
    });

    private void RestoreAndActivate()
    {
        AppWindow.IsShownInSwitchers = true;
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        ShowWindow(windowHandle, ShowWindowRestore);
        Activate();
        SetForegroundWindow(windowHandle);
        UpdatePerformanceSamplingState();
        _viewModel.SetDeviceWatchActive(true);
        _viewModel.RequestDeviceRefresh();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint windowHandle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint windowHandle);

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!_trayLifecycleEnabled || _exitRequested)
        {
            return;
        }

        args.Cancel = true;
        sender.Hide();
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidVisibilityChange)
        {
            UpdatePerformanceSamplingState();
            _viewModel.SetDeviceWatchActive(sender.IsVisible);
            UpdateChromaRestStatusTimer();
            if (sender.IsVisible)
            {
                _viewModel.RequestDeviceRefresh();
            }
        }

        if (!args.DidSizeChange || _enforcingMinimumSize)
        {
            return;
        }

        var workArea = DisplayArea.GetFromWindowId(sender.Id, DisplayAreaFallback.Primary).WorkArea;
        var minimumWidth = Math.Min(MinimumWindowWidth, workArea.Width);
        var minimumHeight = Math.Min(MinimumWindowHeight, workArea.Height);
        var size = sender.Size;
        var width = Math.Max(size.Width, minimumWidth);
        var height = Math.Max(size.Height, minimumHeight);
        if (width == size.Width && height == size.Height)
        {
            return;
        }

        _enforcingMinimumSize = true;
        try
        {
            sender.Resize(new SizeInt32(width, height));
        }
        finally
        {
            _enforcingMinimumSize = false;
        }
    }

    private async void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnActivated;
        if (_silentLaunch)
        {
            AppWindow.Hide();
            LaunchProgressRing.IsActive = false;
            LaunchOverlay.Visibility = Visibility.Collapsed;
            ApplyTitleBarColors(launchOverlayActive: false);
        }
        ResizeForCurrentDisplay();
        var launchStarted = Stopwatch.GetTimestamp();
        LaunchStatusText.Text = AppStrings.Text("Text_528DF7D8");
        var initializationTask = InitializeRuntimeAsync();

        var remaining = TimeSpan.FromMilliseconds(MinimumLaunchDurationMilliseconds) -
            Stopwatch.GetElapsedTime(launchStarted);
        if (remaining > TimeSpan.Zero)
        {
            try
            {
                await Task.Delay(remaining, _lifetime.Token);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
                return;
            }
        }

        if (_silentLaunch)
        {
            return;
        }
        if (File.Exists(IntroductionMarkerPath))
        {
            LaunchStatusText.Text = AppStrings.Text("Text_470691B2");
            HideLaunchOverlay();
        }
        else
        {
            _introductionPendingAfterLaunch = true;
            HideLaunchOverlay();
        }

        _ = initializationTask;
    }

    private async Task InitializeRuntimeAsync()
    {
        // Yield before hardware work so activation returns to the UI dispatcher.
        await Task.Yield();
        try
        {
            await _viewModel.InitializeProfileAsync(_lifetime.Token);
            UpdatePerformanceSamplingState();
            _viewModel.SetDeviceWatchActive(AppWindow.IsVisible);
            _ = ObserveBackgroundLoopAsync(
                () => _viewModel.RunPerformanceLoopAsync(_lifetime.Token),
                AppStrings.Text("Text_79DA5816"));
            _ = ObserveBackgroundLoopAsync(
                () => _viewModel.RunDeviceWatchLoopAsync(_lifetime.Token),
                AppStrings.Text("Text_C2463C0F"));
            _viewModel.RequestDeviceRefresh();
            if (AutomaticUpdatesToggle.IsOn && AppUpdateSettings.AutomaticCheckDue)
            {
                _ = CheckForUpdatesAsync(downloadAutomatically: true);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _viewModel.ReportApplicationError(AppStrings.FormatText("ApplicationInitializationError",
                exception.Message));
        }
    }

    private async Task ObserveBackgroundLoopAsync(Func<Task> loop, string label)
    {
        try
        {
            await loop();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _viewModel.ReportApplicationError($"{label}: {exception.Message}");
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _chromaRestStatusTimer.Stop();
        _chromaRestStatusTimer.Tick -= OnChromaRestStatusTick;
        _aboutWindow?.Close();
        _aboutWindow = null;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        AppWindow.Closing -= OnAppWindowClosing;
        AppWindow.Changed -= OnAppWindowChanged;
        ShutdownDisplayPowerNotification();
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

}
