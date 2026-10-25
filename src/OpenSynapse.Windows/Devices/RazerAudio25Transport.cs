using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

internal interface IRazerAudio25Transport : IAsyncDisposable
{
    byte ReportId { get; }
    Task<RazerAudio25Response> SendAsync(
        RazerAudio25Command command,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 65-byte feature-report transport for protocol 25 audio devices. The JS middleware sends a
/// 64-byte payload prefixed by report id 7 (USB) or 18 (BLE), with an XOR checksum at byte 62.
/// </summary>
internal sealed class RazerAudio25Transport : IRazerAudio25Transport
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(10);
    private readonly SafeFileHandle _handle;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _deviceWait;
    private readonly Action<string>? _diagnostic;
    private int _sequence;
    private bool _disposed;

    internal RazerAudio25Transport(
        string devicePath,
        byte reportId,
        TimeSpan deviceWait,
        Action<string>? diagnostic = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);
        if (reportId is not (RazerAudio25Protocol.UsbReportId or RazerAudio25Protocol.BleReportId))
        {
            throw new ArgumentOutOfRangeException(nameof(reportId));
        }

        ReportId = reportId;
        _deviceWait = deviceWait;
        _diagnostic = diagnostic;
        _handle = NativeMethods.CreateFile(
            devicePath,
            NativeMethods.GenericRead | NativeMethods.GenericWrite,
            NativeMethods.FileShareRead | NativeMethods.FileShareWrite,
            IntPtr.Zero,
            NativeMethods.OpenExisting,
            0,
            IntPtr.Zero);
        if (_handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            _handle.Dispose();
            throw new Win32Exception(error, "Could not open the protocol 25 audio feature collection.");
        }
    }

    public byte ReportId { get; }

    public async Task<RazerAudio25Response> SendAsync(
        RazerAudio25Command command,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Exception? lastException = null;
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var transaction = unchecked((byte)(Interlocked.Increment(ref _sequence) & 0x1F));
                var request = RazerAudio25Protocol.CreateRequest(ReportId, transaction, command);
                var setResult = await Task.Run(() =>
                {
                    var success = NativeMethods.HidD_SetFeature(_handle, request, request.Length);
                    return (Success: success, Error: Marshal.GetLastWin32Error());
                }, cancellationToken).ConfigureAwait(false);
                if (!setResult.Success)
                {
                    lastException = new Win32Exception(setResult.Error, "Protocol 25 HidD_SetFeature failed.");
                    _diagnostic?.Invoke($"attempt {attempt}: setFeature failed, win32={setResult.Error}");
                    await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                await Task.Delay(_deviceWait, cancellationToken).ConfigureAwait(false);
                var responseBuffer = new byte[RazerAudio25Protocol.ReportLength];
                responseBuffer[0] = ReportId;
                var getResult = await Task.Run(() =>
                {
                    var success = NativeMethods.HidD_GetFeature(_handle, responseBuffer, responseBuffer.Length);
                    return (Success: success, Error: Marshal.GetLastWin32Error());
                }, cancellationToken).ConfigureAwait(false);
                if (!getResult.Success)
                {
                    lastException = new Win32Exception(getResult.Error, "Protocol 25 HidD_GetFeature failed.");
                    _diagnostic?.Invoke($"attempt {attempt}: getFeature failed, win32={getResult.Error}");
                    await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var response = RazerAudio25Protocol.ParseResponse(responseBuffer);
                if (response.ReportId != ReportId ||
                    response.TransactionId != transaction ||
                    response.CommandClass != command.CommandClass ||
                    response.CommandId != command.CommandId)
                {
                    lastException = new InvalidDataException(
                        $"Protocol 25 response mismatch: report=0x{response.ReportId:X2}, " +
                        $"txn=0x{response.TransactionId:X2} (want 0x{transaction:X2}), " +
                        $"class=0x{response.CommandClass:X2} (want 0x{command.CommandClass:X2}), " +
                        $"command=0x{response.CommandId:X2} (want 0x{command.CommandId:X2}).");
                    _diagnostic?.Invoke($"attempt {attempt}: {lastException.Message}");
                    await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                switch (response.Status)
                {
                    case RazerAudio25Status.Success:
                        return response;
                    case RazerAudio25Status.Busy:
                    case RazerAudio25Status.Invalid:
                        lastException = new InvalidOperationException(
                            $"Protocol 25 command 0x{command.CommandClass:X2}:0x{command.CommandId:X2} returned status 0x{(byte)response.Status:X2}.");
                        _diagnostic?.Invoke($"attempt {attempt}: busy/invalid, retrying");
                        await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
                        continue;
                    case RazerAudio25Status.Unsupported:
                        throw new NotSupportedException(
                            $"The device does not support protocol 25 command 0x{command.CommandClass:X2}:0x{command.CommandId:X2}.");
                    default:
                        throw new InvalidOperationException(
                            $"Protocol 25 command 0x{command.CommandClass:X2}:0x{command.CommandId:X2} failed with status 0x{(byte)response.Status:X2}.");
                }
            }

            throw lastException ?? new InvalidOperationException("Protocol 25 command did not complete.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        _handle.Dispose();
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }

    private static class NativeMethods
    {
        internal const uint GenericRead = 0x80000000;
        internal const uint GenericWrite = 0x40000000;
        internal const uint FileShareRead = 0x00000001;
        internal const uint FileShareWrite = 0x00000002;
        internal const uint OpenExisting = 3;

        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
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
        internal static extern bool HidD_SetFeature(
            SafeFileHandle hidDeviceObject,
            byte[] reportBuffer,
            int reportBufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool HidD_GetFeature(
            SafeFileHandle hidDeviceObject,
            byte[] reportBuffer,
            int reportBufferLength);
    }
}
