using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Microsoft.Win32.SafeHandles;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

internal interface IRazerMxicHeadsetTransport : IAsyncDisposable
{
    Task SendAsync(byte[] report, CancellationToken cancellationToken);

    Task<byte[]?> QueryAsync(
        byte[] report,
        byte[] expectedHeader,
        CancellationToken cancellationToken);
}

internal sealed class RazerMxicHeadsetTransport : IRazerMxicHeadsetTransport
{
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(1);
    private readonly FileStream _stream;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Channel<byte[]> _reports = Channel.CreateUnbounded<byte[]>();
    private readonly CancellationTokenSource _readCancellation = new();
    private readonly Task _readLoop;
    private readonly int _inputReportLength;
    private bool _disposed;

    internal RazerMxicHeadsetTransport(string devicePath, int inputReportLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);
        _inputReportLength = Math.Max(inputReportLength, RazerMxicHeadsetProtocol.ReportLength);
        var handle = NativeMethods.CreateFile(
            devicePath,
            NativeMethods.GenericRead | NativeMethods.GenericWrite,
            NativeMethods.FileShareRead | NativeMethods.FileShareWrite,
            IntPtr.Zero,
            NativeMethods.OpenExisting,
            NativeMethods.FileFlagOverlapped,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Could not open the MXIC headset HID control channel.");
        }

        _stream = new FileStream(handle, FileAccess.ReadWrite, _inputReportLength, isAsync: true);
        _readLoop = Task.Run(ReadLoopAsync);
    }

    public async Task SendAsync(byte[] report, CancellationToken cancellationToken)
    {
        await ExecuteAsync(report, null, cancellationToken).ConfigureAwait(false);
    }

    public Task<byte[]?> QueryAsync(
        byte[] report,
        byte[] expectedHeader,
        CancellationToken cancellationToken) =>
        ExecuteAsync(report, expectedHeader, cancellationToken);

    private async Task<byte[]?> ExecuteAsync(
        byte[] report,
        byte[]? expectedHeader,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (_reports.Reader.TryRead(out _))
            {
            }

            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!NativeMethods.HidD_SetOutputReport(_stream.SafeFileHandle, report, report.Length))
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "MXIC HID output report failed.");
                }
            }, cancellationToken).ConfigureAwait(false);

            if (expectedHeader is null)
            {
                return null;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ResponseTimeout);
            try
            {
                while (await _reports.Reader.WaitToReadAsync(timeout.Token).ConfigureAwait(false))
                {
                    while (_reports.Reader.TryRead(out var data))
                    {
                        // The input report id matches the request's, so 0555 stays on 2 and
                        // the Valkyrie T3 family reads its id-7 replies. An empty expected
                        // header selects the protocol40 reply layout.
                        if (expectedHeader.Length == 0)
                        {
                            if (RazerProtocol40HeadsetProtocol.TryReadValue(data, report, out var protocol40))
                            {
                                return protocol40;
                            }
                        }
                        else if (RazerMxicHeadsetProtocol.TryReadValue(
                            data, report[0], expectedHeader, out var value))
                        {
                            return value;
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ReadLoopAsync()
    {
        var buffer = new byte[_inputReportLength];
        try
        {
            while (!_disposed)
            {
                var read = await _stream.ReadAsync(buffer, _readCancellation.Token).ConfigureAwait(false);
                if (read <= 0)
                {
                    break;
                }

                _reports.Writer.TryWrite(buffer[..read]);
            }
        }
        catch (Exception) when (_disposed)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            _reports.Writer.TryComplete();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _reports.Writer.TryComplete();
        await _readCancellation.CancelAsync().ConfigureAwait(false);

        var teardown = Task.Run(async () =>
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
            await _readLoop.ConfigureAwait(false);
        });
        try
        {
            await teardown.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        {
        }
    }

    private static class NativeMethods
    {
        internal const uint GenericRead = 0x80000000;
        internal const uint GenericWrite = 0x40000000;
        internal const uint FileShareRead = 0x00000001;
        internal const uint FileShareWrite = 0x00000002;
        internal const uint OpenExisting = 3;
        internal const uint FileFlagOverlapped = 0x40000000;

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
        internal static extern bool HidD_SetOutputReport(
            SafeFileHandle handle,
            byte[] reportBuffer,
            int reportBufferLength);
    }
}
