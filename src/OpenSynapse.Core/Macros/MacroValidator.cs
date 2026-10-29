namespace OpenSynapse.Core.Macros;

public enum MacroValidationFailure
{
    None,
    StepIncomplete,
    ReferenceInvalid,
}

/// <summary>
/// Structural checks for a macro tree. Pure: no profile, UI or resource access.
/// </summary>
public static class MacroValidator
{
    public static MacroValidationFailure Validate(
        MacroDefinition macro,
        IReadOnlyDictionary<string, MacroDefinition> macros)
    {
        if (!ValidateTree(macro))
        {
            return MacroValidationFailure.StepIncomplete;
        }

        return ValidateReferences(macro, macros)
            ? MacroValidationFailure.None
            : MacroValidationFailure.ReferenceInvalid;
    }

    public static bool ValidateTree(MacroDefinition macro)
    {
        var count = 0;
        return ValidateStepList(macro.Steps, depth: 0, ref count);
    }

    public static bool ValidateSteps(IReadOnlyList<MacroStep> steps)
    {
        var count = 0;
        return ValidateStepList(steps, depth: 0, ref count);
    }

    public static bool ValidateReferences(
        MacroDefinition macro,
        IReadOnlyDictionary<string, MacroDefinition> macros) =>
        ValidateReferenceNode(macro, macros, [], depth: 0);

    private static bool ValidateReferenceNode(
        MacroDefinition macro,
        IReadOnlyDictionary<string, MacroDefinition> map,
        HashSet<string> stack,
        int depth)
    {
        if (depth > MacroDefinition.MaxNestingDepth || !stack.Add(macro.Id))
        {
            return false;
        }

        try
        {
            foreach (var step in MacroStepTree.Enumerate(macro.Steps))
            {
                if (step.Kind != MacroStepKind.MacroReference)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(step.MacroId) ||
                    !map.TryGetValue(step.MacroId, out var referenced) ||
                    !ValidateReferenceNode(referenced, map, stack, depth + 1))
                {
                    return false;
                }
            }

            return true;
        }
        finally
        {
            stack.Remove(macro.Id);
        }
    }

    private static bool ValidateStepList(
        IReadOnlyList<MacroStep> steps,
        int depth,
        ref int count)
    {
        if (depth > MacroDefinition.MaxNestingDepth)
        {
            return false;
        }

        foreach (var step in steps)
        {
            count++;
            if (count > MacroDefinition.MaxSteps)
            {
                return false;
            }

            var valid = step.Kind switch
            {
                MacroStepKind.Input => step.Input?.IsValid(allowPrimaryMouseButton: true) == true,
                MacroStepKind.Delay => true,
                MacroStepKind.Text => !string.IsNullOrEmpty(step.Text) &&
                    step.Text.Length <= MacroStep.MaxTextLength,
                MacroStepKind.RunCommand => !string.IsNullOrEmpty(step.Command) &&
                    step.Command.Length <= MacroStep.MaxCommandLength &&
                    (step.Arguments?.Length ?? 0) <= MacroStep.MaxArgumentsLength &&
                    (step.WorkingDirectory?.Length ?? 0) <= MacroStep.MaxWorkingDirectoryLength,
                MacroStepKind.Loop => step.LoopCount is >= 1 and <= MacroStep.MaxLoopCount &&
                    step.Children.Count > 0,
                MacroStepKind.MacroReference => !string.IsNullOrWhiteSpace(step.MacroId) &&
                    step.MacroId.Length <= 64,
                MacroStepKind.MouseMove => step.MousePath.Count is > 0 and <= MacroStep.MaxMousePathPoints,
                _ => false,
            };
            if (!valid)
            {
                return false;
            }

            if (step.Kind == MacroStepKind.Loop &&
                !ValidateStepList(step.Children, depth + 1, ref count))
            {
                return false;
            }
        }

        return true;
    }
}
