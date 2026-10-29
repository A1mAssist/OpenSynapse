namespace OpenSynapse.Core.Macros;

public static class MacroStepTree
{
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
