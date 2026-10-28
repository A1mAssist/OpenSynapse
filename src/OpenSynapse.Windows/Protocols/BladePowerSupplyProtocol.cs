namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// rzDevice25 00:B7 external power status. The middleware only exposes the raw
/// status byte; no enum or scale is defined in the JS.
/// </summary>
public static class BladePowerSupplyProtocol
{
    public const byte CommandClass = 0x00;
    public const byte CommandId = 0xB7;
    public const byte DataSize = 0x01;

    public static byte[] CreateGetExternalPowerStatusRequest() =>
        RazerFeatureReport.CreateRequest(
            transactionId: 0x00,
            dataSize: DataSize,
            commandClass: CommandClass,
            commandId: CommandId,
            arguments: ReadOnlySpan<byte>.Empty);

    public static byte ParseExternalPowerStatus(ReadOnlySpan<byte> response)
    {
        var request = CreateGetExternalPowerStatusRequest();
        if (!RazerFeatureReport.IsSuccessfulResponse(request, response, DataSize))
        {
            throw new InvalidOperationException(
                "Blade external power status returned an invalid or out-of-order feature report.");
        }

        return response[RazerFeatureReport.ArgumentsOffset];
    }
}
