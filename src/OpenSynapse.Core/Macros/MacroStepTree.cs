namespace OpenSynapse.Core.Macros;

public static class MacroStepTree
{
    public static bool MoveGroup(IList<MacroStep> owner, string groupId, int offset)
    {
        var indices = Enumerable.Range(0, owner.Count)
            .Where(index => StringComparer.Ordinal.Equals(owner[index].GroupId, groupId)).ToList();
        if (offset == 0 || indices.Count == 0 ||
            (offset < 0 && indices[0] == 0) || (offset > 0 && indices[^1] == owner.Count - 1))
            return false;
        var group = indices.Select(index => owner[index]).ToArray();
        var anchor = offset < 0 ? owner[indices[0] - 1] : owner[indices[^1] + 1];
        for (var index = indices.Count - 1; index >= 0; index--) owner.RemoveAt(indices[index]);
        var target = owner.IndexOf(anchor) + (offset < 0 ? 0 : 1);
        foreach (var step in group) owner.Insert(target++, step);
        return true;
    }

    public static bool TryApplyMousePath(
        MacroDefinition owner, MacroDefinition? currentOwner, MacroStep step, IReadOnlyList<MacroMousePoint> points)
    {
        if (!ReferenceEquals(owner, currentOwner) || step.Kind != MacroStepKind.MouseMove ||
            !Enumerate(owner.Steps).Contains(step)) return false;
        step.MousePath = points.Select(point => new MacroMousePoint
        {
            X = point.X, Y = point.Y, OffsetMilliseconds = point.OffsetMilliseconds,
        }).ToList();
        return true;
    }

    public static int CountSteps(IEnumerable<MacroStep> steps)
    {
        var count = 0;
        foreach (var step in steps)
        {
            count++;
            if (step.Kind == MacroStepKind.Loop)
            {
                count += CountSteps(step.Children);
            }
        }

        return count;
    }

    public static IEnumerable<MacroStep> Enumerate(IEnumerable<MacroStep> steps)
    {
        foreach (var step in steps)
        {
            yield return step;
            if (step.Kind == MacroStepKind.Loop)
            {
                foreach (var child in Enumerate(step.Children))
                {
                    yield return child;
                }
            }
        }
    }

    public static bool RemoveEmptyLoops(List<MacroStep> steps)
    {
        var changed = false;
        for (var index = steps.Count - 1; index >= 0; index--)
        {
            var step = steps[index];
            if (step.Kind != MacroStepKind.Loop)
            {
                continue;
            }

            changed |= RemoveEmptyLoops(step.Children);
            if (step.Children.Count > 0)
            {
                continue;
            }

            steps.RemoveAt(index);
            changed = true;
        }

        return changed;
    }
}
