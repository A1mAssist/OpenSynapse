using OpenSynapse.Core.Macros;

namespace OpenSynapse.Windows.Input;

/// <summary>
/// A software macro can only own a private Razer key after the device assignment is
/// cleared. Keyboard and mouse-button triggers are suppressed by the low-level hook
/// instead, so writing Off there would also break the macro.
/// </summary>
public static class RazerSoftwareMacroAssignmentPolicy
{
    // Naga side keys are observed through their keyboard output. Off removes that
    // output, and Raw Input cannot suppress the original key when it is left on.
    public static bool CanAssign(MacroInput input) => input.Kind != MacroInputKind.NagaSideButton;

    public static bool NeedsOff(MacroInput input) =>
        input.Kind is MacroInputKind.RazerKey or
            MacroInputKind.NagaSideButton or
            MacroInputKind.DeviceButton;

    // Standard HID buttons above XBUTTON2 have no low-level hook to suppress them.
    // Keep the device emitting the same usage so OpenSynapse's HID reader can own it.
    public static bool NeedsSelfMapping(MacroInput input) =>
        input.Kind == MacroInputKind.MouseButton &&
        input.MouseButton is >= 6 and <= 10;

    public static bool NeedsDeviceAssignment(MacroInput input) =>
        NeedsOff(input) || NeedsSelfMapping(input);
}
