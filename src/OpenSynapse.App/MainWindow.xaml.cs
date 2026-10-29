using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.App.Runtime;
using OpenSynapse.App.ViewModels;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Graphics;

namespace OpenSynapse.App;

public sealed partial class MainWindow : Window
{
    // Logical pixels: 640 px page content + 48 px compact navigation pane.
    private const int MinimumWindowWidth = 700;
    private const int MinimumWindowHeight = 480;
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
        Func<bool, Task>? setBladeIndicatorDisplayAvailable = null,
        Func<string, bool, Task>? setChromaSourceAllowed = null)
    {
        _viewModel = viewModel;
        _behaviorSettings = behaviorSettings;
        _setChromaRestEnabled = setChromaRestEnabled;
        _getChromaRestSnapshot = getChromaRestSnapshot;
        _setChromaSourceAllowed = setChromaSourceAllowed;
        _setBladeIndicatorDisplayAvailable = setBladeIndicatorDisplayAvailable;
        _silentLaunch = silentLaunch;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException(AppStrings.Text("Text_693F327E"));
        _viewModel.SetPerformanceSamplingEnabled(false);
        _viewModel.SetDeviceWatchActive(false);
        InitializeComponent();
        _viewModel.Hue.Configure(_behaviorSettings.HueBridgeHost, _behaviorSettings.HueAppKey);
        _viewModel.Hue.Persist = (host, appKey) =>
        {
            _behaviorSettings.HueBridgeHost = host;
            _behaviorSettings.HueAppKey = appKey;
            _behaviorSettings.Save();
        };
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _viewModel.SetMacroDispatcher(_dispatcherQueue);
        _viewModel.SetMacroOwnWindowForegroundPredicate(() => IsOwnForegroundWindow(windowHandle));
        _viewModel.SetPadlessHyperBoostConfirmation(ConfirmPadlessHyperBoostAsync);
        _viewModel.SetPadlessHyperBoostAccepted(_behaviorSettings.PadlessHyperBoostAccepted);
        BladeBrightnessSlider.AddHandler(
            UIElement.PointerPressedEvent,
            new PointerEventHandler(BladeBrightnessPointerPressed),
            handledEventsToo: true);
        BladeBrightnessSlider.AddHandler(
            UIElement.PointerReleasedEvent,
            new PointerEventHandler(BladeBrightnessPointerReleased),
            handledEventsToo: true);
        BladeBrightnessSlider.AddHandler(
            UIElement.PointerCaptureLostEvent,
            new PointerEventHandler(BladeBrightnessPointerCaptureLost),
            handledEventsToo: true);
        RefreshLightingPresetOptions();
        Localized.RefreshTree(RootLayout);
        RootLayout.Loaded += (_, _) =>
        {
            Localized.RefreshTree(RootLayout);
            // The first activation can run before XAML has a XamlRoot. Reapply the saved
            // geometry once the layout is live so a low-DPI fallback cannot pin the window
            // to the minimum size for the rest of the session.
            ResizeForCurrentDisplay();
        };
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
        WindowAppearance.Apply(this);
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

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    private void SaveWindowSize()
    {
        var size = AppWindow.Size;
        if (size.Width <= 0 || size.Height <= 0 ||
            (size.Width == _behaviorSettings.WindowWidth && size.Height == _behaviorSettings.WindowHeight))
        {
            return;
        }

        _behaviorSettings.WindowWidth = size.Width;
        _behaviorSettings.WindowHeight = size.Height;
        try
        {
            _behaviorSettings.Save();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            InvalidOperationException or System.Security.SecurityException)
        {
            // Closing the app must not fail because the optional geometry file is unavailable.
        }
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        SaveWindowSize();
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
                if (DevicesPage.Visibility == Visibility.Visible)
                {
                    _ = _viewModel.RefreshHueAsync(_lifetime.Token);
                }
            }
        }

        if (!args.DidSizeChange || _enforcingMinimumSize)
        {
            return;
        }

        var workArea = DisplayArea.GetFromWindowId(sender.Id, DisplayAreaFallback.Primary).WorkArea;
        // AppWindow.Size is physical pixels; the page floor is logical.
        var scale = (Content as FrameworkElement)?.XamlRoot?.RasterizationScale ?? 1d;
        var minimumWidth = Math.Min((int)Math.Round(MinimumWindowWidth * scale), workArea.Width);
        var minimumHeight = Math.Min((int)Math.Round(MinimumWindowHeight * scale), workArea.Height);
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
        var minimumDurationTask = remaining > TimeSpan.Zero
            ? Task.Delay(remaining, _lifetime.Token)
            : Task.CompletedTask;
        try
        {
            await Task.WhenAll(initializationTask, minimumDurationTask);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return;
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
            LaunchStatusText.Text = AppStrings.Text("Text_C2463C0F");
            UpdatePerformanceSamplingState();
            _viewModel.SetDeviceWatchActive(AppWindow.IsVisible);
            _ = ObserveBackgroundLoopAsync(
                () => _viewModel.RunPerformanceLoopAsync(_lifetime.Token),
                AppStrings.Text("Text_79DA5816"));
            _ = ObserveBackgroundLoopAsync(
                () => _viewModel.RunDeviceWatchLoopAsync(_lifetime.Token),
                AppStrings.Text("Text_C2463C0F"));
            _viewModel.RequestDeviceRefresh();
            LaunchStatusText.Text = AppStrings.Text("Text_470691B2");
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
        SaveWindowSize();
        _chromaRestStatusTimer.Stop();
        _chromaRestStatusTimer.Tick -= OnChromaRestStatusTick;
        _aboutWindow?.Close();
        _aboutWindow = null;
        CloseThxAudioWindow();
        CloseBladeFanCurveWindow();
        CloseMacroEditorWindow();
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        AppWindow.Closing -= OnAppWindowClosing;
        AppWindow.Changed -= OnAppWindowChanged;
        ShutdownDisplayPowerNotification();
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

}
