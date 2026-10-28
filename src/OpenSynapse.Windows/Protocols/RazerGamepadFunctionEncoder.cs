namespace OpenSynapse.Windows.Protocols;

/// <summary>The HID modifier bits used by the keyboard function data.</summary>
[Flags]
public enum RazerGamepadModifiers : byte
{
    None = 0,
    LeftCtrl = 1,
    LeftShift = 2,
    LeftAlt = 4,
    LeftGui = 8,
    RightCtrl = 16,
    RightShift = 32,
    RightAlt = 64,
    RightGui = 128,
}

/// <summary>The DPI action encoded in byte 0 of the DPI function data.</summary>
public enum RazerGamepadDpiAction : byte
{
    Up = 1,
    Down = 2,
    Clutch = 5,
    CycleUp = 6,
    CycleDown = 7,
}

/// <summary>The profile-navigation action encoded in byte 0.</summary>
public enum RazerGamepadProfileAction : byte
{
    Up = 1,
    Down = 2,
    Specific = 3,
    CycleUp = 4,
    CycleDown = 5,
}

/// <summary>The scroll action encoded in byte 0 of the scroll-wheel function data.</summary>
public enum RazerGamepadScrollAction : byte
{
    ModeSwitch = 1,
    Up = 2,
    Down = 3,
    CycleUp = 4,
    CycleDown = 5,
}

/// <summary>How a macro assignment repeats; each value selects a different function id.</summary>
public enum RazerGamepadMacroPlayback
{
    Once,
    NTimes,
    ContinuousHeld,
    ContinuousToggle,
    Sequence,
}

public enum RazerGamepadMediaKey : ushort
{
    Eject = 184,
    Mute = 226,
    VolumeDown = 234,
    VolumeUp = 233,
    PreviousTrack = 182,
    Stop = 183,
    PlayPause = 205,
    NextTrack = 181,
    CycleApps = 671,
    BrightnessUp = 111,
    BrightnessDown = 112,
    Spotlight = 545,
}

public enum RazerGamepadPowerKey : byte
{
    SystemPower = 129,
    Sleep = 130,
    Wake = 131,
}

public enum RazerGamepadWindowsShortcut : byte
{
    Screenshot = 99,
    XboxGameBar = 100,
    CycleApps = 101,
}

/// <summary>The controller-to-controller targets from the shared 58701 map.</summary>
public enum RazerGamepadControllerButton : byte
{
    M1 = 16,
    M2 = 17,
    M3 = 18,
    M4 = 19,
    M5 = 20,
    M6 = 21,
    AOrCross = 32,
    BOrCircle = 33,
    XOrSquare = 34,
    YOrTriangle = 35,
    LeftBumper = 36,
    RightBumper = 37,
    LeftTrigger = 38,
    RightTrigger = 39,
    LeftStick = 40,
    RightStick = 41,
    DPadUp = 44,
    DPadDown = 45,
    DPadLeft = 46,
    DPadRight = 47,
    ViewOrCreate = 52,
    MenuOrOptions = 53,
    LeftSensitivityClutch = 55,
    RightSensitivityClutch = 56,
    GlobalSensitivityClutch = 58,
    Touchpad = 59,
    LeftStickUp = 128,
    LeftStickDown = 129,
    LeftStickLeft = 130,
    LeftStickRight = 131,
    RightStickUp = 132,
    RightStickDown = 133,
    RightStickLeft = 134,
    RightStickRight = 135,
}

/// <summary>The Razer-key table from module 7215 (ZI).</summary>
public enum RazerGamepadRazerKey : byte
{
    FnKey = 1,
    RazerKey = 2,
    GameModeKey = 3,
    MacroRecordKey = 4,
    DisplayInternalExternal = 5,
    DisplayBrightnessUp = 6,
    DisplayBrightnessDown = 7,
    KeyboardBacklightUp = 8,
    KeyboardBacklightDown = 9,
    ToggleLowPowerMode = 11,
    MouseDpiUp = 32,
    MouseDpiDown = 33,
    MouseBackward = 34,
    MouseForward = 35,
    MacroKeys = 36,
    SystemSleep = 76,
    MacroKeysEnd = 79,
    ProfileSwitch = 80,
    MouseSniper = 81,
    MouseDpiCycleUp = 82,
    MouseDpiCycleDown = 83,
    MouseScrollWheelFreeNotchSwitch = 84,
    HapticScrollStageUp = 85,
    HapticScrollStageDown = 86,
    HapticScrollStageCycleUp = 87,
    HapticScrollStageCycleDown = 88,
    HyperShift = 89,
    ActuationAdjustmentMode = 112,
    RapidTriggerAdjustmentMode = 113,
    SnapTapToggle = 117,
    MicMute = 216,
    PrintScreen = 70,
}

/// <summary>The encoded function byte block: five data bytes plus the valid size.</summary>
public sealed record RazerGamepadFunctionData(
    RazerGamepadFunction Function,
    byte Size,
    byte[] Data);

/// <summary>
/// Encodes the function blocks from module 4442. Every encoder returns the same
/// five-byte buffer the middleware creates; <see cref="RazerGamepadFunctionData.Size"/>
/// is the byte count the protocol writes at payload offset 4.
/// </summary>
public static class RazerGamepadFunctionEncoder
{
    public static RazerGamepadFunctionData Off() =>
        new(RazerGamepadFunction.Off, 0, new byte[5]);

    public static RazerGamepadFunctionData Button(RazerGamepadButtonId button)
    {
        var data = new byte[5];
        data[0] = (byte)button;
        return new(RazerGamepadFunction.ButtonCode, 1, data);
    }

    public static RazerGamepadFunctionData Key(
        byte hidUsage,
        RazerGamepadModifiers modifiers = RazerGamepadModifiers.None,
        bool nKeyRollOver = false,
        ushort? keysPerSecond = null)
    {
        var data = new byte[5];
        var modifierFlag = nKeyRollOver ? ModifierFlag(hidUsage) : null;
        if (modifierFlag is not null)
        {
            data[0] = (byte)modifierFlag.Value;
        }
        else
        {
            data[0] = (byte)modifiers;
            data[1] = hidUsage;
        }

        if (keysPerSecond is null)
        {
            return new(RazerGamepadFunction.KeyCode, 2, data);
        }

        WriteUInt16(data, 2, Interval(keysPerSecond.Value));
        return new(RazerGamepadFunction.TurboModeKey, 4, data);
    }

    public static RazerGamepadFunctionData TurboButton(
        RazerGamepadButtonId button,
        ushort keysPerSecond = 7)
    {
        var data = new byte[5];
        data[0] = (byte)button;
        WriteUInt16(data, 1, Interval(keysPerSecond));
        return new(RazerGamepadFunction.TurboModeButton, 3, data);
    }

    public static RazerGamepadFunctionData Macro(
        ushort macroId,
        RazerGamepadMacroPlayback playback,
        byte repeatCount = 1)
    {
        var data = new byte[5];
        WriteUInt16(data, 0, macroId);
        return playback switch
        {
            RazerGamepadMacroPlayback.Once => MacroData(RazerGamepadFunction.MacroTypeI, data, 1),
            RazerGamepadMacroPlayback.NTimes => MacroData(RazerGamepadFunction.MacroTypeI, data, repeatCount),
            RazerGamepadMacroPlayback.ContinuousHeld => new(RazerGamepadFunction.MacroTypeII, 2, data),
            RazerGamepadMacroPlayback.ContinuousToggle => new(RazerGamepadFunction.MacroTypeIII, 2, data),
            RazerGamepadMacroPlayback.Sequence => new(RazerGamepadFunction.MacroTypeIV, 2, data),
            _ => throw new ArgumentOutOfRangeException(nameof(playback)),
        };
    }

    public static RazerGamepadFunctionData Dpi(
        RazerGamepadDpiAction action,
        ushort x = 0,
        ushort y = 0)
    {
        var data = new byte[5];
        data[0] = (byte)action;
        if (action != RazerGamepadDpiAction.Clutch)
        {
            return new(RazerGamepadFunction.Dpi, 1, data);
        }

        WriteUInt16(data, 1, x);
        WriteUInt16(data, 3, y);
        return new(RazerGamepadFunction.Dpi, 5, data);
    }

    public static RazerGamepadFunctionData Profile(
        RazerGamepadProfileAction action,
        byte profileId = 0)
    {
        var data = new byte[5];
        data[0] = (byte)action;
        if (action != RazerGamepadProfileAction.Specific)
        {
            return new(RazerGamepadFunction.Profile, 1, data);
        }

        data[1] = profileId;
        return new(RazerGamepadFunction.Profile, 2, data);
    }

    public static RazerGamepadFunctionData Multimedia(RazerGamepadMediaKey key)
    {
        var data = new byte[5];
        WriteUInt16(data, 0, (ushort)key);
        return new(RazerGamepadFunction.MediaKeys, 2, data);
    }

    public static RazerGamepadFunctionData Power(RazerGamepadPowerKey key)
    {
        var data = new byte[5];
        data[0] = (byte)key;
        return new(RazerGamepadFunction.PowerKeys, 1, data);
    }

    public static RazerGamepadFunctionData WindowsShortcut(RazerGamepadWindowsShortcut key)
    {
        var data = new byte[5];
        data[0] = (byte)key;
        return new(RazerGamepadFunction.Win8ShortcutsKey, 1, data);
    }

    public static RazerGamepadFunctionData DoubleClick()
    {
        var data = new byte[5];
        data[0] = 1;
        return new(RazerGamepadFunction.DoubleClick, 1, data);
    }

    public static RazerGamepadFunctionData ModeButton()
    {
        var data = new byte[5];
        data[0] = 1;
        return new(RazerGamepadFunction.ModeButtonKey, 1, data);
    }

    public static RazerGamepadFunctionData Controller(RazerGamepadControllerButton button)
    {
        var data = new byte[5];
        data[0] = (byte)button;
        return new(RazerGamepadFunction.Controller, 1, data);
    }

    public static RazerGamepadFunctionData RazerKey(RazerGamepadRazerKey key)
    {
        var data = new byte[5];
        data[0] = (byte)key;
        return new(RazerGamepadFunction.RazerKey, 1, data);
    }

    public static RazerGamepadFunctionData Scroll(RazerGamepadScrollAction action)
    {
        var data = new byte[5];
        data[0] = (byte)action;
        return new(RazerGamepadFunction.ScrollWheelMode, 1, data);
    }

    private static RazerGamepadFunctionData MacroData(
        RazerGamepadFunction function,
        byte[] data,
        byte repeatCount)
    {
        data[2] = repeatCount;
        return new(function, 3, data);
    }

    private static ushort Interval(ushort keysPerSecond) =>
        (ushort)Math.Clamp(1000 / Math.Max((int)keysPerSecond, 1), 1, ushort.MaxValue);

    private static RazerGamepadModifiers? ModifierFlag(byte hidUsage) => hidUsage switch
    {
        224 => RazerGamepadModifiers.LeftCtrl,
        225 => RazerGamepadModifiers.LeftShift,
        226 => RazerGamepadModifiers.LeftAlt,
        227 => RazerGamepadModifiers.LeftGui,
        228 => RazerGamepadModifiers.RightCtrl,
        229 => RazerGamepadModifiers.RightShift,
        230 => RazerGamepadModifiers.RightAlt,
        231 => RazerGamepadModifiers.RightGui,
        _ => null,
    };

    private static void WriteUInt16(byte[] data, int offset, ushort value)
    {
        data[offset] = (byte)(value >> 8);
        data[offset + 1] = (byte)value;
    }
}
