using OpenSynapse.Core.Macros;

namespace OpenSynapse.Windows.Input;

/// <summary>
/// A software macro can only own a private Razer key after the device assignment is
/// cleared. Keyboard and mouse-button triggers are suppressed by the low-level hook
/// instead, so writing Off there would also break the macro.
/// </summary>
public static class RazerSoftwareMacroAssignmentPolicy
{
    public static bool NeedsOff(MacroInput input) =>
        input.Kind is MacroInputKind.RazerKey or
            MacroInputKind.NagaSideButton or
            MacroInputKind.DeviceButton;
}
