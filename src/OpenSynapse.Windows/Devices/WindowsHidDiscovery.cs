using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using OpenSynapse.Core.Devices;

namespace OpenSynapse.Windows.Devices;

public sealed class WindowsHidDiscovery : IDeviceDiscovery
{
    private readonly RazerDeviceRegistry _registry;
    private readonly Action<string>? _diagnosticLog;
    private readonly HashSet<string> _loggedUnmatchedInterfaces = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _diagnosticSync = new();

    public WindowsHidDiscovery()
        : this(RazerDeviceRegistry.BuiltIn)
    {
    }

    internal WindowsHidDiscovery(RazerDeviceRegistry registry, Action<string>? diagnosticLog = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _diagnosticLog = diagnosticLog;
    }

    public ValueTask<DeviceSnapshot> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        return new ValueTask<DeviceSnapshot>(Task.Run(
            () => Discover(cancellationToken, collapseProductIds: true), cancellationToken));
    }

    public static ValueTask<DeviceSnapshot> DiscoverAllAsync(CancellationToken cancellationToken = default)
    {
        var discovery = new WindowsHidDiscovery(RazerDeviceRegistry.BuiltIn);
        return new ValueTask<DeviceSnapshot>(Task.Run(
            () => discovery.Discover(cancellationToken, collapseProductIds: false), cancellationToken));
    }

    internal static Task<IReadOnlyList<HidInterfaceDescriptor>> FindVendorFeatureInterfacesAsync(
        ushort vendorId,
        ushort featureReportLength,
        CancellationToken cancellationToken = default,
        Action<HidInterfaceDescriptor>? observeInterface = null) =>
        Task.Run(() => FindVendorFeatureInterfaces(
            vendorId, featureReportLength, cancellationToken, observeInterface), cancellationToken);

    internal static Task<IReadOnlyList<HidInterfaceDescriptor>> FindVendorInterfacesAsync(
        ushort vendorId,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => FindVendorFeatureInterfaces(vendorId, null, cancellationToken), cancellationToken);

    public static Task<string?> TryReadSerialNumberAsync(string devicePath, CancellationToken cancellationToken = default) =>
        Task.Run(() => TryReadSerialNumber(devicePath), cancellationToken);

    private static string? TryReadSerialNumber(string devicePath)
    {
        if (string.IsNullOrWhiteSpace(devicePath)) return null;
        var handle = NativeMethods.CreateFile(devicePath, 0,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle == NativeMethods.INVALID_HANDLE_VALUE) return null;
        try
        {
            var buffer = new byte[256];
            if (!NativeMethods.HidD_GetSerialNumberString(handle, buffer, buffer.Length)) return null;
            var serial = Encoding.Unicode.GetString(buffer);
            var terminator = serial.IndexOf('\0');
            if (terminator >= 0) serial = serial[..terminator];
            serial = serial.Trim();
            return IsUsableSerial(serial) ? serial : null;
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    internal static bool IsUsableSerial(string? serial) =>
        !string.IsNullOrWhiteSpace(serial) &&
        !serial.Any(char.IsControl) &&
        !serial.All(character => character == '0') &&
        !serial.All(character => character is 'F' or 'f');

    private static IReadOnlyList<HidInterfaceDescriptor> FindVendorFeatureInterfaces(
        ushort vendorId,
        ushort? featureReportLength,
        CancellationToken cancellationToken,
        Action<HidInterfaceDescriptor>? observeInterface = null)
    {
        var interfaces = new List<HidInterfaceDescriptor>();
        NativeMethods.HidD_GetHidGuid(out var hidGuid);
        var deviceInfoSet = NativeMethods.SetupDiGetClassDevs(
            ref hidGuid,
            IntPtr.Zero,
            IntPtr.Zero,
            NativeMethods.DIGCF_PRESENT | NativeMethods.DIGCF_DEVICEINTERFACE);
        if (deviceInfoSet == NativeMethods.INVALID_HANDLE_VALUE)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetupDiGetClassDevs failed.");
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

                var path = GetDevicePath(deviceInfoSet, ref interfaceData, out var containerId);
                if (path is null)
                {
                    continue;
                }

                var parsedPath = DeviceIdParser.TryParse(path, out var candidateVendorId, out var candidateProductId);
                InterfaceProbe? metadata = null;
                if (!parsedPath)
                {
                    // Bluetooth HID paths usually omit USB-style VID_/PID_ tokens.
                    if (!IsBluetoothHidPath(path))
                    {
                        continue;
                    }

                    metadata = ProbeInterfaceMetadata(path);
                    if (!TryResolveDeviceIdentity(
                            path,
                            metadata.VendorId,
                            metadata.ProductId,
                            out candidateVendorId,
                            out candidateProductId) ||
                        candidateVendorId != vendorId)
                    {
                        continue;
                    }
                }
                else if (candidateVendorId != vendorId)
                {
                    continue;
                }

                var probe = metadata ?? ProbeInterfaceMetadata(path);
                var descriptor = new HidInterfaceDescriptor(
                    path,
                    GetPhysicalDeviceKey(path, containerId),
                    candidateVendorId,
                    candidateProductId,
                    probe.InputReportByteLength,
                    probe.OutputReportByteLength,
                    probe.FeatureReportByteLength,
                    probe.UsagePage,
                    probe.Usage,
                    probe.Access,
                    probe.Product);
                observeInterface?.Invoke(descriptor);
                if (featureReportLength is null || probe.FeatureReportByteLength == featureReportLength)
                {
                    interfaces.Add(descriptor);
                }
            }
        }
        finally
        {
            NativeMethods.SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }

        return interfaces;
    }

    private DeviceSnapshot Discover(
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
                    if (path is null || !seenPaths.Add(path))
                    {
                        continue;
                    }

                    var parsedPath = DeviceIdParser.TryParse(path, out var vendorId, out var productId);
                    InterfaceProbe? metadata = null;
                    if (!parsedPath)
                    {
                        // Windows exposes Bluetooth HID devices through BTHLE/BTHENUM
                        // paths that usually omit USB-style VID_/PID_ tokens.
                        if (!IsBluetoothHidPath(path))
                        {
                            continue;
                        }

                        metadata = ProbeInterfaceMetadata(path);
                        if (!TryResolveDeviceIdentity(
                                path,
                                metadata.VendorId,
                                metadata.ProductId,
                                out vendorId,
                                out productId))
                        {
                            LogUnmatchedInterface(path, metadata);
                            continue;
                        }
                    }

                    var manifest = _registry.Find(vendorId, productId);
                    if (manifest is null)
                    {
                        if (metadata is not null)
                        {
                            LogUnmatchedInterface(path, metadata);
                        }
                        continue;
                    }

                    metadata ??= ProbeInterfaceMetadata(path);
                    var isControlChannel = MatchesCollection(
                        manifest.Collection,
                        metadata.UsagePage,
                        metadata.Usage,
                        metadata.FeatureReportByteLength);
                    var descriptor = new DeviceDescriptor(
                        path,
                        manifest.DisplayName,
                        vendorId,
                        productId,
                        isControlChannel ? metadata.Access : DeviceAccessState.BusyOrUnavailable,
                        isControlChannel ? DeviceCapabilityState.PendingValidation : DeviceCapabilityState.Blocked,
                        metadata.FeatureReportByteLength,
                        metadata.UsagePage,
                        metadata.Usage,
                        manifest.ProtocolFamily,
                        manifest.Category);

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
        return GetDevicePath(deviceInfoSet, ref interfaceData, out _);
    }

    private static string? GetDevicePath(
        IntPtr deviceInfoSet,
        ref NativeMethods.SpDeviceInterfaceData interfaceData,
        out Guid? containerId)
    {
        containerId = null;
        NativeMethods.SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, IntPtr.Zero, 0, out var requiredSize, IntPtr.Zero);
        if (requiredSize <= 0)
        {
            return null;
        }

        var detailData = Marshal.AllocHGlobal(requiredSize);
        try
        {
            Marshal.WriteInt32(detailData, IntPtr.Size == 8 ? 8 : 4);
            var deviceInfo = new NativeMethods.SpDevinfoData
            {
                CbSize = Marshal.SizeOf<NativeMethods.SpDevinfoData>(),
            };
            if (!NativeMethods.SetupDiGetDeviceInterfaceDetail(
                deviceInfoSet, ref interfaceData, detailData, requiredSize, out _, ref deviceInfo))
            {
                return null;
            }

            containerId = TryGetContainerId(deviceInfoSet, ref deviceInfo);
            return Marshal.PtrToStringUni(detailData + 4);
        }
        finally
        {
            Marshal.FreeHGlobal(detailData);
        }
    }

    internal static bool IsBluetoothHidPath(string path) =>
        path.Contains("BTHLE", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("BTHENUM", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("00001812-0000-1000-8000-00805f9b34fb", StringComparison.OrdinalIgnoreCase);

    internal static bool TryResolveDeviceIdentity(
        string path,
        ushort attributeVendorId,
        ushort attributeProductId,
        out ushort vendorId,
        out ushort productId)
    {
        if (DeviceIdParser.TryParse(path, out vendorId, out productId))
        {
            return true;
        }

        vendorId = attributeVendorId;
        productId = attributeProductId;
        return vendorId != 0 && productId != 0;
    }

    private void LogUnmatchedInterface(string path, InterfaceProbe metadata)
    {
        if (_diagnosticLog is null ||
            (!IsBluetoothHidPath(path) &&
             metadata.VendorId != 0x1532 &&
             !metadata.Manufacturer.Contains("Razer", StringComparison.OrdinalIgnoreCase) &&
             !metadata.Product.Contains("Razer", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        lock (_diagnosticSync)
        {
            if (!_loggedUnmatchedInterfaces.Add(path))
            {
                return;
            }
        }

        _diagnosticLog(
            $"unmatched path={DescribeUnmatchedPath(path)}, vendor=0x{metadata.VendorId:X4}, product=0x{metadata.ProductId:X4}, " +
            $"manufacturer='{metadata.Manufacturer}', productName='{metadata.Product}', " +
            $"usage=0x{metadata.UsagePage:X4}:0x{metadata.Usage:X4}, " +
            $"in={metadata.InputReportByteLength}, out={metadata.OutputReportByteLength}, " +
            $"feature={metadata.FeatureReportByteLength}, access={metadata.Access}");
    }

    private static string DescribeUnmatchedPath(string path)
    {
        var parts = path.Split('#');
        var description = parts.Length > 1 ? parts[1] : "<redacted>";
        var revision = description.IndexOf("_REV&", StringComparison.OrdinalIgnoreCase);
        return revision >= 0 ? description[..revision] : description;
    }

    private static InterfaceProbe ProbeInterfaceMetadata(string path)
    {
        var queryHandle = NativeMethods.CreateFile(path, 0, NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE, IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero);
        if (queryHandle == NativeMethods.INVALID_HANDLE_VALUE)
        {
            return new InterfaceProbe(
                false, 0, 0, 0, 0, 0, DeviceAccessState.BusyOrUnavailable, 0, 0, string.Empty, string.Empty);
        }

        ushort inputReportByteLength = 0;
        ushort outputReportByteLength = 0;
        ushort featureReportByteLength = 0;
        ushort usagePage = 0;
        ushort usage = 0;
        ushort vendorId = 0;
        ushort productId = 0;
        var manufacturer = string.Empty;
        var product = string.Empty;
        try
        {
            var attributes = new NativeMethods.HidAttributes
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.HidAttributes>(),
            };
            if (NativeMethods.HidD_GetAttributes(queryHandle, ref attributes))
            {
                vendorId = attributes.VendorId;
                productId = attributes.ProductId;
            }

            manufacturer = ReadHidString(queryHandle, NativeMethods.HidD_GetManufacturerString);
            product = ReadHidString(queryHandle, NativeMethods.HidD_GetProductString);

            if (NativeMethods.HidD_GetPreparsedData(queryHandle, out var preparsedData))
            {
                try
                {
                    if (NativeMethods.HidP_GetCaps(preparsedData, out var caps) >= 0)
                    {
                        featureReportByteLength = caps.FeatureReportByteLength;
                        inputReportByteLength = caps.InputReportByteLength;
                        outputReportByteLength = caps.OutputReportByteLength;
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

        var controlHandle = NativeMethods.CreateFile(path, NativeMethods.GENERIC_WRITE, NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE, IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero);
        if (controlHandle == NativeMethods.INVALID_HANDLE_VALUE)
        {
            return new InterfaceProbe(false, inputReportByteLength, outputReportByteLength,
                featureReportByteLength, usagePage, usage, DeviceAccessState.BusyOrUnavailable,
                vendorId, productId, manufacturer, product);
        }

        NativeMethods.CloseHandle(controlHandle);
        return new InterfaceProbe(false, inputReportByteLength, outputReportByteLength,
            featureReportByteLength, usagePage, usage, DeviceAccessState.Available,
            vendorId, productId, manufacturer, product);
    }

    private static string ReadHidString(
        IntPtr handle,
        Func<IntPtr, byte[], int, bool> reader)
    {
        var buffer = new byte[256];
        if (!reader(handle, buffer, buffer.Length))
        {
            return string.Empty;
        }

        var text = Encoding.Unicode.GetString(buffer);
        var terminator = text.IndexOf('\0');
        return (terminator >= 0 ? text[..terminator] : text).Trim();
    }

    internal static bool MatchesCollection(
        RazerHidCollection collection,
        ushort usagePage,
        ushort usage,
        ushort featureReportByteLength) =>
        usagePage == collection.UsagePage &&
        usage == collection.Usage &&
        featureReportByteLength == collection.FeatureReportLength;

    private sealed record InterfaceProbe(
        bool IsControlChannel,
        ushort InputReportByteLength,
        ushort OutputReportByteLength,
        ushort FeatureReportByteLength,
        ushort UsagePage,
        ushort Usage,
        DeviceAccessState Access,
        ushort VendorId,
        ushort ProductId,
        string Manufacturer,
        string Product);

    internal sealed record HidInterfaceDescriptor(
        string DevicePath,
        string PhysicalDeviceKey,
        ushort VendorId,
        ushort ProductId,
        ushort InputReportByteLength,
        ushort OutputReportByteLength,
        ushort FeatureReportByteLength,
        ushort UsagePage,
        ushort Usage,
        DeviceAccessState Access,
        string ProductName);

    internal static string GetPhysicalDeviceKey(string devicePath)
    {
        return GetPhysicalDeviceKey(devicePath, null);
    }

    internal static string GetPhysicalDeviceKey(string devicePath, Guid? containerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);
        if (containerId is { } value && value != Guid.Empty)
        {
            return $"container:{value:D}";
        }
        var collection = devicePath.LastIndexOf("&col", StringComparison.OrdinalIgnoreCase);
        if (collection < 0)
        {
            return devicePath;
        }
        var instance = devicePath.IndexOf('#', collection);
        return instance >= 0 ? devicePath.Remove(collection, instance - collection) : devicePath;
    }

    private static Guid? TryGetContainerId(
        IntPtr deviceInfoSet,
        ref NativeMethods.SpDevinfoData deviceInfo)
    {
        var propertyKey = NativeMethods.DevpkeyDeviceContainerId;
        var buffer = new byte[16];
        if (!NativeMethods.SetupDiGetDeviceProperty(
            deviceInfoSet,
            ref deviceInfo,
            ref propertyKey,
            out var propertyType,
            buffer,
            (uint)buffer.Length,
            out var requiredSize,
            0) ||
            propertyType != NativeMethods.DevpropTypeGuid ||
            requiredSize != buffer.Length)
        {
            return null;
        }
        return new Guid(buffer);
    }

    private static class NativeMethods
    {
        internal const int DIGCF_PRESENT = 0x00000002;
        internal const int DIGCF_DEVICEINTERFACE = 0x00000010;
        internal const int ERROR_NO_MORE_ITEMS = 259;
        internal const uint DevpropTypeGuid = 0x0000000D;
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
        internal struct SpDevinfoData
        {
            internal int CbSize;
            internal Guid ClassGuid;
            internal uint DevInst;
            internal IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Devpropkey
        {
            internal Guid Fmtid;
            internal uint Pid;

            internal Devpropkey(Guid fmtid, uint pid)
            {
                Fmtid = fmtid;
                Pid = pid;
            }
        }

        internal static readonly Devpropkey DevpkeyDeviceContainerId =
            new(new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"), 2);

        [StructLayout(LayoutKind.Sequential)]
        internal struct HidAttributes
        {
            internal uint Size;
            internal ushort VendorId;
            internal ushort ProductId;
            internal ushort VersionNumber;
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
        internal static extern bool HidD_GetAttributes(IntPtr hidDeviceObject, ref HidAttributes attributes);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool HidD_GetPreparsedData(IntPtr hidDeviceObject, out IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool HidD_GetManufacturerString(IntPtr hidDeviceObject, [Out] byte[] buffer, int bufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool HidD_GetProductString(IntPtr hidDeviceObject, [Out] byte[] buffer, int bufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool HidD_GetSerialNumberString(IntPtr hidDeviceObject, [Out] byte[] buffer, int bufferLength);

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

        [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiGetDeviceInterfaceDetail(
            IntPtr deviceInfoSet,
            ref SpDeviceInterfaceData deviceInterfaceData,
            IntPtr deviceInterfaceDetailData,
            int deviceInterfaceDetailDataSize,
            out int requiredSize,
            ref SpDevinfoData deviceInfoData);

        [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDevicePropertyW", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiGetDeviceProperty(
            IntPtr deviceInfoSet,
            ref SpDevinfoData deviceInfoData,
            ref Devpropkey propertyKey,
            out uint propertyType,
            [Out] byte[] propertyBuffer,
            uint propertyBufferSize,
            out uint requiredSize,
            uint flags);

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
