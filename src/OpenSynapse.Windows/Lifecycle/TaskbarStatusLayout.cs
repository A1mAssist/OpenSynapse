namespace OpenSynapse.Windows.Lifecycle;

internal readonly record struct TaskbarStatusObstacle(int Left, int Right);

internal enum TaskbarOrientation
{
    Horizontal,
    Vertical,
}

internal static class TaskbarStatusLayout
{
    internal static TaskbarOrientation? GetOrientation(int width, int height)
    {
        if (width >= height * 3) return TaskbarOrientation.Horizontal;
        if (height >= width * 3) return TaskbarOrientation.Vertical;
        return null;
    }

    internal static bool TryFindSlot(
        int regionStart,
        int regionEnd,
        int size,
        int gap,
        IReadOnlyList<TaskbarStatusObstacle> obstacles,
        bool fromStart,
        out int start)
    {
        start = 0;
        var extent = regionEnd - regionStart;
        if (size <= 0 || extent < size + gap)
        {
            return false;
        }

        var relative = new List<TaskbarStatusObstacle>(obstacles.Count);
        foreach (var obstacle in obstacles)
        {
            var left = Math.Max(0, obstacle.Left - regionStart);
            var right = Math.Min(extent, obstacle.Right - regionStart);
            if (right > left)
            {
                relative.Add(new(left, right));
            }
        }

        if (!fromStart)
        {
            if (!TryFindRightmostSlot(0, extent, size, gap, relative, out var offset))
            {
                return false;
            }
            start = regionStart + offset;
            return true;
        }

        var mirrored = new List<TaskbarStatusObstacle>(relative.Count);
        foreach (var obstacle in relative)
        {
            mirrored.Add(new(extent - obstacle.Right, extent - obstacle.Left));
        }
        if (!TryFindRightmostSlot(0, extent, size, gap, mirrored, out var mirroredOffset))
        {
            return false;
        }
        start = regionStart + extent - mirroredOffset - size;
        return true;
    }

    internal static (int Start, int End, bool FromStart) GetHorizontalRegion(
        int clientWidth,
        int trayLeft,
        int trayRight,
        int taskListRight)
    {
        if ((trayLeft + trayRight) / 2 > clientWidth / 2)
        {
            // Start and the app buttons own the left run. The gap before the
            // task list is not free space when Start is left-aligned.
            return (Math.Max(0, taskListRight), Math.Max(0, trayLeft), false);
        }

        return (Math.Min(clientWidth, trayRight), clientWidth, true);
    }

    internal static bool TryFindRightmostSlot(
        int leftBound,
        int rightBound,
        int width,
        int gap,
        IReadOnlyList<TaskbarStatusObstacle> obstacles,
        out int left)
    {
        left = 0;
        if (width <= 0 || rightBound - leftBound < width + gap)
        {
            return false;
        }

        var cursor = rightBound - gap;
        while (cursor - width >= leftBound)
        {
            var shifted = int.MaxValue;
            foreach (var obstacle in obstacles)
            {
                if (obstacle.Right <= cursor - width || obstacle.Left >= cursor)
                {
                    continue;
                }

                shifted = Math.Min(shifted, obstacle.Left - gap);
            }

            if (shifted == int.MaxValue)
            {
                left = cursor - width;
                return true;
            }

            cursor = shifted;
        }

        return false;
    }
}
