using OpenSynapse.Windows.Devices;

namespace OpenSynapse.Windows.Protocols;

public sealed record OpenRazerButtonAssignment(
    byte ButtonId,
    byte Mode,
    byte FunctionId,
    IReadOnlyList<byte> Data);

/// <summary>
/// Onboard button assignment (class 0x02, commands 0x84 / 0x8C / 0x0C) as the Razer
/// middleware sends it: profile id, button id, mapping mode, function id and up to five
/// data bytes inside an 80-byte payload.
/// </summary>
internal static class OpenRazerMappingProtocol
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

    internal static OpenRazerRequest GetButtonList() => new(
        "mapping.getButtonList",
        RazerFeatureReport.CreateRequest(
            TransactionId, ReportDataSize, MappingClass, GetButtonListCommand, []));

    internal static OpenRazerRequest GetAssignment(byte buttonId, byte mode) => new(
        "mapping.getAssignment",
        RazerFeatureReport.CreateRequest(
            TransactionId, ReportDataSize, MappingClass, GetAssignmentCommand,
            new[] { ProfileId, buttonId, mode }));

    internal static OpenRazerRequest SetAssignment(
        byte buttonId,
        byte mode,
        byte functionId,
        ReadOnlySpan<byte> data)
    {
        if (data.Length > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(data));
        }

        Span<byte> arguments = stackalloc byte[5 + data.Length];
        arguments[0] = ProfileId;
        arguments[1] = buttonId;
        arguments[2] = mode;
        arguments[3] = functionId;
        arguments[4] = (byte)data.Length;
        data.CopyTo(arguments[5..]);
        return new OpenRazerRequest(
            "mapping.setAssignment",
            RazerFeatureReport.CreateRequest(
                TransactionId, ReportDataSize, MappingClass, SetAssignmentCommand, arguments));
    }

    internal static IReadOnlyList<byte> ParseButtonList(ReadOnlySpan<byte> response)
    {
        var arguments = OpenRazerStandardProtocol.Arguments(response, 1);
        var count = Math.Min((int)arguments[0], arguments.Length - 1);
        var ids = new List<byte>(count);
        for (var index = 0; index < count; index++)
        {
            ids.Add(arguments[1 + index]);
        }
        return ids;
    }

    internal static OpenRazerButtonAssignment ParseAssignment(ReadOnlySpan<byte> response)
    {
        var arguments = OpenRazerStandardProtocol.Arguments(response, 5);
        var size = Math.Min((int)arguments[4], 5);
        return new OpenRazerButtonAssignment(
            arguments[1],
            arguments[2],
            arguments[3],
            arguments.Slice(5, size).ToArray());
    }
}
