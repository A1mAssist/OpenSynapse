using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace OpenSynapse.Windows.Protocols;

internal sealed class RazerHidHidReportTransport
{
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(2);

    internal Task SendOutputAsync(
        string devicePath,
        ReadOnlyMemory<byte> report,
        CancellationToken cancellationToken = default) =>
        SendAsync(devicePath, report, cancellationToken);

    /// <summary>
    /// Feature reports are the only writable path for the Avnera and rzDevice40
    /// Chroma families; HidD_SetOutputReport never reaches their lighting engine.
    /// </summary>
    internal async Task SendFeatureAsync(
        string devicePath,
        ReadOnlyMemory<byte> report,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);
        if (report.IsEmpty)
        {
            throw new ArgumentException("The HID report cannot be empty.", nameof(report));
        }

        var gate = RazerFeatureTransport.EndpointGate(devicePath);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RazerFeatureTransport.ThrowIfNativeCallPending(devicePath);
            await CancellableNativeCall.RunAsync(token =>
            {
                using var handle = NativeMethods.CreateFile(
                    devicePath,
                    NativeMethods.GenericWrite,
                    NativeMethods.FileShareRead | NativeMethods.FileShareWrite,
                    IntPtr.Zero,
                    NativeMethods.OpenExisting,
                    0,
                    IntPtr.Zero);
                if (handle.IsInvalid)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not open the RazerHid HID collection.");
                }

                var bytes = report.ToArray();
                token.ThrowIfCancellationRequested();
                if (!NativeMethods.HidD_SetFeature(handle, bytes, bytes.Length))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(),
                        "RazerHid feature report write failed.");
                }

                return true;
            }, SendTimeout, cancellationToken, task => RazerFeatureTransport.TrackPendingCall(devicePath, task)).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    internal async Task<byte[]> QueryAsync(
        string devicePath,
        ReadOnlyMemory<byte> report,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);
        if (report.IsEmpty)
        {
            throw new ArgumentException("The HID report cannot be empty.", nameof(report));
        }

        var gate = RazerFeatureTransport.EndpointGate(devicePath);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RazerFeatureTransport.ThrowIfNativeCallPending(devicePath);
            return await CancellableNativeCall.RunAsync(token =>
            {
                using var handle = NativeMethods.CreateFile(
                    devicePath,
                    NativeMethods.GenericRead | NativeMethods.GenericWrite,
                    NativeMethods.FileShareRead | NativeMethods.FileShareWrite,
                    IntPtr.Zero,
                    NativeMethods.OpenExisting,
                    0,
                    IntPtr.Zero);
                if (handle.IsInvalid)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not open the RazerHid HID collection.");
                }

                var bytes = report.ToArray();
                token.ThrowIfCancellationRequested();
                if (!NativeMethods.WriteFile(
                        handle,
                        bytes,
                        checked((uint)bytes.Length),
                        out var written,
                        IntPtr.Zero) ||
                    written != bytes.Length)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(),
                        "RazerHid output report write failed.");
                }

                token.ThrowIfCancellationRequested();
                var buffer = new byte[64];
                if (!NativeMethods.ReadFile(
                        handle,
                        buffer,
                        (uint)buffer.Length,
                        out var read,
                        IntPtr.Zero) ||
                    read == 0)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(),
                        "RazerHid input report read failed.");
                }

                return buffer[..(int)read];
            }, timeout, cancellationToken, task => RazerFeatureTransport.TrackPendingCall(devicePath, task)).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// AudioCamyT1/T2 answer with HidD_GetInputReport instead of a streamed input
    /// report: the request must be written first, then the same report id is read
    /// back up to five times 30 ms apart. WriteFile/ReadFile never sees these frames.
    /// </summary>
    internal async Task<byte[]> QueryInputReportAsync(
        string devicePath,
        ReadOnlyMemory<byte> report,
        byte expectedCommand,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);
        if (report.IsEmpty)
        {
            throw new ArgumentException("The HID report cannot be empty.", nameof(report));
        }

        var gate = RazerFeatureTransport.EndpointGate(devicePath);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RazerFeatureTransport.ThrowIfNativeCallPending(devicePath);
            return await CancellableNativeCall.RunAsync(token =>
            {
                using var handle = NativeMethods.CreateFile(
                    devicePath,
                    NativeMethods.GenericRead | NativeMethods.GenericWrite,
                    NativeMethods.FileShareRead | NativeMethods.FileShareWrite,
                    IntPtr.Zero,
                    NativeMethods.OpenExisting,
                    0,
                    IntPtr.Zero);
                if (handle.IsInvalid)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not open the CamyT1 HID collection.");
                }

                var bytes = report.ToArray();
                token.ThrowIfCancellationRequested();
                if (!NativeMethods.HidD_SetOutputReport(handle, bytes, bytes.Length))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(),
                        "CamyT1 output report write failed.");
                }

                var buffer = new byte[bytes.Length];
                for (var attempt = 0; attempt < 5; attempt++)
                {
                    Thread.Sleep(30);
                    token.ThrowIfCancellationRequested();
                    buffer[0] = RazerCamyT1Protocol.ReportId;
                    if (!NativeMethods.HidD_GetInputReport(handle, buffer, buffer.Length) ||
                        buffer[2] != expectedCommand)
                    {
                        continue;
                    }

                    return buffer;
                }

                throw new InvalidDataException(
                    $"CamyT1 command {expectedCommand:X2} did not answer.");
            }, timeout, cancellationToken, task => RazerFeatureTransport.TrackPendingCall(devicePath, task)).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    internal async Task SendOutputReportAsync(
        string devicePath,
        ReadOnlyMemory<byte> report,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);
        if (report.IsEmpty)
        {
            throw new ArgumentException("The HID report cannot be empty.", nameof(report));
        }

        var gate = RazerFeatureTransport.EndpointGate(devicePath);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RazerFeatureTransport.ThrowIfNativeCallPending(devicePath);
            await CancellableNativeCall.RunAsync(token =>
            {
                using var handle = NativeMethods.CreateFile(
                    devicePath,
                    NativeMethods.GenericRead | NativeMethods.GenericWrite,
                    NativeMethods.FileShareRead | NativeMethods.FileShareWrite,
                    IntPtr.Zero,
                    NativeMethods.OpenExisting,
                    0,
                    IntPtr.Zero);
                if (handle.IsInvalid)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not open the CamyT1 HID collection.");
                }

                token.ThrowIfCancellationRequested();
                var bytes = report.ToArray();
                if (!NativeMethods.HidD_SetOutputReport(handle, bytes, bytes.Length))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(),
                        "CamyT1 output report write failed.");
                }

                return true;
            }, timeout, cancellationToken, task => RazerFeatureTransport.TrackPendingCall(devicePath, task)).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task SendAsync(
        string devicePath,
        ReadOnlyMemory<byte> report,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);
        if (report.IsEmpty)
        {
            throw new ArgumentException("The HID report cannot be empty.", nameof(report));
        }

        var gate = RazerFeatureTransport.EndpointGate(devicePath);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RazerFeatureTransport.ThrowIfNativeCallPending(devicePath);
            await CancellableNativeCall.RunAsync(token =>
            {
                using var handle = NativeMethods.CreateFile(
                    devicePath,
                    NativeMethods.GenericWrite,
                    NativeMethods.FileShareRead | NativeMethods.FileShareWrite,
                    IntPtr.Zero,
                    NativeMethods.OpenExisting,
                    0,
                    IntPtr.Zero);
                if (handle.IsInvalid)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not open the RazerHid HID collection.");
                }

                token.ThrowIfCancellationRequested();
                var bytes = report.ToArray();
                if (!NativeMethods.WriteFile(
                        handle,
                        bytes,
                        checked((uint)bytes.Length),
                        out var written,
                        IntPtr.Zero) ||
                    written != bytes.Length)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(),
                        "RazerHid output report write failed.");
                }

                return true;
            }, SendTimeout, cancellationToken, task => RazerFeatureTransport.TrackPendingCall(devicePath, task)).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
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
        internal static extern bool WriteFile(
            SafeFileHandle handle,
            byte[] buffer,
            uint bytesToWrite,
            out uint bytesWritten,
            IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ReadFile(
            SafeFileHandle handle,
            byte[] buffer,
            uint bytesToRead,
            out uint bytesRead,
            IntPtr overlapped);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool HidD_SetFeature(
            SafeFileHandle handle,
            byte[] report,
            int length);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool HidD_SetOutputReport(
            SafeFileHandle handle,
            byte[] report,
            int length);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool HidD_GetInputReport(
            SafeFileHandle handle,
            byte[] report,
            int length);
    }
}
