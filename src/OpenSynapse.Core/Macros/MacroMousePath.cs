namespace OpenSynapse.Core.Macros;

public static class MacroMousePath
{
    public static bool IsValid(IReadOnlyList<MacroMousePoint>? points)
    {
        if (points is null || points.Count is 0 or > MacroStep.MaxMousePathPoints)
        {
            return false;
        }

        var previousOffset = 0;
        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];
            if (point is null ||
                point.X is < -100_000 or > 100_000 ||
                point.Y is < -100_000 or > 100_000 ||
                point.OffsetMilliseconds is < 0 or > MacroDefinition.MaxDelayMilliseconds ||
                (index > 0 && point.OffsetMilliseconds < previousOffset))
            {
                return false;
            }

            previousOffset = point.OffsetMilliseconds;
        }

        return true;
    }
}
