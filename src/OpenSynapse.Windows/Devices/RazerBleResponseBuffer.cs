using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

// Compact headers remain ambiguous until the notification window closes.
internal sealed class RazerBleResponseBuffer(byte[] request)
{
    private readonly List<byte> _bytes = [];
    private bool Matches(ReadOnlySpan<byte> bytes) => bytes.Length >= 8 &&
        bytes[0] == request[0] && bytes[4] == request[4] && bytes[5] == request[5];

    internal byte[]? Append(byte[] fragment)
    {
        if (_bytes.Count >= 8 && !Matches(_bytes.ToArray()) && Matches(fragment)) _bytes.Clear();
        _bytes.AddRange(fragment);
        if (_bytes.Count > RazerBleProtocol30.ResponseHeaderLength + RazerBleProtocol30.MaximumPayloadLength)
            throw new InvalidOperationException("BLE response exceeds the maximum frame length.");
        while (RazerBleProtocol30.TryGetCompleteResponseLength(_bytes.ToArray(), out var length))
        {
            var frame = _bytes.Take(length).ToArray();
            _bytes.RemoveRange(0, length);
            if (Matches(frame)) return frame;
        }
        return null;
    }

    internal byte[]? Finish()
    {
        var frame = _bytes.ToArray();
        if (!Matches(frame)) return null;
        try { _ = RazerBleProtocol30.ParseResponse(frame); return frame; }
        catch (InvalidOperationException) { return null; }
    }
}
