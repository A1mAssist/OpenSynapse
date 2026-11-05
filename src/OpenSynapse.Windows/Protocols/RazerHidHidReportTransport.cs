using System.ComponentModel;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace OpenSynapse.Windows.Protocols;

internal sealed class RazerHidHidReportTransport
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates =
        new(StringComparer.OrdinalIgnoreCase);

    internal Task SendOutputAsync(
        string devicePath,
        ReadOnlyMemory<byte> report,
        CancellationToken cancellationToken = default) =>
        SendAsync(devicePath, report, cancellationToken);

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

        var gate = _gates.GetOrAdd(devicePath, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // shortcut: a wedged ReadFile thread exits when the device responds or the
            // handle closes; switch to overlapped I/O if a device is observed hanging.
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
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
            }, cancellationToken).WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
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

        var gate = _gates.GetOrAdd(devicePath, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
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
            }, cancellationToken).ConfigureAwait(false);
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
    }
}
