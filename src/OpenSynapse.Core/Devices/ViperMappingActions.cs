namespace OpenSynapse.Core.Devices;

public enum ViperMappingActionGroup
{
    Mouse,
    Keyboard,
    Dpi,
    Profile,
    Media,
    Power,
    Scroll,
    RazerKey,
    WindowsShortcut,
    HyperShift,
    Turbo,
}

/// <summary>
/// One selectable Product 184 onboard mapping target. The payloads mirror the
/// 00B8 middleware mapping engine; MacroTypeI-IV are accepted on read so a
/// Synapse-assigned macro does not break the mapping page.
/// </summary>
public sealed record ViperMappingActionDefinition(
    ViperMappingActionGroup Group,
    string NameKey,
    ViperButtonMappingFunction Function,
    byte Value = 0,
    ushort MediaCode = 0,
    bool UsesKeyboard = false,
    bool UsesDpiClutch = false,
    bool UsesTurboRate = false,
    bool UsesTurboButton = false,
    bool UsesProfileId = false);

public static class ViperMappingActions
{
    public static IReadOnlyList<ViperMappingActionDefinition> Definitions { get; } =
    [
        new(ViperMappingActionGroup.Mouse, "Text_39B523BD", ViperButtonMappingFunction.Off),
        MouseButton(1), MouseButton(2), MouseButton(3), MouseButton(4), MouseButton(5),
        MouseButton(6), MouseButton(7), MouseButton(8), MouseButton(9), MouseButton(10),
        new(ViperMappingActionGroup.Keyboard, "Text_80A48F18", ViperButtonMappingFunction.KeyboardKey,
            UsesKeyboard: true),
        new(ViperMappingActionGroup.Mouse, "Text_45977270", ViperButtonMappingFunction.DoubleClick),
        new(ViperMappingActionGroup.Dpi, "ViperMappingDpiUp", ViperButtonMappingFunction.Dpi, 1),
        new(ViperMappingActionGroup.Dpi, "ViperMappingDpiDown", ViperButtonMappingFunction.Dpi, 2),
        new(ViperMappingActionGroup.Dpi, "Text_D9D5D89A", ViperButtonMappingFunction.Dpi, 6),
        new(ViperMappingActionGroup.Dpi, "ViperMappingDpiCycleDown", ViperButtonMappingFunction.Dpi, 7),
        new(ViperMappingActionGroup.Dpi, "ViperMappingDpiClutch", ViperButtonMappingFunction.Dpi,
            UsesDpiClutch: true),
        new(ViperMappingActionGroup.Profile, "ViperMappingProfileUp", ViperButtonMappingFunction.Profile, 1),
        new(ViperMappingActionGroup.Profile, "ViperMappingProfileDown", ViperButtonMappingFunction.Profile, 2),
        new(ViperMappingActionGroup.Profile, "ViperMappingProfileCycleUp", ViperButtonMappingFunction.Profile, 4),
        new(ViperMappingActionGroup.Profile, "ViperMappingProfileCycleDown", ViperButtonMappingFunction.Profile, 5),
        new(ViperMappingActionGroup.Profile, "ViperMappingProfileSpecific", ViperButtonMappingFunction.Profile,
            3, UsesProfileId: true),
        Media("MacroMediaMute", 0x00E2),
        Media("MacroMediaVolumeDown", 0x00EA),
        Media("MacroMediaVolumeUp", 0x00E9),
        Media("MacroMediaPrevious", 0x00B6),
        Media("MacroMediaPlayPause", 0x00CD),
        Media("MacroMediaNext", 0x00B5),
        Media("MacroMediaStop", 0x00B7),
        Media("ViperMappingMediaEject", 0x00B8),
        Media("ViperMappingMediaCycleApps", 0x029F),
        Media("ViperMappingMediaBrightnessUp", 0x006F),
        Media("ViperMappingMediaBrightnessDown", 0x0070),
        Media("ViperMappingMediaSpotlight", 0x0221),
        Simple(ViperMappingActionGroup.Power, "ViperMappingPower", ViperButtonMappingFunction.Power, 129),
        Simple(ViperMappingActionGroup.Power, "ViperMappingSleep", ViperButtonMappingFunction.Power, 130),
        Simple(ViperMappingActionGroup.Power, "ViperMappingWake", ViperButtonMappingFunction.Power, 131),
        Simple(ViperMappingActionGroup.Scroll, "ViperMappingScrollUp", ViperButtonMappingFunction.ScrollWheelMode, 2),
        Simple(ViperMappingActionGroup.Scroll, "ViperMappingScrollDown", ViperButtonMappingFunction.ScrollWheelMode, 3),
        Simple(ViperMappingActionGroup.Scroll, "ViperMappingScrollCycleUp", ViperButtonMappingFunction.ScrollWheelMode, 4),
        Simple(ViperMappingActionGroup.Scroll, "ViperMappingScrollCycleDown", ViperButtonMappingFunction.ScrollWheelMode, 5),
        Simple(ViperMappingActionGroup.RazerKey, "ViperMappingRazerFn", ViperButtonMappingFunction.RazerKey, 1),
        Simple(ViperMappingActionGroup.RazerKey, "ViperMappingRazerKey", ViperButtonMappingFunction.RazerKey, 2),
        Simple(ViperMappingActionGroup.RazerKey, "ViperMappingGameMode", ViperButtonMappingFunction.RazerKey, 3),
        Simple(ViperMappingActionGroup.RazerKey, "ViperMappingMacroRecord", ViperButtonMappingFunction.RazerKey, 4),
        Simple(ViperMappingActionGroup.RazerKey, "ViperMappingSwitchDisplay", ViperButtonMappingFunction.RazerKey, 5),
        Simple(ViperMappingActionGroup.RazerKey, "ViperMappingDisplayBrightnessUp", ViperButtonMappingFunction.RazerKey, 6),
        Simple(ViperMappingActionGroup.RazerKey, "ViperMappingDisplayBrightnessDown", ViperButtonMappingFunction.RazerKey, 7),
        Simple(ViperMappingActionGroup.RazerKey, "ViperMappingKeyboardBacklightUp", ViperButtonMappingFunction.RazerKey, 8),
        Simple(ViperMappingActionGroup.RazerKey, "ViperMappingKeyboardBacklightDown", ViperButtonMappingFunction.RazerKey, 9),
        Simple(ViperMappingActionGroup.RazerKey, "ViperMappingLowPower", ViperButtonMappingFunction.RazerKey, 11),
        Simple(ViperMappingActionGroup.RazerKey, "ViperMappingRazerDpiUp", ViperButtonMappingFunction.RazerKey, 32),
        Simple(ViperMappingActionGroup.RazerKey, "ViperMappingRazerDpiDown", ViperButtonMappingFunction.RazerKey, 33),
        Simple(ViperMappingActionGroup.RazerKey, "ViperMappingBack", ViperButtonMappingFunction.RazerKey, 34),
        Simple(ViperMappingActionGroup.RazerKey, "ViperMappingForward", ViperButtonMappingFunction.RazerKey, 35),
        Simple(ViperMappingActionGroup.RazerKey, "ViperMappingMacroKeys", ViperButtonMappingFunction.RazerKey, 36),
        Simple(ViperMappingActionGroup.RazerKey, "ViperMappingPrintScreen", ViperButtonMappingFunction.RazerKey, 70),
        Simple(ViperMappingActionGroup.RazerKey, "ViperMappingSystemSleep", ViperButtonMappingFunction.RazerKey, 76),
        Simple(ViperMappingActionGroup.RazerKey, "ViperMappingProfileSwitch", ViperButtonMappingFunction.RazerKey, 80),
        Simple(ViperMappingActionGroup.RazerKey, "ViperMappingSniper", ViperButtonMappingFunction.RazerKey, 81),
        Simple(ViperMappingActionGroup.RazerKey, "ViperMappingRazerDpiCycleUp", ViperButtonMappingFunction.RazerKey, 82),
        Simple(ViperMappingActionGroup.RazerKey, "ViperMappingRazerDpiCycleDown", ViperButtonMappingFunction.RazerKey, 83),
        Simple(ViperMappingActionGroup.RazerKey, "ViperMappingScrollModeSwitch", ViperButtonMappingFunction.RazerKey, 84),
        Simple(ViperMappingActionGroup.RazerKey, "ViperMappingHapticScrollUp", ViperButtonMappingFunction.RazerKey, 85),
        Simple(ViperMappingActionGroup.RazerKey, "ViperMappingHapticScrollDown", ViperButtonMappingFunction.RazerKey, 86),
        Simple(ViperMappingActionGroup.RazerKey, "ViperMappingHapticScrollCycleUp", ViperButtonMappingFunction.RazerKey, 87),
        Simple(ViperMappingActionGroup.RazerKey, "ViperMappingHapticScrollCycleDown", ViperButtonMappingFunction.RazerKey, 88),
        Simple(ViperMappingActionGroup.RazerKey, "ViperMappingHyperShift", ViperButtonMappingFunction.RazerKey, 89),
        Simple(ViperMappingActionGroup.RazerKey, "ViperMappingActuation", ViperButtonMappingFunction.RazerKey, 112),
        Simple(ViperMappingActionGroup.RazerKey, "ViperMappingRapidTrigger", ViperButtonMappingFunction.RazerKey, 113),
        Simple(ViperMappingActionGroup.RazerKey, "ViperMappingSnapTap", ViperButtonMappingFunction.RazerKey, 117),
        Simple(ViperMappingActionGroup.RazerKey, "ViperMappingMicMute", ViperButtonMappingFunction.RazerKey, 216),
        Simple(ViperMappingActionGroup.WindowsShortcut, "ViperMappingScreenshot", ViperButtonMappingFunction.WindowsShortcut, 99),
        Simple(ViperMappingActionGroup.WindowsShortcut, "ViperMappingXboxGameBar", ViperButtonMappingFunction.WindowsShortcut, 100),
        Simple(ViperMappingActionGroup.WindowsShortcut, "ViperMappingCycleApps", ViperButtonMappingFunction.WindowsShortcut, 101),
        new(ViperMappingActionGroup.HyperShift, "ViperMappingHyperShiftToggle", ViperButtonMappingFunction.HyperShift),
        new(ViperMappingActionGroup.Turbo, "ViperMappingTurboKey", ViperButtonMappingFunction.KeyboardTurbo,
            UsesKeyboard: true, UsesTurboRate: true),
        new(ViperMappingActionGroup.Turbo, "ViperMappingTurboButton", ViperButtonMappingFunction.MouseTurbo,
            UsesTurboButton: true, UsesTurboRate: true),
    ];

    public static bool TryApply(
        ViperButtonAssignment source,
        int index,
        byte modifier,
        byte usage,
        int dpiX,
        int dpiY,
        int turboRate,
        byte turboButton,
        out ViperButtonAssignment result,
        int profileId = 1)
    {
        if (index < 0 || index >= Definitions.Count)
        {
            result = source;
            return false;
        }

        var definition = Definitions[index];
        var interval = Interval(turboRate);
        var data = definition.Function switch
        {
            ViperButtonMappingFunction.Off => Array.Empty<byte>(),
            ViperButtonMappingFunction.MouseButton => [definition.Value],
            ViperButtonMappingFunction.KeyboardKey => [modifier, usage],
            ViperButtonMappingFunction.DoubleClick => [(byte)1],
            ViperButtonMappingFunction.Dpi when definition.UsesDpiClutch =>
                [(byte)5, High(ClampDpi(dpiX)), Low(ClampDpi(dpiX)),
                    High(ClampDpi(dpiY)), Low(ClampDpi(dpiY))],
            ViperButtonMappingFunction.Dpi => [definition.Value],
            ViperButtonMappingFunction.Profile when definition.UsesProfileId =>
                [(byte)3, (byte)Math.Clamp(profileId, 1, 5)],
            ViperButtonMappingFunction.Profile => [definition.Value],
            ViperButtonMappingFunction.Power => [definition.Value],
            ViperButtonMappingFunction.ScrollWheelMode => [definition.Value],
            ViperButtonMappingFunction.MediaKey => [High(definition.MediaCode), Low(definition.MediaCode)],
            ViperButtonMappingFunction.RazerKey => [definition.Value],
            ViperButtonMappingFunction.WindowsShortcut => [definition.Value],
            ViperButtonMappingFunction.HyperShift => [(byte)1],
            ViperButtonMappingFunction.KeyboardTurbo =>
                [modifier, usage, High(interval), Low(interval)],
            ViperButtonMappingFunction.MouseTurbo =>
                [turboButton, High(interval), Low(interval)],
            _ => null,
        };
        if (data is null)
        {
            result = source;
            return false;
        }

        result = source with { Function = definition.Function, FunctionData = data };
        return true;
    }

    public static int IndexOf(ViperButtonAssignment assignment) =>
        assignment.Function switch
        {
            ViperButtonMappingFunction.Off => Find(ViperButtonMappingFunction.Off, _ => true),
            ViperButtonMappingFunction.MouseButton when assignment.FunctionData.Count == 1 =>
                Find(ViperButtonMappingFunction.MouseButton,
                    definition => definition.Value == assignment.FunctionData[0]),
            ViperButtonMappingFunction.KeyboardKey =>
                Find(ViperButtonMappingFunction.KeyboardKey, _ => true),
            ViperButtonMappingFunction.DoubleClick =>
                Find(ViperButtonMappingFunction.DoubleClick, _ => true),
            ViperButtonMappingFunction.Dpi when assignment.FunctionData.Count == 5 &&
                assignment.FunctionData[0] == 5 =>
                Find(ViperButtonMappingFunction.Dpi, definition => definition.UsesDpiClutch),
            ViperButtonMappingFunction.Dpi when assignment.FunctionData.Count == 1 =>
                Find(ViperButtonMappingFunction.Dpi,
                    definition => !definition.UsesDpiClutch &&
                        definition.Value == assignment.FunctionData[0]),
            ViperButtonMappingFunction.Profile when assignment.FunctionData.Count >= 2 &&
                assignment.FunctionData[0] == 3 =>
                Find(ViperButtonMappingFunction.Profile, definition => definition.UsesProfileId),
            ViperButtonMappingFunction.Profile when assignment.FunctionData.Count == 1 =>
                Find(ViperButtonMappingFunction.Profile,
                    definition => definition.Value == assignment.FunctionData[0]),
            ViperButtonMappingFunction.Power when assignment.FunctionData.Count == 1 =>
                Find(ViperButtonMappingFunction.Power,
                    definition => definition.Value == assignment.FunctionData[0]),
            ViperButtonMappingFunction.ScrollWheelMode when assignment.FunctionData.Count == 1 =>
                Find(ViperButtonMappingFunction.ScrollWheelMode,
                    definition => definition.Value == assignment.FunctionData[0]),
            ViperButtonMappingFunction.MediaKey when assignment.FunctionData.Count == 2 =>
                Find(ViperButtonMappingFunction.MediaKey, definition =>
                    definition.MediaCode == ((assignment.FunctionData[0] << 8) |
                        assignment.FunctionData[1])),
            ViperButtonMappingFunction.RazerKey when assignment.FunctionData.Count == 1 =>
                Find(ViperButtonMappingFunction.RazerKey,
                    definition => definition.Value == assignment.FunctionData[0]),
            ViperButtonMappingFunction.WindowsShortcut when assignment.FunctionData.Count == 1 =>
                Find(ViperButtonMappingFunction.WindowsShortcut,
                    definition => definition.Value == assignment.FunctionData[0]),
            ViperButtonMappingFunction.HyperShift =>
                Find(ViperButtonMappingFunction.HyperShift, _ => true),
            ViperButtonMappingFunction.KeyboardTurbo =>
                Find(ViperButtonMappingFunction.KeyboardTurbo, _ => true),
            ViperButtonMappingFunction.MouseTurbo when assignment.FunctionData.Count >= 1 =>
                Find(ViperButtonMappingFunction.MouseTurbo,
                    definition => definition.Value == assignment.FunctionData[0]),
            _ => -1,
        };

    public static int GroupStart(ViperMappingActionGroup group)
    {
        for (var index = 0; index < Definitions.Count; index++)
        {
            if (Definitions[index].Group == group)
            {
                return index;
            }
        }
        return -1;
    }

    private static int Find(
        ViperButtonMappingFunction function,
        Func<ViperMappingActionDefinition, bool> predicate)
    {
        for (var index = 0; index < Definitions.Count; index++)
        {
            if (Definitions[index].Function == function && predicate(Definitions[index]))
            {
                return index;
            }
        }
        return -1;
    }

    private static ViperMappingActionDefinition MouseButton(byte code) =>
        new(ViperMappingActionGroup.Mouse, "", ViperButtonMappingFunction.MouseButton, code);

    private static ViperMappingActionDefinition Media(string nameKey, ushort code) =>
        new(ViperMappingActionGroup.Media, nameKey, ViperButtonMappingFunction.MediaKey, MediaCode: code);

    private static ViperMappingActionDefinition Simple(
        ViperMappingActionGroup group,
        string nameKey,
        ViperButtonMappingFunction function,
        byte value) =>
        new(group, nameKey, function, value);

    private static ushort Interval(int keysPerSecond)
    {
        var rate = Math.Clamp(keysPerSecond, 1, 50);
        return (ushort)Math.Clamp(1000 / rate, 1, ushort.MaxValue);
    }

    private static int ClampDpi(int value) =>
        Math.Clamp((int)Math.Round(value / 50d, MidpointRounding.AwayFromZero) * 50, 100, 30000);

    private static byte High(int value) => checked((byte)(value >> 8));

    private static byte Low(int value) => (byte)(value & 0xFF);
}
