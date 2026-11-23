using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

internal interface IRazerLaylaInputSource : IDisposable
{
    Task RunAsync(Func<RazerLaylaInput, CancellationToken, Task> dispatch, CancellationToken cancellationToken);
}

internal sealed class RazerLaylaHidInput(FileStream stream, int reportLength) : IRazerLaylaInputSource
{
    internal static async Task<IRazerLaylaInputSource> OpenAsync(RazerBleDevice device, CancellationToken token)
    {
        if (!Guid.TryParse(device.ContainerId, out var container) || container == Guid.Empty)
            throw new InvalidOperationException("Control Pod has no physical container identity; HID input cannot be associated safely.");
        var interfaces = await WindowsHidDiscovery.FindVendorInterfacesAsync(0x1532, token).ConfigureAwait(false);
        var matches = interfaces.Where(item => MatchesIdentity(item, container) && HasInputReportOne(item)).ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException($"Control Pod needs one HID report 1 collection in container {container:D}; found {matches.Length}.");
        var item = matches[0];
        var handle = CreateFile(item.DevicePath, 0x80000000, 3, 0, 3, 0x40000000, 0);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error, "Could not open Control Pod input.");
        }
        try { return new RazerLaylaHidInput(new FileStream(handle, FileAccess.Read, item.InputReportByteLength, true), item.InputReportByteLength); }
        catch { handle.Dispose(); throw; }
    }

    internal static bool MatchesIdentity(WindowsHidDiscovery.HidInterfaceDescriptor item, Guid container) =>
        container != Guid.Empty && item.VendorId == 0x1532 && item.ProductId == RazerLaylaProtocol.ProductId &&
        item.InputReportByteLength >= 4 && Guid.TryParse(item.ContainerId, out var actual) && actual == container;

    private static bool HasInputReportOne(WindowsHidDiscovery.HidInterfaceDescriptor item)
    {
        using var handle = CreateFile(item.DevicePath, 0, 3, 0, 3, 0, 0);
        if (handle.IsInvalid || !HidD_GetPreparsedData(handle, out var preparsed)) return false;
        try
        {
            var report = new byte[item.InputReportByteLength];
            // HidP only initializes memory from the descriptor; no device request is sent.
            return HidP_InitializeReportForID(0, 1, preparsed, report, (uint)report.Length) == 0x00110000;
        }
        finally { HidD_FreePreparsedData(preparsed); }
    }

    public async Task RunAsync(Func<RazerLaylaInput, CancellationToken, Task> dispatch, CancellationToken cancellationToken)
    {
        var buffer = new byte[reportLength];
        while (true)
        {
            var length = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (length == 0) throw new EndOfStreamException("Control Pod input disconnected.");
            if (RazerLaylaProtocol.ParseInput(buffer.AsSpan(0, length)) is { } input)
                await dispatch(input, cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose() => stream.Dispose();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, nint security, uint creation, uint flags, nint template);
    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out nint preparsed);
    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_FreePreparsedData(nint preparsed);
    [DllImport("hid.dll")]
    private static extern int HidP_InitializeReportForID(int reportType, byte reportId, nint preparsed, byte[] report, uint length);
}
