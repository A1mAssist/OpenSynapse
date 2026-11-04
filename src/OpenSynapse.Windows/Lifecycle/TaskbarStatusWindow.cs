using System.Runtime.InteropServices;
using Microsoft.Win32;
using OpenSynapse.Core.Sensors;
using static OpenSynapse.Windows.Lifecycle.TaskbarStatusNative;

namespace OpenSynapse.Windows.Lifecycle;

/// <summary>
/// A small GDI child of the taskbar. The host window stays alive when Explorer
/// restarts; the child is recreated and reparented by the host timer.
/// </summary>
public sealed partial class TaskbarStatusWindow : IDisposable
{
    private const uint HostTimerId = 1;
    private const uint HostTimerInterval = 2000;
    private const int BaseWidthDip = 96;
    private const int MaximumWidthDip = 200;
    private const int TargetHeightDip = 30;
    private const int MinimumHeightDip = 22;
    private const int MaximumHeightDip = 36;
    private const int GapDip = 8;
    private const int PaddingDip = 14;
    private const int PerformanceGapDip = 10;
    private const int PerformanceMinWidthDip = 150;
    private const int BoltWidthDip = 8;
    private const int BoltHeightDip = 14;
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
    private IntPtr _tooltipWindow;
    private IntPtr _batteryFont;
    private IntPtr _performanceFont;
    private IntPtr _cjkFont;
    private IntPtr _tooltipTextPointer;
    private Rect _lastStatusRect;
    private TaskbarStatusSnapshot _snapshot = new(null, null, string.Empty);
    private string _tooltipText = string.Empty;
    private string _automationText = string.Empty;
    private bool _tooltipAdded;
    private bool _visible;
    private bool _showPerformance;
    private bool _themeDark = true;
    private bool _attachFailed;
    private bool _disposed;
    private int _dpi = 96;

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

    public event Action? Activated;

    public void Update(TaskbarStatusSnapshot snapshot, string tooltip, string automationText)
    {
        if (_disposed) return;
        _snapshot = snapshot;
        _tooltipText = tooltip ?? string.Empty;
        _automationText = automationText ?? string.Empty;
        if (_statusWindow == IntPtr.Zero) return;

        SetWindowTextW(_statusWindow, _automationText);
        UpdateTooltip();
        RepositionAndShow();
        InvalidateRect(_statusWindow, IntPtr.Zero, false);
    }

    public void Hide()
    {
        _visible = false;
        if (_statusWindow != IntPtr.Zero)
        {
            ShowWindow(_statusWindow, SwHide);
        }
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
            _attachFailed = false;
        }

        if (_attachFailed) return;

        if (_statusWindow == IntPtr.Zero || !IsWindow(_statusWindow))
        {
            DestroyStatusWindow();
            CreateStatusWindow(taskbar);
        }

        if (_statusWindow != IntPtr.Zero)
        {
            RepositionAndShow();
        }
    }

    private void CreateStatusWindow(IntPtr taskbar)
    {
        var previousContext = SetThreadDpiAwarenessContext(GetWindowDpiAwarenessContext(taskbar));
        try
        {
            var instance = GetModuleHandleW(null);

            var status = CreateWindowExW(
                WsExToolWindow | WsExNoActivate,
                _statusClassName,
                string.Empty,
                WsPopup,
                0, 0, 0, 0,
                IntPtr.Zero,
                IntPtr.Zero,
                instance,
                IntPtr.Zero);
            if (status == IntPtr.Zero)
            {
                _log?.Invoke(
                    $"taskbar-status: status window creation failed ({Marshal.GetLastWin32Error()}).");
                _attachFailed = true;
                return;
            }

            ShowWindow(status, SwHide);
            var style = unchecked((uint)GetWindowLongW(status, GwlStyle));
            style &= 0x7FFFFFFF;
            style |= (uint)WsChild;
            SetWindowLongW(status, GwlStyle, unchecked((int)style));

            SetLastError(0);
            SetParent(status, taskbar);
            if (GetParent(status) != taskbar)
            {
                _log?.Invoke(
                    $"taskbar-status: attach failed (SetParent error {Marshal.GetLastWin32Error()}).");
                DestroyWindow(status);
                _attachFailed = true;
                return;
            }

            _statusWindow = status;
            _taskbarWindow = taskbar;
            _lastStatusRect = default;
            _dpi = (int)GetDpiForWindow(taskbar);
            if (_dpi <= 0) _dpi = (int)GetDpiForWindow(status);
            if (_dpi <= 0) _dpi = 96;
            _themeDark = ReadThemeDark();
            CreateFonts();
            CreateTooltipWindow();
            SetWindowTextW(status, _automationText);
            UpdateTooltip();
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
        if (_tooltipWindow != IntPtr.Zero)
        {
            DestroyWindow(_tooltipWindow);
            _tooltipWindow = IntPtr.Zero;
        }
        _tooltipAdded = false;
        FreeTooltipText();
        DeleteFont(ref _batteryFont);
        DeleteFont(ref _performanceFont);
        DeleteFont(ref _cjkFont);
        if (_statusWindow != IntPtr.Zero)
        {
            DestroyWindow(_statusWindow);
            _statusWindow = IntPtr.Zero;
        }
        _visible = false;
        _lastStatusRect = default;
    }

    private void RepositionAndShow()
    {
        if (_statusWindow == IntPtr.Zero) return;
        if (!TryGetLayout(out var x, out var y, out var width, out var height))
        {
            Hide();
            return;
        }

        var rect = new Rect { Left = x, Top = y, Right = x + width, Bottom = y + height };
        var changed = rect.Left != _lastStatusRect.Left ||
                      rect.Top != _lastStatusRect.Top ||
                      rect.Right != _lastStatusRect.Right ||
                      rect.Bottom != _lastStatusRect.Bottom;
        if (changed)
        {
            if (!SetWindowPos(
                    _statusWindow,
                    IntPtr.Zero,
                    x, y, width, height,
                    SwpNoActivate | SwpShowWindow))
            {
                _log?.Invoke(
                    $"taskbar-status: positioning failed ({Marshal.GetLastWin32Error()}).");
                Hide();
                return;
            }

            _lastStatusRect = rect;
            _visible = true;
            return;
        }

        if (!_visible)
        {
            ShowWindow(_statusWindow, SwShowNoActivate);
            _visible = true;
        }
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

        var monitor = MonitorFromWindow(taskbar, MonitorDefaultToNearest);
        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfoW(monitor, ref monitorInfo) ||
            taskbarRect.Width <= taskbarRect.Height ||
            taskbarRect.Bottom < monitorInfo.Monitor.Bottom - 2)
        {
            return false;
        }

        var scale = _dpi / 96.0;
        var clientHeight = client.Height;
        height = Math.Min(
            (int)Math.Round(TargetHeightDip * scale),
            (int)Math.Round(MaximumHeightDip * scale));
        height = Math.Min(height, clientHeight - (int)Math.Round(8 * scale));
        height = Math.Max((int)Math.Round(MinimumHeightDip * scale), height);
        if (height > clientHeight || clientHeight < MinimumHeightDip * scale)
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
        var performanceMinimum = (int)Math.Round(PerformanceMinWidthDip * scale);

        _showPerformance = !string.IsNullOrWhiteSpace(_snapshot.PerformanceMode) &&
            TaskbarStatusPolicy.ShowPerformanceSegment(available, performanceMinimum);
        width = MeasureWidth(_showPerformance);
        if (width > maximumWidth)
        {
            _showPerformance = false;
            width = MeasureWidth(false);
        }
        width = Math.Clamp(width, minimumWidth, maximumWidth);
        if (width > available)
        {
            _showPerformance = false;
            width = MeasureWidth(false);
            if (width > available)
            {
                return false;
            }
        }

        x = right - gap - width;
        y = Math.Max(0, (clientHeight - height) / 2);
        return x >= 0;
    }

}
