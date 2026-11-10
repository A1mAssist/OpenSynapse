using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using OpenSynapse.Core.Diagnostics;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// The HID exchange itself: readiness, application-side diagnostics, the transport
/// call, and dropping a cached endpoint once a write fails at the OS layer.
/// </summary>
internal sealed class RazerHidRequestExecutor
{
    private readonly IRazerFeatureTransport _transport;
    private readonly ConcurrentDictionary<string, string> _endpointCache;
    private readonly Action<ushort, string> _diagnostic;
    private readonly bool _traceSuccessfulSends;

    public RazerHidRequestExecutor(
        IRazerFeatureTransport transport,
        ConcurrentDictionary<string, string> endpointCache,
        Action<ushort, string> diagnostic)
        : this(transport, endpointCache, diagnostic, traceSuccessfulSends: null)
    {
    }

    internal RazerHidRequestExecutor(
        IRazerFeatureTransport transport,
        ConcurrentDictionary<string, string> endpointCache,
        Action<ushort, string> diagnostic,
        bool? traceSuccessfulSends)
    {
        _transport = transport;
        _endpointCache = endpointCache;
        _diagnostic = diagnostic;
        _traceSuccessfulSends = traceSuccessfulSends ?? LocalDiagnosticLog.TraceSuccessfulHidRequests;
    }

    public async Task<byte[]> ExecuteAsync(
        RazerHidDeviceConnection connection,
        RazerHidRequest request,
        CancellationToken cancellationToken)
    {
        RequireReady(connection);
        // The transport log only records a request once it reaches the HID layer, so a
        // command dropped before that point would be invisible. This application-side
        // trace is opt-in: at 60 Hz it would append to disk for every frame row.
        if (_traceSuccessfulSends)
        {
            _diagnostic(connection.Definition.ProductId,
                $"send {request.BuilderName}: txn=0x{request.Report[2]:X2}, size={request.Report[6]}, class=0x{request.Report[7]:X2}, command=0x{request.Report[8]:X2}, args={Convert.ToHexString(request.Report.AsSpan(RazerFeatureReport.ArgumentsOffset, request.Report[6]))}");
        }
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
        catch (RazerDeviceTimeoutException exception)
        {
            // Busy/silent is a capability result, not a dead endpoint.
            throw new RazerDeviceTimeoutException(
                exception.IsRetryableBusy
                    ? $"The device is busy (0x01) for {request.BuilderName} (command 0x{request.Report[7]:X2}:0x{request.Report[8]:X2})."
                    : $"{request.BuilderName} did not answer (command 0x{request.Report[7]:X2}:0x{request.Report[8]:X2}): {exception.Message}",
                exception.IsRetryableBusy);
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or COMException or InvalidOperationException)
        {
            _endpointCache.TryRemove(connection.InstanceId, out _);
            throw;
        }
    }

    public Task ExecuteWithoutResultAsync(
        RazerHidDeviceConnection connection,
        RazerHidRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(connection, request, cancellationToken);

    public static void RequireReady(RazerHidDeviceConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (!connection.IsReady || string.IsNullOrWhiteSpace(connection.DevicePath))
        {
            throw new InvalidOperationException("The RazerHid Windows feature endpoint is not resolved.");
        }
    }
}
