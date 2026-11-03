using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// P2 profile reads travel through the RzUdd driver at 0x88883024. The handoff
/// confirms the IOCTL and packet shape, but no product id is mapped to P2 yet,
/// so device discovery never reaches this transport. It is a read-only building
/// block for a live P2 probe: the packet guard rejects any set request before the
/// driver is opened.
/// </summary>
internal static class RazerHidP2Transport
{
    internal const uint TransferControlCode = 0x88883024;
    private const uint ShareReadWrite = 3;
    private const uint OpenExisting = 3;

    internal static ValueTask<byte[]> ReadAsync(
        string devicePath,
        byte[] packet,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);
        ArgumentNullException.ThrowIfNull(packet);
        var requestFrame = RazerHidP2Protocol.CreateTransportFrame(packet);
        return new(Task.Run(() => Read(devicePath, requestFrame, packet), cancellationToken));
    }

    private static byte[] Read(string devicePath, byte[] requestFrame, byte[] requestPacket)
    {
        using var handle = CreateFile(devicePath, 0, ShareReadWrite, 0, OpenExisting, 0, 0);
        if (handle.IsInvalid)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Could not open the Razer RzUdd endpoint for a P2 read.");
        }

        var response = new byte[requestFrame.Length];
        if (!DeviceIoControl(
                handle,
                TransferControlCode,
                requestFrame,
                requestFrame.Length,
                response,
                response.Length,
                out var returned,
                0))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                $"The Razer RzUdd endpoint rejected the {requestFrame.Length}-byte P2 read.");
        }

        if (returned != response.Length)
        {
            throw new InvalidDataException(
                $"The Razer RzUdd endpoint returned {returned} bytes for a {response.Length}-byte P2 read.");
        }

        return RazerHidP2Protocol.ParseTransportFrame(response, requestPacket);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        nint securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        nint templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint controlCode,
        byte[] input,
        int inputLength,
        byte[] output,
        int outputLength,
        out uint bytesReturned,
        nint overlapped);
}
