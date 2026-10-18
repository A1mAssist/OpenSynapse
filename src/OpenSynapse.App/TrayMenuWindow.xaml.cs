using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
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
    private const uint WmSysCommand = 0x0112;
    private const nuint ScMonitorPower = 0xF170;
    private const nint MonitorPowerOff = 2;
    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndBroadcast = new(0xFFFF);
    private readonly IntPtr _windowHandle;
    private bool _hostReady;
    private bool _showPending;
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
        MenuRoot.RequestedTheme = ElementTheme.Dark;
        RefreshLocalization();
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

    public event Action? ShowRequested;
    public event Action? ExitRequested;
    public event Action<string>? NavigationRequested;

    public void RefreshLocalization()
    {
        Localized.RefreshTree(MenuRoot);
    }

    public void ShowAt(int x, int y)
    {
        if (_closing) return;
        if (!_hostReady)
        {
            _queuedShow = (x, y);
            return;
        }
        if (_showPending || _menuOpen) return;

        _showPending = true;
        MenuRoot.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var scale = GetDpiForWindow(_windowHandle) / 96d;
        if (scale <= 0) scale = 1;
        var desired = MenuRoot.DesiredSize;
        var width = Math.Max(1, (int)Math.Ceiling((desired.Width > 0 ? desired.Width : 240) * scale));
        var height = Math.Max(1, (int)Math.Ceiling((desired.Height > 0 ? desired.Height : 280) * scale));
        var workArea = DisplayArea.GetFromPoint(
            new PointInt32(x, y),
            DisplayAreaFallback.Primary).WorkArea;
        var anchorX = Math.Clamp(x, workArea.X, workArea.X + workArea.Width - AnchorSize);
        var anchorY = Math.Clamp(y, workArea.Y, workArea.Y + workArea.Height - AnchorSize);
        var left = anchorX + width <= workArea.X + workArea.Width ? anchorX : anchorX - width;
        var top = anchorY - height;
        if (top < workArea.Y) top = anchorY;
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
        if (sender is Microsoft.UI.Xaml.Controls.Button { Tag: string page })
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
        if (!_menuOpen) return;
        HideMenu();
    }

    private void HideMenu()
    {
        _menuOpen = false;
        _showPending = false;
        RemoveHooks();
        SetLayeredWindowAttributes(_windowHandle, 0, 0, LwaAlpha);
        AppWindow.Hide();
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
        _keyboardHook = SetWindowsHookEx(WhKeyboardLl, _keyboardHookProc, GetModuleHandle(null), 0);
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
        IntPtr windowHandle, uint colorKey, byte alpha, uint flags);

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

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(
        int left, int top, int right, int bottom, int width, int height);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr windowHandle, IntPtr region, bool redraw);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr gdiObject);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(
        IntPtr windowHandle, uint message, nuint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
