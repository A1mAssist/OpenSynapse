namespace OpenSynapse.Windows.Lifecycle;

internal readonly record struct TaskbarStatusObstacle(int Left, int Right);

internal static class TaskbarStatusLayout
{
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
