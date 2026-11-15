namespace OpenSynapse.Windows.Protocols;

/// <summary>Analog stick dead zones, as the middleware reports them.</summary>
public readonly record struct RazerGamepadZones(
    ushort DeadZone,
    ushort SoftBumper,
    ushort DeltaThreshold,
    bool PreventDoubleDeadZone);

/// <summary>The analog channel type carried by a Control Id List entry.</summary>
public enum RazerGamepadAnalogType : byte
{
    NotApplicable = 0,
    JoystickX = 1,
    JoystickY = 2,
    JoystickZ = 3,
    Trigger = 4,
    VibrationMotor = 5,
}

/// <summary>The value written by the trigger-mode command.</summary>
public enum RazerGamepadTriggerMode : byte
{
    Analog = 0,
    Digital = 1,
}

/// <summary>The 0/1 option used by the stick-circularity command.</summary>
public enum RazerGamepadCircularity : byte
{
    Standard = 0,
    Circular = 1,
}

/// <summary>The SOCD cleaning mode from the middleware feature module.</summary>
public enum RazerGamepadSocdMode : byte
{
    Neutral = 0,
    LastInputPriority = 1,
    FirstInputPriority = 2,
    NoCleaning = 3,
    AbsoluteUpPriority = 4,
}

/// <summary>The calibration selector bit set from the middleware module.</summary>
[Flags]
public enum RazerGamepadCalibrationControl : byte
{
    None = 0,
    LeftJoystick = 1,
    RightJoystick = 2,
    LeftTrigger = 4,
    RightTrigger = 8,
}

/// <summary>Normal or Hypershift, as used by the button-assignment commands.</summary>
public enum RazerGamepadButtonMode : byte
{
    Normal = 0,
    Hypershift = 1,
}

/// <summary>The button id table from the middleware's shared enum module (35922).</summary>
public enum RazerGamepadButtonId : byte
{
    NotApplicable = 0,
    LeftButton = 1,
    RightButton = 2,
    ScrollButton = 3,
    Button4 = 4,
    Button5 = 5,
    Button6 = 6,
    Button7 = 7,
    Button8 = 8,
    ScrollUp = 9,
    ScrollDown = 10,
    DPIUp = 11,
    DPIDown = 12,
    UserSelect = 13,
    ProfileSwitch = 14,
    SniperButton = 15,
    Macro1 = 16,
    Macro2 = 17,
    Macro3 = 18,
    Macro4 = 19,
    Macro5 = 20,
    Macro6 = 21,
    Macro7 = 22,
    Macro8 = 23,
    Macro9 = 24,
    Macro10 = 25,
    Macro11 = 26,
    Macro12 = 27,
    Macro13 = 28,
    Macro14 = 29,
    Macro15 = 30,
    Macro16 = 31,
    A = 32,
    B = 33,
    X = 34,
    Y = 35,
    LBumper = 36,
    RBumper = 37,
    LTrigger = 38,
    RTrigger = 39,
    LJoystick = 40,
    RJoystick = 41,
    Start = 42,
    Back = 43,
    DPadUp = 44,
    DPadDown = 45,
    DPadLeft = 46,
    DPadRight = 47,
    LShoulder = 48,
    RShoulder = 49,
    XboxGuide = 50,
    ConnectWireless = 51,
    LeftTilt = 52,
    RightTilt = 53,
    ClutchGlobal = 54,
    ClutchLeft = 55,
    ClutchRight = 56,
    HyperShift = 57,
    Home = 58,
    Select = 59,
    View = 60,
    Share = 61,
    Power = 62,
    ButtonSleep = 63,
    SPad1 = 64,
    SPad2 = 65,
    SPad3 = 66,
    SPad4 = 67,
    SPad5 = 68,
    SPad6 = 69,
    SPad7 = 70,
    SPad8 = 71,
    SPad9 = 72,
    SPad10 = 73,
    SPad11 = 74,
    SPad12 = 75,
    HPad1 = 80,
    HPad2 = 81,
    HPad3 = 82,
    HPad4 = 83,
    HPad5 = 84,
    HPad6 = 85,
    HPad7 = 86,
    DPICycleUp = 96,
    DPICycleDown = 97,
    PowerButton = 98,
    PreviousButton = 99,
    NextButton = 100,
    VolumeUp = 101,
    VolumeDown = 102,
    Mute = 103,
    ScrollLeft = 104,
    ScrollRight = 105,
    ScrollFreeNotchSwitch = 106,
    ScrollHapticStageUp = 107,
    ScrollHapticStageDown = 108,
    ScrollHapticCycleUp = 109,
    ScrollHapticCycleDown = 110,
    BleepButton = 112,
    L4IOSController = 113,
    R4IOSController = 114,
    L5IOSController = 115,
    R5IOSController = 116,
    CController = 117,
    ZController = 118,
    PrintScreen = 119,
    CaptureScreen = 120,
    RapidFire = 121,
}

/// <summary>The function id table from the middleware's shared enum module (35922).</summary>
public enum RazerGamepadFunction : byte
{
    Off = 0,
    ButtonCode = 1,
    KeyCode = 2,
    MacroTypeI = 3,
    MacroTypeII = 4,
    MacroTypeIII = 5,
    Dpi = 6,
    Profile = 7,
    Lighting = 8,
    PowerKeys = 9,
    MediaKeys = 10,
    DoubleClick = 11,
    ModeButtonKey = 12,
    TurboModeKey = 13,
    TurboModeButton = 14,
    MacroTypeIV = 15,
    Controller = 16,
    RazerKey = 17,
    ScrollWheelMode = 18,
    Win8ShortcutsKey = 21,
}

public readonly record struct RazerGamepadAnalogControl(
    byte ClassId,
    byte ControlId,
    RazerGamepadAnalogType Type,
    byte Index);

public readonly record struct RazerGamepadDigitalTrigger(
    byte ActuationPoint,
    bool IsRapidTrigger);

public sealed record RazerGamepadButtonAssignment(
    byte ButtonId,
    RazerGamepadButtonMode Mode,
    RazerGamepadFunction Function,
    byte DataSize,
    IReadOnlyList<byte> Data);

public readonly record struct RazerGamepadRawInput(
    ushort LeftX,
    ushort LeftY,
    ushort RightX,
    ushort RightY,
    ushort LeftTrigger,
    ushort RightTrigger);

public sealed record RazerGamepadCalibration(byte AnalogId, IReadOnlyList<ushort> Values);

/// <summary>
/// Protocol 25 class 0x0C gamepad commands shared by the Wolverine V3/V4 and Raiju V3
/// bundles (chunk 2620). Every request is a 91-byte feature report; the service supplies
/// report id 4, or 10 for 0A57/0A74/0A7C as those bundles declare. The rest of the frame
/// matches <see cref="RazerFeatureReport"/>.
/// Triples are [dataSize, class, id]:
///   sensitivity   get [3,12,130] set [3,12,2]
///   zones         get [9,12,133] set [9,12,5]
///   vibration     get [3,12,134] set [3,12,6]
///   trigger range get [4,12,147] set [4,12,19]
///   trigger mode  get [3,12,149] set [3,12,21]
/// The first two payload bytes are the profile id and control id; replies echo the
/// class and control id before the value. Analog-control and calibration commands use
/// their own documented argument blocks.
/// </summary>
public static partial class RazerGamepadProtocol
{
}
