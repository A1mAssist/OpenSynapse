using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.Windows.Protocols;

public sealed partial class RazerFeatureTransport
{
    private sealed class RazerFeatureSession(
        SafeFileHandle handle,
        SemaphoreSlim gate,
        string devicePath,
        ushort? logicalProductId) : IRazerFeatureSession
    {
        private readonly object _transactionSync = new();
        private byte _transactionId;
        private int _dockTransactionSequence = -1;
        private int _disposed;
        private Task? _pendingCall;

        private void EnsureUsable()
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (_pendingCall is not null) throw new IOException("The HID session timed out and must be reopened.");
            ThrowIfNativeCallPending(devicePath);
        }

        private void OnPending(Task task)
        {
            _pendingCall = task;
            TrackPendingCall(devicePath, task);
        }

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
                EnsureUsable();
                PrepareSessionTransaction(devicePath, logicalProductId, report, ref _dockTransactionSequence);
                var result = await CancellableNativeCall.RunAsync(() =>
                {
                    var success = NativeMethods.HidD_SetFeature(handle, report, report.Length);
                    return (Success: success, Error: Marshal.GetLastWin32Error());
                }, NativeCallTimeout, cancellationToken, OnPending).ConfigureAwait(false);
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

            // A timed-out native call can still own these buffers after the pump returns.
            requests = requests.Select(request => request.ToArray()).ToArray();

            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureUsable();
                // One bounded worker owns the whole frame, without a pool hop for every row.
                await CancellableNativeCall.RunAsync(token => SendFrameAsync(token).GetAwaiter().GetResult(),
                    NativeCallTimeout, cancellationToken, OnPending).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }

            async Task<bool> SendFrameAsync(CancellationToken frameToken)
            {
                var pacedKeyboard = logicalProductId is { } productId &&
                    StudioKeyboardCatalog.TryGet(RazerProductAliases.Canonical(productId), out _);
                for (var index = 0; index < requests.Count - 1; index++)
                {
                    var request = requests[index];
                    PrepareSessionTransaction(devicePath, logicalProductId, request, ref _dockTransactionSequence);
                    var result = await SetFeatureAsync(handle, request, directIo: true, frameToken)
                        .ConfigureAwait(false);
                    if (!result.Success)
                    {
                        throw new Win32Exception(result.Error, "A Razer frame report was rejected.");
                    }
                    // rzDevice25.setChromaFrameLightingEngine spaces consecutive rows by 1 ms.
                    if (pacedKeyboard)
                        await WaitAsync(TimeSpan.FromMilliseconds(1), directIo: true, frameToken).ConfigureAwait(false);
                }

                var last = requests[^1];
                PrepareSessionTransaction(devicePath, logicalProductId, last, ref _dockTransactionSequence);
                await ExecuteOnHandleAsync(
                    handle,
                    last,
                    deviceWait,
                    last[0],
                    frameToken,
                    allowRemainingPacketsMismatch: true,
                    usesLeviathanReportId: false,
                    usesMouseDockLinker: UsesMouseDockLinker(devicePath),
                    directIo: true).ConfigureAwait(false);
                return true;
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
                EnsureUsable();
                PrepareSessionTransaction(devicePath, logicalProductId, request, ref _dockTransactionSequence);
                if (directIo)
                    return await CancellableNativeCall.RunAsync(token => ExecuteOnHandleAsync(
                        handle, request, deviceWait, responseReportId, token,
                        allowRemainingPacketsMismatch, false, UsesMouseDockLinker(devicePath),
                        directIo: true).GetAwaiter().GetResult(), NativeCallTimeout, cancellationToken,
                        OnPending).ConfigureAwait(false);
                return await ExecuteOnHandleAsync(
                    handle,
                    request,
                    deviceWait,
                    responseReportId,
                    cancellationToken,
                    allowRemainingPacketsMismatch,
                    usesLeviathanReportId: false,
                    usesMouseDockLinker: UsesMouseDockLinker(devicePath),
                    directIo: false, onPending: OnPending).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                await gate.WaitAsync().ConfigureAwait(false);
                try { DisposeHandleAfterCall(handle, _pendingCall); }
                finally { gate.Release(); }
            }
        }
    }

    internal static void PrepareSessionTransaction(
        string devicePath, ushort? logicalProductId, byte[] request, ref int sequence)
    {
        if (!DeviceIdParser.TryParse(devicePath, out var vendorId, out var productId) ||
            vendorId != 0x1532)
        {
            return;
        }

        var isHost = RazerDualDongleCatalog.CanHost(productId);
        if (logicalProductId is { } keyboardProductId &&
            StudioKeyboardCatalog.TryGet(RazerProductAliases.Canonical(keyboardProductId), out _))
        {
            // Share the settings channel's rolling transaction counter across frame sessions.
            PrepareStandardTransaction(devicePath, request, logicalProductId);
        }
        else if (isHost && logicalProductId is { } childProductId && childProductId != productId)
        {
            // Linked children share their rolling counter with single queries across HID opens.
            PrepareStandardTransaction(devicePath, request, childProductId);
        }
        else if (productId is 0x00A4 or 0x00CF or 0x00F1 or 0x02DA || isHost && IsLinkerCommand(request[7], request[8]))
        {
            request[2] = PrepareTransactionId(
                devicePath, request[2], request[7], request[8], ref sequence, logicalProductId);
        }
    }
}
