using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Storage;
using Windows.UI.ViewManagement;

namespace OpenSynapse.App;

public sealed partial class MainWindow
{
    private static readonly TimeSpan IntroductionCloseTimeout = TimeSpan.FromSeconds(2);
    private static readonly string IntroductionMarkerPath =
        AppDataPaths.Combine("introduction-v2.done");
    private static readonly string LegacyIntroductionMarkerPath =
        AppDataPaths.Combine("introduction-v1.done");
    private bool _introductionTransitioning;
    private bool _introductionPendingAfterLaunch;
    private bool _startupPromptPending = true;
    private int _introductionStep = -1;
    private FrameworkElement? _introductionTarget;
    private IReadOnlyList<IntroductionStep>? _introductionSteps;
    private AboutWindow? _aboutWindow;

    private static bool HasSeenIntroduction =>
        File.Exists(IntroductionMarkerPath) || File.Exists(LegacyIntroductionMarkerPath);

    private int IntroductionStepCount => _introductionSteps?.Count ?? 0;

    internal void ShowIntroduction()
    {
        _introductionSteps = null;
        _ = ShowIntroductionStepAsync(0);
    }

    internal void ShowAboutWindow()
    {
        if (_aboutWindow is null)
        {
            _aboutWindow = new AboutWindow(AppWindow, RootLayout.XamlRoot?.RasterizationScale ?? 1d);
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
        if (IntroductionStepCount == 0)
        {
            return;
        }

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

            await EnsureIntroductionStepsAsync();
            if (_exitRequested || _introductionSteps is not { Count: > 0 } steps)
            {
                return;
            }

            _introductionStep = Math.Clamp(step, 0, steps.Count - 1);
            var definition = steps[_introductionStep];
            var target = await PrepareIntroductionStepAsync(definition);

            RefreshIntroductionLocalization();
            IntroductionProgressText.Text = $"{_introductionStep + 1} / {steps.Count}";
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
            if (definition.Kind == IntroductionStepKind.DeviceCards)
            {
                var cardIndex = 0;
                if (definition.Device is { } device)
                {
                    var cards = _viewModel.PrimaryDevices;
                    for (var index = 0; index < cards.Count; index++)
                    {
                        if (ReferenceEquals(cards[index], device))
                        {
                            cardIndex = index;
                            break;
                        }
                    }
                }
                // ItemsRepeater virtualizes its children; resolve the real card after layout.
                target = DevicesPage.DeviceCardsRepeaterElement.TryGetElement(cardIndex) as FrameworkElement
                    ?? target;
            }

            if (definition.Kind is IntroductionStepKind.DeviceControls or IntroductionStepKind.DeviceCards)
            {
                DevicesPage.CenterOn(target);
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
            IntroductionTip.PreferredPlacement = ChooseIntroductionPlacement(target);
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

    private async Task EnsureIntroductionStepsAsync()
    {
        if (_introductionSteps is not null)
        {
            return;
        }

        _viewModel.Refresh.RequestDeviceRefresh();
        var deadline = Environment.TickCount64 + 2500;
        while (_viewModel.ConnectedDevices.Count == 0 &&
               Environment.TickCount64 < deadline &&
               !_lifetime.IsCancellationRequested)
        {
            await Task.Delay(100, _lifetime.Token);
        }

        if (_lifetime.IsCancellationRequested)
        {
            return;
        }

        _introductionSteps = BuildIntroductionSteps();
    }

    private IReadOnlyList<IntroductionStep> BuildIntroductionSteps()
    {
        var navigation = new IntroductionStep(
            IntroductionStepKind.Navigation,
            "IntroductionV2NavigationTitle",
            _viewModel.IsLiteMode
                ? "IntroductionV2NavigationBodyLite"
                : "IntroductionV2NavigationBody");
        var peripheral = _viewModel.ConnectedDevices.FirstOrDefault(device =>
            device.Category is DeviceCategory.Keyboard or DeviceCategory.Mouse);
        var laptop = _viewModel.ConnectedDevices.FirstOrDefault(device =>
            device.Category == DeviceCategory.Laptop);
        var devices = new IntroductionStep(
            IntroductionStepKind.DeviceCards,
            "IntroductionV2DevicesTitle",
            "IntroductionV2DevicesBody",
            peripheral ?? laptop ?? _viewModel.ConnectedDevices.FirstOrDefault());
        if (peripheral is not null)
        {
            return
            [
                navigation,
                devices,
                new IntroductionStep(
                    IntroductionStepKind.DeviceControls,
                    "IntroductionV2DeviceSettingsTitle",
                    peripheral.Category == DeviceCategory.Keyboard
                        ? "IntroductionV2KeyboardBody"
                        : "IntroductionV2MouseBody",
                    peripheral),
            ];
        }

        if (laptop is not null)
        {
            return
            [
                navigation,
                devices,
                new IntroductionStep(
                    IntroductionStepKind.DeviceControls,
                    "IntroductionV2BladeTitle",
                    "IntroductionV2BladeBody",
                    laptop),
            ];
        }

        if (_viewModel.ConnectedDevices.Count > 0)
        {
            // Other devices (headsets, accessories) keep the device-card step but are not
            // claimed to be missing just because they are not a mouse, keyboard, or Blade.
            return [navigation, devices];
        }

        return
        [
            navigation,
            new IntroductionStep(
                IntroductionStepKind.NoDevices,
                "IntroductionV2NoDevicesTitle",
                "IntroductionV2NoDevicesBody"),
        ];
    }

    private async Task<FrameworkElement> PrepareIntroductionStepAsync(IntroductionStep step)
    {
        switch (step.Kind)
        {
            case IntroductionStepKind.Navigation:
                RootNavigationView.SelectedItem = _viewModel.IsLiteMode
                    ? DevicesNavigationItem
                    : OverviewNavigationItem;
                return DevicesNavigationItem;
            case IntroductionStepKind.DeviceCards:
                RootNavigationView.SelectedItem = DevicesNavigationItem;
                DevicesPage.ScrollToTop();
                return DevicesPage.DeviceCardsRepeaterElement;
            case IntroductionStepKind.DeviceControls:
                RootNavigationView.SelectedItem = DevicesNavigationItem;
                if (step.Device is { Category: DeviceCategory.Laptop })
                {
                    DevicesPage.SelectDevice("blade");
                }
                else if (step.Device is { } device)
                {
                    await DevicesPage.ShowDeviceAsync(device.Source);
                }

                DevicesPage.ScrollToTop();
                return step.Device is { Category: DeviceCategory.Laptop }
                    ? DevicesPage.BladeIntroductionAnchor
                    : DevicesPage.IntroductionDeviceAnchor;
            default:
                RootNavigationView.SelectedItem = DevicesNavigationItem;
                DevicesPage.ScrollToTop();
                return DevicesPage.NoDevicesAnchor;
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

    private TeachingTipPlacementMode ChooseIntroductionPlacement(FrameworkElement target)
    {
        var position = target.TransformToVisual(RootLayout)
            .TransformPoint(new global::Windows.Foundation.Point(0, 0));
        const double estimatedTipHeight = 240;
        const double margin = 12;
        return position.Y >= estimatedTipHeight + margin
            ? TeachingTipPlacementMode.Top
            : TeachingTipPlacementMode.Bottom;
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
        _introductionSteps = null;
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
            TryPromptForStartupUpdate();
            return;
        }

        _introductionPendingAfterLaunch = false;
        _ = ShowIntroductionStepAsync(0);
    }

    private void TryPromptForStartupUpdate()
    {
        if (!_startupPromptPending || _introductionPendingAfterLaunch ||
            _introductionTransitioning ||
            _introductionStep >= 0 || !AppWindow.IsVisible)
        {
            return;
        }

        _startupPromptPending = false;
        _ = SettingsPage.PromptForStartupUpdateAsync();
    }

    private void RefreshIntroductionLocalization()
    {
        if (_introductionStep < 0 ||
            _introductionSteps is not { } steps ||
            _introductionStep >= steps.Count)
        {
            return;
        }

        var step = steps[_introductionStep];
        IntroductionTip.Title = AppStrings.Text(step.TitleKey);
        IntroductionBodyText.Text = AppStrings.Text(step.BodyKey);
        IntroductionNextButton.Content = _introductionStep == steps.Count - 1
            ? AppStrings.Text("Text_1E13BD21")
            : AppStrings.Text("Text_10C83C81");
    }

    private enum IntroductionStepKind
    {
        Navigation,
        DeviceCards,
        DeviceControls,
        NoDevices,
    }

    private sealed record IntroductionStep(
        IntroductionStepKind Kind,
        string TitleKey,
        string BodyKey,
        ConnectedDeviceRowViewModel? Device = null);
}
