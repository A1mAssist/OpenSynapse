using System.Runtime.InteropServices;
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
            var (regionStart, regionEnd, fromStart) = GetFreeRegion(
                taskbar, client, vertical: true);
            var obstacles = GetForeignTaskbarObstacles(
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
        var (horizontalStart, horizontalEnd, horizontalFromStart) =
            GetFreeRegion(taskbar, client, vertical: false);
        var horizontalObstacles = GetForeignTaskbarObstacles(
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
        return false;
    }

    private static (int Start, int End, bool FromStart) GetFreeRegion(
        IntPtr taskbar,
        Rect client,
        bool vertical)
    {
        var tray = FindWindowExW(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        if (tray != IntPtr.Zero && GetWindowRect(tray, out var trayRect))
        {
            var topLeft = new Point { X = trayRect.Left, Y = trayRect.Top };
            var bottomRight = new Point { X = trayRect.Right, Y = trayRect.Bottom };
            if (ScreenToClient(taskbar, ref topLeft) &&
                ScreenToClient(taskbar, ref bottomRight))
            {
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
        }

        return vertical
            ? (0, client.Height, false)
            : (0, client.Width, false);
    }

    private static IReadOnlyList<TaskbarStatusObstacle> GetForeignTaskbarObstacles(
        IntPtr taskbar,
        bool vertical,
        int crossExtent,
        int freeExtent)
    {
        // Third-party taskbar overlays and the shell's own app-button strip are
        // both obstacles; the tray is handled as the free-region boundary.
        var obstacles = new List<TaskbarStatusObstacle>();
        AddTaskListObstacle(
            taskbar, vertical, crossExtent, freeExtent, obstacles);

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

            AddObstacle(taskbar, rect, vertical, crossExtent, freeExtent, obstacles);
            return true;
        }, IntPtr.Zero);
        return obstacles;
    }

    private static void AddTaskListObstacle(
        IntPtr taskbar,
        bool vertical,
        int crossExtent,
        int freeExtent,
        List<TaskbarStatusObstacle> obstacles)
    {
        var taskList = FindTaskList(taskbar);
        if (taskList == IntPtr.Zero ||
            !IsWindowVisible(taskList) ||
            !GetWindowRect(taskList, out var rect) ||
            rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        AddObstacle(
            taskbar,
            rect,
            vertical,
            crossExtent,
            freeExtent,
            obstacles);
    }

    private static IntPtr FindTaskList(IntPtr taskbar)
    {
        var taskList = FindWindowExW(taskbar, IntPtr.Zero, "MSTaskSwWClass", null);
        return taskList != IntPtr.Zero
            ? taskList
            : FindWindowExW(taskbar, IntPtr.Zero, "MSTaskListWClass", null);
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
