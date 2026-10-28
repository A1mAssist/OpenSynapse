namespace OpenSynapse.Windows.Protocols;

public enum RazerButtonAssignmentMode : byte
{
    NormalMode = 0,
    HypershiftMode = 1,
}

public enum RazerButtonFunctionType : byte
{
    Off = 0,
    ButtonCode = 1,
    KeyCode = 2,
    MacroTypeI = 3,
    MacroTypeII = 4,
    MacroTypeIII = 5,
    DPI = 6,
    Profile = 7,
    Lighting = 8,
    PowerKeys = 9,
    MediaKeys = 10,
    DoubleClick = 11,
    ModeButtonkey = 12,
    TurboModeKey = 13,
    TurboModeButton = 14,
    MacroTypeIV = 15,
    Controller = 16,
    RazerKey = 17,
}

public sealed record RazerButtonAssignment(
    byte ButtonId,
    RazerButtonAssignmentMode Mode,
    RazerButtonFunctionType FunctionId,
    byte DataSize,
    IReadOnlyList<byte> Data);

/// <summary>
/// Protocol 30 class 0x08 button assignment commands from the Razer middleware.
/// The tuple's first byte is only a local buffer hint; the wire length comes from the
/// actual payload. Param6/param7 sit outside that length, so they follow the 91-byte
/// report command header while the declared size stays at the payload length.
/// </summary>
public static class RazerButtonAssignmentProtocol
{
    private const byte CommandClass = 0x08;
    private const byte GetButtonListCommand = 0x80;
    private const byte GetAssignmentListCommand = 0x83;
    private const byte GetSingleAssignmentCommand = 0x84;
    private const byte SetSingleAssignmentCommand = 0x04;
    private const byte GetDataSize = 0x00;
    private const int SetPayloadPrefixLength = 5;

    public static byte[] CreateGetButtonListRequest(byte transactionId, byte classId = 0) =>
        RazerFeatureReport.CreateRequestWithDeclaredSize(
            transactionId,
            GetDataSize,
            CommandClass,
            GetButtonListCommand,
            new byte[] { 0x00, classId });

    public static byte[] CreateGetAssignmentListRequest(byte transactionId, byte classId = 0) =>
        RazerFeatureReport.CreateRequestWithDeclaredSize(
            transactionId,
            GetDataSize,
            CommandClass,
            GetAssignmentListCommand,
            new byte[] { classId, 0x00 });

    public static byte[] CreateGetSingleAssignmentRequest(
        byte transactionId,
        byte classId,
        byte buttonId) =>
        RazerFeatureReport.CreateRequestWithDeclaredSize(
            transactionId,
            GetDataSize,
            CommandClass,
            GetSingleAssignmentCommand,
            new byte[] { classId, buttonId });

    public static byte[] CreateSetSingleAssignmentRequest(
        byte transactionId,
        byte classId,
        byte buttonId,
        RazerButtonAssignmentMode mode,
        RazerButtonFunctionType functionId,
        ReadOnlySpan<byte> data)
    {
        if (data.Length > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(data), "Button assignments carry at most 5 data bytes.");
        }

        Span<byte> arguments = stackalloc byte[7 + data.Length];
        arguments[0] = classId;
        arguments[1] = buttonId;
        arguments[2] = classId;
        arguments[3] = buttonId;
        arguments[4] = (byte)mode;
        arguments[5] = (byte)functionId;
        arguments[6] = (byte)data.Length;
        data.CopyTo(arguments[7..]);
        // Me[0] = 255 sizes the middleware's local buffer; the wire length is the 5+n payload.
        return RazerFeatureReport.CreateRequestWithDeclaredSize(
            transactionId,
            (byte)(SetPayloadPrefixLength + data.Length),
            CommandClass,
            SetSingleAssignmentCommand,
            arguments);
    }

    public static IReadOnlyList<byte> ParseButtonList(ReadOnlySpan<byte> response) =>
        OpenRazerStandardProtocol.Arguments(response, 0).ToArray();

    public static RazerButtonAssignment ParseAssignmentListEntry(
        ReadOnlySpan<byte> response,
        RazerButtonAssignmentMode mode)
    {
        var (minimumLength, functionIndex, firstValueIndex) = mode switch
        {
            RazerButtonAssignmentMode.NormalMode => ((byte)3, 2, 4),
            RazerButtonAssignmentMode.HypershiftMode => ((byte)4, 3, 5),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
        var data = OpenRazerStandardProtocol.Arguments(response, minimumLength);
        var values = new List<byte>();
        for (var index = firstValueIndex; index < data.Length; index += 2)
        {
            values.Add(data[index]);
        }

        return new RazerButtonAssignment(
            data[0],
            mode,
            (RazerButtonFunctionType)data[functionIndex],
            checked((byte)values.Count),
            values);
    }

    public static RazerButtonAssignment ParseSingleAssignment(
        ReadOnlySpan<byte> response,
        RazerButtonAssignmentMode mode)
    {
        var (minimumLength, functionIndex, sizeIndex, firstValueIndex) = mode switch
        {
            RazerButtonAssignmentMode.NormalMode => ((byte)15, 2, 4, 6),
            RazerButtonAssignmentMode.HypershiftMode => ((byte)16, 3, 5, 7),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
        var data = OpenRazerStandardProtocol.Arguments(response, minimumLength);

        // The middleware reports dataSize separately and still reads all five fixed slots.
        var values = new byte[5];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = data[firstValueIndex + (index * 2)];
        }

        return new RazerButtonAssignment(
            data[0],
            mode,
            (RazerButtonFunctionType)data[functionIndex],
            data[sizeIndex],
            values);
    }
}
