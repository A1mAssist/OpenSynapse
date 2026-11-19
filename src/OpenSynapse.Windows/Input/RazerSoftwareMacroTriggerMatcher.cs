using OpenSynapse.Core.Macros;

namespace OpenSynapse.Windows.Input;

/// <summary>
/// Matches a macro page trigger against the input resolved for a mapping row.
/// A software trigger is always unmodified and scoped to one HyperShift layer.
/// </summary>
public static class RazerSoftwareMacroTriggerMatcher
{
    /// <summary>
    /// One mapping row that still routes a private key to a software macro.
    /// Two mapping buttons can share one private key code, so a trigger is only
    /// abandoned when every row that reaches it has moved to another action.
    /// </summary>
    public readonly record struct Claim(string MacroId, MacroInput Input, bool HyperShiftLayer);

    public static int FindIndex(
        IReadOnlyList<MacroDefinition> macros,
        MacroInput input,
        bool hyperShiftLayer,
        out MacroTriggerMode mode)
    {
        for (var index = 0; index < macros.Count; index++)
        {
            var trigger = macros[index].AllTriggers.FirstOrDefault(candidate =>
                string.IsNullOrEmpty(candidate.DeviceId) &&
                candidate.Modifiers == MacroModifierKeys.None &&
                candidate.HyperShiftLayer == hyperShiftLayer &&
                candidate.Input.Matches(input));
            if (trigger is null)
            {
                continue;
            }

            mode = trigger.Mode;
            return index;
        }

        mode = MacroTriggerMode.Press;
        return -1;
    }

    public static bool IsClaimed(
        string macroId,
        MacroTrigger trigger,
        IReadOnlyList<Claim> claims)
    {
        foreach (var claim in claims)
        {
            if (StringComparer.Ordinal.Equals(claim.MacroId, macroId) &&
                claim.HyperShiftLayer == trigger.HyperShiftLayer &&
                claim.Input.Matches(trigger.Input))
            {
                return true;
            }
        }

        return false;
    }
}
