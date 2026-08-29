using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace OpenSynapse.App;

public sealed partial class MainWindow
{
    private static readonly TimeSpan IntroductionCloseTimeout = TimeSpan.FromSeconds(2);
    private static readonly string IntroductionMarkerPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenSynapse",
        "introduction-v1.done");
    private bool _introductionTransitioning;
    private bool _introductionPendingAfterLaunch;
    private int _introductionStep = -1;
    private FrameworkElement? _introductionTarget;
    private AboutWindow? _aboutWindow;

    private int IntroductionStepCount =>
        _viewModel.ViperDeviceVisibility == Visibility.Visible ? 4 : 3;

    private void ShowIntroductionClick(object sender, RoutedEventArgs e) =>
        _ = ShowIntroductionStepAsync(0);

    private void OpenAboutClick(object sender, RoutedEventArgs e) => ShowAboutWindow();

    private void ShowAboutWindow()
    {
        if (_aboutWindow is null)
        {
            _aboutWindow = new AboutWindow();
            _aboutWindow.Closed += (_, _) => _aboutWindow = null;
        }
        _aboutWindow.Activate();
    }

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
            _viewModel.ReportApplicationError(AppStrings.FormatText("WalkthroughStateError",
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
}
