using System.Collections.Concurrent;
using System.ComponentModel;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// The HID exchange itself: readiness, application-side diagnostics, the transport
/// call, and dropping a cached endpoint once a write fails at the OS layer.
/// </summary>
internal sealed class OpenRazerRequestExecutor
{
    private readonly IRazerFeatureTransport _transport;
    private readonly ConcurrentDictionary<string, string> _endpointCache;
    private readonly Action<ushort, string> _diagnostic;

    public OpenRazerRequestExecutor(
        IRazerFeatureTransport transport,
        ConcurrentDictionary<string, string> endpointCache,
        Action<ushort, string> diagnostic)
    {
        _transport = transport;
        _endpointCache = endpointCache;
        _diagnostic = diagnostic;
    }

    public async Task<byte[]> ExecuteAsync(
        OpenRazerDeviceConnection connection,
        OpenRazerRequest request,
        CancellationToken cancellationToken)
    {
        RequireReady(connection);
        // The transport log only records a request once it reaches the HID layer, so a
        // command dropped before that point would be invisible. Record the application
        // side as well to tell "never sent" apart from "sent and refused".
        _diagnostic(connection.Definition.ProductId,
            $"send {request.BuilderName}: txn=0x{request.Report[2]:X2}, size={request.Report[6]}, class=0x{request.Report[7]:X2}, command=0x{request.Report[8]:X2}, args={Convert.ToHexString(request.Report.AsSpan(RazerFeatureReport.ArgumentsOffset, request.Report[6]))}");
        try
        {
            return await _transport.QueryPreparedAsync(
                connection.DevicePath!, request.Report, connection.Definition.DeviceWait,
                cancellationToken).ConfigureAwait(false);
        }
        catch (RazerCommandRejectedException exception)
        {
            throw new NotSupportedException(exception.Message, exception);
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException)
        {
            _endpointCache.TryRemove(connection.InstanceId, out _);
            throw;
        }
    }

    public Task ExecuteWithoutResultAsync(
        OpenRazerDeviceConnection connection,
        OpenRazerRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(connection, request, cancellationToken);

    public static void RequireReady(OpenRazerDeviceConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (!connection.IsReady || string.IsNullOrWhiteSpace(connection.DevicePath))
        {
            throw new InvalidOperationException("The OpenRazer Windows feature endpoint is not resolved.");
        }
    }
}
