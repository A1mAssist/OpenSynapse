using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class UsbAccessoriesWorkspace
{
    /// <summary>
    /// Headset stands, lamps and speakers sit on standby power and stay lit after
    /// the session ends. They only have a Chroma page, so the RazerHid shutdown
    /// sweep never sees them.
    /// </summary>
    internal void TurnOffChromaLightingForShutdown(TimeSpan timeout)
    {
        var connections = ChromaDevices.Select(row => row.Connection).ToArray();
        if (connections.Length == 0)
        {
            return;
        }

        var task = Task.Run(async () =>
        {
            await Task.WhenAll(connections.Select(async connection =>
            {
                try
                {
                    using var deviceTimeout =
                        new CancellationTokenSource(TimeSpan.FromMilliseconds(700));
                    var service = new RazerChromaStandardService(
                        new RazerFeatureTransport(),
                        connection.DevicePath,
                        TimeSpan.FromMilliseconds(20),
                        message => _diagnosticLog.TryWrite("chroma", message),
                        connection.Channel,
                        connection.ProductId);
                    await service.TurnOffForShutdownAsync(deviceTimeout.Token).ConfigureAwait(false);
                    _diagnosticLog.TryWrite(
                        "lighting-shutdown",
                        $"chroma {connection.ProductId:X4} off request sent.");
                }
                catch (Exception exception)
                {
                    _diagnosticLog.TryWrite(
                        "lighting-shutdown",
                        $"chroma {connection.ProductId:X4} off failed: {exception.Message}");
                }
            })).ConfigureAwait(false);
        });
        try
        {
            if (!task.Wait(timeout))
            {
                _diagnosticLog.TryWrite(
                    "lighting-shutdown",
                    $"chroma off timed out after {timeout.TotalSeconds:0.#}s.");
            }
        }
        catch (AggregateException exception)
        {
            _diagnosticLog.TryWrite(
                "lighting-shutdown",
                $"chroma off failed: {exception.GetBaseException().Message}");
        }
    }
}
