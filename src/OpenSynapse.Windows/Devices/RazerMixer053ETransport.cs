using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

internal interface IRazerMixer053ETransport : IAsyncDisposable
{
    Task SendFeatureAsync(byte[] report, CancellationToken cancellationToken);

    Task<byte[]?> QueryFeatureAsync(
        byte[] request,
        Func<byte[], bool> isMatch,
        CancellationToken cancellationToken);

    Task SendOutputAsync(byte[] report, CancellationToken cancellationToken);

    Task<byte[]?> ReadStatusAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Synchronous HID feature/output/input transport for the 053E mixer collection.
/// The response is read with HidD_GetFeature and the hardware status with
/// HidD_GetInputReport, matching the bundle's feature-report and report-9 paths.
/// </summary>
internal sealed class RazerMixer053ETransport : IRazerMixer053ETransport
{
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ReadRetryDelay = TimeSpan.FromMilliseconds(10);
    private const int MaxFeatureReadAttempts = 20;
    private const int MaxStatusReadAttempts = 3;

    private readonly SafeFileHandle _handle;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    internal RazerMixer053ETransport(string devicePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);

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
            throw new Win32Exception(error, "Could not open the 053E HID control channel.");
        }
    }

    public async Task SendFeatureAsync(byte[] report, CancellationToken cancellationToken)
    {
        ValidateFeatureReport(report);
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!NativeMethods.HidD_SetFeature(_handle, report, report.Length))
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "053E HID feature report failed.");
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<byte[]?> QueryFeatureAsync(
        byte[] request,
        Func<byte[], bool> isMatch,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(isMatch);
        ValidateFeatureReport(request);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!NativeMethods.HidD_SetFeature(_handle, request, request.Length))
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "053E HID feature request failed.");
                }
            }, cancellationToken).ConfigureAwait(false);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ResponseTimeout);
            try
            {
                for (var attempt = 0; attempt < MaxFeatureReadAttempts; attempt++)
                {
                    var candidate = new byte[RazerMixer053EProtocol.FeatureReportLength];
                    candidate[0] = RazerMixer053EProtocol.FeatureReportId;
                    var success = await Task.Run(
                        () => NativeMethods.HidD_GetFeature(_handle, candidate, candidate.Length),
                        timeout.Token).ConfigureAwait(false);
                    if (success && isMatch(candidate))
                    {
                        return candidate;
                    }

                    await Task.Delay(ReadRetryDelay, timeout.Token).ConfigureAwait(false);
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

    public async Task SendOutputAsync(byte[] report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.Length != RazerMixer053EProtocol.OutputReportLength)
        {
            throw new ArgumentException(
                $"A 053E output report must be {RazerMixer053EProtocol.OutputReportLength} bytes.",
                nameof(report));
        }

        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!NativeMethods.HidD_SetOutputReport(_handle, report, report.Length))
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "053E HID output report failed.");
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<byte[]?> ReadStatusAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            for (var attempt = 0; attempt < MaxStatusReadAttempts; attempt++)
            {
                var report = new byte[RazerMixer053EProtocol.StatusReportLength];
                report[0] = RazerMixer053EProtocol.StatusReportId;
                var success = await Task.Run(
                    () => NativeMethods.HidD_GetInputReport(_handle, report, report.Length),
                    cancellationToken).ConfigureAwait(false);
                if (success && report[0] == RazerMixer053EProtocol.StatusReportId)
                {
                    return report;
                }

                if (attempt + 1 < MaxStatusReadAttempts)
                {
                    await Task.Delay(ReadRetryDelay, cancellationToken).ConfigureAwait(false);
                }
            }

            return null;
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

    private static void ValidateFeatureReport(byte[] report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.Length != RazerMixer053EProtocol.FeatureReportLength)
        {
            throw new ArgumentException(
                $"A 053E feature report must be {RazerMixer053EProtocol.FeatureReportLength} bytes.",
                nameof(report));
        }

        if (report[0] != RazerMixer053EProtocol.FeatureReportId)
        {
            throw new ArgumentException("A 053E feature report must use report id 7.", nameof(report));
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
        internal static extern bool HidD_SetFeature(
            SafeFileHandle handle,
            byte[] reportBuffer,
            int reportBufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool HidD_GetFeature(
            SafeFileHandle handle,
            byte[] reportBuffer,
            int reportBufferLength);

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
