using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenSynapse.Windows.Devices;

/// <summary>Present Razer camera found through SetupAPI.</summary>
public readonly record struct RazerKiyoDeviceInfo(
    ushort ProductId,
    Guid ContainerId,
    string FriendlyName);

/// <summary>
/// Finds the Windows container id for a Razer USB camera. The native camera DLLs
/// expect the same <c>{GUID}</c> form that StringFromGUID2 produces.
/// </summary>
internal static class RazerKiyoDiscovery
{
    private const int DigcfPresent = 0x00000002;
    private const int DigcfAllClasses = 0x00000004;
    private const int ErrorNoMoreItems = 259;
    private const uint SpdrpDeviceDesc = 0x00000000;
    private const uint SpdrpHardwareId = 0x00000001;
    private const uint SpdrpFriendlyName = 0x0000000C;
    private const uint DevpropTypeGuid = 0x0000000D;
    private static readonly IntPtr InvalidHandleValue = new(-1);
    private static readonly Devpropkey DeviceContainerId = new()
    {
        FormatId = new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"),
        PropertyId = 2,
    };

    public static IReadOnlyList<RazerKiyoDeviceInfo> FindDevices()
    {
        var found = new Dictionary<Guid, RazerKiyoDeviceInfo>();
        var scores = new Dictionary<Guid, int>();
        var deviceInfoSet = SetupDiGetClassDevs(
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero,
            DigcfPresent | DigcfAllClasses);
        if (deviceInfoSet == InvalidHandleValue)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not enumerate camera devices.");
        }

        try
        {
            for (uint index = 0; ; index++)
            {
                var device = new SpDevinfoData { Size = Marshal.SizeOf<SpDevinfoData>() };
                if (!SetupDiEnumDeviceInfo(deviceInfoSet, index, ref device))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == ErrorNoMoreItems)
                    {
                        break;
                    }

                    throw new Win32Exception(error, "Could not enumerate a camera device.");
                }

                var hardwareIds = ReadHardwareIds(deviceInfoSet, ref device);
                var productId = MatchProductId(hardwareIds);
                if (productId is null ||
                    !TryGetContainerId(deviceInfoSet, ref device, out var containerId))
                {
                    continue;
                }

                var score = CameraNodeScore(hardwareIds);
                if (scores.TryGetValue(containerId, out var previousScore) && previousScore > score)
                {
                    continue;
                }

                scores[containerId] = score;
                found[containerId] = new RazerKiyoDeviceInfo(
                    productId.Value,
                    containerId,
                    ReadFriendlyName(deviceInfoSet, ref device));
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }

        return found.Values.ToArray();
    }

    public static Guid? FindContainerId(ushort productId)
    {
        foreach (var device in FindDevices())
        {
            if (device.ProductId == productId)
            {
                return device.ContainerId;
            }
        }

        return null;
    }

    private static ushort? MatchProductId(IReadOnlyList<string> hardwareIds)
    {
        foreach (var device in RazerKiyoDevices.All)
        {
            if (hardwareIds.Any(id => id.Contains(
                    $"VID_1532&PID_{device.ProductId:X4}",
                    StringComparison.OrdinalIgnoreCase)))
            {
                return device.ProductId;
            }
        }

        return null;
    }

    // A camera node (USB class 0E) gives the best friendly name. Composite
    // parents are fallbacks; audio nodes come last.
    private static int CameraNodeScore(IReadOnlyList<string> hardwareIds)
    {
        if (hardwareIds.Any(id => id.Contains("Class_0E", StringComparison.OrdinalIgnoreCase)))
        {
            return 2;
        }

        return hardwareIds.Any(id => id.Contains("Class_01", StringComparison.OrdinalIgnoreCase)) ? 0 : 1;
    }

    private static string[] ReadHardwareIds(IntPtr deviceInfoSet, ref SpDevinfoData device)
    {
        var buffer = new byte[2048];
        if (!SetupDiGetDeviceRegistryProperty(
                deviceInfoSet,
                ref device,
                SpdrpHardwareId,
                out _,
                buffer,
                (uint)buffer.Length,
                out var requiredSize) ||
            requiredSize == 0)
        {
            return [];
        }

        return Encoding.Unicode
            .GetString(buffer, 0, (int)Math.Min(requiredSize, (uint)buffer.Length))
            .Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    private static string ReadFriendlyName(IntPtr deviceInfoSet, ref SpDevinfoData device)
    {
        var friendlyName = ReadRegistryString(deviceInfoSet, ref device, SpdrpFriendlyName);
        return string.IsNullOrWhiteSpace(friendlyName)
            ? ReadRegistryString(deviceInfoSet, ref device, SpdrpDeviceDesc)
            : friendlyName;
    }

    private static string ReadRegistryString(
        IntPtr deviceInfoSet,
        ref SpDevinfoData device,
        uint property)
    {
        var buffer = new byte[1024];
        if (!SetupDiGetDeviceRegistryProperty(
                deviceInfoSet,
                ref device,
                property,
                out _,
                buffer,
                (uint)buffer.Length,
                out var requiredSize) ||
            requiredSize < 2)
        {
            return string.Empty;
        }

        return Encoding.Unicode
            .GetString(buffer, 0, (int)Math.Min(requiredSize, (uint)buffer.Length))
            .TrimEnd('\0')
            .Trim();
    }

    private static bool TryGetContainerId(
        IntPtr deviceInfoSet,
        ref SpDevinfoData device,
        out Guid containerId)
    {
        containerId = Guid.Empty;
        var key = DeviceContainerId;
        var size = Marshal.SizeOf<Guid>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!SetupDiGetDeviceProperty(
                    deviceInfoSet,
                    ref device,
                    ref key,
                    out var propertyType,
                    buffer,
                    size,
                    out var requiredSize,
                    0) ||
                propertyType != DevpropTypeGuid ||
                requiredSize != size)
            {
                return false;
            }

            containerId = Marshal.PtrToStructure<Guid>(buffer);
            return containerId != Guid.Empty;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevinfoData
    {
        internal int Size;
        internal Guid ClassGuid;
        internal uint DeviceInstance;
        internal IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Devpropkey
    {
        internal Guid FormatId;
        internal uint PropertyId;
    }

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr SetupDiGetClassDevs(
        IntPtr classGuid,
        IntPtr enumerator,
        IntPtr parentWindow,
        int flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInfo(
        IntPtr deviceInfoSet,
        uint memberIndex,
        ref SpDevinfoData deviceInfoData);

    [DllImport(
        "setupapi.dll",
        EntryPoint = "SetupDiGetDeviceRegistryPropertyW",
        SetLastError = true,
        CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceRegistryProperty(
        IntPtr deviceInfoSet,
        ref SpDevinfoData deviceInfoData,
        uint property,
        out uint propertyRegDataType,
        byte[] propertyBuffer,
        uint propertyBufferSize,
        out uint requiredSize);

    [DllImport(
        "setupapi.dll",
        EntryPoint = "SetupDiGetDevicePropertyW",
        SetLastError = true,
        CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceProperty(
        IntPtr deviceInfoSet,
        ref SpDevinfoData deviceInfoData,
        ref Devpropkey propertyKey,
        out uint propertyType,
        IntPtr propertyBuffer,
        int propertyBufferSize,
        out int requiredSize,
        uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);
}
