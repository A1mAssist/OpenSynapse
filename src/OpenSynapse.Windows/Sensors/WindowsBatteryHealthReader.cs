using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using OpenSynapse.Core.Sensors;

namespace OpenSynapse.Windows.Sensors;

internal static class WindowsBatteryHealthReader
{
    private static readonly Guid BatteryInterfaceClass = new("72631E54-78A4-11D0-BCF7-00AA00B7B32A");

    private const int ErrorInsufficientBuffer = 122;
    private const int ErrorNoMoreItems = 259;
    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfDeviceInterface = 0x00000010;
    private const uint GenericRead = 0x80000000;
    private const uint ShareReadWrite = 0x00000003;
    private const uint OpenExisting = 3;
    private const uint BatteryCapacityRelative = 0x40000000;
    private const uint IoctlBatteryQueryTag = 0x00294040;
    private const uint IoctlBatteryQueryInformation = 0x00294044;

    public static BatteryHealthSnapshot? TryRead()
    {
        try
        {
            return Read();
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or OverflowException)
        {
            return null;
        }
    }

    private static BatteryHealthSnapshot? Read()
    {
        var interfaceClass = BatteryInterfaceClass;
        var deviceSet = SetupDiGetClassDevs(
            ref interfaceClass, null, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (deviceSet == new IntPtr(-1))
        {
            return null;
        }

        try
        {
            ulong designCapacity = 0;
            ulong fullChargeCapacity = 0;
            uint cycleCount = 0;
            var found = false;
            for (uint index = 0; ; index++)
            {
                if (!TryGetDevicePath(deviceSet, ref interfaceClass, index, out var path))
                {
                    break;
                }
                if (path is null || !TryReadInformation(path, out var information) ||
                    (information.Capabilities & BatteryCapacityRelative) != 0 ||
                    information.DesignedCapacity == 0)
                {
                    continue;
                }

                designCapacity += information.DesignedCapacity;
                fullChargeCapacity += information.FullChargedCapacity;
                cycleCount = Math.Max(cycleCount, information.CycleCount);
                found = true;
            }

            if (!found || designCapacity == 0 ||
                designCapacity > uint.MaxValue || fullChargeCapacity > uint.MaxValue)
            {
                return null;
            }

            return new BatteryHealthSnapshot(
                (uint)designCapacity, (uint)fullChargeCapacity, cycleCount);
        }
        finally
        {
            _ = SetupDiDestroyDeviceInfoList(deviceSet);
        }
    }

    private static bool TryGetDevicePath(
        IntPtr deviceSet,
        ref Guid interfaceClass,
        uint index,
        out string? devicePath)
    {
        var interfaceData = new DeviceInterfaceData
        {
            Size = Marshal.SizeOf<DeviceInterfaceData>(),
        };
        if (!SetupDiEnumDeviceInterfaces(
            deviceSet, IntPtr.Zero, ref interfaceClass, index, ref interfaceData))
        {
            var error = Marshal.GetLastWin32Error();
            devicePath = null;
            if (error == ErrorNoMoreItems)
            {
                return false;
            }
            throw new Win32Exception(error);
        }

        _ = SetupDiGetDeviceInterfaceDetail(
            deviceSet, ref interfaceData, IntPtr.Zero, 0, out var requiredSize, IntPtr.Zero);
        var error2 = Marshal.GetLastWin32Error();
        var minimumSize = (uint)(IntPtr.Size == 8 ? 8 : 6);
        if (error2 != ErrorInsufficientBuffer || requiredSize < minimumSize || requiredSize > int.MaxValue)
        {
            devicePath = null;
            return true;
        }

        var detail = Marshal.AllocHGlobal(checked((int)requiredSize));
        try
        {
            Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
            if (!SetupDiGetDeviceInterfaceDetail(
                deviceSet, ref interfaceData, detail, requiredSize, out _, IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            devicePath = Marshal.PtrToStringUni(IntPtr.Add(detail, 4));
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(detail);
        }
    }

    private static bool TryReadInformation(string path, out BatteryInformation information)
    {
        information = default;
        using var handle = CreateFile(
            path, GenericRead, ShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            return false;
        }

        uint timeout = 0;
        if (!DeviceIoControlTag(
                handle, IoctlBatteryQueryTag, ref timeout, sizeof(uint),
                out var batteryTag, sizeof(uint), out _, IntPtr.Zero) ||
            batteryTag == 0)
        {
            return false;
        }

        var query = new BatteryQueryInformation
        {
            BatteryTag = batteryTag,
            InformationLevel = 0,
        };
        return DeviceIoControlInformation(
            handle, IoctlBatteryQueryInformation, ref query,
            (uint)Marshal.SizeOf<BatteryQueryInformation>(), out information,
            (uint)Marshal.SizeOf<BatteryInformation>(), out _, IntPtr.Zero);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInterfaceData
    {
        public int Size;
        public Guid InterfaceClassGuid;
        public int Flags;
        public UIntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BatteryQueryInformation
    {
        public uint BatteryTag;
        public int InformationLevel;
        public int AtRate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BatteryInformation
    {
        public uint Capabilities;
        public byte Technology;
        public byte Reserved1;
        public byte Reserved2;
        public byte Reserved3;
        public byte Chemistry1;
        public byte Chemistry2;
        public byte Chemistry3;
        public byte Chemistry4;
        public uint DesignedCapacity;
        public uint FullChargedCapacity;
        public uint DefaultAlert1;
        public uint DefaultAlert2;
        public uint CriticalBias;
        public uint CycleCount;
    }

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr SetupDiGetClassDevs(
        ref Guid classGuid, string? enumerator, IntPtr parentWindow, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid,
        uint memberIndex, ref DeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr deviceInfoSet, ref DeviceInterfaceData deviceInterfaceData,
        IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize,
        out uint requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(
        string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControlTag(
        SafeFileHandle device, uint ioControlCode, ref uint inputBuffer, uint inputBufferSize,
        out uint outputBuffer, uint outputBufferSize, out uint bytesReturned, IntPtr overlapped);

    [DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControlInformation(
        SafeFileHandle device, uint ioControlCode, ref BatteryQueryInformation inputBuffer,
        uint inputBufferSize, out BatteryInformation outputBuffer, uint outputBufferSize,
        out uint bytesReturned, IntPtr overlapped);
}
