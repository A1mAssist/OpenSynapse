namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// A canned action offered by the gamepad button-mapping editor. Each entry delegates
/// to <see cref="RazerGamepadFunctionEncoder"/>; <see cref="LabelKey"/> is resolved by
/// the app so this layer stays culture neutral.
/// </summary>
public sealed record RazerGamepadAssignmentPreset(
    string LabelKey,
    RazerGamepadFunctionData? Definition)
{
    /// <summary>True for "keep current", which writes nothing.</summary>
    public bool IsUnchanged => Definition is null;

    public RazerGamepadFunctionData Encode() =>
        Definition ?? throw new InvalidOperationException(
            $"Preset '{LabelKey}' keeps the current assignment and cannot be encoded.");
}

/// <summary>The fixed action list shown by the gamepad mapping editor.</summary>
public static class RazerGamepadAssignmentPresets
{
    // HID Usage Table, Keyboard/Keypad page (0x07):
    // W = 0x1A, A = 0x04, S = 0x16, D = 0x07,
    // Space = 0x2C, Enter = 0x28, Escape = 0x29, Tab = 0x2B.
    public static IReadOnlyList<RazerGamepadAssignmentPreset> All { get; } =
    [
        new("RazerGamepadAssignmentKeep", null),
        new("RazerGamepadAssignmentOff", RazerGamepadFunctionEncoder.Off()),
        new("RazerGamepadAssignmentControllerA", RazerGamepadFunctionEncoder.Controller(RazerGamepadControllerButton.AOrCross)),
        new("RazerGamepadAssignmentControllerB", RazerGamepadFunctionEncoder.Controller(RazerGamepadControllerButton.BOrCircle)),
        new("RazerGamepadAssignmentControllerX", RazerGamepadFunctionEncoder.Controller(RazerGamepadControllerButton.XOrSquare)),
        new("RazerGamepadAssignmentControllerY", RazerGamepadFunctionEncoder.Controller(RazerGamepadControllerButton.YOrTriangle)),
        new("RazerGamepadAssignmentControllerLB", RazerGamepadFunctionEncoder.Controller(RazerGamepadControllerButton.LeftBumper)),
        new("RazerGamepadAssignmentControllerRB", RazerGamepadFunctionEncoder.Controller(RazerGamepadControllerButton.RightBumper)),
        new("RazerGamepadAssignmentControllerLT", RazerGamepadFunctionEncoder.Controller(RazerGamepadControllerButton.LeftTrigger)),
        new("RazerGamepadAssignmentControllerRT", RazerGamepadFunctionEncoder.Controller(RazerGamepadControllerButton.RightTrigger)),
        new("RazerGamepadAssignmentControllerLS", RazerGamepadFunctionEncoder.Controller(RazerGamepadControllerButton.LeftStick)),
        new("RazerGamepadAssignmentControllerRS", RazerGamepadFunctionEncoder.Controller(RazerGamepadControllerButton.RightStick)),
        new("RazerGamepadAssignmentControllerDPadUp", RazerGamepadFunctionEncoder.Controller(RazerGamepadControllerButton.DPadUp)),
        new("RazerGamepadAssignmentControllerDPadDown", RazerGamepadFunctionEncoder.Controller(RazerGamepadControllerButton.DPadDown)),
        new("RazerGamepadAssignmentControllerDPadLeft", RazerGamepadFunctionEncoder.Controller(RazerGamepadControllerButton.DPadLeft)),
        new("RazerGamepadAssignmentControllerDPadRight", RazerGamepadFunctionEncoder.Controller(RazerGamepadControllerButton.DPadRight)),
        new("RazerGamepadAssignmentControllerView", RazerGamepadFunctionEncoder.Controller(RazerGamepadControllerButton.ViewOrCreate)),
        new("RazerGamepadAssignmentControllerMenu", RazerGamepadFunctionEncoder.Controller(RazerGamepadControllerButton.MenuOrOptions)),
        new("RazerGamepadAssignmentKeyW", RazerGamepadFunctionEncoder.Key(0x1A)),
        new("RazerGamepadAssignmentKeyA", RazerGamepadFunctionEncoder.Key(0x04)),
        new("RazerGamepadAssignmentKeyS", RazerGamepadFunctionEncoder.Key(0x16)),
        new("RazerGamepadAssignmentKeyD", RazerGamepadFunctionEncoder.Key(0x07)),
        new("RazerGamepadAssignmentKeySpace", RazerGamepadFunctionEncoder.Key(0x2C)),
        new("RazerGamepadAssignmentKeyEnter", RazerGamepadFunctionEncoder.Key(0x28)),
        new("RazerGamepadAssignmentKeyEscape", RazerGamepadFunctionEncoder.Key(0x29)),
        new("RazerGamepadAssignmentKeyTab", RazerGamepadFunctionEncoder.Key(0x2B)),
    ];
}