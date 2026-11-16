using System.Runtime.InteropServices;

namespace OpenSynapse.Windows.Input;

/// <summary>
/// Reads the HID preparsed data and button usages for Raw Input HID frames.
/// The system mouse collection is exclusive, so Raw Input is the only user-mode
/// path to HID buttons above XBUTTON2.
/// </summary>
internal sealed class RawInputHid
{
    private const uint RidiDeviceInfo = 0x2000000B;
    private const uint RidiPreparsedData = 0x20000005;
    private const uint RimTypeHid = 2;
    private const ushort RazerVendorId = 0x1532;
    private const ushort GenericDesktopUsagePage = 0x0001;
    private const ushort MouseUsage = 0x0002;
    private const ushort ButtonUsagePage = 0x0009;

    private readonly Dictionary<nint, DeviceInfo> _devices = [];

    internal DeviceInfo? GetDeviceInfo(nint device, string devicePath)
    {
        if (_devices.TryGetValue(device, out var cached) &&
            StringComparer.OrdinalIgnoreCase.Equals(cached.DevicePath, devicePath))
        {
            return cached;
        }

        if (cached != default)
        {
            if (cached.Preparsed.IsAllocated)
            {
                cached.Preparsed.Free();
            }
            _devices.Remove(device);
        }

        var info = new RidDeviceInfo
        {
            Size = checked((uint)Marshal.SizeOf<RidDeviceInfo>()),
        };
        var size = info.Size;
        var buffer = Marshal.AllocHGlobal(checked((int)size));
        try
        {
            Marshal.StructureToPtr(info, buffer, false);
            if (GetRawInputDeviceInfo(device, RidiDeviceInfo, buffer, ref size) == uint.MaxValue ||
                size < checked((uint)Marshal.SizeOf<RidDeviceInfo>()))
            {
                return null;
            }

            info = Marshal.PtrToStructure<RidDeviceInfo>(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        if (info.Type != RimTypeHid)
        {
            return null;
        }

        var vendorId = checked((ushort)info.Data.Hid.VendorId);
        var productId = checked((ushort)info.Data.Hid.ProductId);
        var usagePage = info.Data.Hid.UsagePage;
        var usage = info.Data.Hid.Usage;
        var preparsed = default(GCHandle);
        if (vendorId == RazerVendorId &&
            usagePage == GenericDesktopUsagePage &&
            usage == MouseUsage)
        {
            uint preparsedSize = 0;
            if (GetRawInputDeviceInfo(device, RidiPreparsedData, (byte[]?)null, ref preparsedSize) != uint.MaxValue &&
                preparsedSize > 0)
            {
                var bytes = new byte[checked((int)preparsedSize)];
                if (GetRawInputDeviceInfo(device, RidiPreparsedData, bytes, ref preparsedSize) != uint.MaxValue)
                {
                    preparsed = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                }
            }
        }

        var result = new DeviceInfo(
            devicePath,
            vendorId,
            productId,
            usagePage,
            usage,
            preparsed);
        _devices[device] = result;
        return result;
    }

    internal int[] ReadExtraButtons(DeviceInfo info, byte[] report)
    {
        if (info.VendorId != RazerVendorId ||
            info.UsagePage != GenericDesktopUsagePage ||
            info.Usage != MouseUsage ||
            !info.Preparsed.IsAllocated)
        {
            return [];
        }

        var usages = new ushort[16];
        var usageLength = (uint)usages.Length;
        if (HidP_GetUsages(
                0,
                ButtonUsagePage,
                0,
                usages,
                ref usageLength,
                info.Preparsed.AddrOfPinnedObject(),
                report,
                checked((uint)report.Length)) < 0)
        {
            return [];
        }

        var buttons = new List<int>(2);
        for (var index = 0; index < usageLength && index < usages.Length; index++)
        {
            if (usages[index] > 5)
            {
                buttons.Add(usages[index]);
            }
        }
        return buttons.Count == 0 ? [] : buttons.ToArray();
    }

    internal void ReleaseAll()
    {
        foreach (var info in _devices.Values)
        {
            if (info.Preparsed.IsAllocated)
            {
                info.Preparsed.Free();
            }
        }
        _devices.Clear();
    }

    internal readonly record struct DeviceInfo(
        string DevicePath,
        ushort VendorId,
        ushort ProductId,
        ushort UsagePage,
        ushort Usage,
        GCHandle Preparsed);

    [StructLayout(LayoutKind.Sequential)]
    private struct RidDeviceInfo
    {
        internal uint Size;
        internal uint Type;
        internal RidDeviceInfoUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct RidDeviceInfoUnion
    {
        [FieldOffset(0)] internal RidDeviceInfoMouse Mouse;
        [FieldOffset(0)] internal RidDeviceInfoKeyboard Keyboard;
        [FieldOffset(0)] internal RidDeviceInfoHid Hid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RidDeviceInfoMouse
    {
        internal uint Id;
        internal uint NumberOfButtons;
        internal uint SampleRate;
        internal int HasHorizontalWheel;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RidDeviceInfoKeyboard
    {
        internal uint Type;
        internal uint SubType;
        internal uint KeyboardMode;
        internal uint NumberOfFunctionKeys;
        internal uint NumberOfIndicators;
        internal uint NumberOfKeysTotal;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RidDeviceInfoHid
    {
        internal uint VendorId;
        internal uint ProductId;
        internal uint VersionNumber;
        internal ushort UsagePage;
        internal ushort Usage;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputDeviceInfo(nint device, uint command, nint data, ref uint size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputDeviceInfo(nint device, uint command, byte[]? data, ref uint size);

    [DllImport("hid.dll")]
    private static extern int HidP_GetUsages(
        int reportType,
        ushort usagePage,
        ushort linkCollection,
        [Out] ushort[] usageList,
        ref uint usageLength,
        nint preparsedData,
        byte[] report,
        uint reportLength);
}
