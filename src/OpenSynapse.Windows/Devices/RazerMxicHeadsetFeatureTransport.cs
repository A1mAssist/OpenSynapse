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
    private readonly SemaphoreSlim _gate;
    private readonly string? _path;
    private byte _transaction;
    private bool _disposed;
    private Task? _pending;

    internal RazerMxicHeadsetFeatureTransport(string devicePath)
        : this(new NativeFeatureIo(devicePath), devicePath)
    {
    }

    internal RazerMxicHeadsetFeatureTransport(IRazerMxicHeadsetFeatureIo io, string? path = null)
    {
        _io = io;
        _path = path;
        _gate = path is null ? new(1, 1) : RazerFeatureTransport.EndpointGate(path);
    }

    private byte[] Prepare(byte[] report)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_pending is not null) throw new IOException("rzDevice40 session cannot be reused after an unfinished HID call.");
        if (_path is not null) RazerFeatureTransport.ThrowIfNativeCallPending(_path);
        if (report.Length != RazerProtocol40HeadsetProtocol.ReportLength || report[0] != RazerProtocol40HeadsetProtocol.ReportId)
            throw new ArgumentException("A 64-byte rzDevice40 report is required.", nameof(report));
        var request = report.ToArray();
        _transaction = (byte)((_transaction % 31) + 1);
        request[2] = (byte)((report[2] & 0xE0) | _transaction);
        return request;
    }

    public async Task SendAsync(byte[] report, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            report = Prepare(report);
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
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            report = Prepare(report);
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
                    if ((report[8] & 0x80) == 0) return value;
                    if (!value.AsSpan().StartsWith(expectedHeader))
                        throw new InvalidDataException("rzDevice40 response selectors did not match.");
                    return value[expectedHeader.Length..];
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

    private async Task<T> RunNativeAsync<T>(
        Func<T> call,
        CancellationToken cancellationToken)
    {
        try
        {
            return await CancellableNativeCall
                .RunAsync(call, ResponseTimeout, cancellationToken, pending =>
                {
                    _pending = pending;
                    if (_path is not null) RazerFeatureTransport.TrackPendingCall(_path, pending);
                })
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
            if (_pending is { IsCompleted: false } pending)
                _ = pending.ContinueWith(_ => _io.Dispose(), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            else _io.Dispose();
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
