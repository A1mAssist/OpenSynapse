using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Microsoft.Win32.SafeHandles;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

internal interface IRazerHeadsetHidTransport : IAsyncDisposable
{
    Task<byte[]?> QueryAsync(
        byte[] report,
        Func<byte[], bool> isMatch,
        CancellationToken cancellationToken);
}

/// <summary>
/// 64-byte HID output/input channel for the AudioValkyrie / AudioBillie family.
/// The caller owns response matching so each protocol can decide which report and
/// header identify its reply. HID handles are not seekable: never use RandomAccess with
/// an offset on them; use a write handle with WriteFile or an OVERLAPPED same-handle write.
/// </summary>
internal sealed class RazerHeadsetHidTransport : IRazerHeadsetHidTransport
{
    private const int ReportLength = 64;
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(1);
    private readonly SafeFileHandle _handle;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Channel<byte[]> _reports = Channel.CreateUnbounded<byte[]>();
    private readonly Task _readLoop;
    private readonly string _devicePath;
    private readonly RazerHidHidReportTransport _output = new();
    private bool _disposed;

    internal RazerHeadsetHidTransport(string devicePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);
        _devicePath = devicePath;
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
                Marshal.GetLastWin32Error(),
                "Could not open the headset HID control channel.");
        }

        _handle = handle;
        _readLoop = Task.Factory.StartNew(
            ReadLoop,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    public async Task<byte[]?> QueryAsync(
        byte[] report,
        Func<byte[], bool> isMatch,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(isMatch);
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (_reports.Reader.TryRead(out _))
            {
            }

            // AudioValkyrie/Billie bundles use hid.write, not HidD_SetOutputReport.
            // HID handles are not seekable, so a separate write handle is used while the
            // read loop keeps the original overlapped handle.
            await _output.SendOutputAsync(_devicePath, report, cancellationToken).ConfigureAwait(false);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ResponseTimeout);
            try
            {
                while (await _reports.Reader.WaitToReadAsync(timeout.Token).ConfigureAwait(false))
                {
                    while (_reports.Reader.TryRead(out var data))
                    {
                        if (data.Length >= ReportLength && isMatch(data))
                        {
                            return data;
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

    private void ReadLoop()
    {
        var buffer = new byte[ReportLength];
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
                        Marshal.GetLastWin32Error(),
                        "Headset HID input report read failed.");
                }
                if (read == 0)
                {
                    break;
                }

                // The next ReadFile overwrites this buffer, so hand the consumer a copy.
                _reports.Writer.TryWrite(buffer[..(int)read].ToArray());
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

        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
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
