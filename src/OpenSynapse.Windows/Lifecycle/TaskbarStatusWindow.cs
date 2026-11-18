using System.Runtime.InteropServices;
using System.Text;
using OpenSynapse.Core.Sensors;
using static OpenSynapse.Windows.Lifecycle.TaskbarStatusNative;

namespace OpenSynapse.Windows.Lifecycle;

/// <summary>
/// A small GDI strip hosted by each taskbar. The host window stays alive when
/// Explorer restarts; every strip is recreated and reattached by the host timer.
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
    private const int VerticalRowHeightDip = 24;
    private const int VerticalMinimumWidthDip = 28;
    private const int GapDip = 6;
    private const int PaddingDip = 8;
    private const int VerticalPaddingDip = 3;
    private const int BatteryIconGapDip = 5;
    private const int DeviceGapDip = 8;
    private const int IconBadgeDip = 18;
    private const int IconBadgeRadiusDip = 5;
    private const int ChargingGapDip = 3;
    private const int BoltWidthDip = 7;
    private const int BoltHeightDip = 12;
    private const int NullPen = 8;

    private sealed class StatusSurface
    {
        public IntPtr Taskbar;
        public IntPtr Window;
        public Rect LastRect;
        public int Dpi = 96;
        public int VisibleBatteryCount;
        public int LayoutFailures;
        public int AttachFailures;
        public long NextAttachAttempt;
        public bool IsChild;
        public bool Vertical;
        public IntPtr BatteryFont;
        public IntPtr IconFont;
    }

    private readonly Action<string>? _log;
    private readonly WndProc _hostProcedure;
    private readonly WndProc _statusProcedure;
    private readonly string _hostClassName;
    private readonly string _statusClassName;
    private readonly uint _taskbarCreatedMessage;
    private readonly List<StatusSurface> _surfaces = [];
    private readonly Dictionary<IntPtr, StatusSurface> _surfacesByWindow = [];
    private IntPtr _hostWindow;
    private TaskbarStatusSnapshot _snapshot = new([]);
    private string _automationText = string.Empty;
    private bool _themeDark = true;
    private bool _contentAvailable;
    private bool _disposed;

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

        if (contentChanged)
        {
            foreach (var surface in _surfaces)
            {
                if (surface.Window != IntPtr.Zero)
                {
                    SetWindowTextW(surface.Window, _automationText);
                }
            }
        }
        EnsureAttached();
        if (contentChanged) InvalidateAll();
    }

    public void Hide()
    {
        foreach (var surface in _surfaces)
        {
            HideSurface(surface);
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
        DestroyAllSurfaces();
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
        var taskbars = EnumerateTaskbars();
        if (taskbars.Count == 0)
        {
            DestroyAllSurfaces();
            return;
        }

        for (var index = _surfaces.Count - 1; index >= 0; index--)
        {
            if (!taskbars.Contains(_surfaces[index].Taskbar))
            {
                DestroySurface(_surfaces[index]);
            }
        }

        foreach (var taskbar in taskbars)
        {
            var surface = FindSurface(taskbar);
            if (surface is null)
            {
                surface = new StatusSurface { Taskbar = taskbar };
                _surfaces.Add(surface);
            }

            if (surface.Window != IntPtr.Zero &&
                (!IsWindow(surface.Window) || GetParent(surface.Window) != taskbar))
            {
                DestroySurfaceWindow(surface);
            }

            if (surface.Window == IntPtr.Zero)
            {
                if (Environment.TickCount64 < surface.NextAttachAttempt) continue;
                CreateStatusWindow(surface);
                if (surface.Window == IntPtr.Zero) continue;
            }

            RepositionAndShow(surface);
        }
    }

    private StatusSurface? FindSurface(IntPtr taskbar)
    {
        foreach (var surface in _surfaces)
        {
            if (surface.Taskbar == taskbar) return surface;
        }
        return null;
    }

    private static List<IntPtr> EnumerateTaskbars()
    {
        var taskbars = new List<IntPtr>(3);
        EnumWindows((window, _) =>
        {
            var name = new StringBuilder(64);
            GetClassNameW(window, name, name.Capacity);
            var className = name.ToString();
            if (className is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            {
                taskbars.Add(window);
            }
            return true;
        }, IntPtr.Zero);
        return taskbars;
    }

    private void CreateStatusWindow(StatusSurface surface)
    {
        var taskbar = surface.Taskbar;
        var previousContext = SetThreadDpiAwarenessContext(GetWindowDpiAwarenessContext(taskbar));
        try
        {
            var instance = GetModuleHandleW(null);
            var status = CreateWindowExW(
                // Click-through: the strip is display only, so taskbar clicks must reach the taskbar.
                WsExToolWindow | WsExNoActivate | WsExLayered | WsExTransparent,
                _statusClassName,
                string.Empty,
                WsPopup,
                0, 0, 0, 0,
                // Owner until the child conversion succeeds; this is also the
                // fallback if Explorer refuses SetParent.
                taskbar,
                IntPtr.Zero,
                instance,
                IntPtr.Zero);
            if (status == IntPtr.Zero)
            {
                RecordAttachFailure(
                    surface, "status window creation failed", Marshal.GetLastWin32Error());
                return;
            }

            ShowWindow(status, SwHide);
            var style = GetWindowLongW(status, GwlStyle);
            SetWindowLongW(status, GwlStyle, (style & ~WsPopup) | WsChild);
            SetLastError(0);
            _ = SetParent(status, taskbar);
            var parentError = Marshal.GetLastWin32Error();
            if ((GetWindowLongW(status, GwlStyle) & WsChild) != 0 &&
                GetParent(status) == taskbar &&
                GetAncestor(status, GaRoot) == taskbar)
            {
                // A child is painted with the taskbar itself, so Show desktop
                // and auto-hide carry it along without a z-order race.
                surface.IsChild = true;
                // Join the taskbar's child band once; later positioning keeps
                // this z-order instead of re-inserting on every timer tick.
                SetWindowPos(
                    status, IntPtr.Zero, 0, 0, 0, 0,
                    SwpNoMove | SwpNoSize | SwpNoActivate);
            }
            else
            {
                _log?.Invoke(
                    $"taskbar-status: child attach failed on 0x{taskbar.ToInt64():X} " +
                    $"({parentError}); using an owner popup.");
                var currentStyle = GetWindowLongW(status, GwlStyle);
                SetWindowLongW(status, GwlStyle, (currentStyle & ~WsChild) | WsPopup);
                surface.IsChild = false;
            }

            // Black is the transparent color key; the icon badge and text stay.
            if (!SetLayeredWindowAttributes(status, 0, 255, LwaColorKey))
            {
                _log?.Invoke(
                    $"taskbar-status: transparency failed ({Marshal.GetLastWin32Error()}).");
            }

            surface.Window = status;
            surface.AttachFailures = 0;
            surface.NextAttachAttempt = 0;
            surface.LastRect = default;
            surface.Dpi = (int)GetDpiForWindow(taskbar);
            if (surface.Dpi <= 0) surface.Dpi = (int)GetDpiForWindow(status);
            if (surface.Dpi <= 0) surface.Dpi = 96;
            surface.Vertical = IsVerticalTaskbar(taskbar);
            _themeDark = ReadThemeDark();
            CreateFonts(surface);
            _surfacesByWindow[status] = surface;
            SetWindowTextW(status, _automationText);
            RepositionAndShow(surface);
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

    private void DestroySurface(StatusSurface surface)
    {
        DestroySurfaceWindow(surface);
        _surfaces.Remove(surface);
    }

    private void DestroySurfaceWindow(StatusSurface surface)
    {
        DeleteSurfaceFonts(surface);
        if (surface.Window != IntPtr.Zero)
        {
            _surfacesByWindow.Remove(surface.Window);
            DestroyWindow(surface.Window);
            surface.Window = IntPtr.Zero;
        }
        surface.LastRect = default;
        surface.IsChild = false;
    }

    private void DestroyAllSurfaces()
    {
        for (var index = _surfaces.Count - 1; index >= 0; index--)
        {
            DestroySurface(_surfaces[index]);
        }
    }

    private void RecordAttachFailure(StatusSurface surface, string message, int error)
    {
        surface.AttachFailures++;
        var delay = Math.Min(4000, 500 << Math.Min(surface.AttachFailures - 1, 3));
        surface.NextAttachAttempt = Environment.TickCount64 + delay;
        if (surface.AttachFailures == 1 || surface.AttachFailures % 10 == 0)
        {
            _log?.Invoke(
                $"taskbar-status: {message} on 0x{surface.Taskbar.ToInt64():X} " +
                $"({error}); retry {surface.AttachFailures} in {delay} ms.");
        }
    }

    private void RepositionAndShow(StatusSurface surface)
    {
        if (surface.Window == IntPtr.Zero || !_contentAvailable) return;
        var taskbar = surface.Taskbar;
        if (!IsWindow(taskbar) || !IsWindowVisible(taskbar) || IsFullscreenAppActive())
        {
            HideSurface(surface);
            return;
        }
        if (!TryGetLayout(surface, out var x, out var y, out var width, out var height))
        {
            // Explorer briefly invalidates the taskbar while input methods and
            // overflow flyouts change. Keep the last frame instead of blinking out.
            if (++surface.LayoutFailures < 30) return;
            _log?.Invoke(
                $"taskbar-status: layout unavailable on 0x{taskbar.ToInt64():X}; " +
                "hiding the strip.");
            HideSurface(surface);
            return;
        }
        surface.LayoutFailures = 0;

        // Explorer raises its own content bridge when Start or a flyout opens; a
        // child strip below it is painted over. Re-assert the top of the child band.
        if (surface.IsChild)
        {
            var above = GetWindow(surface.Window, GwHwndPrev);
            if (above != IntPtr.Zero && IsWindowVisible(above))
            {
                SetWindowPos(
                    surface.Window,
                    IntPtr.Zero,
                    0, 0, 0, 0,
                    SwpNoMove | SwpNoSize | SwpNoActivate);
            }
        }

        if (!surface.IsChild)
        {
            var origin = new Point { X = x, Y = y };
            if (!ClientToScreen(taskbar, ref origin))
            {
                _log?.Invoke(
                    $"taskbar-status: positioning failed ({Marshal.GetLastWin32Error()}).");
                HideSurface(surface);
                return;
            }
            x = origin.X;
            y = origin.Y;
        }

        var rect = new Rect { Left = x, Top = y, Right = x + width, Bottom = y + height };
        var visible = IsWindowVisible(surface.Window);
        var changed = rect.Left != surface.LastRect.Left ||
                      rect.Top != surface.LastRect.Top ||
                      rect.Right != surface.LastRect.Right ||
                      rect.Bottom != surface.LastRect.Bottom;
        if (changed)
        {
            var flags = SwpNoActivate | SwpNoZOrder;
            if (!visible) flags |= SwpShowWindow;
            if (!SetWindowPos(
                    surface.Window,
                    IntPtr.Zero,
                    x, y, width, height,
                    flags))
            {
                _log?.Invoke(
                    $"taskbar-status: positioning failed ({Marshal.GetLastWin32Error()}).");
                HideSurface(surface);
                return;
            }

            surface.LastRect = rect;
            if (!surface.IsChild) RaiseOwnerAboveTaskbar(surface);
            return;
        }

        if (!visible)
        {
            ShowWindow(surface.Window, SwShowNoActivate);
        }
        if (!surface.IsChild) RaiseOwnerAboveTaskbar(surface);
    }

    private static void HideSurface(StatusSurface surface)
    {
        surface.LayoutFailures = 0;
        if (surface.Window != IntPtr.Zero)
        {
            ShowWindow(surface.Window, SwHide);
        }
    }

    /// <summary>
    /// The child path inherits taskbar z-order. This is only for the elevated
    /// fallback, where SetParent is refused and Show desktop would cover an
    /// owned popup.
    /// </summary>
    private void RaiseOwnerAboveTaskbar(StatusSurface surface)
    {
        var above = GetWindow(surface.Taskbar, GwHwndPrev);
        if (above == surface.Window) return;
        if (above == IntPtr.Zero) above = new IntPtr(-1);
        SetWindowPos(
            surface.Window,
            above,
            0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    private void InvalidateAll()
    {
        foreach (var surface in _surfaces)
        {
            if (surface.Window != IntPtr.Zero)
            {
                InvalidateRect(surface.Window, IntPtr.Zero, false);
            }
        }
    }

    private void ResetLayouts()
    {
        foreach (var surface in _surfaces)
        {
            surface.LastRect = default;
        }
    }
}
