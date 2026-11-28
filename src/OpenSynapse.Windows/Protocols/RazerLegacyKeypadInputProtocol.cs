using System.Buffers.Binary;

namespace OpenSynapse.Windows.Protocols;

internal static class RazerLegacyKeypadInputProtocol
{
    internal const uint EnumerateIoctl = 0x88883000;
    internal const uint ControlIoctl = 0x88883004;
    internal const uint ReadInputIoctl = 0x88883008;
    internal const int EventLength = 64;

    internal static byte[] Enumerate(uint index)
    {
        var data = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16), index);
        return data;
    }

    internal static byte[] CaptureCommand(uint command)
    {
        var data = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), command);
        return data;
    }

    internal static int Status(ReadOnlySpan<byte> reply)
    {
        if (reply.Length < 8) throw new InvalidDataException("The legacy keypad reply has no status.");
        return BinaryPrimitives.ReadInt32LittleEndian(reply);
    }

    internal static IReadOnlyList<BladeMappingInputEvent> ParseInput(ReadOnlySpan<byte> frame)
    {
        if (frame.Length != EventLength) throw new InvalidDataException("A legacy keypad input record must be 64 bytes.");
        var events = new List<BladeMappingInputEvent>();
        if (BinaryPrimitives.ReadUInt32LittleEndian(frame[8..]) != 0x8000000E) return events;
        var kind = BinaryPrimitives.ReadUInt32LittleEndian(frame[4..]);
        if (kind == 2)
        {
            var scan = BinaryPrimitives.ReadUInt16LittleEndian(frame[12..]);
            var flags = BinaryPrimitives.ReadUInt16LittleEndian(frame[14..]);
            if (scan == 0 || flags > 1) throw new InvalidDataException("Unknown legacy keypad keyboard record.");
            // rzudd drops E0; these keypads have arrow keys and no numeric keypad.
            events.Add(new(BladeMappingInputKind.Keyboard, scan, (flags & 1) == 0,
                scan is 0x48 or 0x50 or 0x4B or 0x4D));
        }
        else if (kind == 1)
        {
            var flags = BinaryPrimitives.ReadUInt16LittleEndian(frame[12..]);
            if ((flags & ~0x07FF) != 0) throw new InvalidDataException("Unknown legacy keypad mouse flags.");
            for (var button = 1; button <= 5; button++)
            {
                var down = 1 << ((button - 1) * 2);
                if ((flags & down) != 0) events.Add(new(BladeMappingInputKind.MouseButton, button, true));
                if ((flags & (down << 1)) != 0) events.Add(new(BladeMappingInputKind.MouseButton, button, false));
            }
            if ((flags & 0x0400) != 0)
                events.Add(new(BladeMappingInputKind.MouseWheel, BinaryPrimitives.ReadInt16LittleEndian(frame[14..]), true));
        }
        else throw new InvalidDataException("Unknown legacy keypad input kind.");
        return events;
    }
}
