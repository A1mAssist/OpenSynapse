using System.Runtime.InteropServices;

namespace OpenSynapse.Windows.Devices;

public sealed partial class WindowsHidDiscovery
{
    private static readonly Guid SystemContainerId =
        new("00000000-0000-0000-FFFF-FFFFFFFFFFFF");

    internal static string GetPhysicalDeviceKey(string devicePath)
    {
        return GetPhysicalDeviceKey(devicePath, null, null);
    }

    internal static string GetPhysicalDeviceKey(string devicePath, Guid? containerId)
    {
        return GetPhysicalDeviceKey(devicePath, containerId, null);
    }

    internal static string GetPhysicalDeviceKey(
        string devicePath,
        Guid? containerId,
        string? usbParentDeviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);
        if (containerId is { } value && value != Guid.Empty && value != SystemContainerId)
        {
            return $"container:{value:D}";
        }

        // The system container is shared by unrelated devices, and a HID path alone
        // identifies an interface. The USB parent is the physical device when present.
        if (!string.IsNullOrWhiteSpace(usbParentDeviceId) && !IsBluetoothHidPath(devicePath))
        {
            return $"usb-parent:{usbParentDeviceId}";
        }

        var collection = devicePath.LastIndexOf("&col", StringComparison.OrdinalIgnoreCase);
        if (collection < 0)
        {
            return devicePath;
        }
        var instance = devicePath.IndexOf('#', collection);
        return instance >= 0 ? devicePath.Remove(collection, instance - collection) : devicePath;
    }

    private static string? TryGetUsbParentDeviceId(uint devInst)
    {
        if (devInst == 0 ||
            !TryGetParentDevice(devInst, out var parentDevInst) ||
            !TryGetDeviceInstanceId(parentDevInst, out var parentId))
        {
            return null;
        }

        if (parentId.Contains("&MI_", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryGetParentDevice(parentDevInst, out var compositeDevInst) ||
                !TryGetDeviceInstanceId(compositeDevInst, out var compositeId))
            {
                return null;
            }
            parentId = compositeId;
        }

        return parentId.StartsWith(@"USB\VID_", StringComparison.OrdinalIgnoreCase) ? parentId : null;
    }

    private static bool TryGetParentDevice(uint devInst, out uint parentDevInst) =>
        NativeMethods.CM_Get_Parent(out parentDevInst, devInst, 0) == 0;

    private static bool TryGetDeviceInstanceId(uint devInst, out string instanceId)
    {
        var buffer = new char[256];
        if (NativeMethods.CM_Get_Device_IDW(devInst, buffer, buffer.Length, 0) != 0)
        {
            instanceId = string.Empty;
            return false;
        }

        var terminator = Array.IndexOf(buffer, '\0');
        instanceId = new string(buffer, 0, terminator >= 0 ? terminator : buffer.Length);
        return instanceId.Length > 0;
    }

    private static partial class NativeMethods
    {
        [DllImport("cfgmgr32.dll")]
        internal static extern uint CM_Get_Parent(out uint parentDevInst, uint devInst, uint flags);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        internal static extern uint CM_Get_Device_IDW(uint devInst, [Out] char[] buffer, int bufferLen, uint flags);
    }
}
