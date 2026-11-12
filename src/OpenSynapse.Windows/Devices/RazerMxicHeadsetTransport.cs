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

    Task<byte?> SendAndReadStatusAsync(byte[] report, CancellationToken cancellationToken);
}

/// <summary>
/// HID handles are not seekable. Never use RandomAccess with an offset on these handles;
/// use a write handle with WriteFile or an OVERLAPPED same-handle write.
/// </summary>
internal sealed class RazerMxicHeadsetTransport : IRazerMxicHeadsetTransport
{
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ControlReportTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ControlReportDelay = TimeSpan.FromMilliseconds(5);
    private readonly SafeFileHandle _handle;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Channel<byte[]> _reports = Channel.CreateUnbounded<byte[]>();
    private readonly Task _readLoop;
    private readonly int _inputReportLength;
    private readonly string _devicePath;
    private readonly RazerHidHidReportTransport _output = new();
    private readonly bool _useSetOutputReport;
    private readonly bool _useControlStatusReport;
    private bool _disposed;

    internal RazerMxicHeadsetTransport(
        string devicePath,
        int inputReportLength,
        bool useSetOutputReport = false,
        bool useControlStatusReport = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);
        _devicePath = devicePath;
        _inputReportLength = Math.Max(inputReportLength, RazerMxicHeadsetProtocol.ReportLength);
        _useSetOutputReport = useSetOutputReport;
        _useControlStatusReport = useControlStatusReport;
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
                "Could not open the MXIC headset HID control channel.");
        }

        _handle = handle;
        _readLoop = Task.Factory.StartNew(
            ReadLoop,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    public async Task SendAsync(byte[] report, CancellationToken cancellationToken)
    {
        await ExecuteAsync(report, null, null, cancellationToken).ConfigureAwait(false);
    }

    public Task<byte[]?> QueryAsync(
        byte[] report,
        byte[] expectedHeader,
        CancellationToken cancellationToken) =>
        ExecuteAsync(report, expectedHeader, null, cancellationToken);

    public async Task<byte?> SendAndReadStatusAsync(
        byte[] report,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (_reports.Reader.TryRead(out _))
            {
            }

            await SendReportAsync(report, cancellationToken).ConfigureAwait(false);
            await Task.Delay(ControlReportDelay, cancellationToken).ConfigureAwait(false);
            return await ReadControlStatusAsync(report[0], cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Sends a report and returns the first reply that satisfies <paramref name="matches"/>.
    /// Billie's lighting replies use the flat 02/C0 01 01 shape, not the MXIC PI frame.
    /// </summary>
    public Task<byte[]?> QueryRawAsync(
        byte[] report,
        Func<byte[], bool> matches,
        CancellationToken cancellationToken) =>
        ExecuteAsync(report, null, matches, cancellationToken);

    private async Task<byte[]?> ExecuteAsync(
        byte[] report,
        byte[]? expectedHeader,
        Func<byte[], bool>? rawMatches,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // AudioMxicDevice sends the command again when getInputReport returns a
            // non-OK status. The first status after a wireless wake-up can be transient,
            // so a single failed status must not discard the whole read.
            var attempts = expectedHeader is not null && _useControlStatusReport ? 3 : 1;
            for (var attempt = 0; attempt < attempts; attempt++)
            {
                while (_reports.Reader.TryRead(out _))
                {
                }

                await SendReportAsync(report, cancellationToken).ConfigureAwait(false);

                if (expectedHeader is null && rawMatches is null)
                {
                    return null;
                }

                if (expectedHeader is not null && _useControlStatusReport)
                {
                    await Task.Delay(ControlReportDelay, cancellationToken).ConfigureAwait(false);
                    var status = await ReadControlStatusAsync(report[0], cancellationToken).ConfigureAwait(false);
                    if (status is not null && status != RazerMxicHeadsetProtocol.StatusOk)
                    {
                        await Task.Delay(
                                TimeSpan.FromMilliseconds(10 * (attempt + 1)),
                                cancellationToken)
                            .ConfigureAwait(false);
                        continue;
                    }
                }

                // AudioMxicDevice waits 5 ms between the status report and hid.read. Send a
                // command and then take its payload from the same input stream Synapse uses.
                await Task.Delay(ControlReportDelay, cancellationToken).ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(ResponseTimeout);
                try
                {
                    while (await _reports.Reader.WaitToReadAsync(timeout.Token).ConfigureAwait(false))
                    {
                        while (_reports.Reader.TryRead(out var data))
                        {
                            if (rawMatches is not null)
                            {
                                if (rawMatches(data))
                                {
                                    return data;
                                }

                                continue;
                            }

                            // The input report id matches the request's, so 0555 stays on 2 and
                            // the Valkyrie T3 family reads its id-7 replies. An empty expected
                            // header selects the protocol40 reply layout.
                            if (expectedHeader is null)
                            {
                                continue;
                            }

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
            }

            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SendReportAsync(byte[] report, CancellationToken cancellationToken)
    {
        if (_useSetOutputReport)
        {
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!NativeMethods.HidD_SetOutputReport(_handle, report, report.Length))
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "MXIC HID output report failed.");
                }
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Synapse's AudioMxicDevice uses hid.write for this family. HID handles are not
        // seekable, so RandomAccess cannot be used; a separate write handle reaches the
        // vendor OUT endpoint without disturbing the read loop.
        await _output.SendOutputAsync(_devicePath, report, cancellationToken).ConfigureAwait(false);
    }

    private async Task<byte?> ReadControlStatusAsync(
        byte reportId,
        CancellationToken cancellationToken)
    {
        if (!_useControlStatusReport)
        {
            return null;
        }

        var candidate = new byte[_inputReportLength];
        candidate[0] = reportId;

        // HidD_GetInputReport is a blocking control transfer. Do not run it on the
        // interrupt-read handle: a device that never answers would hold the transport
        // gate forever and leave the card on "正在读取…". A separate handle and a
        // bounded wait keep a status read optional, just like AudioMxicDevice treats
        // an empty getInputReport result.
        SafeFileHandle? statusHandle = null;
        try
        {
            statusHandle = NativeMethods.CreateFile(
                _devicePath,
                NativeMethods.GenericRead,
                NativeMethods.FileShareRead | NativeMethods.FileShareWrite,
                IntPtr.Zero,
                NativeMethods.OpenExisting,
                0,
                IntPtr.Zero);
            if (statusHandle.IsInvalid)
            {
                return null;
            }

            var read = Task.Run(
                () => NativeMethods.HidD_GetInputReport(
                    statusHandle,
                    candidate,
                    candidate.Length));
            bool succeeded;
            try
            {
                succeeded = await read.WaitAsync(ControlReportTimeout, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return null;
            }

            // HidD_GetInputReport returns the report id followed by the payload. Synapse
            // strips the id and uses the next byte as the STATUS_OK/STATUS_FAIL flag.
            return succeeded && candidate.Length > 1 ? candidate[1] : null;
        }
        finally
        {
            statusHandle?.Dispose();
        }
    }

    private void ReadLoop()
    {
        var buffer = new byte[_inputReportLength];
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
                        "MXIC headset input report read failed.");
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

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool HidD_SetOutputReport(
            SafeFileHandle handle,
            byte[] reportBuffer,
            int reportBufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool HidD_GetInputReport(
            SafeFileHandle handle,
            byte[] reportBuffer,
            int reportBufferLength);

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
