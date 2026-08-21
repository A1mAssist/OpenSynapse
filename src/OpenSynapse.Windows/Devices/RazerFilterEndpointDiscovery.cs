using System.ComponentModel;
using System.Runtime.InteropServices;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Discovers the RZCONTROL interface used by Product 710's installed Razer
/// filter driver. Discovery does not open the endpoint or change driver state.
/// </summary>
public static class RazerFilterEndpointDiscovery
{
    internal static readonly Guid InterfaceClassGuid =
        new("E3BE005D-D130-4910-88FF-09AE02F680E9");

    private const string Product710PathFragment =
        "RZCONTROL#VID_1532&PID_02C6&MI_00#";

    public static ValueTask<string?> DiscoverProduct710Async(
        CancellationToken cancellationToken = default) =>
        DiscoverProduct710Async(null, cancellationToken);

    public static ValueTask<string?> DiscoverProduct710Async(
        Guid expectedContainerId,
        CancellationToken cancellationToken = default)
    {
        if (expectedContainerId == Guid.Empty)
        {
            throw new ArgumentException(
                "Product 710 ContainerId cannot be empty.",
                nameof(expectedContainerId));
        }

        return DiscoverProduct710Async((Guid?)expectedContainerId, cancellationToken);
    }

    public static ValueTask<string?> DiscoverProduct710ForFeatureAsync(
        string featureDevicePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(featureDevicePath);
        return new(Task.Run(() =>
        {
            NativeMethods.HidD_GetHidGuid(out var hidGuid);
            var containerIds = EnumerateCandidates(hidGuid, cancellationToken)
                .Where(candidate => string.Equals(
                    candidate.Path,
                    featureDevicePath,
                    StringComparison.OrdinalIgnoreCase))
                .Select(static candidate => candidate.ContainerId)
                .Distinct()
                .ToArray();
            return containerIds.Length == 1
                ? SelectProduct710Endpoint(
                    EnumerateCandidates(InterfaceClassGuid, cancellationToken),
                    containerIds[0])
                : null;
        }, cancellationToken));
    }

    internal static string? SelectProduct710Endpoint(
        IEnumerable<RazerFilterEndpointCandidate> candidates,
        Guid? expectedContainerId = null)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (expectedContainerId == Guid.Empty)
        {
            throw new ArgumentException(
                "Product 710 ContainerId cannot be empty.",
                nameof(expectedContainerId));
        }

        var matches = candidates
            .Where(static candidate =>
                candidate.ContainerId != Guid.Empty &&
                candidate.Path.Contains(
                    Product710PathFragment,
                    StringComparison.OrdinalIgnoreCase))
            .Where(candidate =>
                expectedContainerId is null ||
                candidate.ContainerId == expectedContainerId.Value)
            .Select(static candidate => candidate.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return matches.Length == 1 ? matches[0] : null;
    }

    private static ValueTask<string?> DiscoverProduct710Async(
        Guid? expectedContainerId,
        CancellationToken cancellationToken) =>
        new(Task.Run(
            () => SelectProduct710Endpoint(
                EnumerateCandidates(InterfaceClassGuid, cancellationToken),
                expectedContainerId),
            cancellationToken));

    private static IReadOnlyList<RazerFilterEndpointCandidate> EnumerateCandidates(
        Guid interfaceGuid,
        CancellationToken cancellationToken)
    {
        var candidates = new List<RazerFilterEndpointCandidate>();
        var deviceInfoSet = NativeMethods.SetupDiGetClassDevs(
            ref interfaceGuid,
            IntPtr.Zero,
            IntPtr.Zero,
            NativeMethods.DigcfPresent | NativeMethods.DigcfDeviceInterface);
        if (deviceInfoSet == NativeMethods.InvalidHandleValue)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Could not enumerate Razer filter endpoints.");
        }

        try
        {
            for (uint index = 0; ; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var interfaceData = new NativeMethods.SpDeviceInterfaceData
                {
                    Size = Marshal.SizeOf<NativeMethods.SpDeviceInterfaceData>(),
                };
                if (!NativeMethods.SetupDiEnumDeviceInterfaces(
                        deviceInfoSet,
                        IntPtr.Zero,
                        ref interfaceGuid,
                        index,
                        ref interfaceData))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == NativeMethods.ErrorNoMoreItems)
                    {
                        break;
                    }

                    throw new Win32Exception(error, "Could not enumerate a Razer filter endpoint.");
                }

                var candidate = ReadCandidate(deviceInfoSet, ref interfaceData);
                if (candidate is not null)
                {
                    candidates.Add(candidate.Value);
                }
            }
        }
        finally
        {
            NativeMethods.SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }

        return candidates;
    }

    private static RazerFilterEndpointCandidate? ReadCandidate(
        IntPtr deviceInfoSet,
        ref NativeMethods.SpDeviceInterfaceData interfaceData)
    {
        var deviceInfoData = new NativeMethods.SpDevinfoData
        {
            Size = Marshal.SizeOf<NativeMethods.SpDevinfoData>(),
        };
        NativeMethods.SetupDiGetDeviceInterfaceDetail(
            deviceInfoSet,
            ref interfaceData,
            IntPtr.Zero,
            0,
            out var requiredSize,
            ref deviceInfoData);
        var sizeError = Marshal.GetLastWin32Error();
        if (requiredSize <= 0 || sizeError != NativeMethods.ErrorInsufficientBuffer)
        {
            return null;
        }

        var detail = Marshal.AllocHGlobal(requiredSize);
        try
        {
            Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
            deviceInfoData.Size = Marshal.SizeOf<NativeMethods.SpDevinfoData>();
            if (!NativeMethods.SetupDiGetDeviceInterfaceDetail(
                    deviceInfoSet,
                    ref interfaceData,
                    detail,
                    requiredSize,
                    out _,
                    ref deviceInfoData))
            {
                return null;
            }

            var path = Marshal.PtrToStringUni(detail + 4);
            if (string.IsNullOrWhiteSpace(path) ||
                !TryGetContainerId(deviceInfoSet, ref deviceInfoData, out var containerId))
            {
                return null;
            }

            return new RazerFilterEndpointCandidate(path, containerId);
        }
        finally
        {
            Marshal.FreeHGlobal(detail);
        }
    }

    private static bool TryGetContainerId(
        IntPtr deviceInfoSet,
        ref NativeMethods.SpDevinfoData deviceInfoData,
        out Guid containerId)
    {
        containerId = Guid.Empty;
        var propertyKey = NativeMethods.DeviceContainerId;
        var size = Marshal.SizeOf<Guid>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!NativeMethods.SetupDiGetDeviceProperty(
                    deviceInfoSet,
                    ref deviceInfoData,
                    ref propertyKey,
                    out var propertyType,
                    buffer,
                    size,
                    out var requiredSize,
                    0) ||
                propertyType != NativeMethods.DevpropTypeGuid ||
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

    private static class NativeMethods
    {
        internal const int DigcfPresent = 0x00000002;
        internal const int DigcfDeviceInterface = 0x00000010;
        internal const int ErrorInsufficientBuffer = 122;
        internal const int ErrorNoMoreItems = 259;
        internal const uint DevpropTypeGuid = 0x0000000D;
        internal static readonly IntPtr InvalidHandleValue = new(-1);
        internal static readonly Devpropkey DeviceContainerId = new()
        {
            FormatId = new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"),
            PropertyId = 2,
        };

        [StructLayout(LayoutKind.Sequential)]
        internal struct SpDeviceInterfaceData
        {
            internal int Size;
            internal Guid InterfaceClassGuid;
            internal int Flags;
            internal IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct SpDevinfoData
        {
            internal int Size;
            internal Guid ClassGuid;
            internal uint DeviceInstance;
            internal IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Devpropkey
        {
            internal Guid FormatId;
            internal uint PropertyId;
        }

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern IntPtr SetupDiGetClassDevs(
            ref Guid classGuid,
            IntPtr enumerator,
            IntPtr parentWindow,
            int flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiEnumDeviceInterfaces(
            IntPtr deviceInfoSet,
            IntPtr deviceInfoData,
            ref Guid interfaceClassGuid,
            uint memberIndex,
            ref SpDeviceInterfaceData deviceInterfaceData);

        [DllImport(
            "setupapi.dll",
            EntryPoint = "SetupDiGetDeviceInterfaceDetailW",
            SetLastError = true,
            CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiGetDeviceInterfaceDetail(
            IntPtr deviceInfoSet,
            ref SpDeviceInterfaceData deviceInterfaceData,
            IntPtr deviceInterfaceDetailData,
            int deviceInterfaceDetailDataSize,
            out int requiredSize,
            ref SpDevinfoData deviceInfoData);

        [DllImport(
            "setupapi.dll",
            EntryPoint = "SetupDiGetDevicePropertyW",
            SetLastError = true,
            CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiGetDeviceProperty(
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
        internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("hid.dll")]
        internal static extern void HidD_GetHidGuid(out Guid hidGuid);
    }
}

internal readonly record struct RazerFilterEndpointCandidate(
    string Path,
    Guid ContainerId);
