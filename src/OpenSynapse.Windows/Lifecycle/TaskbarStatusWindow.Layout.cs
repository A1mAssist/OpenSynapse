using System.Runtime.InteropServices;
using System.Text;
using OpenSynapse.Core.Sensors;
using static OpenSynapse.Windows.Lifecycle.TaskbarStatusNative;

namespace OpenSynapse.Windows.Lifecycle;

public sealed partial class TaskbarStatusWindow
{
    private static bool IsFullscreenAppActive()
    {
        return SHQueryUserNotificationState(out var state) == 0 &&
            IsFullscreenNotificationState(state);
    }

    private static bool IsVerticalTaskbar(IntPtr taskbar)
    {
        return GetWindowRect(taskbar, out var rect) &&
            TaskbarStatusLayout.GetOrientation(rect.Width, rect.Height) ==
                TaskbarOrientation.Vertical;
    }

    private bool TryGetLayout(
        StatusSurface surface,
        out int x,
        out int y,
        out int width,
        out int height)
    {
        x = y = width = height = 0;
        surface.LayoutUnsafe = false;
        var taskbar = surface.Taskbar;
        if (!GetWindowRect(taskbar, out var taskbarRect) ||
            !GetClientRect(taskbar, out var client))
        {
            return false;
        }

        var orientation = TaskbarStatusLayout.GetOrientation(
            taskbarRect.Width, taskbarRect.Height);
        if (orientation is null) return false;
        var vertical = orientation == TaskbarOrientation.Vertical;
        if (surface.Vertical != vertical)
        {
            surface.Vertical = vertical;
            CreateFonts(surface);
        }

        var taskbarDpi = (int)GetDpiForWindow(taskbar);
        if (taskbarDpi > 0 && taskbarDpi != surface.Dpi)
        {
            surface.Dpi = taskbarDpi;
            CreateFonts(surface);
        }

        var monitor = MonitorFromWindow(taskbar, MonitorDefaultToNearest);
        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfoW(monitor, ref monitorInfo))
        {
            return false;
        }

        var scale = surface.Dpi / 96.0;
        var gap = Math.Max(1, (int)Math.Round(GapDip * scale));
        var batteryCount = Math.Min(
            _snapshot.Batteries.Count, TaskbarStatusPolicy.MaximumBatteryCount);
        if (batteryCount <= 0) return false;

        if (!TryGetTrayBounds(surface, taskbarRect, out var trayRect))
        {
            surface.LayoutUnsafe = true;
            return false;
        }
        var region = GetFreeRegion(taskbar, client, vertical, trayRect);

        if (vertical)
        {
            var verticalWidth = Math.Min(
                client.Width,
                Math.Max(
                    (int)Math.Round(VerticalMinimumWidthDip * scale),
                    client.Width - (int)Math.Round(VerticalPaddingDip * 2 * scale)));
            if (verticalWidth < (int)Math.Round(VerticalMinimumWidthDip * scale))
            {
                return false;
            }

            var widest = 0;
            for (var index = 0; index < batteryCount; index++)
            {
                widest = Math.Max(
                    widest,
                    MeasureTextWidth(
                        surface,
                        TaskbarStatusPolicy.FormatBattery(
                            _snapshot.Batteries[index].Percent)));
            }
            if (widest + (int)Math.Round(VerticalPaddingDip * 2 * scale) > verticalWidth)
            {
                return false;
            }

            var rowHeight = Math.Max(
                1, (int)Math.Round(VerticalRowHeightDip * scale));
            var (regionStart, regionEnd, fromStart) = region;
            var obstacles = GetTaskbarObstacles(
                taskbar,
                vertical: true,
                crossExtent: client.Width,
                freeExtent: client.Height);
            for (var count = batteryCount; count >= 1; count--)
            {
                var candidate = rowHeight * count;
                if (!TaskbarStatusLayout.TryFindSlot(
                        regionStart,
                        regionEnd,
                        candidate,
                        gap,
                        obstacles,
                        fromStart,
                        out var top))
                {
                    continue;
                }

                surface.VisibleBatteryCount = count;
                width = verticalWidth;
                height = candidate;
                x = Math.Max(0, (client.Width - width) / 2);
                y = top;
                return true;
            }
            surface.LayoutUnsafe = true;
            return false;
        }

        var clientHeight = client.Height;
        height = Math.Min(
            (int)Math.Round(TargetHeightDip * scale),
            (int)Math.Round(MaximumHeightDip * scale));
        height = Math.Min(
            clientHeight,
            Math.Max((int)Math.Round(MinimumHeightDip * scale), height));
        if (clientHeight < (int)Math.Round(8 * scale))
        {
            return false;
        }

        var maximumWidth = (int)Math.Round(MaximumWidthDip * scale);
        var minimumWidth = (int)Math.Round(BaseWidthDip * scale);
        var (horizontalStart, horizontalEnd, horizontalFromStart) = region;
        var horizontalObstacles = GetTaskbarObstacles(
            taskbar,
            vertical: false,
            crossExtent: client.Height,
            freeExtent: client.Width);
        for (var count = batteryCount; count >= 1; count--)
        {
            var candidate = Math.Clamp(
                MeasureWidth(surface, count), minimumWidth, maximumWidth);
            if (!TaskbarStatusLayout.TryFindSlot(
                    horizontalStart,
                    horizontalEnd,
                    candidate,
                    gap,
                    horizontalObstacles,
                    horizontalFromStart,
                    out var left))
            {
                continue;
            }

            surface.VisibleBatteryCount = count;
            width = candidate;
            x = left;
            y = Math.Max(0, (clientHeight - height) / 2);
            return true;
        }
        surface.LayoutUnsafe = true;
        return false;
    }

    internal static (int Start, int End, bool FromStart) GetFreeRegion(
        IntPtr taskbar,
        Rect client,
        bool vertical,
        Rect trayRect)
    {
        var topLeft = new Point { X = trayRect.Left, Y = trayRect.Top };
        var bottomRight = new Point { X = trayRect.Right, Y = trayRect.Bottom };
        if (ScreenToClient(taskbar, ref topLeft) &&
            ScreenToClient(taskbar, ref bottomRight))
        {
            if (bottomRight.X <= 0 || topLeft.X >= client.Width ||
                bottomRight.Y <= 0 || topLeft.Y >= client.Height)
                return (0, 0, false);
            topLeft.X = Math.Clamp(topLeft.X, 0, client.Width);
            topLeft.Y = Math.Clamp(topLeft.Y, 0, client.Height);
            bottomRight.X = Math.Clamp(bottomRight.X, 0, client.Width);
            bottomRight.Y = Math.Clamp(bottomRight.Y, 0, client.Height);
            if (vertical)
            {
                var trayCenter = (topLeft.Y + bottomRight.Y) / 2;
                if (trayCenter > client.Height / 2)
                {
                    return (0, Math.Max(0, topLeft.Y), false);
                }
                return (Math.Min(client.Height, bottomRight.Y), client.Height, true);
            }

            var trayCenterX = (topLeft.X + bottomRight.X) / 2;
            if (trayCenterX > client.Width / 2)
            {
                var taskListRight = 0;
                var taskList = FindTaskList(taskbar);
                if (taskList != IntPtr.Zero &&
                    IsWindowVisible(taskList) &&
                    GetWindowRect(taskList, out var taskListRect))
                {
                    var taskListRightPoint = new Point
                    {
                        X = taskListRect.Right,
                        Y = taskListRect.Top,
                    };
                    if (ScreenToClient(taskbar, ref taskListRightPoint))
                    {
                        taskListRight = taskListRightPoint.X;
                    }
                }
                return TaskbarStatusLayout.GetHorizontalRegion(
                    client.Width,
                    topLeft.X,
                    bottomRight.X,
                    taskListRight);
            }
            return (Math.Min(client.Width, bottomRight.X), client.Width, true);
        }
        return (0, 0, false);
    }

    internal static IReadOnlyList<TaskbarStatusObstacle> GetTaskbarObstacles(
        IntPtr taskbar,
        bool vertical,
        int crossExtent,
        int freeExtent)
    {
        // Explorer's clocks and app buttons occupy space even without TrayNotifyWnd.
        var obstacles = new List<TaskbarStatusObstacle>();
        var name = new StringBuilder(128);
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
                GetClassNameW(child, name, name.Capacity);
                if (name.ToString() is not ("TrayNotifyWnd" or "TrayClockWClass" or
                    "ClockButton" or "MSTaskSwWClass" or "MSTaskListWClass" or "Start"))
                {
                    return true;
                }
            }

            AddObstacle(taskbar, rect, vertical, crossExtent, freeExtent, obstacles);
            return true;
        }, IntPtr.Zero);
        return obstacles;
    }

    internal static bool TryGetNativeTrayBounds(IntPtr taskbar, out Rect bounds)
    {
        Rect? found = null;
        var name = new StringBuilder(128);
        EnumChildWindows(taskbar, (child, _) =>
        {
            GetClassNameW(child, name, name.Capacity);
            if (name.ToString() is "TrayNotifyWnd" or "TrayClockWClass" or "ClockButton" &&
                IsWindowVisible(child) && GetWindowRect(child, out var rect) &&
                rect.Width > 0 && rect.Height > 0)
            {
                found = found is { } previous ? new Rect
                {
                    Left = Math.Min(previous.Left, rect.Left),
                    Top = Math.Min(previous.Top, rect.Top),
                    Right = Math.Max(previous.Right, rect.Right),
                    Bottom = Math.Max(previous.Bottom, rect.Bottom),
                } : rect;
            }
            return true;
        }, IntPtr.Zero);
        bounds = found.GetValueOrDefault();
        return found.HasValue;
    }

    private static bool TryGetTrayBounds(StatusSurface surface, Rect taskbarRect, out Rect bounds)
    {
        if (TryGetNativeTrayBounds(surface.Taskbar, out bounds)) return true;

        var sameBounds = surface.TrayQueryBounds.Equals(taskbarRect) &&
            surface.TrayQueryDpi == surface.Dpi;
        if (!sameBounds) surface.AutomationTrayBounds = null;
        if (surface.TrayQuery is { IsCompleted: true } query)
        {
            surface.AutomationTrayBounds = sameBounds &&
                Environment.TickCount64 < surface.NextTrayQuery + 1000 && query.IsCompletedSuccessfully
                ? query.Result : null;
            _ = query.Exception;
            surface.TrayQuery = null;
        }
        if (surface.TrayQuery is null &&
            (!sameBounds || Environment.TickCount64 >= surface.NextTrayQuery))
        {
            surface.TrayQueryBounds = taskbarRect;
            surface.TrayQueryDpi = surface.Dpi;
            surface.NextTrayQuery = Environment.TickCount64 + 1000;
            var taskbar = surface.Taskbar;
            surface.TrayQuery = Task.Run(() => TaskbarStatusAutomation.ReadTrayBounds(taskbar));
        }
        if (Environment.TickCount64 >= surface.NextTrayQuery + 1000)
            surface.AutomationTrayBounds = null;
        if (surface.AutomationTrayBounds is not { } physical) return false;

        // UIA reports physical screen pixels, including on mixed-DPI secondary monitors.
        var topLeft = new Point { X = physical.Left, Y = physical.Top };
        var bottomRight = new Point { X = physical.Right, Y = physical.Bottom };
        if (!PhysicalToLogicalPointForPerMonitorDPI(surface.Taskbar, ref topLeft) ||
            !PhysicalToLogicalPointForPerMonitorDPI(surface.Taskbar, ref bottomRight)) return false;
        bounds = new Rect
        {
            Left = topLeft.X, Top = topLeft.Y, Right = bottomRight.X, Bottom = bottomRight.Y,
        };
        return true;
    }

    private static IntPtr FindTaskList(IntPtr taskbar)
    {
        var taskList = IntPtr.Zero;
        var name = new StringBuilder(128);
        EnumChildWindows(taskbar, (child, _) =>
        {
            GetClassNameW(child, name, name.Capacity);
            if (name.ToString() is not ("MSTaskSwWClass" or "MSTaskListWClass") ||
                !IsWindowVisible(child)) return true;
            taskList = child;
            return false;
        }, IntPtr.Zero);
        return taskList;
    }

    private static void AddObstacle(
        IntPtr taskbar,
        Rect rect,
        bool vertical,
        int crossExtent,
        int freeExtent,
        List<TaskbarStatusObstacle> obstacles)
    {
        var topLeft = new Point { X = rect.Left, Y = rect.Top };
        var bottomRight = new Point { X = rect.Right, Y = rect.Bottom };
        if (!ScreenToClient(taskbar, ref topLeft) ||
            !ScreenToClient(taskbar, ref bottomRight))
        {
            return;
        }

        if (vertical)
        {
            if (bottomRight.X <= 0 || topLeft.X >= crossExtent ||
                bottomRight.Y <= 0 || topLeft.Y >= freeExtent)
            {
                return;
            }
            obstacles.Add(new TaskbarStatusObstacle(topLeft.Y, bottomRight.Y));
        }
        else
        {
            if (bottomRight.Y <= 0 || topLeft.Y >= crossExtent ||
                bottomRight.X <= 0 || topLeft.X >= freeExtent)
            {
                return;
            }
            obstacles.Add(new TaskbarStatusObstacle(topLeft.X, bottomRight.X));
        }
    }
}
