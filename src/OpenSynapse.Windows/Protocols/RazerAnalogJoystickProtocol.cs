namespace OpenSynapse.Windows.Protocols;

public readonly record struct RazerAnalogJoystickMappingMode(byte ProfileId, byte MappingMode);

/// <summary>
/// Controller button ids from the Huntsman V3 Pro middleware bundle.
/// </summary>
public enum RazerControllerButton : byte
{
    A = 32,
    B = 33,
    X = 34,
    Y = 35,
    LeftBumper = 36,
    RightBumper = 37,
    LeftTrigger = 38,
    RightTrigger = 39,
    LeftStick = 40,
    RightStick = 41,
    Start = 42,
    Back = 43,
    DPadUp = 44,
    DPadDown = 45,
    DPadLeft = 46,
    DPadRight = 47,
    L_Shoulder = 48,
    R_Shoulder = 49,
    Guide = 50,
    LeftStickUp = 128,
    LeftStickDown = 129,
    LeftStickLeft = 130,
    LeftStickRight = 131,
}

/// <summary>
/// Protocol 2 analog-joystick report/mapping commands from the Huntsman V3 Pro bundle.
/// The Get/Set ids are adjacent (0xB8/0x38 and 0xB9/0x39).
/// </summary>
public static class RazerAnalogJoystickProtocol
{
    private const byte CommandClass = 0x02;
    private const byte GetReportMode = 0xB8;
    private const byte SetReportMode = 0x38;
    private const byte GetMappingMode = 0xB9;
    private const byte SetMappingMode = 0x39;

    public static byte[] CreateGetReportModeRequest(byte transactionId) =>
        RazerFeatureReport.CreateRequest(
            transactionId, 1, CommandClass, GetReportMode, new byte[] { 0x00 });

    public static byte[] CreateSetReportModeRequest(byte transactionId, byte reportMode) =>
        RazerFeatureReport.CreateRequest(
            transactionId, 1, CommandClass, SetReportMode, new[] { reportMode });

    public static byte[] CreateGetMappingModeRequest(byte transactionId, byte profileId) =>
        RazerFeatureReport.CreateRequest(
            transactionId, 2, CommandClass, GetMappingMode, new[] { profileId, (byte)0x00 });

    public static byte[] CreateSetMappingModeRequest(
        byte transactionId,
        byte profileId,
        byte mappingMode) =>
        RazerFeatureReport.CreateRequest(
            transactionId, 2, CommandClass, SetMappingMode, new[] { profileId, mappingMode });

    public static byte ParseReportMode(ReadOnlySpan<byte> response) =>
        RazerHidStandardProtocol.Arguments(response, 1)[0];

    public static RazerAnalogJoystickMappingMode ParseMappingMode(ReadOnlySpan<byte> response)
    {
        var data = RazerHidStandardProtocol.Arguments(response, 2);
        return new RazerAnalogJoystickMappingMode(data[0], data[1]);
    }
}
