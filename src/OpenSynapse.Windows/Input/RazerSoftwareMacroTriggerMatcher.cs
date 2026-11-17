using OpenSynapse.Core.Macros;

namespace OpenSynapse.Windows.Input;

/// <summary>
/// Matches a macro page trigger against the input resolved for a mapping row.
/// A software trigger is always unmodified and scoped to one HyperShift layer.
/// </summary>
public static class RazerSoftwareMacroTriggerMatcher
{
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
}
