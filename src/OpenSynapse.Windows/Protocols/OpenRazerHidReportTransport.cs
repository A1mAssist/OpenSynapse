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
        SendBatchAsync(devicePath, [report], cancellationToken);

    /// <summary>
    /// Writes related reports through one HID handle. The Kraken V4 lighting
    /// path depends on brightness and frame reports arriving in order.
    /// </summary>
    internal Task SendOutputBatchAsync(
        string devicePath,
        IReadOnlyList<byte[]> reports,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reports);
        var batch = new ReadOnlyMemory<byte>[reports.Count];
        for (var index = 0; index < reports.Count; index++)
        {
            batch[index] = reports[index];
        }

        return SendBatchAsync(devicePath, batch, cancellationToken);
    }

    private async Task SendBatchAsync(
        string devicePath,
        IReadOnlyList<ReadOnlyMemory<byte>> reports,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);
        if (reports.Count == 0)
        {
            throw new ArgumentException("At least one HID report is required.", nameof(reports));
        }

        foreach (var report in reports)
        {
            if (report.IsEmpty)
            {
                throw new ArgumentException("The HID report cannot be empty.", nameof(reports));
            }
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

                foreach (var report in reports)
                {
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
                            "OpenRazer output report write failed.");
                    }
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

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WriteFile(
            SafeFileHandle handle,
            byte[] buffer,
            uint bytesToWrite,
            out uint bytesWritten,
            IntPtr overlapped);
    }
}
