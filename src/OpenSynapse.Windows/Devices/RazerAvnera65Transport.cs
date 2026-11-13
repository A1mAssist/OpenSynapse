using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

internal interface IRazerAvnera65Transport : IAsyncDisposable
{
    Task SendAsync(byte[] report, CancellationToken cancellationToken);

    Task<byte[]?> QueryAsync(
        byte[] report,
        byte[] expectedHeader,
        CancellationToken cancellationToken);
}

/// <summary>
/// AudioAvnera feature-report transport. The bundle writes a 64-byte frame
/// (report id 255) and reads a 65-byte feature report. Keep this separate from
/// RazerFeatureTransport, whose prepared-request contract is fixed at 91 bytes.
/// </summary>
internal sealed class RazerAvnera65Transport : IRazerAvnera65Transport
{
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(50);
    private readonly IRazerAvnera65Io _io;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Exception? _ioFailure;
    private bool _disposed;

    internal RazerAvnera65Transport(string devicePath)
        : this(new NativeFeatureIo(devicePath))
    {
    }

    internal RazerAvnera65Transport(IRazerAvnera65Io io) => _io = io;

    public async Task SendAsync(byte[] report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.Length != RazerAvnera65Protocol.WriteLength)
        {
            throw new ArgumentException("An AudioAvnera request must be 64 bytes.", nameof(report));
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ThrowIfIoFailed();
            await RunIoAsync(() =>
            {
                _io.Send(report);
                return true;
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<byte[]?> QueryAsync(
        byte[] report,
        byte[] expectedHeader,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(expectedHeader);
        if (report.Length != RazerAvnera65Protocol.WriteLength)
        {
            throw new ArgumentException("An AudioAvnera request must be 64 bytes.", nameof(report));
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ThrowIfIoFailed();
            await RunIoAsync(() =>
            {
                _io.Send(report);
                return true;
            }, cancellationToken).ConfigureAwait(false);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ResponseTimeout);
            for (var attempt = 0; attempt < 10; attempt++)
            {
                await Task.Delay(RetryDelay, timeout.Token).ConfigureAwait(false);
                var reply = await RunIoAsync(_io.Read, timeout.Token).ConfigureAwait(false);
                if (RazerAvnera65Protocol.Matches(reply, expectedHeader))
                {
                    return reply;
                }
            }

            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _disposed = true;
            _io.Dispose();
        }
        finally
        {
            _gate.Release();
        }
    }

    private void ThrowIfIoFailed()
    {
        if (_ioFailure is not null) throw new IOException(_ioFailure.Message, _ioFailure);
    }

    private async Task<T> RunIoAsync<T>(Func<T> call, CancellationToken cancellationToken)
    {
        try
        {
            return await CancellableNativeCall.RunAsync(call, ResponseTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException exception) when (!_disposed)
        {
            // A timed-out native call may still own the channel after its cancellation grace.
            _ioFailure = new IOException(
                "The previous native I/O may still be active; reopen the device before retrying.", exception);
            // Callers already treat IOException as an expected device failure; a raw
            // TimeoutException would escape the headset view model and crash the page.
            throw _ioFailure;
        }
        catch (OperationCanceledException exception) when (!_disposed)
        {
            // A caller cancellation also leaves the native call in an unknown state.
            _ioFailure = new IOException(
                "The previous native I/O may still be active; reopen the device before retrying.", exception);
            throw;
        }
    }

    internal interface IRazerAvnera65Io : IDisposable
    {
        void Send(byte[] report);
        byte[] Read();
    }

    private sealed class NativeFeatureIo : IRazerAvnera65Io
    {
        private readonly SafeFileHandle _handle;

        internal NativeFeatureIo(string devicePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);
            _handle = CreateFile(
                devicePath,
                0xC0000000,
                3,
                IntPtr.Zero,
                3,
                0,
                IntPtr.Zero);
            if (_handle.IsInvalid)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Could not open the AudioAvnera feature channel.");
            }
        }

        public void Send(byte[] report)
        {
            if (!HidD_SetFeature(_handle, report, report.Length))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "AudioAvnera feature write failed.");
            }
        }

        public byte[] Read()
        {
            var report = new byte[RazerAvnera65Protocol.ReadLength];
            report[0] = RazerAvnera65Protocol.ReportId;
            if (!HidD_GetFeature(_handle, report, report.Length))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "AudioAvnera feature read failed.");
            }

            return report;
        }

        public void Dispose() => _handle.Dispose();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(
            string path,
            uint access,
            uint share,
            IntPtr security,
            uint creation,
            uint flags,
            IntPtr template);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool HidD_SetFeature(SafeFileHandle handle, byte[] report, int length);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool HidD_GetFeature(SafeFileHandle handle, byte[] report, int length);
    }
}
