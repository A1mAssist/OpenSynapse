namespace OpenSynapse.Windows.Protocols;

public static partial class RazerHeadsetAudioProtocol
{
    public static byte[] GetEdition(byte transactionId) =>
        Create(transactionId, 3, 0x00, 0x86);

    public static byte[] GetSpatialMode(byte transactionId) =>
        Create(transactionId, 2, 0x08, 0x90, ReadProfile);

    public static byte[] SetSpatialMode(byte transactionId, byte mode)
    {
        if (mode > 3) throw new ArgumentOutOfRangeException(nameof(mode));
        return Create(transactionId, 2, 0x08, 0x10, WriteProfile, mode);
    }

    public static byte ReadSpatialMode(ReadOnlySpan<byte> response)
    {
        var data = Arguments(response);
        Require(data, 2);
        return data[1] <= 3 ? data[1]
            : throw new InvalidDataException($"Invalid Leviathan spatial mode: {data[1]}.");
    }

    internal static ReadOnlySpan<byte> GetArguments(ReadOnlySpan<byte> response) => Arguments(response);
}
