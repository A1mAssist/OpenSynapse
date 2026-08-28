using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using OpenSynapse.Core.Diagnostics;

namespace OpenSynapse.Windows.Protocols;

public interface IRazerFeatureTransport
{
    Task<byte[]> QueryAsync(
        string devicePath,
        byte transactionId,
        byte dataSize,
        byte commandClass,
        byte commandId,
        ReadOnlyMemory<byte> arguments,
        TimeSpan deviceWait,
        CancellationToken cancellationToken,
        bool allowRemainingPacketsMismatch = false);

    Task<byte[]> QueryPreparedAsync(
        string devicePath,
        ReadOnlyMemory<byte> request,
        TimeSpan deviceWait,
        CancellationToken cancellationToken,
        bool allowRemainingPacketsMismatch = false) =>
        throw new NotSupportedException("This transport does not support prepared feature requests.");

    Task<IRazerFeatureSession> OpenSessionAsync(
        string devicePath,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("This transport does not support persistent HID sessions.");

    async Task SendBatchAsync(
        string devicePath,
        IReadOnlyList<byte[]> requests,
        TimeSpan rowDelay,
        CancellationToken cancellationToken)
    {
        foreach (var request in requests)
        {
            await QueryAsync(
                devicePath,
                request[2],
                request[6],
                request[7],
                request[8],
                request.AsMemory(RazerFeatureReport.ArgumentsOffset, request[6]),
                rowDelay,
                cancellationToken).ConfigureAwait(false);
        }
    }
}

public interface IRazerFeatureSession : IAsyncDisposable
{
    byte NextTransactionId();

    Task SendAsync(ReadOnlyMemory<byte> request, CancellationToken cancellationToken);

    async Task SendBatchAsync(
        IReadOnlyList<byte[]> requests,
        TimeSpan rowDelay,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requests);
        foreach (var request in requests)
        {
            await SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (rowDelay > TimeSpan.Zero)
            {
                await Task.Delay(rowDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    Task<byte[]> QueryAsync(
        byte transactionId,
        byte dataSize,
        byte commandClass,
        byte commandId,
        ReadOnlyMemory<byte> arguments,
        TimeSpan deviceWait,
        byte responseReportId,
        CancellationToken cancellationToken,
        bool allowRemainingPacketsMismatch = false);
}

public sealed class RazerFeatureTransport : IRazerFeatureTransport
{
    private readonly LocalDiagnosticLog? _diagnosticLog;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates =
        new(StringComparer.OrdinalIgnoreCase);

    public RazerFeatureTransport(LocalDiagnosticLog? diagnosticLog = null)
    {
        _diagnosticLog = diagnosticLog;
    }

    public Task<byte[]> QueryAsync(
        string devicePath,
        byte transactionId,
        byte dataSize,
        byte commandClass,
        byte commandId,
        ReadOnlyMemory<byte> arguments,
        TimeSpan deviceWait,
        CancellationToken cancellationToken,
        bool allowRemainingPacketsMismatch = false)
    {
        var request = RazerFeatureReport.CreateRequest(transactionId, dataSize, commandClass, commandId, arguments.Span);
        return ExecuteAsync(
            devicePath,
            request,
            deviceWait,
            responseReportId: 0,
            cancellationToken,
            allowRemainingPacketsMismatch);
    }

    public async Task<IRazerFeatureSession> OpenSessionAsync(
        string devicePath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);
        var handle = await OpenHandleAsync(devicePath, cancellationToken).ConfigureAwait(false);
        var gate = _gates.GetOrAdd(devicePath, static _ => new SemaphoreSlim(1, 1));
        return new RazerFeatureSession(handle, gate);
    }

    internal Task<byte[]> SendPreparedAsync(
        string devicePath,
        ReadOnlyMemory<byte> request,
        TimeSpan deviceWait,
        CancellationToken cancellationToken,
        bool allowRemainingPacketsMismatch = false)
    {
        RazerFeatureReport.ValidatePreparedStarlightRequest(request.Span);
        return ExecuteAsync(
            devicePath,
            request.ToArray(),
            deviceWait,
            responseReportId: 0,
            cancellationToken,
            allowRemainingPacketsMismatch);
    }

    public Task<byte[]> QueryPreparedAsync(
        string devicePath,
        ReadOnlyMemory<byte> request,
        TimeSpan deviceWait,
        CancellationToken cancellationToken,
        bool allowRemainingPacketsMismatch = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);
        RazerFeatureReport.ValidatePreparedRequest(request.Span);
        return ExecuteAsync(
            devicePath,
            request.ToArray(),
            deviceWait,
            responseReportId: 0,
            cancellationToken,
            allowRemainingPacketsMismatch);
    }

    public Task SendBatchAsync(
        string devicePath,
        IReadOnlyList<byte[]> requests,
        TimeSpan rowDelay,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count == 0)
            throw new ArgumentException("The HID report batch cannot be empty.", nameof(requests));
        if (requests.Any(request => request.Length != RazerFeatureReport.Length))
            throw new ArgumentException("Every HID report in the batch must be 91 bytes.", nameof(requests));

        return ExecuteBatchAsync(devicePath, requests, rowDelay, cancellationToken);
    }

    private async Task ExecuteBatchAsync(
        string devicePath,
        IReadOnlyList<byte[]> requests,
        TimeSpan rowDelay,
        CancellationToken cancellationToken)
    {
        var gate = _gates.GetOrAdd(devicePath, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                var handle = NativeMethods.CreateFile(
                    devicePath,
                    NativeMethods.GENERIC_WRITE,
                    NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
                    IntPtr.Zero,
                    NativeMethods.OPEN_EXISTING,
                    0,
                    IntPtr.Zero);
                var openError = Marshal.GetLastWin32Error();
                using (handle)
                {
                    if (handle.IsInvalid)
                        throw new Win32Exception(openError, "Could not open the Razer feature collection.");

                    foreach (var request in requests)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!NativeMethods.HidD_SetFeature(handle, request, request.Length))
                            throw new Win32Exception(Marshal.GetLastWin32Error(), "Razer matrix batch write failed.");
                        if (rowDelay > TimeSpan.Zero)
                            Thread.Sleep(rowDelay);
                    }
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<byte[]> ExecuteAsync(
        string devicePath,
        byte[] request,
        TimeSpan deviceWait,
        byte responseReportId,
        CancellationToken cancellationToken,
        bool allowRemainingPacketsMismatch)
    {
        // ponytail: paths are bounded by locally attached HID collections; prune only if
        // long-running hot-plug churn ever makes this cache measurably large.
        var gate = _gates.GetOrAdd(devicePath, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            try
            {
                using var handle = await OpenHandleAsync(devicePath, cancellationToken).ConfigureAwait(false);
                return await ExecuteOnHandleAsync(
                    handle,
                    request,
                    deviceWait,
                    responseReportId,
                    cancellationToken,
                    allowRemainingPacketsMismatch,
                    message => WriteDiagnostic(devicePath, request, message)).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException or NotSupportedException)
            {
                WriteDiagnostic(devicePath, request, $"failed: {exception.GetType().Name}: {exception.Message}");
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<SafeFileHandle> OpenHandleAsync(
        string devicePath,
        CancellationToken cancellationToken)
    {
        var openResult = await Task.Run(() =>
        {
            var handle = NativeMethods.CreateFile(
                devicePath,
                NativeMethods.GENERIC_WRITE,
                NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
                IntPtr.Zero,
                NativeMethods.OPEN_EXISTING,
                0,
                IntPtr.Zero);
            return (Handle: handle, Error: Marshal.GetLastWin32Error());
        }, cancellationToken).ConfigureAwait(false);
        if (!openResult.Handle.IsInvalid)
        {
            return openResult.Handle;
        }

        openResult.Handle.Dispose();
        throw new Win32Exception(openResult.Error, "Could not open the Razer feature collection.");
    }

    private static async Task<byte[]> ExecuteOnHandleAsync(
        SafeFileHandle handle,
        byte[] request,
        TimeSpan deviceWait,
        byte responseReportId,
        CancellationToken cancellationToken,
        bool allowRemainingPacketsMismatch,
        Action<string>? diagnostic = null)
    {
        string? lastError = null;
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var setResult = await Task.Run(() =>
            {
                var success = NativeMethods.HidD_SetFeature(handle, request, request.Length);
                return (Success: success, Error: Marshal.GetLastWin32Error());
            }, cancellationToken).ConfigureAwait(false);
            if (!setResult.Success)
            {
                lastError = new Win32Exception(setResult.Error).Message;
                diagnostic?.Invoke($"attempt {attempt}: HidD_SetFeature failed, win32={setResult.Error} ({lastError})");
                await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken).ConfigureAwait(false);
                continue;
            }

            await Task.Delay(deviceWait, cancellationToken).ConfigureAwait(false);
            var response = new byte[RazerFeatureReport.Length];
            response[0] = responseReportId;
            var getResult = await Task.Run(() =>
            {
                var success = NativeMethods.HidD_GetFeature(handle, response, response.Length);
                return (Success: success, Error: Marshal.GetLastWin32Error());
            }, cancellationToken).ConfigureAwait(false);
            if (!getResult.Success)
            {
                lastError = new Win32Exception(getResult.Error).Message;
                diagnostic?.Invoke($"attempt {attempt}: HidD_GetFeature failed, win32={getResult.Error} ({lastError})");
                await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (!RazerFeatureReport.MatchesReportId(response, responseReportId) ||
                !RazerFeatureReport.Matches(request, response, allowRemainingPacketsMismatch))
            {
                lastError = "The device returned an incorrect report ID, an out-of-order report, or a report that failed validation; close Synapse and retry.";
                diagnostic?.Invoke($"attempt {attempt}: response validation failed, status=0x{response[1]:X2}, txn=0x{response[2]:X2}, class=0x{response[7]:X2}, command=0x{response[8]:X2}");
            }
            else if (response[1] == 0x02)
            {
                diagnostic?.Invoke($"attempt {attempt}: success, status=0x02");
                return response;
            }
            else if (response[1] == 0x01)
            {
                // OpenRazer treats BUSY as an accepted command for these devices.
                diagnostic?.Invoke($"attempt {attempt}: accepted, status=0x01 (busy)");
                return response;
            }
            else if (response[1] == 0x05)
            {
                throw new NotSupportedException("The device does not support this command.");
            }
            else
            {
                lastError = response[1] switch
                {
                    0x01 => "The device is busy (0x01).",
                    0x03 => "The device rejected the query (0x03).",
                    0x04 => "The device timed out (0x04); wake the device, close Synapse, and retry.",
                    _ => $"Device response status: 0x{response[1]:X2}.",
                };
                diagnostic?.Invoke($"attempt {attempt}: {lastError}");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException(lastError ?? "Razer feature query failed.");
    }

    private void WriteDiagnostic(string devicePath, byte[] request, string message)
    {
        if (!devicePath.Contains("pid_027a", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        _diagnosticLog?.TryWrite(
            "openrazer-hid",
            $"path={DescribeDevicePath(devicePath)}, txn=0x{request[2]:X2}, size={request[6]}, class=0x{request[7]:X2}, command=0x{request[8]:X2}: {message}");
    }

    internal static string DescribeDevicePath(string devicePath)
    {
        var parts = devicePath.Split('#');
        return parts.Length > 1 ? parts[1] : "<redacted>";
    }

    private sealed class RazerFeatureSession(
        SafeFileHandle handle,
        SemaphoreSlim gate) : IRazerFeatureSession
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

            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await Task.Run(() =>
                {
                    foreach (var request in requests)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!NativeMethods.HidD_SetFeature(handle, request, request.Length))
                        {
                            throw new Win32Exception(
                                Marshal.GetLastWin32Error(),
                                "Razer feature batch write failed.");
                        }
                        if (rowDelay > TimeSpan.Zero)
                        {
                            Thread.Sleep(rowDelay);
                        }
                    }
                }, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task<byte[]> QueryAsync(
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
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await ExecuteOnHandleAsync(
                    handle,
                    request,
                    deviceWait,
                    responseReportId,
                    cancellationToken,
                    allowRemainingPacketsMismatch).ConfigureAwait(false);
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

    private static class NativeMethods
    {
        internal const uint GENERIC_WRITE = 0x40000000;
        internal const uint FILE_SHARE_READ = 0x00000001;
        internal const uint FILE_SHARE_WRITE = 0x00000002;
        internal const uint OPEN_EXISTING = 3;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern SafeFileHandle CreateFile(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool HidD_SetFeature(SafeFileHandle hidDeviceObject, byte[] reportBuffer, int reportBufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool HidD_GetFeature(SafeFileHandle hidDeviceObject, byte[] reportBuffer, int reportBufferLength);
    }
}
