using OpenSynapse.Windows.Devices;

namespace OpenSynapse.Windows.Protocols;

public sealed record RazerHidButtonAssignment(
    byte ButtonId,
    byte Mode,
    byte FunctionId,
    IReadOnlyList<byte> Data)
{
    /// <summary>Analog keyboards keep the actuation band that belonged to the key.</summary>
    public byte ThresholdL { get; init; }

    public byte ThresholdH { get; init; }
}

/// <summary>
/// Onboard button assignment (class 0x02, commands 0x84 / 0x8C / 0x0C) as the Razer
/// middleware sends it: profile id, button id, mapping mode, function id and up to five
/// data bytes inside an 80-byte payload.
/// </summary>
internal static class RazerHidMappingProtocol
{
    internal const byte TransactionId = 0x1F;
    internal const byte ProfileId = 0x01;
    internal const int ReportDataSize = 80;
    internal const byte NormalMode = 0x00;
    internal const byte HypershiftMode = 0x01;
    private const byte MappingClass = 0x02;
    private const byte GetButtonListCommand = 0x84;
    private const byte GetAssignmentCommand = 0x8C;
    private const byte SetAssignmentCommand = 0x0C;
    private const byte GetKeyAssignmentCommand = 0x8D;
    private const byte SetKeyAssignmentCommand = 0x0D;
    private const byte GetAnalogKeyAssignmentCommand = 0x92;
    private const byte SetAnalogKeyAssignmentCommand = 0x12;

    internal static RazerHidRequest GetButtonList() => new(
        "mapping.getButtonList",
        RazerFeatureReport.CreateRequest(
            TransactionId, ReportDataSize, MappingClass, GetButtonListCommand, []));

    internal static RazerHidRequest GetAssignment(byte buttonId, byte mode) => new(
        "mapping.getAssignment",
        RazerFeatureReport.CreateRequest(
            TransactionId, ReportDataSize, MappingClass, GetAssignmentCommand,
            new[] { ProfileId, buttonId, mode }));

    /// <summary>Keyboards read and write the key table (0x8D/0x0D), not the button table.</summary>
    internal static RazerHidRequest GetKeyAssignment(byte keyId, byte mode) => new(
        "mapping.getKeyAssignment",
        RazerFeatureReport.CreateRequest(
            TransactionId, ReportDataSize, MappingClass, GetKeyAssignmentCommand,
            new[] { ProfileId, keyId, mode }));

    internal static RazerHidRequest GetAnalogKeyAssignment(byte keyId, byte mode) => new(
        "mapping.getAnalogKeyAssignment",
        RazerFeatureReport.CreateRequest(
            TransactionId, ReportDataSize, MappingClass, GetAnalogKeyAssignmentCommand,
            new[] { ProfileId, keyId, mode }));

    internal static RazerHidRequest SetAssignment(
        byte buttonId,
        byte mode,
        byte functionId,
        ReadOnlySpan<byte> data) =>
        CreateAssignmentRequest("mapping.setAssignment", SetAssignmentCommand, buttonId, mode, functionId, data);

    internal static RazerHidRequest SetKeyAssignment(
        byte keyId,
        byte mode,
        byte functionId,
        ReadOnlySpan<byte> data) =>
        CreateAssignmentRequest("mapping.setKeyAssignment", SetKeyAssignmentCommand, keyId, mode, functionId, data);

    internal static RazerHidRequest SetAnalogKeyAssignment(
        byte keyId,
        byte mode,
        byte thresholdL,
        byte thresholdH,
        byte functionId,
        ReadOnlySpan<byte> data)
    {
        data = NormalizeKeyData(functionId, data);

        if (data.Length > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(data));
        }

        Span<byte> arguments = stackalloc byte[7 + data.Length];
        arguments[0] = ProfileId;
        arguments[1] = keyId;
        arguments[2] = mode;
        arguments[3] = thresholdL;
        arguments[4] = thresholdH;
        arguments[5] = functionId;
        arguments[6] = (byte)data.Length;
        data.CopyTo(arguments[7..]);
        return new RazerHidRequest(
            "mapping.setAnalogKeyAssignment",
            RazerFeatureReport.CreateRequest(
                TransactionId, ReportDataSize, MappingClass, SetAnalogKeyAssignmentCommand, arguments));
    }

    private static RazerHidRequest CreateAssignmentRequest(
        string builderName,
        byte command,
        byte id,
        byte mode,
        byte functionId,
        ReadOnlySpan<byte> data)
    {
        data = NormalizeKeyData(functionId, data);

        if (data.Length > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(data));
        }

        Span<byte> arguments = stackalloc byte[5 + data.Length];
        arguments[0] = ProfileId;
        arguments[1] = id;
        arguments[2] = mode;
        arguments[3] = functionId;
        arguments[4] = (byte)data.Length;
        data.CopyTo(arguments[5..]);
        return new RazerHidRequest(
            builderName,
            RazerFeatureReport.CreateRequest(
                TransactionId, ReportDataSize, MappingClass, command, arguments));
    }

    /// <summary>
    /// Profiles saved by pre-beta-41 builds carry the HID usage in the modifier byte; the
    /// firmware reads that as a chord, so rewrite it to the single modifier bit on the way out.
    /// </summary>
    private static ReadOnlySpan<byte> NormalizeKeyData(byte functionId, ReadOnlySpan<byte> data)
    {
        if (functionId == RazerHidMappingFunctions.KeyCode && data.Length >= 2 && data[1] == 0 &&
            data[0] is >= 224 and <= 231)
        {
            return new byte[] { (byte)(1 << (data[0] - 224)), 0 };
        }

        return data;
    }

    internal static IReadOnlyList<byte> ParseButtonList(ReadOnlySpan<byte> response)
    {
        var arguments = RazerHidStandardProtocol.Arguments(response, 1);
        var count = Math.Min((int)arguments[0], arguments.Length - 1);
        var ids = new List<byte>(count);
        for (var index = 0; index < count; index++)
        {
            ids.Add(arguments[1 + index]);
        }
        return ids;
    }

    internal static RazerHidButtonAssignment ParseAssignment(ReadOnlySpan<byte> response)
    {
        var arguments = RazerHidStandardProtocol.Arguments(response, 5);
        var size = Math.Min((int)arguments[4], 5);
        return new RazerHidButtonAssignment(
            arguments[1],
            arguments[2],
            arguments[3],
            arguments.Slice(5, size).ToArray());
    }

    internal static RazerHidButtonAssignment ParseAnalogAssignment(ReadOnlySpan<byte> response)
    {
        var arguments = RazerHidStandardProtocol.Arguments(response, 7);
        var size = Math.Min((int)arguments[6], 5);
        return new RazerHidButtonAssignment(
            arguments[1],
            arguments[2],
            arguments[5],
            arguments.Slice(7, size).ToArray())
        {
            ThresholdL = arguments[3],
            ThresholdH = arguments[4],
        };
    }
}
