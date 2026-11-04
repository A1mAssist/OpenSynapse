using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Lighting;
using OpenSynapse.Core.Storage;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;
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
    private readonly AppBehaviorSettings _behaviorSettings;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly bool _silentLaunch;
    private readonly Func<bool, Task>? _setBladeIndicatorDisplayAvailable;
    private readonly Func<bool, Task>? _setChromaRestEnabled;
    private readonly Func<ChromaRestSnapshot>? _getChromaRestSnapshot;
    private readonly Func<string, bool, Task>? _setChromaSourceAllowed;
    private readonly Action<bool>? _setTaskbarStatusEnabled;
    private readonly IBladeLightingController? _bladeLightingController;
    private readonly StudioInputSampler? _studioInputSampler;
    private DisplayPowerNotification? _displayPowerNotification;
    private bool _trayLifecycleEnabled;
    private bool _taskbarStatusEnabled;
    private bool _exitRequested;
    private bool _enforcingMinimumSize;

    internal MainViewModel ViewModel => _viewModel;
    internal CancellationToken LifetimeToken => _lifetime.Token;
    internal AppBehaviorSettings BehaviorSettings => _behaviorSettings;
    internal Func<bool, Task>? SetChromaRestEnabled => _setChromaRestEnabled;
    internal Func<ChromaRestSnapshot>? GetChromaRestSnapshot => _getChromaRestSnapshot;
    internal Func<string, bool, Task>? SetChromaSourceAllowed => _setChromaSourceAllowed;
    internal void SetTaskbarStatusEnabled(bool enabled)
    {
        _taskbarStatusEnabled = enabled;
        _setTaskbarStatusEnabled?.Invoke(enabled);
        UpdatePerformanceSamplingState();
    }
    internal Grid RootLayoutElement => RootLayout;

    internal MainWindow(
        MainViewModel viewModel,
        AppBehaviorSettings behaviorSettings,
        bool silentLaunch = false,
        Func<bool, Task>? setChromaRestEnabled = null,
        Func<ChromaRestSnapshot>? getChromaRestSnapshot = null,
        Func<bool, Task>? setBladeIndicatorDisplayAvailable = null,
        Func<string, bool, Task>? setChromaSourceAllowed = null,
        IBladeLightingController? bladeLightingController = null,
        Action<bool>? setTaskbarStatusEnabled = null)
    {
        _viewModel = viewModel;
        _bladeLightingController = bladeLightingController;
        _behaviorSettings = behaviorSettings;
        _setChromaRestEnabled = setChromaRestEnabled;
        _getChromaRestSnapshot = getChromaRestSnapshot;
        _setChromaSourceAllowed = setChromaSourceAllowed;
        _setTaskbarStatusEnabled = setTaskbarStatusEnabled;
        _taskbarStatusEnabled = behaviorSettings.TaskbarStatusEnabled;
        _setBladeIndicatorDisplayAvailable = setBladeIndicatorDisplayAvailable;
        _silentLaunch = silentLaunch;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException(AppStrings.Text("Text_693F327E"));
        _viewModel.Telemetry.SetPerformanceSamplingEnabled(false);
        _viewModel.Refresh.SetDeviceWatchActive(false);
        InitializeComponent();
        // The card needs a real value on first open without delaying the window.
        _ = _viewModel.CpuPower.RefreshCpuPowerLimitSafelyAsync();
        _ = _viewModel.CpuCurve.RefreshSafelyAsync();
        RootNavigationView.DataContext = _viewModel;
        DiagnosticsPage.Host = this;
        SettingsPage.Host = this;
        MacrosPage.Host = this;
        ProfilesPage.Host = this;
        DevicesPage.Host = this;
        LightingStudioPage.Host = this;
        _studioInputSampler = new StudioInputSampler();
        LightingStudioPage.Initialize(
            _bladeLightingController,
            () => _viewModel.CurrentDeviceDescriptors,
            BuildStudioDevices,
            CreateStudioHidRouter(_studioInputSampler),
            _studioInputSampler,
            CreateStudioArgbRouter(_studioInputSampler));
        DevicesPage.RefreshAdvancedFeaturesVisibility();
        MacrosPage.OpenEditorRequested += (_, _) => OpenMacroEditorWindow();
        OverviewPage.DeviceRequested += OnOverviewDeviceRequested;
        _viewModel.Hue.Configure(_behaviorSettings.HueBridgeHost, _behaviorSettings.HueAppKey);
        _viewModel.Hue.Persist = (host, appKey) =>
        {
            _behaviorSettings.HueBridgeHost = host;
            _behaviorSettings.HueAppKey = appKey;
            _behaviorSettings.Save();
        };
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _viewModel.Macros.SetMacroDispatcher(_dispatcherQueue);
        _viewModel.Macros.SetMacroOwnWindowForegroundPredicate(() => IsOwnForegroundWindow(windowHandle));
        _viewModel.Blade.SetPadlessHyperBoostConfirmation(DevicesPage.ConfirmPadlessHyperBoostAsync);
        _viewModel.Blade.SetPadlessHyperBoostAccepted(_behaviorSettings.PadlessHyperBoostAccepted);
        DevicesPage.RefreshLightingPresetOptions();
        Localized.RefreshTree(RootLayout);
        RootLayout.Loaded += (_, _) =>
        {
            Localized.RefreshTree(RootLayout);
#if DEBUG
            StartPageIfRequested();
#endif
            // The first activation can run before XAML has a XamlRoot. Reapply the saved
            // geometry once the layout is live so a low-DPI fallback cannot pin the window
            // to the minimum size for the rest of the session.
            ResizeForCurrentDisplay();
        };
        SettingsPage.Initialize();
        SettingsPage.InitializeBehaviorSettingsUi();
        ProfilesPage.InitializeBehaviorSettingsUi();
        SettingsPage.InitializeUpdateUi();
        var appIconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "OpenSynapse.ico");
        if (File.Exists(appIconPath))
        {
            AppWindow.SetIcon(appIconPath);
        }
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.RazerHid.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.RazerHid.Devices.CollectionChanged += OnRazerHidDevicesChanged;
        _viewModel.Display.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.Blade.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.Viper.PropertyChanged += OnViewModelPropertyChanged;
        WindowAppearance.Apply(this);
        ApplyDarkTheme();
        ApplyTitleBarColors(launchOverlayActive: true);
        Activated += OnActivated;
        Closed += OnClosed;
        AppWindow.Closing += OnAppWindowClosing;
        AppWindow.Changed += OnAppWindowChanged;
        _displayPowerNotification = new DisplayPowerNotification(
            this,
            _viewModel,
            _dispatcherQueue,
            _setBladeIndicatorDisplayAvailable,
            _lifetime.Token);
        _displayPowerNotification.Initialize();
        SettingsPage.UpdateChromaRestStatusTimer(AppWindow.IsVisible);
    }

    internal void RefreshLocalizationAfterLanguageChange()
    {
        Localized.RefreshTree(RootLayout);
        DevicesPage.RefreshLightingPresetOptions();
        DevicesPage.RefreshDedicatedGpuMode();
        _aboutWindow?.RefreshLocalization();
        _macroEditorWindow?.RefreshLocalization();
        ((App)Application.Current).RefreshTrayLocalization();
        RefreshIntroductionLocalization();
    }

    internal void RefreshLightingPresetOptions() => DevicesPage.RefreshLightingPresetOptions();

    internal void RefreshAdvancedFeaturesVisibility() => DevicesPage.RefreshAdvancedFeaturesVisibility();

    internal void InitializePicker(object picker) =>
        WinRT.Interop.InitializeWithWindow.Initialize(
            picker,
            WinRT.Interop.WindowNative.GetWindowHandle(this));

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
        _viewModel.Refresh.SetDeviceWatchActive(true);
        _viewModel.Refresh.RequestDeviceRefresh();
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
            _viewModel.Refresh.SetDeviceWatchActive(sender.IsVisible);
            SettingsPage.UpdateChromaRestStatusTimer(sender.IsVisible);
            if (sender.IsVisible)
            {
                _viewModel.Refresh.RequestDeviceRefresh();
                RequestStudioDeviceRefresh();
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
            // Start the cheap system sampler before profile and device work so CPU,
            // memory and storage have a first frame while the launch overlay is up.
            UpdatePerformanceSamplingState();
            _ = ObserveBackgroundLoopAsync(
                () => _viewModel.Telemetry.RunPerformanceLoopAsync(_lifetime.Token),
                AppStrings.Text("Text_79DA5816"));
            await _viewModel.InitializeProfileAsync(_lifetime.Token);
            LaunchStatusText.Text = AppStrings.Text("Text_C2463C0F");
            _viewModel.Refresh.SetDeviceWatchActive(AppWindow.IsVisible);
            _ = ObserveBackgroundLoopAsync(
                () => _viewModel.Refresh.RunDeviceWatchLoopAsync(_lifetime.Token),
                AppStrings.Text("Text_C2463C0F"));
            _viewModel.Refresh.RequestDeviceRefresh();
            LaunchStatusText.Text = AppStrings.Text("Text_470691B2");
            if (SettingsPage.AutomaticUpdatesEnabled && AppUpdateSettings.AutomaticCheckDue)
            {
                _ = SettingsPage.CheckForUpdatesAsync(downloadAutomatically: true);
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
            _viewModel.ReportApplicationError($"{label}: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static string? CategoryName(DeviceCategory category) =>
        category == DeviceCategory.Unknown ? null : category.ToString().ToLowerInvariant();

    private IReadOnlyList<StudioDeviceOption> BuildStudioDevices()
    {
        var options = new List<StudioDeviceOption>();
        foreach (var descriptor in _viewModel.CurrentDeviceDescriptors)
        {
            if (StudioBladeDevices.Resolve(descriptor) is not { } profile) continue;
            options.Add(new StudioDeviceOption(
                descriptor.Name,
                descriptor,
                descriptor.Id,
                descriptor.ProductId,
                profile.Rows,
                profile.Columns,
                IsBlade: true,
                BladeFrameMode: profile.Mode,
                Category: "keyboard"));
        }

        foreach (var row in _viewModel.RazerHid.Devices)
        {
            var connection = row.Connection;
            if (connection.Definition.Category == DeviceCategory.Laptop) continue;
            if (connection.Definition.ProductId == StudioArgbDevice.ProductId) continue;

            var staticZone = StudioHidOutputRouter.FindStaticZone(connection);
            var writesFrames = connection.Capabilities.Contains(RazerHidBackendCapability.MatrixFrameWrite);
            if (connection.Definition.MatrixDimensions is { } dimensions &&
                (writesFrames || staticZone is null))
            {
                options.Add(new StudioDeviceOption(
                    connection.Definition.DisplayName,
                    null,
                    connection.InstanceId,
                    connection.Definition.ProductId,
                    dimensions.Rows,
                    dimensions.Columns,
                    Category: CategoryName(connection.Definition.Category))
                {
                    HasNoPixelOutput = connection.IsReady && !writesFrames,
                });
                continue;
            }

            if (staticZone is not null)
            {
                // Without frame writes, every LED in the zone shows one color; the
                // canvas gets one representative cell instead of a per-key promise.
                options.Add(new StudioDeviceOption(
                    connection.Definition.DisplayName,
                    null,
                    connection.InstanceId,
                    connection.Definition.ProductId,
                    1,
                    1,
                    IsSingleZone: true,
                    Category: CategoryName(connection.Definition.Category)));
            }
        }

        // Keyboards owned by the keyboard page join the canvas through a synthetic
        // matrix definition; their settings page stays unchanged.
        foreach (var keyboard in _viewModel.Accessories.KeyboardDevices)
        {
            var connection = StudioKeyboardCatalog.CreateConnection(keyboard.Connection);
            if (connection?.Definition.MatrixDimensions is not { } keyboardMatrix ||
                options.Any(option => string.Equals(
                    option.EffectiveInstanceId, connection.InstanceId, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            options.Add(new StudioDeviceOption(
                connection.Definition.DisplayName,
                null,
                connection.InstanceId,
                connection.Definition.ProductId,
                keyboardMatrix.Rows,
                keyboardMatrix.Columns,
                Category: "keyboard"));
        }

        if (_viewModel.RazerHid.ArgbConnection is { } argb)
        {
            var info = StudioArgbDevice.Create(
                argb.InstanceId, argb.DisplayName, ArgbStripSettings.Load());
            options.Add(new StudioDeviceOption(
                info.Name,
                null,
                info.InstanceId,
                info.ProductId,
                info.Rows,
                info.Columns,
                IsArgb: true,
                Layout: info.Layout,
                Category: "argb"));
        }

        return options;
    }

    internal async Task RefreshStudioArgbAsync()
    {
        try
        {
            await _viewModel.RazerHid.RefreshArgbDiscoveredAsync(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Device enumeration failing must not break opening the studio page.
        }
    }

    private StudioHidOutputRouter? CreateStudioHidRouter(StudioInputSampler sampler)
    {
        var service = _viewModel.RazerHid.DeviceService;
        if (service is null) return null;
        return new StudioHidOutputRouter(
            service,
            () => _viewModel.RazerHid.CurrentConnections
                .Where(connection =>
                    connection.Definition.Category != DeviceCategory.Laptop &&
                    connection.Definition.ProductId != StudioArgbDevice.ProductId)
                .Concat(_viewModel.Accessories.KeyboardDevices
                    .Select(row => StudioKeyboardCatalog.CreateConnection(row.Connection))
                    .Where(connection => connection is not null)
                    .Select(connection => connection!))
                .ToArray(),
            inputProvider: (rows, columns) => sampler.Sample(rows, columns));
    }

    private StudioArgbOutputRouter? CreateStudioArgbRouter(StudioInputSampler sampler)
    {
        var service = _viewModel.RazerHid.SpecialLightingService;
        if (service is null) return null;
        return new StudioArgbOutputRouter(
            service,
            () => ArgbStripSettings.Load(),
            (rows, columns) => sampler.Sample(rows, columns));
    }

    internal void ForwardBladeMappingInput(BladeMappingInputEvent input)
    {
        if (!input.IsDown) return;
        _dispatcherQueue.TryEnqueue(() => LightingStudioPage.HandleMappingInput(input));
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        SaveWindowSize();
        SettingsPage.Shutdown();
        _aboutWindow?.Close();
        _aboutWindow = null;
        CloseThxAudioWindow();
        CloseBladeFanCurveWindow();
        CloseMacroEditorWindow();
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.RazerHid.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.RazerHid.Devices.CollectionChanged -= OnRazerHidDevicesChanged;
        _viewModel.Display.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.Blade.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.Viper.PropertyChanged -= OnViewModelPropertyChanged;
        AppWindow.Closing -= OnAppWindowClosing;
        AppWindow.Changed -= OnAppWindowChanged;
        _displayPowerNotification?.Dispose();
        _displayPowerNotification = null;
        _lifetime.Cancel();
        _lifetime.Dispose();
        if (_studioInputSampler is { } sampler) _ = sampler.DisposeAsync().AsTask();
    }

}
