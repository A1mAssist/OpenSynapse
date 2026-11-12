using System.Runtime.InteropServices;

namespace OpenSynapse.Windows.Devices;

/// <summary>A present USB device instance, used when Windows exposes no HID collection.</summary>
internal readonly record struct WindowsUsbDevice(string InstanceId, ushort ProductId);

/// <summary>
/// Present-only USB device enumeration. The HID path can disappear while the
/// USB composite device is still attached, so the audio page needs this
/// independent view to keep the device card visible.
/// </summary>
internal static class WindowsUsbDiscovery
{
    private const int CrSuccess = 0;
    private const int CmGetIdListFilterPresent = 0x00000100;

    public static Task<IReadOnlyList<WindowsUsbDevice>> FindPresentAsync(
        ushort vendorId,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => FindPresent(vendorId, cancellationToken), cancellationToken);

    internal static IReadOnlyList<WindowsUsbDevice> FindPresent(
        ushort vendorId,
        CancellationToken cancellationToken)
    {
        var flags = CmGetIdListFilterPresent;
        if (CM_Get_Device_ID_List_SizeW(out var length, null, flags) != CrSuccess || length == 0)
        {
            return [];
        }

        var buffer = new char[length];
        if (CM_Get_Device_ID_ListW(null, buffer, length, flags) != CrSuccess)
        {
            return [];
        }

        var devices = new List<WindowsUsbDevice>();
        foreach (var instanceId in new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!instanceId.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase) ||
                !DeviceIdParser.TryParse(instanceId, out var candidateVendorId, out var productId) ||
                candidateVendorId != vendorId)
            {
                continue;
            }

            devices.Add(new WindowsUsbDevice(instanceId, productId));
        }

        return devices;
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int CM_Get_Device_ID_List_SizeW(
        out uint length,
        string? filter,
        int flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int CM_Get_Device_ID_ListW(
        string? filter,
        [Out] char[]? buffer,
        uint bufferLength,
        int flags);
}
