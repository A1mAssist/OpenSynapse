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
    private const byte GetKeyAssignmentListCommand = 0x8F;
    private const byte GetAssignmentCommand = 0x8C;
    private const byte SetAssignmentCommand = 0x0C;
    private const byte GetKeyAssignmentCommand = 0x8D;
    private const byte SetKeyAssignmentCommand = 0x0D;
    private const byte GetMultiKeyAssignmentCommand = 0x9E;
    private const byte SetMultiKeyAssignmentCommand = 0x1E;
    private const byte GetAnalogKeyAssignmentCommand = 0x92;
    private const byte SetAnalogKeyAssignmentCommand = 0x12;
    private const int MultiKeyAssignmentStride = 9;

    internal static RazerHidRequest GetButtonList() => new(
        "mapping.getButtonList",
        RazerFeatureReport.CreateRequest(
            TransactionId, ReportDataSize, MappingClass, GetButtonListCommand, []));

    /// <summary>
    /// Keyboards list their key table with 0x8F. The 0x84 button list also carries
    /// modifier buttons that 0x8D rejects, so it cannot drive keyboard rows.
    /// </summary>
    internal static RazerHidRequest GetKeyAssignmentList(byte profileId, byte mode) => new(
        "mapping.getKeyAssignmentList",
        RazerFeatureReport.CreateRequest(
            TransactionId, ReportDataSize, MappingClass, GetKeyAssignmentListCommand,
            new[] { profileId, mode, (byte)0 }));

    internal static RazerHidRequest GetAssignment(byte profileId, byte buttonId, byte mode) => new(
        "mapping.getAssignment",
        RazerFeatureReport.CreateRequest(
            TransactionId, ReportDataSize, MappingClass, GetAssignmentCommand,
            new[] { profileId, buttonId, mode }));

    /// <summary>Keyboards read and write the key table (0x8D/0x0D), not the button table.</summary>
    internal static RazerHidRequest GetKeyAssignment(byte profileId, byte keyId, byte mode) => new(
        "mapping.getKeyAssignment",
        RazerFeatureReport.CreateRequest(
            TransactionId, ReportDataSize, MappingClass, GetKeyAssignmentCommand,
            new[] { profileId, keyId, mode }));

    /// <summary>
    /// Huntsman V3 Pro firmware rejects 0x8D for modifier and navigation keys. Synapse
    /// reads every key through the multi-key table instead, one nine-byte slot per key.
    /// </summary>
    internal static RazerHidRequest GetMultiKeyAssignment(byte profileId, byte keyId, byte mode)
    {
        var arguments = new byte[1 + MultiKeyAssignmentStride];
        arguments[0] = profileId;
        arguments[1] = keyId;
        arguments[2] = mode;
        return new RazerHidRequest(
            "mapping.getMultiKeyAssignment",
            RazerFeatureReport.CreateRequest(
                TransactionId, (byte)arguments.Length, MappingClass,
                GetMultiKeyAssignmentCommand, arguments));
    }

    internal static RazerHidRequest GetAnalogKeyAssignment(byte profileId, byte keyId, byte mode) => new(
        "mapping.getAnalogKeyAssignment",
        RazerFeatureReport.CreateRequest(
            TransactionId, ReportDataSize, MappingClass, GetAnalogKeyAssignmentCommand,
            new[] { profileId, keyId, mode }));

    internal static RazerHidRequest SetAssignment(
        byte profileId,
        byte buttonId,
        byte mode,
        byte functionId,
        ReadOnlySpan<byte> data) =>
        CreateAssignmentRequest(
            "mapping.setAssignment", SetAssignmentCommand, profileId, buttonId, mode, functionId, data);

    internal static RazerHidRequest SetKeyAssignment(
        byte profileId,
        byte keyId,
        byte mode,
        byte functionId,
        ReadOnlySpan<byte> data) =>
        CreateAssignmentRequest(
            "mapping.setKeyAssignment", SetKeyAssignmentCommand, profileId, keyId, mode, functionId, data);

    internal static RazerHidRequest SetMultiKeyAssignment(
        byte profileId,
        byte keyId,
        byte mode,
        byte functionId,
        ReadOnlySpan<byte> data)
    {
        if (data.Length > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(data));
        }

        var arguments = new byte[1 + MultiKeyAssignmentStride];
        arguments[0] = profileId;
        arguments[1] = keyId;
        arguments[2] = mode;
        arguments[3] = functionId;
        arguments[4] = (byte)data.Length;
        data.CopyTo(arguments.AsSpan(5));
        return new RazerHidRequest(
            "mapping.setMultiKeyAssignment",
            RazerFeatureReport.CreateRequest(
                TransactionId, (byte)arguments.Length, MappingClass,
                SetMultiKeyAssignmentCommand, arguments));
    }

    internal static RazerHidRequest SetAnalogKeyAssignment(
        byte profileId,
        byte keyId,
        byte mode,
        byte thresholdL,
        byte thresholdH,
        byte functionId,
        ReadOnlySpan<byte> data)
    {
        if (data.Length > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(data));
        }

        Span<byte> arguments = stackalloc byte[7 + data.Length];
        arguments[0] = profileId;
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
        byte profileId,
        byte id,
        byte mode,
        byte functionId,
        ReadOnlySpan<byte> data)
    {
        if (data.Length > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(data));
        }

        Span<byte> arguments = stackalloc byte[5 + data.Length];
        arguments[0] = profileId;
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

    /// <summary>The reply is [classId, mode, keyId...]; the first two bytes are not ids.</summary>
    internal static IReadOnlyList<byte> ParseKeyAssignmentList(ReadOnlySpan<byte> response)
    {
        var arguments = RazerHidStandardProtocol.Arguments(response, 2);
        return arguments.Length <= 2 ? [] : arguments[2..].ToArray();
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

    /// <summary>The reply is [classId, keyId, mode, functionId, dataSize, data[5]] per key.</summary>
    internal static RazerHidButtonAssignment ParseMultiKeyAssignment(
        ReadOnlySpan<byte> response,
        byte keyId)
    {
        var arguments = RazerHidStandardProtocol.Arguments(response, 10);
        for (var offset = 1; offset + MultiKeyAssignmentStride <= arguments.Length;
             offset += MultiKeyAssignmentStride)
        {
            if (arguments[offset] != keyId)
            {
                continue;
            }

            var size = Math.Min((int)arguments[offset + 3], 5);
            return new RazerHidButtonAssignment(
                arguments[offset],
                arguments[offset + 1],
                arguments[offset + 2],
                arguments.Slice(offset + 4, size).ToArray());
        }

        throw new InvalidDataException(
            $"RazerHid did not return key 0x{keyId:X2} in the multi-key assignment table.");
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
