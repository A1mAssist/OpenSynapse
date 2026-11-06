using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Storage;
using Windows.Graphics;

namespace OpenSynapse.App;

public sealed partial class PerformanceOverlayWindow : Window
{
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const long WsDlgFrame = 0x00400000;
    private const long WsBorder = 0x00800000;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExLayer = 0x00080000;
    private const long WsExWindowEdge = 0x00000100;
    private const long WsExStaticEdge = 0x00020000;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private const int HwndTopmost = -1;
    private const int WmNcLButtonDown = 0x00A1;
    private const int HtCaption = 2;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmWindowCornerRound = 2;
    private const int DwmBorderColor = 34;
    private const int DwmBorderColorDark = 0x001A1A1A;
    private const uint LwaAlpha = 0x00000002;
    private const byte OverlayAlpha = 232;
    private const double CompactWidthDip = 316;
    private const double CompactHeightDip = 104;
    private const double DetailedWidthDip = 420;
    private const double DetailedHeightDip = 160;
    private const double ScreenMarginDip = 16;
    private const int SparklineSamples = 24;
    private const double SparklineWidthDip = 178;
    private const double SparklineHeightDip = 70;
    private readonly MainViewModel _viewModel;
    private readonly AppBehaviorSettings _settings;
    private readonly IntPtr _windowHandle;
    private readonly Queue<double> _cpuHistory = new();
    private readonly Queue<double> _gpuHistory = new();
    private bool _closed;

    public PerformanceOverlayWindow(MainViewModel viewModel, AppBehaviorSettings settings)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DragRegion.DataContext = viewModel;
        _settings = settings;
        _viewModel.Blade.PropertyChanged += OnBladeChanged;
        _viewModel.Telemetry.PropertyChanged += OnTelemetryChanged;
        AppWindow.IsShownInSwitchers = false;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var extendedStyle = GetWindowLongPtr(_windowHandle, GwlExStyle).ToInt64();
        extendedStyle &= ~(WsExWindowEdge | WsExStaticEdge);
        SetWindowLongPtr(_windowHandle, GwlExStyle, new IntPtr(extendedStyle | WsExToolWindow | WsExLayer));
        var style = GetWindowLongPtr(_windowHandle, GwlStyle).ToInt64();
        style &= ~(WsDlgFrame | WsBorder);
        SetWindowLongPtr(_windowHandle, GwlStyle, new IntPtr(style));
        _ = SetWindowPos(
            _windowHandle,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
        _ = SetLayeredWindowAttributes(_windowHandle, 0, OverlayAlpha, LwaAlpha);
        var round = DwmWindowCornerRound;
        _ = DwmSetWindowAttribute(_windowHandle, DwmWindowCornerPreference, ref round, sizeof(int));
        var borderColor = DwmBorderColorDark;
        _ = DwmSetWindowAttribute(_windowHandle, DwmBorderColor, ref borderColor, sizeof(int));
        SeedHistory(_cpuHistory, _viewModel.Telemetry.CpuPercent);
        SeedHistory(_gpuHistory, _viewModel.Telemetry.GpuPercent);
        RefreshLocalization();
        UpdateModeIcon();
        ApplyLayout(_settings.PerformanceOverlayDetailed);
        Closed += OnClosed;
    }

    internal void RefreshLayout() => ApplyLayout(_settings.PerformanceOverlayDetailed);

    internal void RefreshLocalization()
    {
        Title = AppStrings.Text("PerformanceOverlayTitle");
        var fanLabel = AppStrings.Text("PerformanceOverlayFanLabel");
        CompactFanLabel.Text = fanLabel;
        DetailedFanLabel.Text = fanLabel;
        AutomationProperties.SetName(DragRegion, Title);
    }

    private void ApplyLayout(bool detailed)
    {
        if (IsWindowVisible(_windowHandle) && GetWindowRect(_windowHandle, out var current))
        {
            _settings.PerformanceOverlayX = current.Left;
            _settings.PerformanceOverlayY = current.Top;
        }

        CompactCard.Visibility = detailed ? Visibility.Collapsed : Visibility.Visible;
        DetailedCard.Visibility = detailed ? Visibility.Visible : Visibility.Collapsed;
        PositionOnDisplay();
        UpdateSparklines();
    }

    private void OnBladeChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(BladeWorkspace.PerformanceModeIndex) or
            nameof(BladeWorkspace.PerformanceModeCards))
        {
            UpdateModeIcon();
        }
    }

    private void OnTelemetryChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(TelemetryWorkspace.TelemetryTimeText)) return;
        AddSample(_cpuHistory, _viewModel.Telemetry.CpuPercent);
        AddSample(_gpuHistory, _viewModel.Telemetry.GpuPercent);
        UpdateSparklines();
    }

    private void UpdateModeIcon()
    {
        var cards = _viewModel.Blade.PerformanceModeCards;
        var index = _viewModel.Blade.PerformanceModeIndex;
        var glyph = index >= 0 && index < cards.Count
            ? cards[index].IconGlyph
            : "\uE945";
        CompactModeIcon.Glyph = glyph;
        DetailedModeIcon.Glyph = glyph;
    }

    private static void SeedHistory(Queue<double> samples, double value)
    {
        samples.Clear();
        var sample = Math.Clamp(value, 0, 100);
        for (var index = 0; index < SparklineSamples; index++)
        {
            samples.Enqueue(sample);
        }
    }

    private static void AddSample(Queue<double> samples, double value)
    {
        samples.Enqueue(Math.Clamp(value, 0, 100));
        while (samples.Count > SparklineSamples)
        {
            samples.Dequeue();
        }
    }

    private void UpdateSparklines()
    {
        CpuSparkline.Points = BuildPoints(_cpuHistory);
        GpuSparkline.Points = BuildPoints(_gpuHistory);
        CpuArea.Points = BuildAreaPoints(_cpuHistory);
        GpuArea.Points = BuildAreaPoints(_gpuHistory);
    }

    private static PointCollection BuildPoints(IReadOnlyCollection<double> samples)
    {
        var points = new PointCollection();
        if (samples.Count == 0) return points;

        var step = SparklineWidthDip / Math.Max(1, SparklineSamples - 1);
        var start = SparklineWidthDip - (samples.Count - 1) * step;
        var index = 0;
        foreach (var sample in samples)
        {
            var y = SparklineHeightDip - Math.Clamp(sample, 0, 100) / 100 * SparklineHeightDip;
            points.Add(new global::Windows.Foundation.Point(start + index * step, y));
            index++;
        }

        return points;
    }

    private static PointCollection BuildAreaPoints(IReadOnlyCollection<double> samples)
    {
        var points = BuildPoints(samples);
        if (points.Count == 0) return points;

        var last = points[points.Count - 1];
        var first = points[0];
        points.Add(new global::Windows.Foundation.Point(last.X, SparklineHeightDip));
        points.Add(new global::Windows.Foundation.Point(first.X, SparklineHeightDip));
        return points;
    }

    private void PositionOnDisplay()
    {
        var dpi = GetDpiForWindow(_windowHandle);
        var scale = dpi > 0 ? dpi / 96d : 1d;
        var widthDip = _settings.PerformanceOverlayDetailed ? DetailedWidthDip : CompactWidthDip;
        var heightDip = _settings.PerformanceOverlayDetailed ? DetailedHeightDip : CompactHeightDip;
        var width = Math.Max(1, (int)Math.Round(widthDip * scale));
        var height = Math.Max(1, (int)Math.Round(heightDip * scale));
        var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        var workArea = display.WorkArea;
        var x = _settings.PerformanceOverlayX ??
            workArea.X + workArea.Width - width - (int)Math.Round(ScreenMarginDip * scale);
        var y = _settings.PerformanceOverlayY ??
            workArea.Y + (int)Math.Round(ScreenMarginDip * scale);
        workArea = DisplayArea.GetFromPoint(
            new PointInt32(x, y),
            DisplayAreaFallback.Primary).WorkArea;
        x = Math.Clamp(x, workArea.X, Math.Max(workArea.X, workArea.X + workArea.Width - width));
        y = Math.Clamp(y, workArea.Y, Math.Max(workArea.Y, workArea.Y + workArea.Height - height));
        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
        _ = SetWindowPos(
            _windowHandle,
            new IntPtr(HwndTopmost),
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder);
    }

    private void DragRegionPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(DragRegion);
        if (!point.Properties.IsLeftButtonPressed) return;
        ReleaseCapture();
        _ = SendMessage(_windowHandle, WmNcLButtonDown, new IntPtr(HtCaption), IntPtr.Zero);
        e.Handled = true;
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        if (_closed) return;
        _closed = true;
        _viewModel.Blade.PropertyChanged -= OnBladeChanged;
        _viewModel.Telemetry.PropertyChanged -= OnTelemetryChanged;
        if (!GetWindowRect(_windowHandle, out var rect)) return;
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        var workArea = DisplayArea.GetFromPoint(
            new PointInt32(rect.Left, rect.Top),
            DisplayAreaFallback.Primary).WorkArea;
        _settings.PerformanceOverlayX = Math.Clamp(
            rect.Left,
            workArea.X,
            Math.Max(workArea.X, workArea.X + workArea.Width - width));
        _settings.PerformanceOverlayY = Math.Clamp(
            rect.Top,
            workArea.Y,
            Math.Max(workArea.Y, workArea.Y + workArea.Height - height));
        try
        {
            _settings.Save();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            InvalidOperationException or System.Security.SecurityException)
        {
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(
        IntPtr window,
        uint colorKey,
        byte alpha,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr window, int attribute, ref int value, int size);
}