namespace OpenSynapse.Windows.Protocols;

public sealed partial class RazerFeatureTransport
{
    public Task<byte[]> QueryPreparedForProductAsync(
        string devicePath,
        ushort logicalProductId,
        ReadOnlyMemory<byte> request,
        TimeSpan deviceWait,
        CancellationToken cancellationToken,
        bool allowRemainingPacketsMismatch = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);
        RazerAudioFeatureProtocol.ValidatePreparedRequest(logicalProductId, request.Span);
        return ExecuteAsync(
            devicePath,
            request.ToArray(),
            deviceWait,
            request.Span[0],
            cancellationToken,
            allowRemainingPacketsMismatch,
            logicalProductId);
    }
}
