using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Microsoft.Win32.SafeHandles;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

internal sealed class BlackSharkHeadsetTransport : IAsyncDisposable
{
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(1);
    private readonly FileStream _stream;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Channel<byte[]> _reports = Channel.CreateUnbounded<byte[]>();
    private readonly Task _readLoop;
    private readonly int _inputReportLength;
    private bool _disposed;

    internal BlackSharkHeadsetTransport(string devicePath, int inputReportLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);
        _inputReportLength = Math.Max(inputReportLength, BlackSharkV3ProProtocol.ReportLength);
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
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not open the BlackShark HID control channel.");
        }

        _stream = new FileStream(handle, FileAccess.ReadWrite, _inputReportLength, isAsync: true);
        _readLoop = Task.Run(ReadLoopAsync);
    }

    internal async Task<BlackSharkV3ProResponse?> SendAsync(
        byte[] report,
        bool expectResponse,
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
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "BlackShark HID output report failed.");
                }
            }, cancellationToken).ConfigureAwait(false);

            if (!expectResponse)
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
                        if (BlackSharkV3ProProtocol.HasValidCrc(data) &&
                            BlackSharkV3ProProtocol.TryParseResponse(data, out var response) &&
                            response.CommandClass == report[10] &&
                            response.Subtype == 0x01)
                        {
                            return response;
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
                var read = await _stream.ReadAsync(buffer, CancellationToken.None).ConfigureAwait(false);
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
        await _stream.DisposeAsync().ConfigureAwait(false);
        try
        {
            await _readLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _gate.Dispose();
        _reports.Writer.TryComplete();
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
