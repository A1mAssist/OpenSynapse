namespace OpenSynapse.Windows.Protocols;

public enum RazerLaylaInput { Click, DoubleClick, TripleClick, Hold, SourceClick, SourceDoubleClick, Clockwise, Anticlockwise }

/// <summary>0566/rzDevice30Layla and rzHardwareEventsProtocolLaylaParser.</summary>
public static class RazerLaylaProtocol
{
    public const ushort ProductId = 0x0566;
    public static readonly Guid ServiceUuid = new("0000fd65-0000-1000-8000-00805f9b34fb");
    public static readonly Guid WriteUuid = new("416d0000-2d52-617a-6572-424c4501f40a");
    public static readonly Guid NotifyUuid = new("416d0001-2d52-617a-6572-424c4501f40a");

    public static byte[] CreateFrame(byte transaction, byte commandClass, byte command, ReadOnlySpan<byte> payload = default)
    {
        var frame = RazerBleProtocol30.CreateFrame(transaction, commandClass, command, payload: payload);
        (frame[1], frame[3]) = (frame[3], frame[1]);
        return frame;
    }

    public static RazerBleResponse ParseResponse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 8) throw new InvalidOperationException("Layla reply has no complete header.");
        var length = (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];
        // The local Electron strategy accepts this firmware length anomaly.
        if (length > 65535) length = bytes[1] | (bytes[2] << 8) | (bytes[3] << 16);
        if (bytes.Length != 8 + length) throw new InvalidOperationException("Layla reply length mismatch.");
        return new(bytes[0], bytes[4], bytes[5], bytes[7], bytes[8..].ToArray(), 8);
    }

    public static RazerLaylaInput? ParseInput(ReadOnlySpan<byte> report)
    {
        if (report.Length < 4 || report[0] != 1) return null;
        // The JS gives source precedence over top, and top precedence over rotation.
        if (report[1] != 0) return report[1] switch { 1 => RazerLaylaInput.SourceClick, 2 => RazerLaylaInput.SourceDoubleClick, _ => null };
        if (report[2] != 0) return report[2] switch { 1 => RazerLaylaInput.Click, 2 => RazerLaylaInput.DoubleClick, 3 => RazerLaylaInput.TripleClick, 4 => RazerLaylaInput.Hold, _ => null };
        return report[3] switch { 1 => RazerLaylaInput.Clockwise, 255 => RazerLaylaInput.Anticlockwise, _ => null };
    }
}
