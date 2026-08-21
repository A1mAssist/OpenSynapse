using System.Buffers.Binary;

namespace OpenSynapse.Windows.Protocols;

internal static class RazerFilterInputProtocol
{
    internal const uint ReadInput = 0x88883018;
    internal const uint EnableInputRedirect = 0x8888301C;
    internal const uint SetInputHook = 0x88883024;
    internal const uint ClearInputHook = 0x8888302C;
    internal const int InputFrameLength = 0x130;
    internal const int InputHookLength = 0x124;

    // Product 710's 23 official hooks all use modifier zero; Fn layering stays host-side.
    internal static byte[] CreateKeyboardHook(ushort scanCode, ushort flag)
    {
        var payload = new byte[InputHookLength];
        WriteKeyboardKey(payload, scanCode, flag);
        return payload;
    }

    internal static byte[] CreateKeyboardClearKey(ushort scanCode, ushort flag)
    {
        var payload = new byte[0x20];
        WriteKeyboardKey(payload, scanCode, flag);
        return payload;
    }

    private static void WriteKeyboardKey(Span<byte> payload, ushort scanCode, ushort flag)
    {
        if ((flag & ~3) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(flag),
                "Product 710 keyboard hook flag must be 0..3.");
        }

        BinaryPrimitives.WriteUInt32LittleEndian(payload[4..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(payload[10..], scanCode);
        BinaryPrimitives.WriteUInt16LittleEndian(payload[12..], (ushort)(flag & ~1));
    }

    internal static byte[] CreateKeyboardRedirect(bool enabled)
    {
        var payload = new byte[5];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, 1);
        payload[4] = enabled ? (byte)1 : (byte)0;
        return payload;
    }

    internal static bool TryParseInputFrame(
        ReadOnlySpan<byte> frame,
        out BladeMappingInputEvent input)
    {
        input = default;
        if (frame.Length != InputFrameLength)
            return false;

        var eventType = BinaryPrimitives.ReadUInt32LittleEndian(frame[8..]);
        var kind = BinaryPrimitives.ReadUInt32LittleEndian(frame[16..]);
        if ((eventType, kind) is not ((2, 1) or (4, 3)))
            return false;

        var flag = BinaryPrimitives.ReadUInt16LittleEndian(frame[24..]);
        if ((kind == 1 && (flag & ~3) != 0) ||
            (kind == 3 && (flag & ~1) != 0))
        {
            return false;
        }

        input = new BladeMappingInputEvent(
            kind == 1 ? BladeMappingInputKind.Keyboard : BladeMappingInputKind.RazerKey,
            BinaryPrimitives.ReadUInt16LittleEndian(frame[22..]),
            (flag & 1) == 0,
            kind == 1 && (flag & 2) != 0);
        return true;
    }
}
