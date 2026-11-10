namespace OpenSynapse.Windows.Protocols;

public static partial class RazerHeadsetAudioProtocol
{
    public static byte[] GetEdition(byte transactionId) =>
        Create(transactionId, 3, 0x00, 0x86);

    internal static ReadOnlySpan<byte> GetArguments(ReadOnlySpan<byte> response) => Arguments(response);
}
