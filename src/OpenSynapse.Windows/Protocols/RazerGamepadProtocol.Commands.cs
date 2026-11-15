namespace OpenSynapse.Windows.Protocols;

public static partial class RazerGamepadProtocol
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
    private const byte ButtonIdListGet = 0x84;
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
        Create(transactionId, 2, AdcMaskGet, []);

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

    private static readonly byte[] VibrationScaleSteps = [0, 17, 33, 50, 67, 83, 100];

    /// <summary>Maps the seven UI vibration steps to the middleware's 0..100 wire values.</summary>
    public static byte ToVibrationScale(byte index) =>
        VibrationScaleSteps[Math.Clamp(index, (byte)0, (byte)6)];

    /// <summary>Maps a raw 0..100 wire value back to the nearest UI step.</summary>
    public static byte ToVibrationIndex(byte scale)
    {
        var best = 0;
        for (var index = 1; index < VibrationScaleSteps.Length; index++)
        {
            if (Math.Abs(scale - VibrationScaleSteps[index]) <
                Math.Abs(scale - VibrationScaleSteps[best]))
            {
                best = index;
            }
        }

        return (byte)best;
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
