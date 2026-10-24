using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.Graphics;

namespace OpenSynapse.App;

public sealed partial class TrayMenuWindow : Window
{
    private const int AnchorSize = 2;
    private const int GwlExStyle = -20;
    private const long WsExLayered = 0x00080000;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExNoActivate = 0x08000000;
    private const uint LwaAlpha = 0x00000002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private const uint WmSysCommand = 0x0112;
    private const nuint ScMonitorPower = 0xF170;
    private const nint MonitorPowerOff = 2;
    private const int WhMouseLl = 14;
    private const int WhKeyboardLl = 13;
    private const uint WmLButtonDown = 0x0201;
    private const uint WmRButtonDown = 0x0204;
    private const uint WmMButtonDown = 0x0207;
    private const uint WmKeyDown = 0x0100;
    private const uint WmSysKeyDown = 0x0104;
    private const int VkEscape = 0x1B;
    private const uint GaRoot = 2;
    private const int CornerRadius = 8;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmWindowCornerDoNotRound = 1;
    private const int DwmBorderColor = 34;
    private const int DwmColorNone = unchecked((int)0xFFFFFFFE);
    private const double PanelContentHeightDip = 5 * 32 + 2 * 5 + 8;
    // Button margin + padding + the text's left offset. The icon sits inside that offset;
    // adding the icon width again makes the panel too wide and leaves a heavy right gap.
    private const double PanelLeadingDip = 3 + 4 + 28;
    private const double PanelTrailingDip = 3 + 4 + 8;
    private const double MinPanelWidthDip = 120;
    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndBroadcast = new(0xFFFF);
    private readonly IntPtr _windowHandle;
    private readonly LocalDiagnosticLog _diagnosticLog = new();
    private bool _hostReady;
    private bool _showPending;
    private bool _prerendered;
    private bool _menuOpen;
    private bool _closing;
    private (int X, int Y)? _queuedShow;
    private IntPtr _mouseHook;
    private IntPtr _keyboardHook;
    private LowLevelMouseProc? _mouseHookProc;
    private LowLevelKeyboardProc? _keyboardHookProc;

    public TrayMenuWindow()
    {
        InitializeComponent();
        RefreshLocalization();
        MenuRoot.RequestedTheme = ElementTheme.Dark;
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

        DisableDwmFrame();
        var extendedStyle = GetWindowLongPtr(_windowHandle, GwlExStyle).ToInt64();
        SetWindowLongPtr(
            _windowHandle,
            GwlExStyle,
            new IntPtr(extendedStyle | WsExLayered | WsExToolWindow | WsExNoActivate));
        SetLayeredWindowAttributes(_windowHandle, 0, 0, LwaAlpha);

        Closed += (_, _) => RemoveHooks();

        // Load the panel now, but keep the host invisible until the first show.
        SetWindowPos(
            _windowHandle,
            HwndTopmost,
            0,
            0,
            1,
            1,
            SwpNoActivate | SwpShowWindow | SwpNoOwnerZOrder);
        _ = MenuRoot.DispatcherQueue.TryEnqueue(() =>
        {
            _hostReady = true;
            AppWindow.Hide();
            if (_queuedShow is { } queued)
            {
                _queuedShow = null;
                ShowAt(queued.X, queued.Y);
            }
        });
    }
    // The labels stretch to the panel, so their DesiredSize is the panel width, not the
    // text width. Measure the longest string off-tree instead.
    private readonly TextBlock _widthProbe = new()
    {
        FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Microsoft YaHei UI,Segoe UI"),
        FontSize = 14,
    };

    private double MeasurePanelWidthDip()
    {
        var widest = 0d;
        foreach (var label in new[] { OpenMainPanelText, DevicesText, ProfilesText, TurnOffDisplayText, ExitText })
        {
            _widthProbe.Text = label.Text;
            _widthProbe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            widest = Math.Max(widest, _widthProbe.DesiredSize.Width);
        }

        // Device names are never abbreviated, so the longest one sets the width.
        _widthProbe.FontSize = 13;
        foreach (var name in _batteryNames)
        {
            _widthProbe.Text = name.Text;
            _widthProbe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            widest = Math.Max(widest, 32 + _widthProbe.DesiredSize.Width + BatteryValueWidthDip);
        }
        _widthProbe.FontSize = 14;

        return Math.Max(MinPanelWidthDip, Math.Ceiling(PanelLeadingDip + widest + PanelTrailingDip));
    }

    private double MeasureContentHeightDip()
    {
        // Counted, not measured: MenuRoot stretches to the client area, so DesiredSize
        // would feed the previous window size back in.
        var separators = _batteryValues.Count > 0 ? 3 : 2;
        return 5 * 32 + separators * 5 + 8 + _batteryValues.Count * BatteryRowHeightDip;
    }

    /// <summary>
    /// Fills the battery rows from discovery, so the names are on screen before any
    /// percentage arrives. Rebuilt per open; values start as a placeholder.
    /// </summary>
    private void PopulateBatteryRows(IReadOnlyList<PeripheralBattery> devices)
    {
        BatteryPanel.Children.Clear();
        _batteryNames.Clear();
        _batteryValues.Clear();
        _batteryRows.Clear();
        foreach (var device in devices)
        {
            var icon = new Viewbox
            {
                Width = 16,
                Height = 16,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new FontIcon
                {
                    Glyph = device.Glyph,
                    FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Application.Current.Resources["IconFontFamily"],
                    FontSize = 14,
                },
            };
            var name = new TextBlock
            {
                Text = device.Name,
                FontSize = 13,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Microsoft YaHei UI,Segoe UI"),
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var value = new TextBlock
            {
                Text = device.Percent is int percent ? $"{percent}%" : "\u2026",
                FontSize = 13,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono,Consolas"),
                MinWidth = 34,
                Margin = new Thickness(0, 0, 8, 0),
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var layout = new Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            layout.Children.Add(icon);
            Grid.SetColumn(name, 1);
            layout.Children.Add(name);
            Grid.SetColumn(value, 2);
            layout.Children.Add(value);

            // The row is a menu item, so clicking it opens that device.
            var row = new Button
            {
                Style = (Style)MenuRoot.Resources["TrayMenuButtonStyle"],
                Height = BatteryRowHeightDip,
                Tag = device.Source,
                Content = layout,
            };
            AutomationProperties.SetName(row, $"{device.Name} {value.Text}");
            row.Click += BatteryRowClick;
            BatteryPanel.Children.Add(row);
            _batteryNames.Add(name);
            _batteryValues.Add(value);
            _batteryRows.Add(row);
        }

        var visibility = devices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        BatteryPanel.Visibility = visibility;
        BatterySeparator.Visibility = visibility;
    }

    private void BatteryRowClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: { } source })
        {
            DismissMenu();
            DeviceRequested?.Invoke(source);
        }
    }

    /// <summary>
    /// Reads the batteries once per open. The panel is already on screen, so this only
    /// fills values in; it never delays the menu.
    /// </summary>
    private async Task RefreshBatteryAsync()
    {
        if (BatteryReader is null || _batteryValues.Count == 0)
        {
            return;
        }

        _batteryRead?.Cancel();
        var cancellation = new CancellationTokenSource();
        _batteryRead = cancellation;
        IReadOnlyList<PeripheralBattery>? results = null;
        try
        {
            results = await BatteryReader(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            // Leave results null: every row falls back to the no-answer mark.
        }

        MenuRoot.DispatcherQueue.TryEnqueue(() =>
        {
            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            for (var index = 0; index < _batteryValues.Count; index++)
            {
                var percent = results is not null && index < results.Count
                    ? results[index].Percent
                    : null;
                var text = percent is int value ? $"{value}%" : "\u2014";
                _batteryValues[index].Text = text;
                AutomationProperties.SetName(_batteryRows[index], $"{_batteryNames[index].Text} {text}");
            }
        });
    }



    public event Action? ShowRequested;
    public event Action? ExitRequested;
    public event Action<string>? NavigationRequested;

    public event Action<object>? DeviceRequested;

    /// <summary>Names of the battery-capable peripherals, known before any read.</summary>
    public Func<IReadOnlyList<PeripheralBattery>>? BatterySnapshot { get; set; }

    /// <summary>Reads those batteries. Called only while the menu is opening.</summary>
    public Func<CancellationToken, Task<IReadOnlyList<PeripheralBattery>>>? BatteryReader { get; set; }

    private const double BatteryRowHeightDip = 28;
    private const double BatteryValueWidthDip = 44;
    private readonly List<TextBlock> _batteryNames = [];
    private readonly List<TextBlock> _batteryValues = [];
    private readonly List<Button> _batteryRows = [];
    private CancellationTokenSource? _batteryRead;

    public void RefreshLocalization()
    {
        Localized.Refresh(OpenMainPanelText);
        Localized.Refresh(DevicesText);
        Localized.Refresh(ProfilesText);
        Localized.Refresh(TurnOffDisplayText);
        Localized.Refresh(ExitText);
        AutomationProperties.SetName(OpenMainPanelMenuItem, OpenMainPanelText.Text);
        AutomationProperties.SetName(DevicesMenuItem, DevicesText.Text);
        AutomationProperties.SetName(ProfilesMenuItem, ProfilesText.Text);
        AutomationProperties.SetName(TurnOffDisplayMenuItem, TurnOffDisplayText.Text);
        AutomationProperties.SetName(ExitMenuItem, ExitText.Text);
    }

    public void ShowAt(int x, int y)
    {
        if (_closing)
        {
            return;
        }
        if (!_hostReady)
        {
            _queuedShow = (x, y);
            return;
        }
        if (_showPending || _menuOpen)
        {
            return;
        }

        _showPending = true;
        PopulateBatteryRows(BatterySnapshot?.Invoke() ?? []);
        SetLayeredWindowAttributes(_windowHandle, 0, 0, LwaAlpha);
        if (!SetWindowPos(
                _windowHandle,
                HwndTopmost,
                x,
                y,
                1,
                1,
                SwpNoActivate | SwpShowWindow | SwpNoOwnerZOrder))
        {
            _showPending = false;
            HideMenu();
            return;
        }

        // MenuRoot stretches to the client area, so DesiredSize would feed the previous
        // window size back in and grow every time. Count the fixed rows instead.
        var scale = MenuRoot.XamlRoot?.RasterizationScale ?? GetDpiForWindow(_windowHandle) / 96d;
        if (scale <= 0)
        {
            scale = 1;
        }
        var desired = new Size(MeasurePanelWidthDip(), MeasureContentHeightDip());

        // SetWindowPos takes the outer frame while XAML lays out the smaller client area.
        GetWindowRect(_windowHandle, out var frameWindow);
        GetClientRect(_windowHandle, out var frameClient);
        var frameX = Math.Max(0, (frameWindow.Right - frameWindow.Left) -
                                (frameClient.Right - frameClient.Left));
        var frameY = Math.Max(0, (frameWindow.Bottom - frameWindow.Top) -
                                (frameClient.Bottom - frameClient.Top));
        var width = Math.Max(1, (int)Math.Ceiling(desired.Width * scale) + frameX);
        var height = Math.Max(1, (int)Math.Ceiling(desired.Height * scale) + frameY);
        var workArea = DisplayArea.GetFromPoint(
            new PointInt32(x, y),
            DisplayAreaFallback.Primary).WorkArea;
        var anchorX = Math.Clamp(x, workArea.X, workArea.X + workArea.Width - AnchorSize);
        var anchorY = Math.Clamp(y, workArea.Y, workArea.Y + workArea.Height - AnchorSize);
        var left = anchorX + width <= workArea.X + workArea.Width ? anchorX : anchorX - width;
        var top = anchorY - height;
        if (top < workArea.Y)
        {
            top = anchorY;
        }
        left = Math.Clamp(left, workArea.X, Math.Max(workArea.X, workArea.X + workArea.Width - width));
        top = Math.Clamp(top, workArea.Y, Math.Max(workArea.Y, workArea.Y + workArea.Height - height));

        SetLayeredWindowAttributes(_windowHandle, 0, 0, LwaAlpha);
        if (!SetWindowPos(
                _windowHandle,
                HwndTopmost,
                left,
                top,
                width,
                height,
                SwpNoActivate | SwpShowWindow | SwpNoOwnerZOrder))
        {
            _showPending = false;
            HideMenu();
            return;
        }

        ApplyRoundedRegion(width, height, scale);
        SetLayeredWindowAttributes(_windowHandle, 0, 255, LwaAlpha);
        MenuRoot.UpdateLayout();
        _menuOpen = true;
        _showPending = false;
        InstallHooks();
        // The first show is the offscreen pre-render pass, not a real animation.
        StartOpenAnimation(animateFrom: _prerendered);
        _prerendered = true;
        _ = RefreshBatteryAsync();
    }


    // Mirrors the system flyout: a short fade plus a rise out of the tray anchor.
    private const double OpenDurationMs = 110;
    private const double RiseDistanceDip = 8;
    private DispatcherQueueTimer? _openTimer;

    private void StartOpenAnimation(bool animateFrom)
    {
        if (!animateFrom)
        {
            MenuRoot.Opacity = 1d;
            MenuRoot.Translation = default;
            return;
        }

        _openTimer?.Stop();
        MenuRoot.Opacity = 0d;
        MenuRoot.Translation = new System.Numerics.Vector3(0f, (float)RiseDistanceDip, 0f);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _openTimer = MenuRoot.DispatcherQueue.CreateTimer();
        _openTimer.IsRepeating = true;
        _openTimer.Tick += (_, _) =>
        {
            if (_closing)
            {
                _openTimer?.Stop();
                return;
            }

            var t = Math.Clamp(stopwatch.Elapsed.TotalMilliseconds / OpenDurationMs, 0d, 1d);
            var eased = 1d - Math.Pow(1d - t, 3d);
            MenuRoot.Opacity = eased;
            MenuRoot.Translation = new System.Numerics.Vector3(
                0f,
                (float)(RiseDistanceDip * (1d - eased)),
                0f);
            if (t < 1d)
            {
                return;
            }

            ResetOpenAnimation();
        };
        _openTimer.Start();
    }

    private void ResetOpenAnimation()
    {
        _openTimer?.Stop();
        _openTimer = null;
        MenuRoot.Opacity = 1d;
        MenuRoot.Translation = default;
    }
    public void CloseMenuHost()
    {
        _closing = true;
        DismissMenu();
        Close();
    }

    private void OpenMainPanelClick(object sender, RoutedEventArgs e)
    {
        DismissMenu();
        ShowRequested?.Invoke();
    }

    private void NavigateClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string page })
        {
            DismissMenu();
            NavigationRequested?.Invoke(page);
        }
    }

    private void TurnOffDisplayClick(object sender, RoutedEventArgs e)
    {
        DismissMenu();
        PostMessage(HwndBroadcast, WmSysCommand, ScMonitorPower, MonitorPowerOff);
    }

    private void ExitClick(object sender, RoutedEventArgs e)
    {
        DismissMenu();
        ExitRequested?.Invoke();
    }

    private void DismissMenu()
    {
        if (!_menuOpen && !_showPending)
        {
            return;
        }
        HideMenu();
    }

    private void HideMenu()
    {
        _openTimer?.Stop();
        _menuOpen = false;
        _showPending = false;
        _batteryRead?.Cancel();
        RemoveHooks();
        SetLayeredWindowAttributes(_windowHandle, 0, 0, LwaAlpha);
        AppWindow.Hide();
    }

    private void DisableDwmFrame()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return;
        }

        // DWM paints a border outside the XAML content, so XAML CornerRadius cannot remove it.
        var noBorder = DwmColorNone;
        _ = DwmSetWindowAttribute(_windowHandle, DwmBorderColor, ref noBorder, sizeof(int));
        var doNotRound = DwmWindowCornerDoNotRound;
        _ = DwmSetWindowAttribute(
            _windowHandle, DwmWindowCornerPreference, ref doNotRound, sizeof(int));
    }

    private void ApplyRoundedRegion(int width, int height, double scale)
    {
        GetWindowRect(_windowHandle, out var window);
        GetClientRect(_windowHandle, out var client);
        var origin = new NativePoint();
        ClientToScreen(_windowHandle, ref origin);
        var left = Math.Max(0, origin.X - window.Left);
        var top = Math.Max(0, origin.Y - window.Top);
        var clientWidth = Math.Max(0, client.Right - client.Left);
        var clientHeight = Math.Max(0, client.Bottom - client.Top);
        if (clientWidth == 0 || clientHeight == 0)
        {
            left = 0;
            top = 0;
            clientWidth = width;
            clientHeight = height;
        }
        var radius = Math.Max(1, (int)Math.Round(CornerRadius * scale));
        var region = CreateRoundRectRgn(
            left, top, left + clientWidth + 1, top + clientHeight + 1, radius * 2, radius * 2);
        if (region != IntPtr.Zero && SetWindowRgn(_windowHandle, region, true) == 0)
        {
            DeleteObject(region);
        }
    }

    private void InstallHooks()
    {
        RemoveHooks();
        _mouseHookProc = MouseHookProc;
        _keyboardHookProc = KeyboardHookProc;
        _mouseHook = SetWindowsHookEx(WhMouseLl, _mouseHookProc, GetModuleHandle(null), 0);
        if (_mouseHook == IntPtr.Zero)
        {
            _diagnosticLog.TryWrite(
                "tray-menu",
                $"mouse hook installation failed: {Marshal.GetLastWin32Error()}");
        }
        _keyboardHook = SetWindowsHookEx(WhKeyboardLl, _keyboardHookProc, GetModuleHandle(null), 0);
        if (_keyboardHook == IntPtr.Zero)
        {
            _diagnosticLog.TryWrite(
                "tray-menu",
                $"keyboard hook installation failed: {Marshal.GetLastWin32Error()}");
        }
    }

    private IntPtr MouseHookProc(int code, IntPtr message, IntPtr data)
    {
        var notification = (uint)message.ToInt64();
        if (_menuOpen && code >= 0 &&
            (notification == WmLButtonDown ||
             notification == WmRButtonDown ||
             notification == WmMButtonDown))
        {
            var point = Marshal.PtrToStructure<MouseHookData>(data).Point;
            var target = WindowFromPoint(point);
            var root = GetAncestor(target, GaRoot);
            if (root != _windowHandle)
            {
                MenuRoot.DispatcherQueue.TryEnqueue(DismissMenu);
            }
        }
        return CallNextHookEx(_mouseHook, code, message, data);
    }

    private IntPtr KeyboardHookProc(int code, IntPtr message, IntPtr data)
    {
        var notification = (uint)message.ToInt64();
        if (_menuOpen && code >= 0 &&
            (notification == WmKeyDown || notification == WmSysKeyDown) &&
            Marshal.PtrToStructure<KeyboardHookData>(data).VirtualKey == VkEscape)
        {
            MenuRoot.DispatcherQueue.TryEnqueue(DismissMenu);
            return new IntPtr(1);
        }
        return CallNextHookEx(_keyboardHook, code, message, data);
    }

    private void RemoveHooks()
    {
        if (_mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
        if (_keyboardHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }
        _mouseHookProc = null;
        _keyboardHookProc = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseHookData
    {
        public NativePoint Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardHookData
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr LowLevelMouseProc(int code, IntPtr message, IntPtr data);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr LowLevelKeyboardProc(int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr windowHandle, int index);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr windowHandle, int index, IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(
        IntPtr windowHandle,
        uint colorKey,
        byte alpha,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr windowHandle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr windowHandle, out WindowRect bounds);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr windowHandle, out WindowRect bounds);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr windowHandle, ref NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr windowHandle, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int hookType, Delegate callback, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(
        IntPtr hook, int code, IntPtr message, IntPtr data);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr windowHandle, int attribute, ref int value, int size);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(
        int left, int top, int right, int bottom, int width, int height);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr windowHandle, IntPtr region, bool redraw);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr gdiObject);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(
        IntPtr windowHandle, uint message, nuint wParam, nint lParam);
}

