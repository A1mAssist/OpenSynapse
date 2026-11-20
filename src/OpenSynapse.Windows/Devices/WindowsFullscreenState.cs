using System.Runtime.InteropServices;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Detects an application that owns the whole screen. QUNS covers exclusive
/// Direct3D and presentation mode; the foreground window geometry covers the
/// borderless fullscreen games that QUNS never reports.
/// </summary>
public static class WindowsFullscreenState
{
    private const int QunsBusy = 2;
    private const int QunsRunningD3dFullScreen = 3;
    private const int QunsPresentationMode = 4;
    private const int GwlStyle = -16;
    private const long WsCaption = 0x00C00000;
    private const int DwmwaExtendedFrameBounds = 9;
    private const int DwmwaCloaked = 14;
    private const uint MonitorDefaultToNearest = 2;

    public static bool IsFullscreen() => IsFullscreen(out _);

    public static bool IsFullscreen(out string source)
    {
        source = "none";
        if (SHQueryUserNotificationState(out var state) == 0 &&
            state is QunsBusy or QunsRunningD3dFullScreen or QunsPresentationMode)
        {
            source = "quns";
            return true;
        }

        var window = GetForegroundWindow();
        if (window == IntPtr.Zero)
        {
            return false;
        }

        _ = GetWindowThreadProcessId(window, out var processId);
        if (processId == 0 || processId == (uint)Environment.ProcessId)
        {
            return false;
        }

        // The desktop and every taskbar surface are full-screen rectangles too.
        var shellWindow = GetShellWindow();
        if (shellWindow != IntPtr.Zero && window == shellWindow ||
            IsShellClass(window))
        {
            return false;
        }

        if (DwmGetWindowAttribute(window, DwmwaCloaked, out int cloaked, sizeof(int)) == 0 &&
            cloaked != 0)
        {
            return false;
        }

        var style = GetWindowLongPtrW(window, GwlStyle).ToInt64();
        if ((style & WsCaption) != 0)
        {
            return false;
        }

        if (!TryGetWindowBounds(window, out var bounds))
        {
            return false;
        }

        var monitor = MonitorFromWindow(window, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfoW(monitor, ref info))
        {
            return false;
        }

        if (!IsFullscreenGeometry(false, bounds, info.Monitor.ToFullscreenRect()))
        {
            return false;
        }

        source = "geometry";
        return true;
    }

    internal readonly record struct FullscreenRect(int Left, int Top, int Right, int Bottom)
    {
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    internal static bool IsFullscreenGeometry(
        bool hasCaption,
        FullscreenRect window,
        FullscreenRect monitor,
        int tolerance = 2)
    {
        if (hasCaption || window.Width <= 0 || window.Height <= 0 ||
            monitor.Width <= 0 || monitor.Height <= 0)
        {
            return false;
        }

        return window.Left <= monitor.Left + tolerance &&
            window.Top <= monitor.Top + tolerance &&
            window.Right >= monitor.Right - tolerance &&
            window.Bottom >= monitor.Bottom - tolerance;
    }

    private static bool IsShellClass(IntPtr window)
    {
        var name = new char[64];
        var length = GetClassNameW(window, name, name.Length);
        if (length <= 0)
        {
            return false;
        }

        var className = new string(name, 0, length);
        return className is "Progman" or "WorkerW" or "Shell_TrayWnd";
    }

    private static bool TryGetWindowBounds(IntPtr window, out FullscreenRect bounds)
    {
        if (DwmGetWindowAttribute(
                window,
                DwmwaExtendedFrameBounds,
                out NativeRect frame,
                Marshal.SizeOf<NativeRect>()) == 0 &&
            frame.Right > frame.Left &&
            frame.Bottom > frame.Top)
        {
            bounds = frame.ToFullscreenRect();
            return true;
        }

        if (GetWindowRect(window, out var rect))
        {
            bounds = rect.ToFullscreenRect();
            return true;
        }

        bounds = default;
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;

        internal readonly FullscreenRect ToFullscreenRect() => new(Left, Top, Right, Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        internal int Size;
        internal NativeRect Monitor;
        internal NativeRect Work;
        internal uint Flags;
    }

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr window, [Out] char[] className, int maxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowLongPtrW(IntPtr window, int index);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        IntPtr window,
        int attribute,
        out NativeRect value,
        int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        IntPtr window,
        int attribute,
        out int value,
        int size);
}
