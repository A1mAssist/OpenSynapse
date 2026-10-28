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
/// bundles (chunk 2620). Every request is a 91-byte feature report with report id 4
/// instead of 0; the rest of the frame matches <see cref="RazerFeatureReport"/>.
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
public static class RazerGamepadProtocol
{
    public const byte ReportId = 4;
    public const byte CommandClass = 0x0C;
    public const byte ProfileCommandClass = 0x05;
    public const byte ButtonCommandClass = 0x02;

    private const byte ControlIdListGet = 0x80;
    private const byte AnalogControlGet = 0x81;
    private const byte SensitivityGet = 0x82;
    private const byte SensitivitySet = 0x02;
    private const byte ZonesGet = 0x85;
    private const byte ZonesSet = 0x05;
    private const byte VibrationGet = 0x86;
    private const byte VibrationSet = 0x06;
    private const byte AdcMaskGet = 0x91;
    private const byte AdcMaskSet = 0x11;
    private const byte CircularityGet = 0x92;
    private const byte CircularitySet = 0x12;
    private const byte TriggerRangeGet = 0x93;
    private const byte TriggerRangeSet = 0x13;
    private const byte TriggerModeGet = 0x95;
    private const byte TriggerModeSet = 0x15;
    private const byte DigitalTriggerGet = 0x96;
    private const byte DigitalTriggerSet = 0x16;
    private const byte IgnoreDPadDiagonalsGet = 0x97;
    private const byte IgnoreDPadDiagonalsSet = 0x17;
    private const byte RawInputGet = 0x98;
    private const byte CalibrationGet = 0x99;
    private const byte CalibrationSet = 0x19;
    private const byte SocdGet = 0x9B;
    private const byte SocdSet = 0x1B;
    private const byte ControllerModeSwitchGet = 0x9C;
    private const byte ControllerModeSwitchSet = 0x1C;
    private const byte ActiveProfileGet = 0x84;
    private const byte ButtonIdListGet = 0x80;
    private const byte SingleButtonAssignmentGet = 0x8C;
    private const byte SingleButtonAssignmentSet = 0x0C;

    public static byte[] CreateGetControlIdListRequest(byte transactionId) =>
        Create(transactionId, 80, ControlIdListGet, new byte[80]);

    public static byte[] CreateGetAnalogControlRequest(
        byte transactionId,
        byte profileId,
        byte controlId,
        byte index) =>
        Create(transactionId, 4, AnalogControlGet, [profileId, controlId, index, 0x00]);

    public static byte[] CreateGetSensitivityRequest(
        byte transactionId,
        byte profileId,
        byte controlId) =>
        Create(transactionId, 3, SensitivityGet, [profileId, controlId, 0x00]);

    public static byte[] CreateSetSensitivityRequest(
        byte transactionId,
        byte profileId,
        byte controlId,
        byte sensitivity) =>
        Create(transactionId, 3, SensitivitySet, [profileId, controlId, sensitivity]);

    public static byte[] CreateGetZonesRequest(
        byte transactionId,
        byte profileId,
        byte controlId) =>
        Create(transactionId, 9, ZonesGet, [profileId, controlId, 0, 0, 0, 0, 0, 0, 0]);

    public static byte[] CreateSetZonesRequest(
        byte transactionId,
        byte profileId,
        byte controlId,
        RazerGamepadZones zones) =>
        Create(transactionId, 9, ZonesSet,
        [
            profileId,
            controlId,
            (byte)(zones.DeadZone >> 8),
            (byte)zones.DeadZone,
            (byte)(zones.SoftBumper >> 8),
            (byte)zones.SoftBumper,
            (byte)(zones.DeltaThreshold >> 8),
            (byte)zones.DeltaThreshold,
            zones.PreventDoubleDeadZone ? (byte)1 : (byte)0,
        ]);

    public static byte[] CreateGetVibrationScaleRequest(
        byte transactionId,
        byte profileId,
        byte motorId) =>
        Create(transactionId, 3, VibrationGet, [profileId, motorId, 0x00]);

    public static byte[] CreateSetVibrationScaleRequest(
        byte transactionId,
        byte profileId,
        byte motorId,
        byte scale) =>
        Create(transactionId, 3, VibrationSet, [profileId, motorId, scale]);

    public static byte[] CreateGetTriggerRangeRequest(
        byte transactionId,
        byte profileId,
        byte controlId) =>
        Create(transactionId, 4, TriggerRangeGet, [profileId, controlId, 0x00, 0x00]);

    public static byte[] CreateSetTriggerRangeRequest(
        byte transactionId,
        byte profileId,
        byte controlId,
        byte start,
        byte end) =>
        Create(transactionId, 4, TriggerRangeSet, [profileId, controlId, start, end]);

    public static byte[] CreateGetTriggerModeRequest(
        byte transactionId,
        byte profileId,
        byte controlId) =>
        Create(transactionId, 3, TriggerModeGet, [profileId, controlId, 0x00]);

    public static byte[] CreateSetTriggerModeRequest(
        byte transactionId,
        byte profileId,
        byte controlId,
        byte mode) =>
        Create(transactionId, 3, TriggerModeSet, [profileId, controlId, mode]);

    public static byte[] CreateGetAdcMaskRequest(byte transactionId) =>
        Create(transactionId, 2, AdcMaskGet, [0x00, 0x00]);

    public static byte[] CreateSetAdcMaskRequest(byte transactionId, ushort mask) =>
        Create(transactionId, 2, AdcMaskSet,
        [
            (byte)(mask >> 8),
            (byte)mask,
        ]);

    public static byte[] CreateGetCircularityRequest(
        byte transactionId,
        byte profileId,
        byte controlId) =>
        Create(transactionId, 3, CircularityGet, [profileId, controlId, 0x00]);

    public static byte[] CreateSetCircularityRequest(
        byte transactionId,
        byte profileId,
        byte controlId,
        RazerGamepadCircularity option) =>
        Create(transactionId, 3, CircularitySet, [profileId, controlId, (byte)option]);

    public static byte[] CreateGetDigitalTriggerRequest(
        byte transactionId,
        byte profileId,
        byte controlId) =>
        Create(transactionId, 4, DigitalTriggerGet, [profileId, controlId, 0x00, 0x00]);

    public static byte[] CreateSetDigitalTriggerRequest(
        byte transactionId,
        byte profileId,
        byte controlId,
        RazerGamepadDigitalTrigger trigger) =>
        Create(transactionId, 4, DigitalTriggerSet,
            [profileId, controlId, trigger.ActuationPoint, trigger.IsRapidTrigger ? (byte)0x01 : (byte)0x00]);

    public static byte[] CreateGetIgnoreDPadDiagonalsRequest(byte transactionId, byte profileId) =>
        Create(transactionId, 2, IgnoreDPadDiagonalsGet, [profileId, 0x00]);

    public static byte[] CreateSetIgnoreDPadDiagonalsRequest(
        byte transactionId,
        byte profileId,
        bool enabled) =>
        Create(transactionId, 2, IgnoreDPadDiagonalsSet, [profileId, enabled ? (byte)0x01 : (byte)0x00]);

    public static byte[] CreateGetSocdRequest(byte transactionId, byte profileId) =>
        Create(transactionId, 2, SocdGet, [profileId, 0x00]);

    public static byte[] CreateSetSocdRequest(
        byte transactionId,
        byte profileId,
        RazerGamepadSocdMode mode) =>
        Create(transactionId, 2, SocdSet, [profileId, (byte)mode]);

    public static byte[] CreateGetControllerModeSwitchRequest(byte transactionId, byte profileId) =>
        Create(transactionId, 2, ControllerModeSwitchGet, [profileId, 0x00]);

    public static byte[] CreateSetControllerModeSwitchRequest(
        byte transactionId,
        byte profileId,
        byte option) =>
        Create(transactionId, 2, ControllerModeSwitchSet, [profileId, option]);

    public static byte[] CreateGetActiveProfileIdRequest(byte transactionId) =>
        CreateForClass(transactionId, 1, ProfileCommandClass, ActiveProfileGet, [0x00]);

    public static byte[] CreateGetButtonIdListRequest(byte transactionId) =>
        CreateForClass(transactionId, 80, ButtonCommandClass, ButtonIdListGet, new byte[80]);

    public static IReadOnlyList<byte> ReadButtonIdList(ReadOnlySpan<byte> response)
    {
        var data = ReadData(response);
        var total = data[0];
        if (total > 80 || data.Length < total + 1)
        {
            throw new InvalidDataException("The gamepad button-id reply is truncated.");
        }

        return data.Slice(1, total).ToArray();
    }

    public static byte[] CreateGetButtonAssignmentRequest(
        byte transactionId,
        byte profileId,
        byte buttonId,
        RazerGamepadButtonMode mode)
    {
        var arguments = new byte[80];
        arguments[0] = profileId;
        arguments[1] = buttonId;
        arguments[2] = (byte)mode;
        return CreateForClass(transactionId, 80, ButtonCommandClass, SingleButtonAssignmentGet, arguments);
    }

    public static byte[] CreateSetButtonAssignmentRequest(
        byte transactionId,
        byte profileId,
        byte buttonId,
        RazerGamepadButtonMode mode,
        RazerGamepadFunction function,
        byte dataSize,
        ReadOnlySpan<byte> data)
    {
        if (dataSize > 5 || data.Length > 5)
        {
            throw new ArgumentException("Button-assignment data is limited to five bytes.", nameof(data));
        }

        var arguments = new byte[80];
        arguments[0] = profileId;
        arguments[1] = buttonId;
        arguments[2] = (byte)mode;
        arguments[3] = (byte)function;
        arguments[4] = dataSize;
        data.CopyTo(arguments.AsSpan(5));
        return CreateForClass(transactionId, 80, ButtonCommandClass, SingleButtonAssignmentSet, arguments);
    }

    public static RazerGamepadButtonAssignment ReadButtonAssignment(ReadOnlySpan<byte> response)
    {
        var data = ReadData(response);
        if (data.Length < 10 || data[0] != ButtonCommandClass)
        {
            throw new InvalidDataException(
                $"The gamepad reply is not class 0x{ButtonCommandClass:X2} button assignment.");
        }

        var size = data[4];
        if (size > 5)
        {
            throw new InvalidDataException("The gamepad button-assignment data size is out of range.");
        }

        return new(
            data[1],
            (RazerGamepadButtonMode)data[2],
            (RazerGamepadFunction)data[3],
            size,
            data.Slice(5, 5).ToArray());
    }

    /// <summary>The middleware sends the command tuple itself as the raw-input argument block.</summary>
    public static byte[] CreateGetRawInputRequest(byte transactionId) =>
        Create(transactionId, 80, RawInputGet, [80, CommandClass, RawInputGet]);

    public static byte[] CreateGetCalibrationRequest(
        byte transactionId,
        RazerGamepadCalibrationControl control)
    {
        var arguments = new byte[51];
        arguments[0] = (byte)control;
        return Create(transactionId, 51, CalibrationGet, arguments);
    }

    public static byte[] CreateSetCalibrationRequest(
        byte transactionId,
        RazerGamepadCalibration calibration)
    {
        ArgumentNullException.ThrowIfNull(calibration);
        if (calibration.Values.Count != 16)
        {
            throw new ArgumentException("Calibration must contain 16 16-bit values.", nameof(calibration));
        }

        var arguments = new byte[51];
        arguments[0] = calibration.AnalogId;
        var first = calibration.AnalogId switch
        {
            (byte)RazerGamepadCalibrationControl.LeftJoystick => 0,
            (byte)RazerGamepadCalibrationControl.RightJoystick => 6,
            (byte)RazerGamepadCalibrationControl.LeftTrigger => 12,
            (byte)RazerGamepadCalibrationControl.RightTrigger => 14,
            _ => -1,
        };
        if (first < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(calibration), "Unknown calibration control.");
        }

        var count = first is 12 or 14 ? 2 : 6;
        var offset = 1 + first * 2;
        for (var index = 0; index < count; index++)
        {
            var value = calibration.Values[first + index];
            arguments[offset + index * 2] = (byte)(value >> 8);
            arguments[offset + index * 2 + 1] = (byte)value;
        }

        return Create(transactionId, 51, CalibrationSet, arguments);
    }

    public static byte ReadSensitivity(ReadOnlySpan<byte> response, byte controlId) =>
        ReadValue(response, controlId)[2];

    public static byte ReadVibrationScale(ReadOnlySpan<byte> response, byte controlId) =>
        ReadValue(response, controlId)[2];

    public static byte ReadTriggerMode(ReadOnlySpan<byte> response, byte controlId) =>
        ReadValue(response, controlId)[2];

    public static (byte Start, byte End) ReadTriggerRange(ReadOnlySpan<byte> response, byte controlId)
    {
        var value = ReadValue(response, controlId);
        return (value[2], value[3]);
    }

    public static IReadOnlyList<byte> ReadControlIdList(ReadOnlySpan<byte> response)
    {
        var data = ReadData(response);
        var total = data[0];
        if (total > 80 || data.Length < total + 1)
        {
            throw new InvalidDataException("The gamepad control-id reply is truncated.");
        }

        return data.Slice(1, total).ToArray();
    }

    public static RazerGamepadAnalogControl ReadAnalogControl(
        ReadOnlySpan<byte> response,
        byte controlId)
    {
        var value = ReadValue(response, controlId);
        if (value.Length < 4)
        {
            throw new InvalidDataException("The gamepad analog-control reply is truncated.");
        }

        return new(
            value[0],
            value[1],
            (RazerGamepadAnalogType)value[2],
            value[3]);
    }

    public static ushort ReadAdcMask(ReadOnlySpan<byte> response)
    {
        var data = ReadData(response);
        if (data.Length < 2)
        {
            throw new InvalidDataException("The gamepad ADC-mask reply is truncated.");
        }

        return ReadUInt16(data, 0);
    }

    public static byte ReadCircularity(ReadOnlySpan<byte> response, byte controlId) =>
        ReadValue(response, controlId)[2];

    public static RazerGamepadDigitalTrigger ReadDigitalTrigger(
        ReadOnlySpan<byte> response,
        byte controlId)
    {
        var value = ReadValue(response, controlId);
        if (value.Length < 4)
        {
            throw new InvalidDataException("The gamepad digital-trigger reply is truncated.");
        }

        return new(value[2], value[3] != 0);
    }

    public static byte ReadIgnoreDPadDiagonals(ReadOnlySpan<byte> response) => ReadOption(response);

    public static RazerGamepadSocdMode ReadSocdMode(ReadOnlySpan<byte> response) =>
        (RazerGamepadSocdMode)ReadOption(response);

    public static byte ReadControllerModeSwitch(ReadOnlySpan<byte> response) => ReadOption(response);

    public static byte ReadActiveProfileId(ReadOnlySpan<byte> response) => ReadData(response)[0];

    public static RazerGamepadRawInput ReadRawInput(ReadOnlySpan<byte> response)
    {
        var data = ReadData(response);
        if (data.Length < 12)
        {
            throw new InvalidDataException("The gamepad raw-input reply is truncated.");
        }

        return new(
            ReadUInt16(data, 0),
            ReadUInt16(data, 2),
            ReadUInt16(data, 4),
            ReadUInt16(data, 6),
            ReadUInt16(data, 8),
            ReadUInt16(data, 10));
    }

    public static RazerGamepadCalibration ReadCalibration(ReadOnlySpan<byte> response)
    {
        var data = ReadData(response);
        if (data.Length < 33)
        {
            throw new InvalidDataException("The gamepad calibration reply is truncated.");
        }

        var values = new ushort[16];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = ReadUInt16(data, 1 + index * 2);
        }

        return new(data[0], values);
    }

    public static RazerGamepadZones ReadZones(ReadOnlySpan<byte> response, byte controlId)
    {
        var value = ReadValue(response, controlId);
        return new RazerGamepadZones(
            ReadUInt16(value, 2),
            ReadUInt16(value, 4),
            ReadUInt16(value, 6),
            value[8] != 0);
    }

    private static ReadOnlySpan<byte> ReadValue(ReadOnlySpan<byte> response, byte controlId)
    {
        var value = ReadData(response);
        if (value.Length < 3 || value[0] != CommandClass || value[1] != controlId)
        {
            throw new InvalidDataException(
                $"The gamepad reply is not class 0x{CommandClass:X2} control 0x{controlId:X2}.");
        }

        return value;
    }

    private static ReadOnlySpan<byte> ReadData(ReadOnlySpan<byte> response)
    {
        if (response.Length != RazerFeatureReport.Length)
        {
            throw new ArgumentException(
                $"Razer feature reports must be {RazerFeatureReport.Length} bytes.",
                nameof(response));
        }

        return response[RazerFeatureReport.ArgumentsOffset..];
    }

    private static byte ReadOption(ReadOnlySpan<byte> response)
    {
        var value = ReadData(response);
        if (value.Length < 2 || value[0] != CommandClass)
        {
            throw new InvalidDataException(
                $"The gamepad reply is not class 0x{CommandClass:X2}.");
        }

        return value[1];
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset) =>
        (ushort)((data[offset] << 8) | data[offset + 1]);

    private static byte[] Create(
        byte transactionId,
        byte dataSize,
        byte commandId,
        ReadOnlySpan<byte> arguments)
        => CreateForClass(transactionId, dataSize, CommandClass, commandId, arguments);

    private static byte[] CreateForClass(
        byte transactionId,
        byte dataSize,
        byte commandClass,
        byte commandId,
        ReadOnlySpan<byte> arguments)
    {
        var report = RazerFeatureReport.CreateRequest(
            transactionId,
            dataSize,
            commandClass,
            commandId,
            arguments);
        report[0] = ReportId;
        return report;
    }
}
