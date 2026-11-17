using System.Runtime.InteropServices;
using Microsoft.Win32;
using OpenSynapse.Core.Sensors;
using static OpenSynapse.Windows.Lifecycle.TaskbarStatusNative;

namespace OpenSynapse.Windows.Lifecycle;

/// <summary>
/// A small GDI popup owned by the taskbar. The host window stays alive when
/// Explorer restarts; the popup is recreated and reattached by the host timer.
/// </summary>
public sealed partial class TaskbarStatusWindow : IDisposable
{
    private const uint HostTimerId = 1;
    private const uint HostTimerInterval = 250;
    private const int BaseWidthDip = 64;
    private const int MaximumWidthDip = 240;
    private const int TargetHeightDip = 30;
    private const int MinimumHeightDip = 22;
    private const int MaximumHeightDip = 36;
    private const int GapDip = 6;
    private const int PaddingDip = 8;
    private const int BatteryIconGapDip = 5;
    private const int DeviceGapDip = 8;
    private const int IconBadgeDip = 18;
    private const int IconBadgeRadiusDip = 5;
    private const int ChargingGapDip = 3;
    private const int BoltWidthDip = 7;
    private const int BoltHeightDip = 12;
    private const int NullPen = 8;

    private readonly Action<string>? _log;
    private readonly WndProc _hostProcedure;
    private readonly WndProc _statusProcedure;
    private readonly string _hostClassName;
    private readonly string _statusClassName;
    private readonly uint _taskbarCreatedMessage;
    private IntPtr _hostWindow;
    private IntPtr _statusWindow;
    private IntPtr _taskbarWindow;
    private IntPtr _batteryFont;
    private IntPtr _iconFont;
    private Rect _lastStatusRect;
    private TaskbarStatusSnapshot _snapshot = new([]);
    private int _visibleBatteryCount;
    private string _automationText = string.Empty;
    private bool _themeDark = true;
    private bool _contentAvailable;
    private bool _disposed;
    private int _dpi = 96;
    private int _layoutFailures;
    private int _attachFailures;
    private long _nextAttachAttempt;

    public TaskbarStatusWindow(Action<string>? log = null)
    {
        _log = log;
        _hostProcedure = HostWindowProcedure;
        _statusProcedure = StatusWindowProcedure;
        _taskbarCreatedMessage = RegisterWindowMessageW("TaskbarCreated");
        var instance = GetModuleHandleW(null);
        var suffix = Guid.NewGuid().ToString("N");
        _hostClassName = $"OpenSynapse.TaskbarStatusHost.{suffix}";
        _statusClassName = $"OpenSynapse.TaskbarStatus.{suffix}";

        var hostClass = CreateWindowClass(_hostClassName, _hostProcedure, instance);
        if (RegisterClassExW(ref hostClass) == 0)
        {
            throw new InvalidOperationException(
                $"taskbar-status: host class registration failed ({Marshal.GetLastWin32Error()}).");
        }

        var statusClass = CreateWindowClass(_statusClassName, _statusProcedure, instance);
        if (RegisterClassExW(ref statusClass) == 0)
        {
            throw new InvalidOperationException(
                $"taskbar-status: status class registration failed ({Marshal.GetLastWin32Error()}).");
        }

        _hostWindow = CreateWindowExW(
            WsExToolWindow | WsExNoActivate,
            _hostClassName,
            string.Empty,
            WsPopup,
            0, 0, 0, 0,
            IntPtr.Zero,
            IntPtr.Zero,
            instance,
            IntPtr.Zero);
        if (_hostWindow == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"taskbar-status: host window creation failed ({Marshal.GetLastWin32Error()}).");
        }

        if (SetTimer(_hostWindow, HostTimerId, HostTimerInterval, IntPtr.Zero) == UIntPtr.Zero)
        {
            _log?.Invoke("taskbar-status: lifecycle timer creation failed.");
        }

        EnsureAttached();
    }

    public void Update(TaskbarStatusSnapshot snapshot, string automationText)
    {
        if (_disposed) return;
        automationText ??= string.Empty;
        var contentChanged =
            !TaskbarStatusPolicy.SameContent(_snapshot, snapshot) ||
            !StringComparer.Ordinal.Equals(_automationText, automationText);
        _snapshot = snapshot;
        _automationText = automationText;
        _contentAvailable = true;

        if (_statusWindow != IntPtr.Zero && contentChanged)
        {
            SetWindowTextW(_statusWindow, _automationText);
        }
        EnsureAttached();
        if (_statusWindow != IntPtr.Zero && contentChanged)
        {
            InvalidateRect(_statusWindow, IntPtr.Zero, false);
        }
    }

    public void Hide()
    {
        _layoutFailures = 0;
        if (_statusWindow != IntPtr.Zero)
        {
            ShowWindow(_statusWindow, SwHide);
        }
    }

    public void Clear()
    {
        _contentAvailable = false;
        Hide();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_hostWindow != IntPtr.Zero)
        {
            KillTimer(_hostWindow, HostTimerId);
            DestroyWindow(_hostWindow);
            _hostWindow = IntPtr.Zero;
        }
        DestroyStatusWindow();
        _taskbarWindow = IntPtr.Zero;
        GC.SuppressFinalize(this);
    }

    private static WndClassEx CreateWindowClass(string name, WndProc procedure, IntPtr instance) => new()
    {
        Size = (uint)Marshal.SizeOf<WndClassEx>(),
        WindowProcedure = procedure,
        Instance = instance,
        Cursor = LoadCursorW(IntPtr.Zero, (IntPtr)IdiArrow),
        ClassName = name,
    };

    private void EnsureAttached()
    {
        if (_disposed) return;
        var taskbar = FindWindowW("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero)
        {
            DestroyStatusWindow();
            _taskbarWindow = IntPtr.Zero;
            return;
        }

        if (taskbar != _taskbarWindow)
        {
            DestroyStatusWindow();
            _taskbarWindow = taskbar;
            _attachFailures = 0;
            _nextAttachAttempt = 0;
        }

        if (_statusWindow != IntPtr.Zero &&
            (!IsWindow(_statusWindow) || GetParent(_statusWindow) != taskbar))
        {
            DestroyStatusWindow();
        }

        if (_statusWindow == IntPtr.Zero)
        {
            if (Environment.TickCount64 < _nextAttachAttempt) return;
            CreateStatusWindow(taskbar);
            if (_statusWindow == IntPtr.Zero) return;
        }

        RepositionAndShow();
    }

    private void CreateStatusWindow(IntPtr taskbar)
    {
        var previousContext = SetThreadDpiAwarenessContext(GetWindowDpiAwarenessContext(taskbar));
        try
        {
            var instance = GetModuleHandleW(null);

            var status = CreateWindowExW(
                // Click-through: the strip is display only, so taskbar clicks must reach the taskbar.
                WsExToolWindow | WsExNoActivate | WsExLayered | WsExTransparent,
                _statusClassName,
                string.Empty,
                // Win11 paints its taskbar above child windows, so stay an owned popup.
                WsPopup,
                0, 0, 0, 0,
                taskbar,
                IntPtr.Zero,
                instance,
                IntPtr.Zero);
            if (status == IntPtr.Zero)
            {
                RecordAttachFailure(
                    "status window creation failed", Marshal.GetLastWin32Error());
                return;
            }

            ShowWindow(status, SwHide);
            // Black is the transparent color key; the icon badge and text stay.
            if (!SetLayeredWindowAttributes(status, 0, 255, LwaColorKey))
            {
                _log?.Invoke(
                    $"taskbar-status: transparency failed ({Marshal.GetLastWin32Error()}).");
            }
            SetLastError(0);
            var parent = GetParent(status);
            var parentError = Marshal.GetLastWin32Error();
            if (parent != taskbar)
            {
                DestroyWindow(status);
                RecordAttachFailure("attach failed", parentError);
                return;
            }

            _statusWindow = status;
            _taskbarWindow = taskbar;
            _attachFailures = 0;
            _nextAttachAttempt = 0;
            _lastStatusRect = default;
            _dpi = (int)GetDpiForWindow(taskbar);
            if (_dpi <= 0) _dpi = (int)GetDpiForWindow(status);
            if (_dpi <= 0) _dpi = 96;
            _themeDark = ReadThemeDark();
            CreateFonts();
            SetWindowTextW(status, _automationText);
            RepositionAndShow();
            InvalidateRect(status, IntPtr.Zero, false);
        }
        finally
        {
            if (previousContext != IntPtr.Zero)
            {
                SetThreadDpiAwarenessContext(previousContext);
            }
        }
    }

    private void DestroyStatusWindow()
    {
        DeleteFont(ref _batteryFont);
        DeleteFont(ref _iconFont);
        if (_statusWindow != IntPtr.Zero)
        {
            DestroyWindow(_statusWindow);
            _statusWindow = IntPtr.Zero;
        }
        _lastStatusRect = default;
    }

    private void RecordAttachFailure(string message, int error)
    {
        _attachFailures++;
        var delay = Math.Min(4000, 500 << Math.Min(_attachFailures - 1, 3));
        _nextAttachAttempt = Environment.TickCount64 + delay;
        if (_attachFailures == 1 || _attachFailures % 10 == 0)
        {
            _log?.Invoke(
                $"taskbar-status: {message} ({error}); retry {_attachFailures} in {delay} ms.");
        }
    }

    private void RepositionAndShow()
    {
        if (_statusWindow == IntPtr.Zero || !_contentAvailable) return;
        var taskbar = FindWindowW("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero || IsTaskbarHidden(taskbar) || IsFullscreenAppActive())
        {
            Hide();
            return;
        }
        if (!TryGetLayout(out var x, out var y, out var width, out var height))
        {
            // Explorer briefly invalidates the taskbar while input methods and
            // overflow flyouts change. Keep the last frame instead of blinking out.
            if (++_layoutFailures < 30) return;
            _log?.Invoke("taskbar-status: layout unavailable; hiding the strip.");
            Hide();
            return;
        }
        _layoutFailures = 0;

        var rect = new Rect { Left = x, Top = y, Right = x + width, Bottom = y + height };
        var visible = IsWindowVisible(_statusWindow);
        var changed = rect.Left != _lastStatusRect.Left ||
                      rect.Top != _lastStatusRect.Top ||
                      rect.Right != _lastStatusRect.Right ||
                      rect.Bottom != _lastStatusRect.Bottom;
        if (changed)
        {
            var flags = SwpNoActivate | SwpNoZOrder;
            if (!visible) flags |= SwpShowWindow;
            if (!SetWindowPos(
                    _statusWindow,
                    IntPtr.Zero,
                    x, y, width, height,
                    flags))
            {
                _log?.Invoke(
                    $"taskbar-status: positioning failed ({Marshal.GetLastWin32Error()}).");
                Hide();
                return;
            }

            _lastStatusRect = rect;
            return;
        }

        if (!visible)
        {
            ShowWindow(_statusWindow, SwShowNoActivate);
        }

        RaiseAboveTaskbar(taskbar);
    }

    /// <summary>
    /// Show-desktop re-orders the topmost band and leaves this owned popup behind
    /// the taskbar while it still reports itself visible, so re-insert it above.
    /// </summary>
    private void RaiseAboveTaskbar(IntPtr taskbar)
    {
        var above = GetWindow(taskbar, GwHwndPrev);
        if (above == _statusWindow) return;
        // Nothing above the taskbar means it is the top window; join the topmost
        // band so the strip still lands above it.
        if (above == IntPtr.Zero) above = new IntPtr(-1);
        SetWindowPos(
            _statusWindow,
            above,
            0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    /// <summary>
    /// True when the taskbar is hidden or slid past its monitor edge. The strip
    /// must not infer visibility from whatever window happens to sit on top.
    /// </summary>
    private static bool IsTaskbarHidden(IntPtr taskbar)
    {
        if (!IsWindowVisible(taskbar))
        {
            return true;
        }
        if (!GetWindowRect(taskbar, out var rect))
        {
            return true;
        }

        var monitor = MonitorFromWindow(taskbar, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfoW(monitor, ref info))
        {
            return false;
        }

        // Auto-hide slides the taskbar past the monitor edge while it still
        // reports itself visible. Use a DPI-scaled edge instead of raw pixels.
        var edge = (int)Math.Round(4 * GetDpiForWindow(taskbar) / 96.0);
        if (rect.Top <= info.Monitor.Top + edge)
        {
            return rect.Bottom <= info.Monitor.Top + edge;
        }
        if (rect.Bottom >= info.Monitor.Bottom - edge)
        {
            return rect.Top >= info.Monitor.Bottom - edge;
        }
        return false;
    }

    private static bool IsFullscreenAppActive()
    {
        return SHQueryUserNotificationState(out var state) == 0 &&
            IsFullscreenNotificationState(state);
    }

    private bool TryGetLayout(out int x, out int y, out int width, out int height)
    {
        x = y = width = height = 0;
        var taskbar = FindWindowW("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero || !GetWindowRect(taskbar, out var taskbarRect) ||
            !GetClientRect(taskbar, out var client))
        {
            return false;
        }

        var taskbarDpi = (int)GetDpiForWindow(taskbar);
        if (taskbarDpi > 0 && taskbarDpi != _dpi)
        {
            _dpi = taskbarDpi;
            CreateFonts();
        }

        var monitor = MonitorFromWindow(taskbar, MonitorDefaultToNearest);
        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfoW(monitor, ref monitorInfo) ||
            taskbarRect.Width < taskbarRect.Height * 3)
        {
            return false;
        }

        var scale = _dpi / 96.0;
        var clientHeight = client.Height;
        height = Math.Min(
            (int)Math.Round(TargetHeightDip * scale),
            (int)Math.Round(MaximumHeightDip * scale));
        height = Math.Min(clientHeight, Math.Max((int)Math.Round(MinimumHeightDip * scale), height));
        if (clientHeight < (int)Math.Round(8 * scale))
        {
            return false;
        }

        var tray = FindWindowExW(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        var right = client.Width;
        if (tray != IntPtr.Zero && GetWindowRect(tray, out var trayRect))
        {
            var point = new Point { X = trayRect.Left, Y = trayRect.Top };
            if (ScreenToClient(taskbar, ref point))
            {
                right = point.X;
            }
        }

        var gap = (int)Math.Round(GapDip * scale);
        var available = Math.Max(0, right - gap);
        var maximumWidth = (int)Math.Round(MaximumWidthDip * scale);
        var minimumWidth = (int)Math.Round(BaseWidthDip * scale);

        var obstacles = GetForeignTaskbarObstacles(taskbar, client.Height, right);
        var batteryCount = Math.Min(
            _snapshot.Batteries.Count, TaskbarStatusPolicy.MaximumBatteryCount);
        for (var count = batteryCount; count >= 1; count--)
        {
            var candidate = Math.Clamp(MeasureWidth(count), minimumWidth, maximumWidth);
            if (candidate > available)
            {
                continue;
            }
            if (!TaskbarStatusLayout.TryFindRightmostSlot(
                    leftBound: 0,
                    rightBound: right,
                    width: candidate,
                    gap: gap,
                    obstacles: obstacles,
                    out var left))
            {
                continue;
            }

            var origin = new Point
            {
                X = left,
                Y = Math.Max(0, (clientHeight - height) / 2),
            };
            if (!ClientToScreen(taskbar, ref origin))
            {
                return false;
            }

            _visibleBatteryCount = count;
            width = candidate;
            x = origin.X;
            y = origin.Y;
            return true;
        }
        return false;
    }

    private static IReadOnlyList<TaskbarStatusObstacle> GetForeignTaskbarObstacles(
        IntPtr taskbar,
        int taskbarHeight,
        int right)
    {
        // Taskbar overlays such as TrafficMonitor are child windows owned by another process.
        var obstacles = new List<TaskbarStatusObstacle>();
        _ = GetWindowThreadProcessId(taskbar, out var shellProcessId);
        var ownProcessId = (uint)Environment.ProcessId;
        _ = EnumChildWindows(taskbar, (child, _) =>
        {
            if (!IsWindowVisible(child) ||
                !GetWindowRect(child, out var rect) ||
                rect.Width <= 0 || rect.Height <= 0)
            {
                return true;
            }

            GetWindowThreadProcessId(child, out var processId);
            if (processId == shellProcessId || processId == ownProcessId)
            {
                return true;
            }

            var topLeft = new Point { X = rect.Left, Y = rect.Top };
            var bottomRight = new Point { X = rect.Right, Y = rect.Bottom };
            if (!ScreenToClient(taskbar, ref topLeft) ||
                !ScreenToClient(taskbar, ref bottomRight) ||
                bottomRight.Y <= 0 || topLeft.Y >= taskbarHeight ||
                bottomRight.X <= 0 || topLeft.X >= right)
            {
                return true;
            }

            obstacles.Add(new TaskbarStatusObstacle(topLeft.X, bottomRight.X));
            return true;
        }, IntPtr.Zero);
        return obstacles;
    }

}
