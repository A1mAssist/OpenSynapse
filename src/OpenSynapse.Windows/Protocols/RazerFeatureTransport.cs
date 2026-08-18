using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

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
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates =
        new(StringComparer.OrdinalIgnoreCase);

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

    public Task SendBatchAsync(
        string devicePath,
        IReadOnlyList<byte[]> requests,
        TimeSpan rowDelay,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count == 0)
            throw new ArgumentException("批量 HID 报告不能为空。", nameof(requests));
        if (requests.Any(request => request.Length != RazerFeatureReport.Length))
            throw new ArgumentException("批量 HID 报告必须都是 91 字节。", nameof(requests));

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
                        throw new Win32Exception(openError, "无法打开 Razer feature collection。");

                    foreach (var request in requests)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!NativeMethods.HidD_SetFeature(handle, request, request.Length))
                            throw new Win32Exception(Marshal.GetLastWin32Error(), "Razer 矩阵批量写入失败。");
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
            using var handle = await OpenHandleAsync(devicePath, cancellationToken).ConfigureAwait(false);
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
        throw new Win32Exception(openResult.Error, "无法打开 Razer feature collection。");
    }

    private static async Task<byte[]> ExecuteOnHandleAsync(
        SafeFileHandle handle,
        byte[] request,
        TimeSpan deviceWait,
        byte responseReportId,
        CancellationToken cancellationToken,
        bool allowRemainingPacketsMismatch)
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
                await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (!RazerFeatureReport.MatchesReportId(response, responseReportId) ||
                !RazerFeatureReport.Matches(request, response, allowRemainingPacketsMismatch))
            {
                lastError = "设备返回了错误 report ID、错序或校验失败的报告；请关闭 Synapse 后重试。";
            }
            else if (response[1] == 0x02)
            {
                return response;
            }
            else if (response[1] == 0x05)
            {
                throw new InvalidOperationException("设备不支持该查询命令。");
            }
            else
            {
                lastError = response[1] switch
                {
                    0x01 => "设备正忙（0x01）。",
                    0x03 => "设备拒绝了查询（0x03）。",
                    0x04 => "设备超时（0x04）；请唤醒设备并关闭 Synapse 后重试。",
                    _ => $"设备响应状态 0x{response[1]:X2}。",
                };
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException(lastError ?? "Razer feature 查询失败。");
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
                throw new ArgumentException("Razer feature report 必须是 91 字节。", nameof(request));
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
                    throw new Win32Exception(result.Error, "Razer feature 会话握手失败。");
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
                throw new ArgumentException("Razer feature 批量报告必须包含至少一个 91 字节报告。", nameof(requests));
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
                                "Razer feature 批量写入失败。");
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
