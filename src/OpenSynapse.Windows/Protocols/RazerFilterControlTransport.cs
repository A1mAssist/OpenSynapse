using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Sends HID feature reports through the RZCONTROL endpoint that Razer's
/// per-device filter driver exposes. Windows HID cannot carry the 320-byte
/// ARGB frame itself, but the driver forwards any length to SET_FEATURE.
/// </summary>
internal static class RazerFilterControlTransport
{
    private const uint ShareReadWrite = 3;
    private const uint OpenExisting = 3;

    internal static ValueTask SetFeatureAsync(
        string controlDevicePath,
        byte[] report,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(controlDevicePath);
        ArgumentNullException.ThrowIfNull(report);
        return new(Task.Run(() => SetFeature(controlDevicePath, report), cancellationToken));
    }

    private static void SetFeature(string controlDevicePath, byte[] report)
    {
        using var handle = CreateFile(controlDevicePath, 0, ShareReadWrite, 0, OpenExisting, 0, 0);
        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not open the Razer control endpoint.");

        if (!DeviceIoControl(handle, RazerFilterInputProtocol.SetFeature, report, report.Length, null, 0, out _, 0))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                $"The Razer control endpoint rejected the {report.Length}-byte SET_FEATURE report.");
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode,
        nint securityAttributes, uint creationDisposition, uint flagsAndAttributes, nint templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint controlCode, byte[] input,
        int inputLength, byte[]? output, int outputLength, out uint bytesReturned, nint overlapped);
}
