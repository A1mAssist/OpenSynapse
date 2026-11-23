using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

internal sealed class RazerRipsawTransport(string devicePath)
{
    internal Task<byte[]> QueryAsync(byte[] request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);
        RazerRipsawProtocol.ValidateResponse(request);
        return CancellableNativeCall.RunAsync(() =>
        {
            using var handle = CreateFile(devicePath, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not open Ripsaw HD.");
            if (!HidD_SetFeature(handle, request, request.Length))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Ripsaw HD feature write failed.");
            cancellationToken.ThrowIfCancellationRequested();
            var response = new byte[RazerRipsawProtocol.ReportLength];
            response[0] = RazerRipsawProtocol.ReportId;
            if (!HidD_GetFeature(handle, response, response.Length))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Ripsaw HD feature read failed.");
            return response;
        }, TimeSpan.FromSeconds(2), cancellationToken);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share,
        IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_SetFeature(SafeFileHandle handle, byte[] report, int length);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetFeature(SafeFileHandle handle, byte[] report, int length);
}
