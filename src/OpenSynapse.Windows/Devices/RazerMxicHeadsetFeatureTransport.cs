using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

internal interface IRazerMxicHeadsetFeatureIo : IDisposable
{
    void Send(byte[] report);

    byte[] Read();
}

/// <summary>
/// rzDevice40 writes with hid.sendFeatureReportInBatch and reads the reply with
/// hid.getFeatureReport. The Hammerhead V3 Chroma unit in the field exposes only the
/// 64-byte FF14 feature collection (no 64-byte output endpoint), so the MXIC output
/// transport never reaches it.
/// </summary>
internal sealed class RazerMxicHeadsetFeatureTransport : IRazerMxicHeadsetTransport
{
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(40);
    private readonly IRazerMxicHeadsetFeatureIo _io;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    internal RazerMxicHeadsetFeatureTransport(string devicePath)
        : this(new NativeFeatureIo(devicePath))
    {
    }

    internal RazerMxicHeadsetFeatureTransport(IRazerMxicHeadsetFeatureIo io) => _io = io;

    public async Task SendAsync(byte[] report, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await RunNativeAsync(() => { _io.Send(report); return true; }, cancellationToken)
                .ConfigureAwait(false);
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
        if (expectedHeader.Length != 0)
        {
            throw new NotSupportedException(
                "The rzDevice40 feature channel only carries the protocol40 reply layout.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await RunNativeAsync(() => { _io.Send(report); return true; }, cancellationToken)
                .ConfigureAwait(false);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ResponseTimeout);
            // The first feature read after a write can still return the previous buffer,
            // so retry until the transaction/class/id triplet matches.
            for (var attempt = 0; attempt < 6; attempt++)
            {
                await Task.Delay(RetryDelay, timeout.Token).ConfigureAwait(false);
                var reply = await RunNativeAsync(_io.Read, timeout.Token).ConfigureAwait(false);
                if (RazerProtocol40HeadsetProtocol.TryReadValue(reply, report, out var value))
                {
                    return value;
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

    public Task<byte?> SendAndReadStatusAsync(byte[] report, CancellationToken cancellationToken) =>
        Task.FromException<byte?>(new NotSupportedException(
            "rzDevice40 uses the protocol40 reply layout, not the MXIC status report."));

    private static async Task<T> RunNativeAsync<T>(
        Func<T> call,
        CancellationToken cancellationToken)
    {
        try
        {
            return await CancellableNativeCall
                .RunAsync(call, ResponseTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            throw new IOException("rzDevice40 HID feature call timed out.", exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _io.Dispose();
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed class NativeFeatureIo : IRazerMxicHeadsetFeatureIo
    {
        private readonly SafeFileHandle _handle;

        internal NativeFeatureIo(string path)
        {
            _handle = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (_handle.IsInvalid)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Could not open the rzDevice40 feature channel.");
            }
        }

        public void Send(byte[] report)
        {
            if (!HidD_SetFeature(_handle, report, report.Length))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        public byte[] Read()
        {
            var report = new byte[RazerProtocol40HeadsetProtocol.ReportLength];
            report[0] = RazerProtocol40HeadsetProtocol.ReportId;
            if (!HidD_GetFeature(_handle, report, report.Length))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
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
        private static extern bool HidD_SetFeature(
            SafeFileHandle handle,
            byte[] report,
            int length);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool HidD_GetFeature(
            SafeFileHandle handle,
            byte[] report,
            int length);
    }
}
