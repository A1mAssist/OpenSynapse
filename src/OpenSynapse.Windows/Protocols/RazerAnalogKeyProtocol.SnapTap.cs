namespace OpenSynapse.Windows.Protocols;

public readonly record struct RazerAnalogSnapTapPair(byte KeyId1, byte KeyId2, byte Mode);

public sealed record RazerAnalogSnapTapState(
    byte ProfileId,
    bool Enabled,
    IReadOnlyList<RazerAnalogSnapTapPair> Pairs);

public static partial class RazerAnalogKeyProtocol
{
    public const int MaxSnapTapPairs = 4;

    // Tuples are [15, command class 2, id] from the middleware Snap Tap implementation.
    private const byte GetSnapTapCtrl = 0xA7;
    private const byte SetSnapTapCtrl = 0x27;

    public static byte[] CreateGetAnalogSnapTapRequest(byte transactionId, byte profileId)
    {
        var arguments = new byte[15];
        arguments[0] = profileId;
        return RazerFeatureReport.CreateRequest(
            transactionId, (byte)arguments.Length, CommandClass, GetSnapTapCtrl, arguments);
    }

    public static byte[] CreateSetAnalogSnapTapRequest(
        byte transactionId,
        byte profileId,
        bool enabled,
        IReadOnlyList<RazerAnalogSnapTapPair> pairs)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        if (pairs.Count > MaxSnapTapPairs)
        {
            throw new ArgumentOutOfRangeException(nameof(pairs), $"At most {MaxSnapTapPairs} pairs fit.");
        }

        // The middleware sends the full 15-byte staging buffer, zero padded.
        var arguments = new byte[15];
        arguments[0] = profileId;
        arguments[1] = (byte)pairs.Count;
        arguments[2] = enabled ? (byte)0x01 : (byte)0x00;
        for (var index = 0; index < pairs.Count; index++)
        {
            var offset = 3 + (index * 3);
            arguments[offset] = pairs[index].KeyId1;
            arguments[offset + 1] = pairs[index].KeyId2;
            arguments[offset + 2] = pairs[index].Mode;
        }

        return RazerFeatureReport.CreateRequest(
            transactionId, (byte)arguments.Length, CommandClass, SetSnapTapCtrl, arguments);
    }

    public static RazerAnalogSnapTapState ParseAnalogSnapTap(ReadOnlySpan<byte> response)
    {
        var data = Arguments(response, 3);
        var count = data[1];
        if (count > MaxSnapTapPairs || data.Length < 3 + (count * 3))
        {
            throw new InvalidDataException("Razer response has a malformed Snap Tap record.");
        }

        var pairs = new List<RazerAnalogSnapTapPair>(count);
        for (var offset = 0; offset < count; offset++)
        {
            var index = 3 + (offset * 3);
            pairs.Add(new RazerAnalogSnapTapPair(data[index], data[index + 1], data[index + 2]));
        }

        return new RazerAnalogSnapTapState(data[0], data[2] != 0, pairs);
    }
}
