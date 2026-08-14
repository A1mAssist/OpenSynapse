using System.ComponentModel;
using System.Runtime.InteropServices;
using OpenSynapse.Core.Devices;

namespace OpenSynapse.Windows.Devices;

public sealed class WindowsHidDiscovery : IDeviceDiscovery
{
    private readonly RazerDeviceRegistry _registry;

    public WindowsHidDiscovery()
        : this(RazerDeviceRegistry.BuiltIn)
    {
    }

    internal WindowsHidDiscovery(RazerDeviceRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public ValueTask<DeviceSnapshot> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        return new ValueTask<DeviceSnapshot>(Task.Run(
            () => Discover(_registry, cancellationToken, collapseProductIds: true), cancellationToken));
    }

    public static ValueTask<DeviceSnapshot> DiscoverAllAsync(CancellationToken cancellationToken = default) =>
        new(Task.Run(() => Discover(
            RazerDeviceRegistry.BuiltIn, cancellationToken, collapseProductIds: false), cancellationToken));

    private static DeviceSnapshot Discover(
        RazerDeviceRegistry registry,
        CancellationToken cancellationToken,
        bool collapseProductIds)
    {
        var devices = new List<DeviceDescriptor>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deviceIndexes = new Dictionary<(ushort VendorId, ushort ProductId), int>();

        try
        {
            NativeMethods.HidD_GetHidGuid(out var hidGuid);
            var deviceInfoSet = NativeMethods.SetupDiGetClassDevs(
                ref hidGuid,
                IntPtr.Zero,
                IntPtr.Zero,
                NativeMethods.DIGCF_PRESENT | NativeMethods.DIGCF_DEVICEINTERFACE);

            if (deviceInfoSet == NativeMethods.INVALID_HANDLE_VALUE)
            {
                return new DeviceSnapshot(devices, DateTimeOffset.UtcNow, $"SetupDiGetClassDevs failed: {Marshal.GetLastWin32Error()}");
            }

            try
            {
                for (uint index = 0; ; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var interfaceData = new NativeMethods.SpDeviceInterfaceData
                    {
                        CbSize = Marshal.SizeOf<NativeMethods.SpDeviceInterfaceData>(),
                    };

                    if (!NativeMethods.SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref hidGuid, index, ref interfaceData))
                    {
                        var error = Marshal.GetLastWin32Error();
                        if (error == NativeMethods.ERROR_NO_MORE_ITEMS)
                        {
                            break;
                        }

                        throw new Win32Exception(error, "SetupDiEnumDeviceInterfaces failed.");
                    }

                    var path = GetDevicePath(deviceInfoSet, ref interfaceData);
                    if (path is null || !seenPaths.Add(path) ||
                        !DeviceIdParser.TryParse(path, out var vendorId, out var productId))
                    {
                        continue;
                    }

                    var manifest = registry.Find(vendorId, productId);
                    if (manifest is null)
                    {
                        continue;
                    }

                    var probe = ProbeInterface(path, manifest.Collection);
                    var descriptor = new DeviceDescriptor(
                        path,
                        manifest.DisplayName,
                        vendorId,
                        productId,
                        probe.Access,
                        probe.IsControlChannel ? DeviceCapabilityState.PendingValidation : DeviceCapabilityState.Blocked,
                        probe.FeatureReportByteLength,
                        probe.UsagePage,
                        probe.Usage,
                        manifest.ProtocolFamily);

                    var deviceKey = (vendorId, productId);
                    if (collapseProductIds && deviceIndexes.TryGetValue(deviceKey, out var existingIndex))
                    {
                        var existing = devices[existingIndex];
                        if ((descriptor.Capability == DeviceCapabilityState.PendingValidation && existing.Capability != DeviceCapabilityState.PendingValidation) ||
                            (descriptor.Capability == existing.Capability && descriptor.FeatureReportByteLength > existing.FeatureReportByteLength) ||
                            (descriptor.FeatureReportByteLength == existing.FeatureReportByteLength &&
                             existing.Access != DeviceAccessState.Available && descriptor.Access == DeviceAccessState.Available))
                        {
                            devices[existingIndex] = descriptor;
                        }
                    }
                    else if (collapseProductIds)
                    {
                        deviceIndexes.Add(deviceKey, devices.Count);
                        devices.Add(descriptor);
                    }
                    else
                    {
                        devices.Add(descriptor);
                    }
                }
            }
            finally
            {
                NativeMethods.SetupDiDestroyDeviceInfoList(deviceInfoSet);
            }

            return new DeviceSnapshot(devices, DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return new DeviceSnapshot(devices, DateTimeOffset.UtcNow, exception.Message);
        }
    }

    private static string? GetDevicePath(IntPtr deviceInfoSet, ref NativeMethods.SpDeviceInterfaceData interfaceData)
    {
        NativeMethods.SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, IntPtr.Zero, 0, out var requiredSize, IntPtr.Zero);
        if (requiredSize <= 0)
        {
            return null;
        }

        var detailData = Marshal.AllocHGlobal(requiredSize);
        try
        {
            Marshal.WriteInt32(detailData, IntPtr.Size == 8 ? 8 : 4);
            if (!NativeMethods.SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, detailData, requiredSize, out _, IntPtr.Zero))
            {
                return null;
            }

            return Marshal.PtrToStringUni(detailData + 4);
        }
        finally
        {
            Marshal.FreeHGlobal(detailData);
        }
    }

    private static InterfaceProbe ProbeInterface(string path, RazerHidCollection collection)
    {
        var queryHandle = NativeMethods.CreateFile(path, 0, NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE, IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero);
        if (queryHandle == NativeMethods.INVALID_HANDLE_VALUE)
        {
            return new InterfaceProbe(false, 0, 0, 0, DeviceAccessState.BusyOrUnavailable);
        }

        ushort featureReportByteLength = 0;
        ushort usagePage = 0;
        ushort usage = 0;
        try
        {
            if (NativeMethods.HidD_GetPreparsedData(queryHandle, out var preparsedData))
            {
                try
                {
                    if (NativeMethods.HidP_GetCaps(preparsedData, out var caps) >= 0)
                    {
                        featureReportByteLength = caps.FeatureReportByteLength;
                        usagePage = caps.UsagePage;
                        usage = caps.Usage;
                    }
                }
                finally
                {
                    NativeMethods.HidD_FreePreparsedData(preparsedData);
                }
            }
        }
        finally
        {
            NativeMethods.CloseHandle(queryHandle);
        }

        var isControlChannel = MatchesCollection(
            collection, usagePage, usage, featureReportByteLength);
        if (!isControlChannel)
        {
            return new InterfaceProbe(false, featureReportByteLength, usagePage, usage, DeviceAccessState.BusyOrUnavailable);
        }

        var controlHandle = NativeMethods.CreateFile(path, NativeMethods.GENERIC_WRITE, NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE, IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero);
        if (controlHandle == NativeMethods.INVALID_HANDLE_VALUE)
        {
            return new InterfaceProbe(true, featureReportByteLength, usagePage, usage, DeviceAccessState.BusyOrUnavailable);
        }

        NativeMethods.CloseHandle(controlHandle);
        return new InterfaceProbe(true, featureReportByteLength, usagePage, usage, DeviceAccessState.Available);
    }

    internal static bool MatchesCollection(
        RazerHidCollection collection,
        ushort usagePage,
        ushort usage,
        ushort featureReportByteLength) =>
        usagePage == collection.UsagePage &&
        usage == collection.Usage &&
        featureReportByteLength == collection.FeatureReportLength;

    private sealed record InterfaceProbe(bool IsControlChannel, ushort FeatureReportByteLength, ushort UsagePage, ushort Usage, DeviceAccessState Access);

    private static class NativeMethods
    {
        internal const int DIGCF_PRESENT = 0x00000002;
        internal const int DIGCF_DEVICEINTERFACE = 0x00000010;
        internal const int ERROR_NO_MORE_ITEMS = 259;
        internal const uint GENERIC_READ = 0x80000000;
        internal const uint GENERIC_WRITE = 0x40000000;
        internal const uint FILE_SHARE_READ = 0x00000001;
        internal const uint FILE_SHARE_WRITE = 0x00000002;
        internal const uint OPEN_EXISTING = 3;
        internal static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

        [StructLayout(LayoutKind.Sequential)]
        internal struct SpDeviceInterfaceData
        {
            internal int CbSize;
            internal Guid InterfaceClassGuid;
            internal int Flags;
            internal IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct HidpCaps
        {
            internal ushort Usage;
            internal ushort UsagePage;
            internal ushort InputReportByteLength;
            internal ushort OutputReportByteLength;
            internal ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            internal ushort[] Reserved;
            internal ushort NumberLinkCollectionNodes;
            internal ushort NumberInputButtonCaps;
            internal ushort NumberInputValueCaps;
            internal ushort NumberInputDataIndices;
            internal ushort NumberOutputButtonCaps;
            internal ushort NumberOutputValueCaps;
            internal ushort NumberOutputDataIndices;
            internal ushort NumberFeatureButtonCaps;
            internal ushort NumberFeatureValueCaps;
            internal ushort NumberFeatureDataIndices;
        }

        [DllImport("hid.dll")]
        internal static extern void HidD_GetHidGuid(out Guid hidGuid);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool HidD_GetPreparsedData(IntPtr hidDeviceObject, out IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

        [DllImport("hid.dll")]
        internal static extern int HidP_GetCaps(IntPtr preparsedData, out HidpCaps capabilities);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, int flags);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SpDeviceInterfaceData deviceInterfaceData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SpDeviceInterfaceData deviceInterfaceData, IntPtr deviceInterfaceDetailData, int deviceInterfaceDetailDataSize, out int requiredSize, IntPtr deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern IntPtr CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr handle);
    }
}
