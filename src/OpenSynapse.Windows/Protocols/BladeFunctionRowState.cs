namespace OpenSynapse.Windows.Protocols;

public static class BladeFunctionRowState
{
    public static async Task<bool> ReadPrimaryAsync(
        IRazerFeatureSession session,
        Action<string>? diagnostic,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        var request = BladeSynapsePolicyProtocol.CreateGetFnKeyStateRequest();
        try
        {
            var response = await session.QueryAsync(
                    request[2],
                    request[6],
                    request[7],
                    request[8],
                    request.AsMemory(RazerFeatureReport.ArgumentsOffset, request[6]),
                    TimeSpan.FromMilliseconds(20),
                    responseReportId: 0,
                    cancellationToken)
                .ConfigureAwait(false);
            return !BladeSynapsePolicyProtocol
                .ParseFnKeyState(response, request)
                .MultiFunctionPrimary;
        }
        catch (Exception exception)
        {
            // Older Blade families expose the same setting through the 07:8B swap
            // command instead of the 02:86 live profile.
            var swapRequest = BladeSynapsePolicyProtocol.CreateGetFnKeySwapRequest();
            try
            {
                var swapResponse = await session.QueryAsync(
                        swapRequest[2],
                        swapRequest[6],
                        swapRequest[7],
                        swapRequest[8],
                        swapRequest.AsMemory(RazerFeatureReport.ArgumentsOffset, swapRequest[6]),
                        TimeSpan.FromMilliseconds(20),
                        responseReportId: 0,
                        cancellationToken)
                    .ConfigureAwait(false);
                return !BladeSynapsePolicyProtocol
                    .ParseFnKeySwap(swapResponse, swapRequest)
                    .MultiFunctionPrimary;
            }
            catch (Exception swapException)
            {
                // The shipped graph is function-primary; an unreadable Fn state keeps it.
                diagnostic?.Invoke(
                    $"fn row mode unavailable: {exception.Message}; swap: {swapException.Message}");
                return true;
            }
        }
    }
}
