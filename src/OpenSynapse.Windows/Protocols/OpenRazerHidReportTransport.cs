using System.ComponentModel;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace OpenSynapse.Windows.Protocols;

internal sealed class OpenRazerHidReportTransport
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates =
        new(StringComparer.OrdinalIgnoreCase);

    internal Task SendOutputAsync(
        string devicePath,
        ReadOnlyMemory<byte> report,
        CancellationToken cancellationToken = default) =>
        SendAsync(devicePath, report, cancellationToken);

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
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not open the OpenRazer HID collection.");
                }

                var bytes = report.ToArray();
                if (!NativeMethods.HidD_SetOutputReport(handle, bytes, bytes.Length))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(),
                        "OpenRazer output report write failed.");
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

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool HidD_SetOutputReport(SafeFileHandle handle, byte[] reportBuffer, int reportBufferLength);
    }
}
