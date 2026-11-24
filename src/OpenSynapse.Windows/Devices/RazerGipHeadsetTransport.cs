using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Microsoft.Win32.SafeHandles;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Native GIP headset channel: a fixed 60-byte payload sent as HID report id 0x0F.
/// The transport only moves frames; retry and status handling live in the protocol service.
/// </summary>
internal sealed class RazerGipHeadsetTransport : IAsyncDisposable
{
    private readonly SafeFileHandle _handle;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Channel<byte[]> _reports = Channel.CreateUnbounded<byte[]>();
    private readonly Task _readLoop;
    private readonly int _prefixLength;
    private readonly int _inputLength;
    private readonly string _devicePath;
    private readonly RazerHidHidReportTransport _output = new();
    private uint _readThreadId;
    private bool _disposed;

    internal RazerGipHeadsetTransport(string devicePath, int outputReportByteLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);
        _devicePath = devicePath;
        // Windows counts the report id in OutputReportByteLength. A 60-byte
        // descriptor means the collection has a single implicit report instead.
        _prefixLength = outputReportByteLength >= RazerGipHeadsetProtocol.FrameLength + 1 ? 1 : 0;
        _inputLength = RazerGipHeadsetProtocol.FrameLength + _prefixLength;

        var handle = NativeMethods.CreateFile(
            devicePath,
            NativeMethods.GenericRead | NativeMethods.GenericWrite,
            NativeMethods.FileShareRead | NativeMethods.FileShareWrite,
            IntPtr.Zero,
            NativeMethods.OpenExisting,
            0,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(), "Could not open the GIP headset control channel.");
        }

        _handle = handle;
        _readLoop = Task.Factory.StartNew(
            ReadLoop,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    internal async Task<byte[]> QueryAsync(
        byte[] frame,
        Func<byte[], bool> matches,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (_reports.Reader.TryRead(out _))
            {
            }

            await _output.SendOutputAsync(_devicePath, WithReportId(frame), cancellationToken)
                .ConfigureAwait(false);

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);
            try
            {
                while (await _reports.Reader.WaitToReadAsync(deadline.Token).ConfigureAwait(false))
                {
                    while (_reports.Reader.TryRead(out var report))
                    {
                        if (matches(report))
                        {
                            return report;
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("GIP command did not answer before the deadline.");
            }

            throw new IOException("GIP headset input stream closed.");
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task SendAsync(byte[] frame, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (_reports.Reader.TryRead(out _))
            {
            }

            await _output.SendOutputAsync(_devicePath, WithReportId(frame), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private byte[] WithReportId(byte[] frame)
    {
        if (_prefixLength == 0)
        {
            return frame;
        }

        var report = new byte[frame.Length + 1];
        report[0] = RazerGipHeadsetProtocol.ReportId;
        frame.CopyTo(report, 1);
        return report;
    }

    private void ReadLoop()
    {
        _readThreadId = CancellableNativeCall.CurrentThreadId;
        var buffer = new byte[_inputLength];
        try
        {
            while (!_disposed)
            {
                if (!NativeMethods.ReadFile(
                        _handle,
                        buffer,
                        (uint)buffer.Length,
                        out var read,
                        IntPtr.Zero))
                {
                    if (_disposed)
                    {
                        break;
                    }

                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(), "GIP headset input report read failed.");
                }
                if (read < _inputLength ||
                    (_prefixLength > 0 && buffer[0] != RazerGipHeadsetProtocol.ReportId))
                {
                    continue;
                }

                _reports.Writer.TryWrite(
                    buffer.AsSpan(_prefixLength, RazerGipHeadsetProtocol.FrameLength).ToArray());
            }
        }
        catch (Exception) when (_disposed)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Win32Exception)
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
        CancellableNativeCall.CancelSynchronousIo(Interlocked.Exchange(ref _readThreadId, 0));

        var teardown = Task.Run(async () =>
        {
            _handle.Dispose();
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

        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true,
            CharSet = CharSet.Unicode)]
        internal static extern SafeFileHandle CreateFile(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ReadFile(
            SafeFileHandle handle,
            byte[] buffer,
            uint bytesToRead,
            out uint bytesRead,
            IntPtr overlapped);
    }
}
