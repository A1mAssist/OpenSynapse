using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using OpenSynapse.Core.Diagnostics;
using OpenSynapse.Windows.Devices;

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

    /// <summary>
    /// Uses a prebuilt report and runs the HID exchange on the caller's thread. Frame pumps
    /// use this to avoid rebuilding the report and hopping through the thread pool per row.
    /// </summary>
    Task<byte[]> QueryPreparedFastAsync(
        ReadOnlyMemory<byte> request,
        TimeSpan deviceWait,
        byte responseReportId,
        CancellationToken cancellationToken,
        bool allowRemainingPacketsMismatch = false)
    {
        if (request.Length != RazerFeatureReport.Length)
        {
            throw new ArgumentException("A Razer feature report must be 91 bytes.", nameof(request));
        }

        var report = request.Span;
        return QueryAsync(
            report[2],
            report[6],
            report[7],
            report[8],
            request.Slice(RazerFeatureReport.ArgumentsOffset, report[6]),
            deviceWait,
            responseReportId,
            cancellationToken,
            allowRemainingPacketsMismatch);
    }

    /// <summary>
    /// Writes a frame's reports without waiting for every intermediate acknowledgement.
    /// The final report is queried normally, which also drains any replies the device kept.
    /// </summary>
    Task SendBatchFastAsync(
        IReadOnlyList<byte[]> requests,
        TimeSpan deviceWait,
        CancellationToken cancellationToken)
    {
        if (requests.Count == 0)
        {
            throw new ArgumentException("The HID report batch cannot be empty.", nameof(requests));
        }

        async Task SendAsync()
        {
            foreach (var request in requests)
            {
                await QueryAsync(
                    request[2],
                    request[6],
                    request[7],
                    request[8],
                    request.AsMemory(RazerFeatureReport.ArgumentsOffset, request[6]),
                    deviceWait,
                    request[0],
                    cancellationToken,
                    allowRemainingPacketsMismatch: true).ConfigureAwait(false);
            }
        }

        return SendAsync();
    }
}

internal sealed class RazerCommandRejectedException(string message) : InvalidOperationException(message);

/// <summary>
/// The device stayed silent for one command across the whole retry budget. Callers treat
/// this as a capability the firmware does not answer rather than a transport failure,
/// because a disconnected endpoint fails every command on its path, not just one.
/// </summary>
internal sealed class RazerDeviceTimeoutException(string message, bool retryableBusy = false) : InvalidOperationException(message)
{
    public bool IsRetryableBusy { get; } = retryableBusy;
}

/// <summary>A reply did not match the request's transaction id, command, length or checksum.</summary>
internal sealed class RazerReportMismatchException(string message) : InvalidOperationException(message);

public sealed partial class RazerFeatureTransport : IRazerFeatureTransport
{
    private readonly LocalDiagnosticLog? _diagnosticLog;
    // Feature reports are a request/response pair on one HID collection. Discovery,
    // telemetry and device pages each own a transport instance, so the gate must be
    // process-wide; otherwise one transport can read another's response.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _gates =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, int[]> _leviathanTransactions =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, int[]> _transactionSequences =
        new(StringComparer.OrdinalIgnoreCase);
    private const int MaxQueryAttempts = 5;
    private const int MaxReadAttempts = 10;
    private static readonly TimeSpan ReadRetryDelay = TimeSpan.FromMilliseconds(5);
    private static readonly TimeSpan QueryRetryDelay = TimeSpan.FromMilliseconds(10);

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
        return new RazerFeatureSession(handle, gate, devicePath);
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
        // A prepared request carries its own HID report id; the reply echoes it.
        var responseReportId = request.Span[0];
        return ExecuteAsync(
            devicePath,
            request.ToArray(),
            deviceWait,
            responseReportId,
            cancellationToken,
            allowRemainingPacketsMismatch);
    }

    public async Task SendBatchAsync(
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

        // Every report is answered. Leaving the replies queued makes the next query
        // decode the wrong report, so a batch must consume each response in order.
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
                cancellationToken,
                allowRemainingPacketsMismatch: true).ConfigureAwait(false);
        }
    }

    internal static bool UsesLeviathanReportId(string devicePath) =>
        DeviceIdParser.TryParse(devicePath, out _, out var productId) &&
        RazerHidDeviceCatalog.IsLeviathanV2X(productId);

    internal static byte NextTransactionId(ref int sequence) =>
        unchecked((byte)(System.Threading.Interlocked.Increment(ref sequence) & 0x1F));

    internal static byte NextLeviathanTransactionId(ref int sequence) =>
        NextTransactionId(ref sequence);

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
        var usesLeviathanReportId = UsesLeviathanReportId(devicePath);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (usesLeviathanReportId)
            {
                var sequence = _leviathanTransactions.GetOrAdd(devicePath, static _ => [-1]);
                request[0] = LeviathanV2XProtocol.ReportId;
                request[2] = NextLeviathanTransactionId(ref sequence[0]);
                responseReportId = LeviathanV2XProtocol.ReportId;
            }
            else PrepareStandardTransaction(devicePath, request);

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
                    usesLeviathanReportId,
                    directIo: false,
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
        bool usesLeviathanReportId,
        bool directIo = false,
        Action<string>? diagnostic = null)
    {
        string? lastError = null;
        string? lastValidationError = null;
        var rejectedResponses = 0;
        var silentResponses = 0;
        var busyResponses = 0;
        for (var attempt = 1; attempt <= MaxQueryAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var setResult = await SetFeatureAsync(handle, request, directIo, cancellationToken)
                .ConfigureAwait(false);
            if (!setResult.Success)
            {
                lastError = new Win32Exception(setResult.Error).Message;
                diagnostic?.Invoke($"attempt {attempt}: HidD_SetFeature failed, win32={setResult.Error} ({lastError})");
                await WaitAsync(QueryRetryDelay, directIo, cancellationToken).ConfigureAwait(false);
                continue;
            }

            await WaitAsync(deviceWait, directIo, cancellationToken).ConfigureAwait(false);
            byte[]? response = null;
            string? readError = null;
            for (var readAttempt = 1; readAttempt <= MaxReadAttempts; readAttempt++)
            {
                if (readAttempt > 1)
                {
                    await WaitAsync(ReadRetryDelay, directIo, cancellationToken).ConfigureAwait(false);
                }

                var candidate = new byte[RazerFeatureReport.Length];
                candidate[0] = responseReportId;
                var getResult = await GetFeatureAsync(handle, candidate, directIo, cancellationToken)
                    .ConfigureAwait(false);
                if (!getResult.Success)
                {
                    readError = new Win32Exception(getResult.Error).Message;
                    break;
                }

                var matches = usesLeviathanReportId
                    ? RazerFeatureReport.MatchesLeviathanResponse(request, candidate, allowRemainingPacketsMismatch)
                    : RazerFeatureReport.Matches(request, candidate, allowRemainingPacketsMismatch);
                // The 1354 middleware never checks the reply's report id; the feature
                // collection only has one feature report and the transaction plus command
                // already identify the exchange. Keep the id gate for other devices.
                if ((!usesLeviathanReportId && !RazerFeatureReport.MatchesReportId(candidate, responseReportId)) ||
                    !matches)
                {
                    readError = $"status=0x{candidate[1]:X2}, txn=0x{candidate[2]:X2} (want 0x{request[2]:X2}), class=0x{candidate[7]:X2} (want 0x{request[7]:X2}), command=0x{candidate[8]:X2} (want 0x{request[8]:X2}), len={candidate[6]} (want {request[6]}), crc=0x{candidate[89]:X2}, computed=0x{RazerFeatureReport.CalculateCrc(candidate):X2}";
                    continue;
                }

                response = candidate;
                break;
            }

            if (response is null)
            {
                lastValidationError = string.IsNullOrWhiteSpace(readError) ? "No reply matched the request." : readError;
                lastError = "The device returned an incorrect report ID, an out-of-order report, or a report that failed validation; close Synapse and retry.";
                diagnostic?.Invoke($"attempt {attempt}: response validation failed after {MaxReadAttempts} reads ({lastValidationError})");
                await WaitAsync(QueryRetryDelay, directIo, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (response[1] == 0x02)
            {
                // The firmware answers a successful command without echoing the payload, so
                // the response arguments are the only evidence of what it actually stored.
                if (LocalDiagnosticLog.TraceSuccessfulHidRequests)
                {
                    diagnostic?.Invoke($"attempt {attempt}: success, status=0x02, {DescribeResponseArguments(response)}");
                }
                return response;
            }
            else if (response[1] == 0x01)
            {
                if (IsRetryableBusyResponse(response[1], request[8]))
                {
                    // A busy read has no trustworthy payload; firmware may echo the requested
                    // length while returning an empty reply, so the length alone is not data.
                    lastError = "The device is busy (0x01).";
                    silentResponses++;
                    busyResponses++;
                    diagnostic?.Invoke($"attempt {attempt}: {lastError}; retrying read");
                }
                else
                {
                    // RazerHid treats BUSY as an accepted command for writes.
                    if (LocalDiagnosticLog.TraceSuccessfulHidRequests)
                    {
                        diagnostic?.Invoke($"attempt {attempt}: accepted, status=0x01 (busy)");
                    }
                    return response;
                }
            }
            else if (response[1] == 0x05)
            {
                throw new NotSupportedException("The device does not support this command.");
            }
            else if (response[1] == 0x03)
            {
                rejectedResponses++;
                lastError = "The device rejected the query (0x03).";
                diagnostic?.Invoke($"attempt {attempt}: {lastError}");
            }
            else
            {
                if (response[1] == 0x04)
                {
                    silentResponses++;
                }

                lastError = response[1] switch
                {
                    0x01 => "The device is busy (0x01).",
                    0x04 => $"The device did not answer this command (0x04).",
                    _ => $"Device response status: 0x{response[1]:X2}.",
                };
                diagnostic?.Invoke($"attempt {attempt}: {lastError}");
            }

            await WaitAsync(QueryRetryDelay, directIo, cancellationToken).ConfigureAwait(false);
        }

        if (rejectedResponses == MaxQueryAttempts)
            throw new RazerCommandRejectedException(lastError!);

        // A dead endpoint fails every command on its path, so busy/silent means the
        // firmware does not answer this command, not that the transport is gone.
        if (silentResponses == MaxQueryAttempts)
            throw new RazerDeviceTimeoutException(lastError ?? "The device did not answer this command.",
                busyResponses > 0);

        if (lastValidationError is not null && rejectedResponses == 0 && silentResponses == 0)
            throw new RazerReportMismatchException($"The device returned a report that did not match the request: {lastValidationError}");

        throw new InvalidOperationException(lastError ?? "Razer feature query failed.");
    }

    private static async Task<(bool Success, int Error)> SetFeatureAsync(
        SafeFileHandle handle,
        byte[] request,
        bool directIo,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (directIo)
        {
            var success = NativeMethods.HidD_SetFeature(handle, request, request.Length);
            return (success, Marshal.GetLastWin32Error());
        }

        return await Task.Run(() =>
        {
            var success = NativeMethods.HidD_SetFeature(handle, request, request.Length);
            return (Success: success, Error: Marshal.GetLastWin32Error());
        }, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<(bool Success, int Error)> GetFeatureAsync(
        SafeFileHandle handle,
        byte[] request,
        bool directIo,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (directIo)
        {
            var success = NativeMethods.HidD_GetFeature(handle, request, request.Length);
            return (success, Marshal.GetLastWin32Error());
        }

        return await Task.Run(() =>
        {
            var success = NativeMethods.HidD_GetFeature(handle, request, request.Length);
            return (Success: success, Error: Marshal.GetLastWin32Error());
        }, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WaitAsync(
        TimeSpan delay,
        bool directIo,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (delay <= TimeSpan.Zero)
        {
            return;
        }

        if (directIo)
        {
            Thread.Sleep(delay);
            cancellationToken.ThrowIfCancellationRequested();
            return;
        }

        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
    }

    private void WriteDiagnostic(string devicePath, byte[] request, string message)
    {
        _diagnosticLog?.TryWrite(
            "openrazer-hid",
            $"path={DescribeDevicePath(devicePath)}, txn=0x{request[2]:X2}, size={request[6]}, class=0x{request[7]:X2}, command=0x{request[8]:X2}: {message}");
    }

    internal static bool IsRetryableBusyResponse(byte status, byte commandId) =>
        status == 0x01 && (commandId & 0x80) != 0;

    internal static string DescribeDevicePath(string devicePath)
    {
        var parts = devicePath.Split('#');
        return parts.Length > 1 ? parts[1] : "<redacted>";
    }

    /// <summary>
    /// Formats the response payload for the diagnostic log. Some devices declare a
    /// one-byte payload but answer in the second byte, so a fixed window is shown
    /// instead of the declared length.
    /// </summary>
    internal static string DescribeResponseArguments(ReadOnlySpan<byte> response)
    {
        var length = Math.Min(8, response.Length - RazerFeatureReport.ArgumentsOffset);
        return $"len={response[6]}, args={Convert.ToHexString(response[RazerFeatureReport.ArgumentsOffset..(RazerFeatureReport.ArgumentsOffset + length)])}";
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
