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
    private static readonly TimeSpan ControlReportTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ControlReportRetryDelay = TimeSpan.FromMilliseconds(10);
    private readonly FileStream _stream;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Channel<byte[]> _reports = Channel.CreateUnbounded<byte[]>();
    private readonly CancellationTokenSource _readCancellation = new();
    private readonly Task _readLoop;
    private readonly int _inputReportLength;
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
        _inputReportLength = Math.Max(inputReportLength, RazerMxicHeadsetProtocol.ReportLength);
        _useSetOutputReport = useSetOutputReport;
        _useControlStatusReport = useControlStatusReport;
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
        await ExecuteAsync(report, null, null, cancellationToken).ConfigureAwait(false);
    }

    public Task<byte[]?> QueryAsync(
        byte[] report,
        byte[] expectedHeader,
        CancellationToken cancellationToken) =>
        ExecuteAsync(report, expectedHeader, null, cancellationToken);

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
                var control = await ReadControlReportAsync(
                    report,
                    expectedHeader,
                    cancellationToken).ConfigureAwait(false);
                if (control is not null)
                {
                    return control;
                }
            }

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
                if (!NativeMethods.HidD_SetOutputReport(_stream.SafeFileHandle, report, report.Length))
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "MXIC HID output report failed.");
                }
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Synapse's AudioMxicDevice uses hid.write for this family. WriteFile delivers
        // the report to the vendor OUT endpoint; HidD_SetOutputReport is not equivalent
        // for 0555 and leaves the device waiting for a command.
        await RandomAccess.WriteAsync(
            _stream.SafeFileHandle,
            report.AsMemory(),
            fileOffset: 0,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<byte[]?> ReadControlReportAsync(
        byte[] request,
        byte[] expectedHeader,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ControlReportTimeout);
        while (!timeout.IsCancellationRequested)
        {
            var candidate = new byte[_inputReportLength];
            candidate[0] = request[0];
            try
            {
                if (await Task.Run(
                        () => NativeMethods.HidD_GetInputReport(
                            _stream.SafeFileHandle,
                            candidate,
                            candidate.Length),
                        timeout.Token).ConfigureAwait(false) &&
                    TryMatch(candidate, request, expectedHeader, out var value))
                {
                    return value;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await Task.Delay(ControlReportRetryDelay, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        return null;
    }

    private static bool TryMatch(
        byte[] data,
        byte[] request,
        byte[] expectedHeader,
        out byte[] value)
    {
        if (expectedHeader.Length == 0)
        {
            return RazerProtocol40HeadsetProtocol.TryReadValue(data, request, out value);
        }

        return RazerMxicHeadsetProtocol.TryReadValue(
            data,
            request[0],
            expectedHeader,
            out value);
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

                // The next ReadAsync overwrites this buffer, so hand the consumer a copy.
                _reports.Writer.TryWrite(buffer[..read].ToArray());
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

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool HidD_GetInputReport(
            SafeFileHandle handle,
            byte[] reportBuffer,
            int reportBufferLength);
    }
}
