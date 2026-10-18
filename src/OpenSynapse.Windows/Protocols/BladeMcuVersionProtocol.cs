namespace OpenSynapse.Windows.Protocols;

public static class BladeMcuVersionProtocol
{
    public const byte CommandClass = 0x00;
    public const byte CommandId = 0x87;
    public const int DataSize = 4;

    public static byte[] CreateGetRequest() =>
        RazerFeatureReport.CreateRequest(
            transactionId: 0x00,
            dataSize: DataSize,
            commandClass: CommandClass,
            commandId: CommandId,
            arguments: new byte[DataSize]);

    public static string Parse(ReadOnlySpan<byte> response, ReadOnlySpan<byte> request)
    {
        if (!RazerFeatureReport.IsSuccessfulResponse(request, response, minimumArguments: DataSize))
        {
            throw new InvalidOperationException("Blade MCU version returned an invalid or out-of-order feature report.");
        }

        var offset = RazerFeatureReport.ArgumentsOffset;
        return $"{response[offset]}.{response[offset + 1]}.{response[offset + 2]}.{response[offset + 3]}";
    }
}
