using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.Win32;
using OpenSynapse.App.ViewModels;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.Storage.Pickers;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace OpenSynapse.App;

public sealed partial class MainWindow : Window
{
    private const int MinimumWindowWidth = 1080;
    private const int MinimumWindowHeight = 680;
    private const int ShowWindowRestore = 9;
    private const int MinimumLaunchDurationMilliseconds = 650;
    private static readonly TimeSpan IntroductionCloseTimeout = TimeSpan.FromSeconds(2);
    private static readonly string IntroductionMarkerPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenSynapse",
        "introduction-v1.done");
    private readonly MainViewModel _viewModel;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly bool _silentLaunch;
    private bool _trayLifecycleEnabled;
    private bool _exitRequested;
    private bool _enforcingMinimumSize;
    private bool _touchpadToggleInFlight;
    private bool _introductionTransitioning;
    private bool _languageSelectionReady;
    private Color? _lightingColorBeforeEdit;
    private bool _introductionPendingAfterLaunch;
    private int _introductionStep = -1;
    private FrameworkElement? _introductionTarget;

    public MainWindow(MainViewModel viewModel, bool silentLaunch = false)
    {
        _viewModel = viewModel;
        _silentLaunch = silentLaunch;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("OpenSynapse 主窗口必须在 DispatcherQueue 线程创建。");
        InitializeComponent();
        Localized.RefreshTree(RootLayout);
        RootLayout.Loaded += (_, _) => Localized.RefreshTree(RootLayout);
        SelectLanguage(AppLanguageSettings.Current);
        _languageSelectionReady = true;
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
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    private int IntroductionStepCount =>
        _viewModel.ViperDeviceVisibility == Visibility.Visible ? 4 : 3;

    internal void RequestActivation() => _dispatcherQueue.TryEnqueue(RestoreAndActivate);

    internal void RequestNavigation(string page) => _dispatcherQueue.TryEnqueue(() =>
    {
        var item = RootNavigationView.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.Tag as string, page));
        if (item is not null)
        {
            RootNavigationView.SelectedItem = item;
        }

        RestoreAndActivate();
    });

    internal void RequestStartupChange(bool enabled) => _dispatcherQueue.TryEnqueue(
        () => _ = _viewModel.SetStartupEnabledAsync(enabled, _lifetime.Token));

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

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs args)
    {
        if (args.Mode == PowerModes.Suspend)
        {
            try
            {
                _viewModel.PrepareForSuspendAsync().GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                _dispatcherQueue.TryEnqueue(() =>
                    _viewModel.ReportApplicationError(AppStrings.Format(
                        "SuspendFanRestoreError",
                        "休眠前恢复风扇：{0}",
                        exception.Message)));
            }
        }
        else if (args.Mode == PowerModes.Resume)
        {
            _dispatcherQueue.TryEnqueue(_viewModel.RequestDeviceRefresh);
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
        LaunchStatusText.Text = AppStrings.Get("正在读取配置与设备状态");
        try
        {
            await _viewModel.InitializeAsync(_lifetime.Token);
            _ = ObserveBackgroundLoopAsync(
                () => _viewModel.RunPerformanceLoopAsync(_lifetime.Token),
                AppStrings.Get("性能刷新"));
            _ = ObserveBackgroundLoopAsync(
                () => _viewModel.RunDeviceWatchLoopAsync(_lifetime.Token),
                AppStrings.Get("设备监听"));
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _viewModel.ReportApplicationError(AppStrings.Format(
                "ApplicationInitializationError",
                "应用初始化：{0}",
                exception.Message));
        }

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
            LaunchStatusText.Text = AppStrings.Get("已就绪");
            HideLaunchOverlay();
        }
        else
        {
            _introductionPendingAfterLaunch = true;
            HideLaunchOverlay();
        }
    }

    private void ShowIntroductionClick(object sender, RoutedEventArgs e) =>
        _ = ShowIntroductionStepAsync(0);

    private void IntroductionPreviousClick(object sender, RoutedEventArgs e)
    {
        if (_introductionStep > 0)
        {
            _ = ShowIntroductionStepAsync(_introductionStep - 1);
        }
    }

    private void IntroductionNextClick(object sender, RoutedEventArgs e)
    {
        if (_introductionStep + 1 < IntroductionStepCount)
        {
            _ = ShowIntroductionStepAsync(_introductionStep + 1);
            return;
        }

        CompleteIntroduction();
    }

    private void IntroductionTipCloseClick(TeachingTip sender, object e) =>
        CompleteIntroduction();

    private async Task ShowIntroductionStepAsync(int step)
    {
        if (_introductionTransitioning || _exitRequested)
        {
            return;
        }

        _introductionTransitioning = true;
        try
        {
            IntroductionOverlay.Visibility = Visibility.Collapsed;
            SetIntroductionTarget(null);
            await CloseIntroductionTipAsync();
            if (_exitRequested)
            {
                return;
            }

            _introductionStep = Math.Clamp(step, 0, IntroductionStepCount - 1);

            FrameworkElement target;
            switch (_introductionStep)
            {
                case 0:
                    RootNavigationView.SelectedItem = OverviewNavigationItem;
                    target = DevicesNavigationItem;
                    IntroductionTip.PreferredPlacement = TeachingTipPlacementMode.Bottom;
                    break;
                case 1:
                    RootNavigationView.SelectedItem = OverviewNavigationItem;
                    OverviewPage.ChangeView(null, 0, null, disableAnimation: true);
                    target = SystemTelemetrySection;
                    IntroductionTip.PreferredPlacement = TeachingTipPlacementMode.Bottom;
                    break;
                case 2:
                    RootNavigationView.SelectedItem = DevicesNavigationItem;
                    DevicesPage.ChangeView(null, 0, null, disableAnimation: true);
                    target = DeviceSelectorBar;
                    IntroductionTip.PreferredPlacement = TeachingTipPlacementMode.Bottom;
                    break;
                default:
                    RootNavigationView.SelectedItem = DevicesNavigationItem;
                    SelectDevice("viper");
                    DevicesPage.ChangeView(null, 0, null, disableAnimation: true);
                    target = ViperPollingRateSaveButton;
                    IntroductionTip.PreferredPlacement = TeachingTipPlacementMode.Top;
                    break;
            }

            RefreshIntroductionLocalization();
            IntroductionProgressText.Text = $"{_introductionStep + 1} / {IntroductionStepCount}";
            IntroductionPreviousButton.Visibility = _introductionStep == 0
                ? Visibility.Collapsed
                : Visibility.Visible;
            await Task.Yield();
            if (_exitRequested)
            {
                return;
            }

            await WaitForNextRenderAsync();
            RootLayout.UpdateLayout();
            if (_introductionStep == IntroductionStepCount - 1)
            {
                var targetTop = target.TransformToVisual(DevicesPage)
                    .TransformPoint(new global::Windows.Foundation.Point(0, 0)).Y +
                    DevicesPage.VerticalOffset;
                var centeredOffset = targetTop -
                    Math.Max(0, (DevicesPage.ViewportHeight - target.ActualHeight) / 2);
                DevicesPage.ChangeView(
                    null,
                    Math.Clamp(centeredOffset, 0, DevicesPage.ScrollableHeight),
                    null,
                    disableAnimation: true);
            }
            else
            {
                target.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
            }

            await WaitForNextRenderAsync();
            if (_exitRequested)
            {
                return;
            }

            RootLayout.UpdateLayout();
            SetIntroductionTarget(target);
            UpdateIntroductionOverlay();
            IntroductionOverlay.Visibility = Visibility.Visible;
            IntroductionTip.Target = target;
            IntroductionTip.IsOpen = true;
        }
        finally
        {
            _introductionTransitioning = false;
        }
    }

    private static async Task WaitForNextRenderAsync()
    {
        var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnRendering(object? sender, object args)
        {
            CompositionTarget.Rendering -= OnRendering;
            rendered.TrySetResult();
        }

        CompositionTarget.Rendering += OnRendering;
        try
        {
            await rendered.Task;
        }
        finally
        {
            CompositionTarget.Rendering -= OnRendering;
        }
    }

    private async Task CloseIntroductionTipAsync()
    {
        if (!IntroductionTip.IsOpen)
        {
            IntroductionTip.Target = null;
            return;
        }

        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnClosed(TeachingTip sender, TeachingTipClosedEventArgs args)
        {
            IntroductionTip.Closed -= OnClosed;
            closed.TrySetResult();
        }

        IntroductionTip.Closed += OnClosed;
        try
        {
            IntroductionTip.IsOpen = false;
            await closed.Task.WaitAsync(IntroductionCloseTimeout);
        }
        catch (TimeoutException)
        {
        }
        finally
        {
            IntroductionTip.Closed -= OnClosed;
            IntroductionTip.Target = null;
        }
    }

    private void CompleteIntroduction()
    {
        DismissIntroduction();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(IntroductionMarkerPath)!);
            File.WriteAllText(IntroductionMarkerPath, "1");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _viewModel.ReportApplicationError(AppStrings.Format(
                "WalkthroughStateError",
                "使用引导状态：{0}",
                exception.Message));
        }
    }

    private void DismissIntroduction()
    {
        _introductionPendingAfterLaunch = false;
        IntroductionTip.IsOpen = false;
        IntroductionOverlay.Visibility = Visibility.Collapsed;
        SetIntroductionTarget(null);
        _introductionStep = -1;
    }

    private void RootLayoutSizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateIntroductionOverlay();

    private void IntroductionTargetLayoutUpdated(object? sender, object e) =>
        UpdateIntroductionOverlay();

    private void SetIntroductionTarget(FrameworkElement? target)
    {
        if (_introductionTarget is not null)
        {
            _introductionTarget.LayoutUpdated -= IntroductionTargetLayoutUpdated;
        }

        _introductionTarget = target;
        if (_introductionTarget is not null)
        {
            _introductionTarget.LayoutUpdated += IntroductionTargetLayoutUpdated;
        }
    }

    private void UpdateIntroductionOverlay()
    {
        const double focusPadding = 8;
        var target = _introductionTarget;
        if (target is null || IntroductionOverlay.ActualWidth <= 0 || IntroductionOverlay.ActualHeight <= 0)
        {
            return;
        }

        var targetPosition = target.TransformToVisual(IntroductionOverlay)
            .TransformPoint(new global::Windows.Foundation.Point(0, 0));
        var left = Math.Clamp(targetPosition.X - focusPadding, 0, IntroductionOverlay.ActualWidth);
        var top = Math.Clamp(targetPosition.Y - focusPadding, 0, IntroductionOverlay.ActualHeight);
        var right = Math.Clamp(
            targetPosition.X + target.ActualWidth + focusPadding,
            left,
            IntroductionOverlay.ActualWidth);
        var bottom = Math.Clamp(
            targetPosition.Y + target.ActualHeight + focusPadding,
            top,
            IntroductionOverlay.ActualHeight);

        IntroductionOverlayPath.Data = CreateIntroductionOverlayGeometry(
            IntroductionOverlay.ActualWidth,
            IntroductionOverlay.ActualHeight,
            new global::Windows.Foundation.Rect(
            left,
            top,
            right - left,
            bottom - top));
    }

    private static PathGeometry CreateIntroductionOverlayGeometry(
        double width,
        double height,
        global::Windows.Foundation.Rect focus)
    {
        const double radius = 8;
        var geometry = new PathGeometry { FillRule = FillRule.EvenOdd };
        geometry.Figures.Add(CreateRectangleFigure(0, 0, width, height));

        var left = focus.Left;
        var top = focus.Top;
        var right = focus.Right;
        var bottom = focus.Bottom;
        var corner = Math.Min(radius, Math.Min(focus.Width, focus.Height) / 2);
        var roundedFocus = new PathFigure
        {
            StartPoint = new global::Windows.Foundation.Point(left + corner, top),
            IsClosed = true,
        };
        roundedFocus.Segments.Add(new LineSegment { Point = new global::Windows.Foundation.Point(right - corner, top) });
        roundedFocus.Segments.Add(CreateCorner(right, top + corner, corner));
        roundedFocus.Segments.Add(new LineSegment { Point = new global::Windows.Foundation.Point(right, bottom - corner) });
        roundedFocus.Segments.Add(CreateCorner(right - corner, bottom, corner));
        roundedFocus.Segments.Add(new LineSegment { Point = new global::Windows.Foundation.Point(left + corner, bottom) });
        roundedFocus.Segments.Add(CreateCorner(left, bottom - corner, corner));
        roundedFocus.Segments.Add(new LineSegment { Point = new global::Windows.Foundation.Point(left, top + corner) });
        roundedFocus.Segments.Add(CreateCorner(left + corner, top, corner));
        geometry.Figures.Add(roundedFocus);
        return geometry;
    }

    private static PathFigure CreateRectangleFigure(double left, double top, double width, double height)
    {
        var figure = new PathFigure
        {
            StartPoint = new global::Windows.Foundation.Point(left, top),
            IsClosed = true,
        };
        figure.Segments.Add(new LineSegment { Point = new global::Windows.Foundation.Point(left + width, top) });
        figure.Segments.Add(new LineSegment { Point = new global::Windows.Foundation.Point(left + width, top + height) });
        figure.Segments.Add(new LineSegment { Point = new global::Windows.Foundation.Point(left, top + height) });
        return figure;
    }

    private static ArcSegment CreateCorner(double x, double y, double radius) => new()
    {
        Point = new global::Windows.Foundation.Point(x, y),
        Size = new global::Windows.Foundation.Size(radius, radius),
        SweepDirection = SweepDirection.Clockwise,
    };

    private void HideLaunchOverlay()
    {
        LaunchProgressRing.IsActive = false;
        if (new UISettings().AnimationsEnabled)
        {
            LaunchOverlayFadeStoryboard.Begin();
        }
        else
        {
            LaunchOverlay.Visibility = Visibility.Collapsed;
            ApplyTitleBarColors(launchOverlayActive: false);
            StartPendingIntroduction();
        }
    }

    private void LaunchOverlayFadeCompleted(object sender, object e)
    {
        LaunchOverlay.Visibility = Visibility.Collapsed;
        ApplyTitleBarColors(launchOverlayActive: false);
        StartPendingIntroduction();
    }

    private void StartPendingIntroduction()
    {
        if (!_introductionPendingAfterLaunch)
        {
            return;
        }

        _introductionPendingAfterLaunch = false;
        _ = ShowIntroductionStepAsync(0);
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
            _viewModel.ReportApplicationError($"{label}：{exception.Message}");
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        AppWindow.Closing -= OnAppWindowClosing;
        AppWindow.Changed -= OnAppWindowChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private async void RefreshClick(object sender, RoutedEventArgs e) =>
        await _viewModel.RefreshDevicesAsync(_lifetime.Token);

    private void LanguageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_languageSelectionReady ||
            sender is not ComboBox { SelectedItem: ComboBoxItem { Tag: string language } } ||
            language == AppLanguageSettings.Current)
        {
            return;
        }

        try
        {
            AppLanguageSettings.Save(language);
            AppStrings.Reset();
            _viewModel.RefreshLocalization();
            Localized.RefreshTree(RootLayout);
            ((App)Application.Current).RefreshTrayLocalization();
            RefreshIntroductionLocalization();
            SelectLanguage(language);
        }
        catch (Exception exception)
        {
            _languageSelectionReady = false;
            SelectLanguage(AppLanguageSettings.Current);
            _languageSelectionReady = true;
            _viewModel.ReportApplicationError(AppStrings.Format(
                "LanguageSettingError",
                "界面语言设置：{0}",
                exception.Message));
        }
    }

    private void SelectLanguage(string language)
    {
        AppLanguageComboBox.SelectedItem = AppLanguageComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => StringComparer.Ordinal.Equals(item.Tag as string, language)) ??
            AppLanguageComboBox.Items[0];
    }

    private void RefreshIntroductionLocalization()
    {
        if (_introductionStep < 0)
        {
            return;
        }

        (IntroductionTip.Title, IntroductionBodyText.Text) = _introductionStep switch
        {
            0 => (AppStrings.Get("切换页面"), AppStrings.Get("从左侧进入设备、配置和诊断。")),
            1 => (AppStrings.Get("查看系统状态"), AppStrings.Get("CPU、GPU、内存和硬盘状态都在概览顶部。")),
            2 => (AppStrings.Get("选择设备"), AppStrings.Get("在笔记本和鼠标之间切换，下面会显示对应设置。")),
            _ => (AppStrings.Get("保存鼠标设置"), AppStrings.Get("鼠标改动不会直接写入。确认无误后点“保存”。")),
        };
        IntroductionNextButton.Content = _introductionStep == IntroductionStepCount - 1
            ? AppStrings.Get("完成")
            : AppStrings.Get("下一步");
    }

    private async void ApplyBrightnessClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyBladeBrightnessAsync(_lifetime.Token);

    private async void ApplyLightingEffectClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplySelectedBladeLightingEffectAsync(_lifetime.Token);

    private async void ApplyPerformanceModeClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyBladePerformanceModeAsync(_lifetime.Token);

    private async void AutoApplyComboSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { IsDropDownOpen: true, Tag: string setting })
        {
            return;
        }

        switch (setting)
        {
            case "performance":
                await _viewModel.ApplyBladePerformanceModeAsync(_lifetime.Token);
                break;
            case "charge":
                await _viewModel.ApplyBladeChargeLimitAsync(_lifetime.Token);
                break;
            case "cpuBoost":
                await _viewModel.ApplyBladeCpuBoostAsync(_lifetime.Token);
                break;
            case "gpuBoost":
                await _viewModel.ApplyBladeGpuBoostAsync(_lifetime.Token);
                break;
            case "lighting":
                await _viewModel.ApplySelectedBladeLightingEffectAsync(_lifetime.Token);
                break;
            case "logo":
                await _viewModel.ApplyBladeLogoAsync(_lifetime.Token);
                break;
            case "refreshRate":
                await _viewModel.ApplyInternalDisplayRefreshRateAsync(_lifetime.Token);
                break;
        }
    }

    private async void AutoApplyMaxFanToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch { FocusState: not FocusState.Unfocused })
        {
            await _viewModel.ApplyBladeMaxFanAsync(_lifetime.Token);
        }
    }

    private async void AutoApplyPlatformToggleToggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch { FocusState: not FocusState.Unfocused, Tag: string setting })
        {
            return;
        }

        switch (setting)
        {
            case "gamingMode":
                await _viewModel.ApplyBladeGamingModeAsync(_lifetime.Token);
                break;
            case "startupAnimation":
                await _viewModel.ApplyBladeStartupAnimationAsync(_lifetime.Token);
                break;
            case "oneTimeFullCharge":
                await _viewModel.ApplyBladeOneTimeFullChargeAsync(_lifetime.Token);
                break;
        }
    }

    private async void AutoApplyBrightnessValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (sender is Slider { FocusState: not FocusState.Unfocused })
        {
            await _viewModel.ApplyBladeBrightnessAsync(_lifetime.Token);
        }
    }

    private void LightingColorFlyoutOpened(object sender, object e)
    {
        _lightingColorBeforeEdit = sender is Flyout { Content: ColorPicker picker }
            ? picker.Color
            : null;
    }

    private async void LightingColorFlyoutClosed(object sender, object e)
    {
        var changed = sender is Flyout { Content: ColorPicker picker } &&
            _lightingColorBeforeEdit is Color previous && picker.Color != previous;
        _lightingColorBeforeEdit = null;
        if (changed)
        {
            await _viewModel.ApplySelectedBladeLightingEffectAsync(_lifetime.Token);
        }
    }

    private async void ApplyPollingRateClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyViperPollingRateAsync(_lifetime.Token);

    private async void ApplyDpiClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyViperDpiAsync(_lifetime.Token);

    private async void ApplyIdleClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyViperIdleAsync(_lifetime.Token);

    private async void ApplyDpiStagesClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyViperDpiStagesAsync(_lifetime.Token);

    private async void ApplyChargeLimitClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyBladeChargeLimitAsync(_lifetime.Token);

    private async void ApplyCpuBoostClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyBladeCpuBoostAsync(_lifetime.Token);

    private async void ApplyGpuBoostClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyBladeGpuBoostAsync(_lifetime.Token);

    private async void ApplyMaxFanClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyBladeMaxFanAsync(_lifetime.Token);

    private async void ApplyLogoClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ApplyBladeLogoAsync(_lifetime.Token);

    private async void ToggleTouchpadToggled(object sender, RoutedEventArgs e)
    {
        if (_touchpadToggleInFlight || sender is not ToggleSwitch { FocusState: not FocusState.Unfocused })
        {
            return;
        }

        _touchpadToggleInFlight = true;
        try
        {
            await _viewModel.ToggleBladeTouchpadAsync(_lifetime.Token);
        }
        finally
        {
            _touchpadToggleInFlight = false;
        }
    }

    private async void ReadViperButtonMappingsClick(object sender, RoutedEventArgs e) =>
        await _viewModel.ReadViperButtonMappingsAsync(_lifetime.Token);

    private async void ApplyViperButtonMappingClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ViperButtonAssignmentRowViewModel row })
        {
            await _viewModel.ApplyViperButtonMappingAsync(row, _lifetime.Token);
        }
    }

    private async void ProfileSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: string profileName })
        {
            await _viewModel.SelectProfileAsync(profileName, _lifetime.Token);
        }
    }

    private async void CreateProfileClick(object sender, RoutedEventArgs e) =>
        await _viewModel.CreateProfileAsync(_lifetime.Token);

    private async void CloneProfileClick(object sender, RoutedEventArgs e) =>
        await _viewModel.CloneActiveProfileAsync(_lifetime.Token);

    private async void RenameProfileClick(object sender, RoutedEventArgs e) =>
        await _viewModel.RenameActiveProfileAsync(_lifetime.Token);

    private async void DeleteProfileClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = AppStrings.Get("删除当前配置？"),
            Content = AppStrings.Format(
                "DeleteProfileMessage",
                "将删除“{0}”，设备设置不会被卸载。",
                _viewModel.ActiveProfileName),
            PrimaryButtonText = AppStrings.Get("删除"),
            CloseButtonText = AppStrings.Get("取消"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootNavigationView.XamlRoot,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await _viewModel.DeleteActiveProfileAsync(_lifetime.Token);
        }
    }

    private async void StartupToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle && toggle.IsOn != _viewModel.IsStartupEnabled)
        {
            await _viewModel.SetStartupEnabledAsync(toggle.IsOn, _lifetime.Token);
        }
    }

    private async void SilentStartupToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle && toggle.IsOn != _viewModel.IsSilentStartupEnabled)
        {
            await _viewModel.SetSilentStartupEnabledAsync(toggle.IsOn, _lifetime.Token);
        }
    }

    private async void BindApplicationClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder,
        };
        picker.FileTypeFilter.Add(".exe");
        InitializePicker(picker);
        var file = await picker.PickSingleFileAsync();
        if (file is not null && !string.IsNullOrWhiteSpace(file.Path))
        {
            await _viewModel.BindApplicationAsync(file.Path, _lifetime.Token);
        }
    }

    private async void UnbindApplicationClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string executablePath })
        {
            await _viewModel.UnbindApplicationAsync(executablePath, _lifetime.Token);
        }
    }

    private async void ImportProfilesClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add(".json");
        InitializePicker(picker);
        var file = await picker.PickSingleFileAsync();
        if (file is not null && !string.IsNullOrWhiteSpace(file.Path))
        {
            await _viewModel.ImportProfilesAsync(file.Path, _lifetime.Token);
        }
    }

    private async void ExportProfilesClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"OpenSynapse-{_viewModel.ActiveProfileName}",
        };
        picker.FileTypeChoices.Add(AppStrings.Get("OpenSynapse 配置"), [".json"]);
        InitializePicker(picker);
        var file = await picker.PickSaveFileAsync();
        if (file is not null && !string.IsNullOrWhiteSpace(file.Path))
        {
            await _viewModel.ExportProfilesAsync(file.Path, _lifetime.Token);
        }
    }

    private void InitializePicker(object picker) =>
        WinRT.Interop.InitializeWithWindow.Initialize(
            picker,
            WinRT.Interop.WindowNative.GetWindowHandle(this));

    private void DeviceSelectorClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string device })
        {
            SelectDevice(device);
        }
    }

    private void SelectDevice(string device)
    {
        if (device == "viper" && _viewModel.ViperDeviceVisibility != Visibility.Visible)
        {
            device = "blade";
        }
        BladeDevicePanel.Visibility = device == "blade" ? Visibility.Visible : Visibility.Collapsed;
        ViperDevicePanel.Visibility = device == "viper" ? Visibility.Visible : Visibility.Collapsed;
        UpdateDeviceSelector(device);
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MainViewModel.ViperDeviceVisibility) &&
            _viewModel.ViperDeviceVisibility != Visibility.Visible)
        {
            _dispatcherQueue.TryEnqueue(() => SelectDevice("blade"));
        }
    }

    private void UpdateDeviceSelector(string device)
    {
        var selected = (Brush)Application.Current.Resources["SurfaceRaisedBrush"];
        var transparent = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        BladeDeviceButton.Background = device == "blade" ? selected : transparent;
        ViperDeviceButton.Background = device == "viper" ? selected : transparent;
    }

    private void NavigationChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var page = (args.SelectedItemContainer as NavigationViewItem)?.Tag as string;
        OverviewPage.Visibility = page == "overview" ? Visibility.Visible : Visibility.Collapsed;
        DevicesPage.Visibility = page == "devices" ? Visibility.Visible : Visibility.Collapsed;
        ProfilesPage.Visibility = page == "profiles" ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticsPage.Visibility = page == "diagnostics" ? Visibility.Visible : Visibility.Collapsed;
        AboutPage.Visibility = page == "about" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyDarkTheme()
    {
        RootNavigationView.RequestedTheme = ElementTheme.Dark;
        ApplyTitleBarColors();
        UpdateDeviceSelector(BladeDevicePanel.Visibility == Visibility.Visible ? "blade" : "viper");
    }

    private void ApplyTitleBarColors(bool launchOverlayActive = false)
    {
        var titleBar = AppWindow.TitleBar;
        if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        var background = launchOverlayActive
            ? Color.FromArgb(255, 0x17, 0x17, 0x17)
            : Color.FromArgb(255, 0x20, 0x20, 0x20);
        var foreground = Color.FromArgb(255, 0xF4, 0xF4, 0xF4);
        var hoverBackground = Color.FromArgb(255, 0x38, 0x38, 0x38);
        var pressedBackground = Color.FromArgb(255, 0x4C, 0x4C, 0x4C);

        AppTitleBar.Background = (Brush)Application.Current.Resources[
            launchOverlayActive ? "CanvasBrush" : "SurfaceBrush"];
        titleBar.BackgroundColor = background;
        titleBar.ForegroundColor = foreground;
        titleBar.InactiveBackgroundColor = background;
        titleBar.InactiveForegroundColor = foreground;
        titleBar.ButtonBackgroundColor = background;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonHoverBackgroundColor = hoverBackground;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedBackgroundColor = pressedBackground;
        titleBar.ButtonPressedForegroundColor = foreground;
    }

    private void ResizeForCurrentDisplay()
    {
        var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;
        var scale = (Content as FrameworkElement)?.XamlRoot?.RasterizationScale ?? 1d;
        var width = Math.Min((int)Math.Round(1180 * scale), Math.Max(800, workArea.Width - 48));
        var height = Math.Min((int)Math.Round(800 * scale), Math.Max(600, workArea.Height - 48));
        var bounds = new RectInt32(
            workArea.X + Math.Max(0, (workArea.Width - width) / 2),
            workArea.Y + Math.Max(0, (workArea.Height - height) / 2),
            width,
            height);
        AppWindow.MoveAndResize(bounds);
    }
}
