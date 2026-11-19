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
    internal const uint ProfileTransfer = 0x88883004;
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

    /// <summary>
    /// Legacy keypad P2 profile transfer. The RZCONTROL endpoint takes the
    /// 24-byte header plus records at 0x88883004 and returns the declared
    /// output buffer.
    /// </summary>
    internal static ValueTask<byte[]> TransferAsync(
        string controlDevicePath,
        byte[] input,
        int outputLength,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(controlDevicePath);
        ArgumentNullException.ThrowIfNull(input);
        if (outputLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(outputLength));
        }

        return new(Task.Run(
            () => Transfer(controlDevicePath, input, outputLength),
            cancellationToken));
    }

    private static byte[] Transfer(string controlDevicePath, byte[] input, int outputLength)
    {
        using var handle = CreateFile(controlDevicePath, 0, ShareReadWrite, 0, OpenExisting, 0, 0);
        if (handle.IsInvalid)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Could not open the Razer control endpoint for a keypad profile transfer.");
        }

        var output = new byte[outputLength];
        if (!DeviceIoControl(
                handle,
                ProfileTransfer,
                input,
                input.Length,
                output,
                output.Length,
                out var returned,
                0))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                $"The Razer control endpoint rejected the {input.Length}-byte keypad profile transfer.");
        }

        if (returned < output.Length)
        {
            Array.Resize(ref output, (int)returned);
        }

        return output;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode,
        nint securityAttributes, uint creationDisposition, uint flagsAndAttributes, nint templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint controlCode, byte[] input,
        int inputLength, byte[]? output, int outputLength, out uint bytesReturned, nint overlapped);
}
