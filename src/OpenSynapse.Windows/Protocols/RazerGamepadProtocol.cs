namespace OpenSynapse.Windows.Protocols;

/// <summary>Analog stick dead zones, as the middleware reports them.</summary>
public readonly record struct RazerGamepadZones(
    ushort DeadZone,
    ushort SoftBumper,
    ushort DeltaThreshold,
    bool PreventDoubleDeadZone);

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
/// The first two payload bytes are the control id and its index; replies echo the class
/// and control id before the value.
/// </summary>
public static class RazerGamepadProtocol
{
    public const byte ReportId = 4;
    public const byte CommandClass = 0x0C;

    private const byte SensitivityGet = 0x82;
    private const byte SensitivitySet = 0x02;
    private const byte ZonesGet = 0x85;
    private const byte ZonesSet = 0x05;
    private const byte VibrationGet = 0x86;
    private const byte VibrationSet = 0x06;
    private const byte TriggerRangeGet = 0x93;
    private const byte TriggerRangeSet = 0x13;
    private const byte TriggerModeGet = 0x95;
    private const byte TriggerModeSet = 0x15;

    public static byte[] CreateGetSensitivityRequest(byte transactionId, byte controlId, byte index) =>
        Create(transactionId, 3, SensitivityGet, [controlId, index, 0x00]);

    public static byte[] CreateSetSensitivityRequest(
        byte transactionId,
        byte controlId,
        byte index,
        byte sensitivity) =>
        Create(transactionId, 3, SensitivitySet, [controlId, index, sensitivity]);

    public static byte[] CreateGetZonesRequest(byte transactionId, byte controlId, byte index) =>
        Create(transactionId, 9, ZonesGet, [controlId, index, 0, 0, 0, 0, 0, 0, 0]);

    public static byte[] CreateSetZonesRequest(
        byte transactionId,
        byte controlId,
        byte index,
        RazerGamepadZones zones) =>
        Create(transactionId, 9, ZonesSet,
        [
            controlId,
            index,
            (byte)(zones.DeadZone >> 8),
            (byte)zones.DeadZone,
            (byte)(zones.SoftBumper >> 8),
            (byte)zones.SoftBumper,
            (byte)(zones.DeltaThreshold >> 8),
            (byte)zones.DeltaThreshold,
            zones.PreventDoubleDeadZone ? (byte)1 : (byte)0,
        ]);

    public static byte[] CreateGetVibrationScaleRequest(byte transactionId, byte controlId, byte index) =>
        Create(transactionId, 3, VibrationGet, [controlId, index, 0x00]);

    public static byte[] CreateSetVibrationScaleRequest(
        byte transactionId,
        byte controlId,
        byte index,
        byte scale) =>
        Create(transactionId, 3, VibrationSet, [controlId, index, scale]);

    public static byte[] CreateGetTriggerRangeRequest(byte transactionId, byte controlId, byte index) =>
        Create(transactionId, 4, TriggerRangeGet, [controlId, index, 0x00, 0x00]);

    public static byte[] CreateSetTriggerRangeRequest(
        byte transactionId,
        byte controlId,
        byte index,
        byte start,
        byte end) =>
        Create(transactionId, 4, TriggerRangeSet, [controlId, index, start, end]);

    public static byte[] CreateGetTriggerModeRequest(byte transactionId, byte controlId, byte index) =>
        Create(transactionId, 3, TriggerModeGet, [controlId, index, 0x00]);

    public static byte[] CreateSetTriggerModeRequest(
        byte transactionId,
        byte controlId,
        byte index,
        byte mode) =>
        Create(transactionId, 3, TriggerModeSet, [controlId, index, mode]);

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

    public static RazerGamepadZones ReadZones(ReadOnlySpan<byte> response, byte controlId)
    {
        var value = ReadValue(response, controlId);
        return new RazerGamepadZones(
            (ushort)((value[2] << 8) | value[3]),
            (ushort)((value[4] << 8) | value[5]),
            (ushort)((value[6] << 8) | value[7]),
            value[8] != 0);
    }

    private static ReadOnlySpan<byte> ReadValue(ReadOnlySpan<byte> response, byte controlId)
    {
        if (response.Length != RazerFeatureReport.Length)
        {
            throw new ArgumentException(
                $"Razer feature reports must be {RazerFeatureReport.Length} bytes.",
                nameof(response));
        }

        var value = response[RazerFeatureReport.ArgumentsOffset..];
        if (value.Length < 3 || value[0] != CommandClass || value[1] != controlId)
        {
            throw new InvalidDataException(
                $"The gamepad reply is not class 0x{CommandClass:X2} control 0x{controlId:X2}.");
        }

        return value;
    }

    private static byte[] Create(
        byte transactionId,
        byte dataSize,
        byte commandId,
        ReadOnlySpan<byte> arguments)
    {
        var report = RazerFeatureReport.CreateRequest(
            transactionId,
            dataSize,
            CommandClass,
            commandId,
            arguments);
        report[0] = ReportId;
        return report;
    }
}
