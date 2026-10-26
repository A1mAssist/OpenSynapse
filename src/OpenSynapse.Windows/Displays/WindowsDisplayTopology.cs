using System.ComponentModel;
using System.Runtime.InteropServices;

namespace OpenSynapse.Windows.Displays;

internal sealed record WindowsDisplayTopologySnapshot(
    bool IsKnown,
    bool HasActiveInternalDisplay,
    bool InternalDisplayRouteKnown,
    string? InternalAdapterName,
    bool HasExternalDisplay)
{
    public static WindowsDisplayTopologySnapshot Unavailable { get; } =
        new(false, false, false, null, false);
}

internal static class WindowsDisplayTopology
{
    public static WindowsDisplayTopologySnapshot Read()
    {
        try
        {
            var api = new WindowsDisplayApi();
            var activePaths = api.QueryActivePaths()
                .Where(path => path.IsActive && path.TargetAvailable)
                .ToArray();
            var internalPaths = activePaths
                .Where(path => IsInternal(path.OutputTechnology))
                .ToArray();
            var hasInternal = internalPaths.Length > 0;
            var hasExternal = activePaths.Any(path => !IsInternal(path.OutputTechnology));
            var routeKnown = false;
            string? internalAdapterName = null;

            if (internalPaths.Length == 1)
            {
                try
                {
                    var sourceName = api.GetSourceName(
                        internalPaths[0].SourceAdapterId,
                        internalPaths[0].SourceId);
                    internalAdapterName = FindAdapterName(sourceName);
                    routeKnown = !string.IsNullOrWhiteSpace(internalAdapterName);
                }
                catch (Exception exception) when (
                    exception is Win32Exception or InvalidOperationException)
                {
                    // The topology itself is still usable; only the route is unknown.
                }
            }

            return new WindowsDisplayTopologySnapshot(
                IsKnown: true,
                HasActiveInternalDisplay: hasInternal,
                InternalDisplayRouteKnown: routeKnown,
                InternalAdapterName: internalAdapterName,
                HasExternalDisplay: hasExternal);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return WindowsDisplayTopologySnapshot.Unavailable;
        }
    }

    public static bool IsDiscreteAdapter(string? adapterName, string discreteAdapterName)
    {
        if (string.IsNullOrWhiteSpace(adapterName))
        {
            return false;
        }

        return adapterName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
               adapterName.Contains(discreteAdapterName, StringComparison.OrdinalIgnoreCase) ||
               discreteAdapterName.Contains(adapterName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInternal(uint outputTechnology) => outputTechnology is
        WindowsDisplayApi.OutputTechnologyLvds or
        WindowsDisplayApi.OutputTechnologyDisplayPortEmbedded or
        WindowsDisplayApi.OutputTechnologyUdiEmbedded or
        WindowsDisplayApi.OutputTechnologyInternal;

    private static string? FindAdapterName(string sourceName)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            return null;
        }

        for (uint index = 0; ; index++)
        {
            var device = CreateDisplayDevice();
            if (!NativeMethods.EnumDisplayDevices(null, index, ref device, 0))
            {
                return null;
            }

            if (StringComparer.OrdinalIgnoreCase.Equals(device.DeviceName, sourceName))
            {
                return string.IsNullOrWhiteSpace(device.DeviceString)
                    ? null
                    : device.DeviceString.Trim();
            }
        }
    }

    private static DisplayDevice CreateDisplayDevice() => new()
    {
        Size = Marshal.SizeOf<DisplayDevice>(),
        DeviceName = string.Empty,
        DeviceString = string.Empty,
        DeviceId = string.Empty,
        DeviceKey = string.Empty,
    };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public uint StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumDisplayDevices(
            string? device,
            uint deviceIndex,
            ref DisplayDevice displayDevice,
            uint flags);
    }
}