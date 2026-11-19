using OpenSynapse.Windows.Devices;

namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Synapse 2 eight-way D-pad table (rzdevicedll DAT_10133220). The firmware
/// exposes nine direction slots; the read frame carries the selector byte and
/// the write frame carries the table's t1/t2/t3 bytes. The two wheel slots
/// (selectors 0x0A/0x0B) also carry a nonzero 16-bit function id, which the
/// caller must persist in the P2 profile before sending this refresh frame.
/// </summary>
internal static class RazerKeypadDpadProtocol
{
    internal const byte MappingClass = 0x02;
    internal const byte ReadCommand = 0x80;
    internal const byte WriteCommand = 0x00;
    internal const byte KeymapId = 0x01;
    internal const int ReadDataSize = 9;

    internal readonly record struct Direction(
        byte Selector,
        byte T1,
        byte T2,
        byte T3,
        ushort FunctionId);

    internal static IReadOnlyList<Direction> Directions { get; } =
    [
        new(0x01, 0x01, 0x01, 0x01, 0),
        new(0x02, 0x02, 0x01, 0x02, 0),
        new(0x03, 0x03, 0x01, 0x03, 0),
        new(0x04, 0x04, 0x01, 0x04, 0),
        new(0x05, 0x05, 0x01, 0x05, 0),
        new(0x08, 0x09, 0x01, 0x09, 0),
        new(0x09, 0x0A, 0x01, 0x0A, 0),
        new(0x0A, 0x0B, 0x06, 0x01, 0x0070),
        new(0x0B, 0x0C, 0x06, 0x02, 0x0073),
    ];

    internal static RazerHidRequest GetDirection(int index) => new(
        "keypad.getDpadDirection",
        RazerFeatureReport.CreateRequest(
            RazerHidMappingProtocol.TransactionId,
            ReadDataSize,
            MappingClass,
            ReadCommand,
            new byte[] { KeymapId, Directions[index].Selector, 0, 0, 0, 0, 0, 0, 0 }));

    internal static RazerHidRequest SetDirection(
        int index,
        byte functionId,
        IReadOnlyList<byte> data)
    {
        var direction = Directions[index];
        byte[] arguments;
        switch (functionId)
        {
            case RazerHidMappingFunctions.Off:
                arguments =
                [
                    KeymapId, direction.T1, direction.T2, 1, direction.T3,
                ];
                break;
            case RazerHidMappingFunctions.ButtonCode when data.Count >= 1:
                arguments =
                [
                    KeymapId, direction.T1, 6, 1, MapButtonInverse(data[0]),
                ];
                break;
            case RazerHidMappingFunctions.KeyCode when data.Count >= 2:
                // FUN_100e87a0 writes the key usage as the sixth data byte and
                // leaves the modifier byte at zero for a D-pad key action, so a
                // modifier + key combo cannot be represented here.
                if (data[0] != 0)
                {
                    throw new NotSupportedException(
                        "A legacy keypad D-pad direction does not support modifier + key combinations.");
                }

                arguments =
                [
                    KeymapId, direction.T1, 2, 2, 0, data[1],
                ];
                break;
            default:
                throw new NotSupportedException(
                    $"A legacy keypad D-pad direction does not support function 0x{functionId:X2}.");
        }

        return new(
            "keypad.setDpadDirection",
            RazerFeatureReport.CreateRequest(
                RazerHidMappingProtocol.TransactionId,
                (byte)arguments.Length,
                MappingClass,
                WriteCommand,
                arguments));
    }

    internal static RazerHidButtonAssignment ParseDirection(
        ReadOnlySpan<byte> response,
        int index,
        byte buttonId,
        byte mode)
    {
        var arguments = RazerHidStandardProtocol.Arguments(response, ReadDataSize);
        if (arguments[4] != Directions[index].Selector)
        {
            throw new InvalidDataException(
                $"The keypad D-pad reply selected 0x{arguments[4]:X2}, " +
                $"not 0x{Directions[index].Selector:X2}.");
        }

        return arguments[5] switch
        {
            0 or 0xB => new RazerHidButtonAssignment(
                buttonId, mode, RazerHidMappingFunctions.Off, []),
            1 => new RazerHidButtonAssignment(
                buttonId, mode, RazerHidMappingFunctions.ButtonCode,
                [MapButton(arguments[7])]),
            2 => new RazerHidButtonAssignment(
                buttonId, mode, RazerHidMappingFunctions.KeyCode,
                [arguments[7], arguments[8]]),
            _ => new RazerHidButtonAssignment(
                buttonId, mode, RazerHidMappingFunctions.Off, []),
        };
    }

    private static byte MapButton(byte value) => value switch
    {
        4 => 5,
        5 => 4,
        6 => 0x0C,
        7 => 0x0D,
        9 => 6,
        10 => 7,
        0x0B => 9,
        0x0C => 10,
        _ => value,
    };

    private static byte MapButtonInverse(byte value) => value switch
    {
        5 => 4,
        4 => 5,
        0x0C => 6,
        0x0D => 7,
        6 => 9,
        7 => 10,
        9 => 0x0B,
        10 => 0x0C,
        _ => value,
    };
}
