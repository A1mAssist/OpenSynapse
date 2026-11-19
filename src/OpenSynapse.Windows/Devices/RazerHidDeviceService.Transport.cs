using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidDeviceService
{
    // The Huntsman V3 Pro family commits onboard writes to flash before it answers.
    // 02CF needs about seven 190 ms retries for one mapping write, so the old
    // 8 x 80 ms budget gave up while the firmware was still committing.
    private const int BusyWriteAttempts = 8;
    private static readonly TimeSpan BusyWriteDelay = TimeSpan.FromMilliseconds(250);

    private Task<byte[]> ExecuteAsync(
        RazerHidDeviceConnection connection,
        RazerHidRequest request,
        CancellationToken cancellationToken) =>
        ExecuteWithWirelessLinkClassificationAsync(connection, request, cancellationToken);

    private async Task ExecuteWithoutResultAsync(
        RazerHidDeviceConnection connection,
        RazerHidRequest request,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await ExecuteAsync(connection, request, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (RazerDeviceTimeoutException exception)
                when (exception.IsRetryableBusy && attempt < BusyWriteAttempts)
            {
                WriteDiagnostic(connection.Definition.ProductId,
                    $"write busy {request.BuilderName}: attempt {attempt}/{BusyWriteAttempts}; retrying");
                await Task.Delay(BusyWriteDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// The lighting engine already retries its own effect writes and streams rows at
    /// 60 Hz, so those paths must not multiply the retry budget.
    /// </summary>
    private Task ExecuteOnceWithoutResultAsync(
        RazerHidDeviceConnection connection,
        RazerHidRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(connection, request, cancellationToken);

    private static void RequireReady(RazerHidDeviceConnection connection) =>
        RazerHidRequestExecutor.RequireReady(connection);
}
