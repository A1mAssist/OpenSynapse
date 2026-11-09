using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace OpenSynapse.Windows.Protocols;

public sealed partial class RazerFeatureTransport
{
    private sealed class RazerFeatureSession(
        SafeFileHandle handle,
        SemaphoreSlim gate,
        string devicePath) : IRazerFeatureSession
    {
        private readonly object _transactionSync = new();
        private byte _transactionId;
        private int _disposed;

        public byte NextTransactionId()
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            lock (_transactionSync)
            {
                var current = _transactionId;
                _transactionId = current == 30 ? (byte)0 : (byte)(current + 1);
                return current;
            }
        }

        public async Task SendAsync(
            ReadOnlyMemory<byte> request,
            CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (request.Length != RazerFeatureReport.Length)
            {
                throw new ArgumentException("A Razer feature report must be 91 bytes.", nameof(request));
            }

            var report = request.ToArray();
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (UsesMouseDockLinker(devicePath)) PrepareStandardTransaction(devicePath, report);
                var result = await Task.Run(() =>
                {
                    var success = NativeMethods.HidD_SetFeature(handle, report, report.Length);
                    return (Success: success, Error: Marshal.GetLastWin32Error());
                }, cancellationToken).ConfigureAwait(false);
                if (!result.Success)
                {
                    throw new Win32Exception(result.Error, "Razer feature session handshake failed.");
                }
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task SendBatchAsync(
            IReadOnlyList<byte[]> requests,
            TimeSpan rowDelay,
            CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            ArgumentNullException.ThrowIfNull(requests);
            if (requests.Count == 0 || requests.Any(request => request.Length != RazerFeatureReport.Length))
            {
                throw new ArgumentException("A Razer feature report batch must contain at least one 91-byte report.", nameof(requests));
            }

            foreach (var request in requests)
            {
                await QueryAsync(
                    request[2],
                    request[6],
                    request[7],
                    request[8],
                    request.AsMemory(RazerFeatureReport.ArgumentsOffset, request[6]),
                    rowDelay,
                    request[0],
                    cancellationToken,
                    allowRemainingPacketsMismatch: true).ConfigureAwait(false);
            }
        }

        public Task<byte[]> QueryAsync(
            byte transactionId,
            byte dataSize,
            byte commandClass,
            byte commandId,
            ReadOnlyMemory<byte> arguments,
            TimeSpan deviceWait,
            byte responseReportId,
            CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var request = RazerFeatureReport.CreateRequest(
                transactionId,
                dataSize,
                commandClass,
                commandId,
                arguments.Span);
            return QueryCoreAsync(
                request,
                deviceWait,
                responseReportId,
                cancellationToken,
                allowRemainingPacketsMismatch,
                directIo: false);
        }

        public Task<byte[]> QueryPreparedFastAsync(
            ReadOnlyMemory<byte> request,
            TimeSpan deviceWait,
            byte responseReportId,
            CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (request.Length != RazerFeatureReport.Length)
            {
                throw new ArgumentException("A Razer feature report must be 91 bytes.", nameof(request));
            }

            return QueryCoreAsync(
                request.ToArray(),
                deviceWait,
                responseReportId,
                cancellationToken,
                allowRemainingPacketsMismatch,
                directIo: true);
        }

        public async Task SendBatchFastAsync(
            IReadOnlyList<byte[]> requests,
            TimeSpan deviceWait,
            CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            ArgumentNullException.ThrowIfNull(requests);
            if (requests.Count == 0 ||
                requests.Any(request => request.Length != RazerFeatureReport.Length))
            {
                throw new ArgumentException(
                    "A Razer feature report batch must contain at least one 91-byte report.",
                    nameof(requests));
            }

            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                for (var index = 0; index < requests.Count - 1; index++)
                {
                    var request = requests[index];
                    if (UsesMouseDockLinker(devicePath)) PrepareStandardTransaction(devicePath, request);
                    var result = await SetFeatureAsync(handle, request, directIo: true, cancellationToken)
                        .ConfigureAwait(false);
                    if (!result.Success)
                    {
                        throw new Win32Exception(result.Error, "A Razer frame report was rejected.");
                    }
                }

                var last = requests[^1];
                if (UsesMouseDockLinker(devicePath)) PrepareStandardTransaction(devicePath, last);
                await ExecuteOnHandleAsync(
                    handle,
                    last,
                    deviceWait,
                    last[0],
                    cancellationToken,
                    allowRemainingPacketsMismatch: true,
                    usesLeviathanReportId: false,
                    directIo: true).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }

        private async Task<byte[]> QueryCoreAsync(
            byte[] request,
            TimeSpan deviceWait,
            byte responseReportId,
            CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch,
            bool directIo)
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (UsesMouseDockLinker(devicePath)) PrepareStandardTransaction(devicePath, request);
                return await ExecuteOnHandleAsync(
                    handle,
                    request,
                    deviceWait,
                    responseReportId,
                    cancellationToken,
                    allowRemainingPacketsMismatch,
                    usesLeviathanReportId: false,
                    directIo: directIo).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                handle.Dispose();
            }
            return ValueTask.CompletedTask;
        }
    }
}
