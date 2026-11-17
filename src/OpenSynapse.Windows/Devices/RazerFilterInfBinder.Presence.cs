using Microsoft.Win32;

namespace OpenSynapse.Windows.Devices;

internal static partial class RazerFilterInfBinder
{
    internal static IReadOnlyList<string> FindInstances(
        IReadOnlyList<ushort> productIds,
        RegistryKey? enumRoot = null,
        IReadOnlySet<ushort>? presentProductIds = null,
        IReadOnlySet<string>? presentInstanceIds = null)
    {
        ArgumentNullException.ThrowIfNull(productIds);
        var ownsRoot = enumRoot is null;
        var root = enumRoot ?? Registry.LocalMachine.OpenSubKey(HidEnumPath);
        if (root is null)
        {
            return [];
        }

        try
        {
            var instances = new List<string>();
            foreach (var productId in productIds)
            {
                if (presentProductIds is not null && !presentProductIds.Contains(productId))
                {
                    continue;
                }

                foreach (var (deviceName, instanceName) in InstanceNames(root, productId))
                {
                    if (!IsPresentInstance(presentInstanceIds, deviceName, instanceName))
                    {
                        continue;
                    }

                    instances.Add(HidInstancePath(deviceName, instanceName));
                }
            }

            return instances;
        }
        finally
        {
            if (ownsRoot)
            {
                root.Dispose();
            }
        }
    }

    /// <summary>
    /// Keyboard collections that are present but still lack the RzDev filter. The
    /// count alone does not say which collection needs the repair.
    /// </summary>
    internal static IReadOnlyList<string> FindUnfilteredKeyboardInstances(
        IReadOnlyList<ushort> productIds,
        RegistryKey? enumRoot = null,
        IReadOnlySet<ushort>? presentProductIds = null,
        IReadOnlySet<(ushort ProductId, int InterfaceNumber)>? coveredInterfaces = null,
        RegistryKey? usbEnumRoot = null,
        IReadOnlySet<string>? presentInstanceIds = null)
    {
        ArgumentNullException.ThrowIfNull(productIds);
        var ownsRoot = enumRoot is null;
        var root = enumRoot ?? Registry.LocalMachine.OpenSubKey(HidEnumPath);
        if (root is null)
        {
            return [];
        }

        var usbRoot = usbEnumRoot;
        var ownsUsbRoot = false;
        if (usbRoot is null && enumRoot is null)
        {
            usbRoot = Registry.LocalMachine.OpenSubKey(UsbEnumPath);
            ownsUsbRoot = usbRoot is not null;
        }

        try
        {
            var instances = new List<string>();
            var filters = productIds.Select(static id => $"RzDev_{id:X4}").ToArray();
            foreach (var productId in productIds)
            {
                if (presentProductIds is not null && !presentProductIds.Contains(productId))
                {
                    continue;
                }

                var productHasCoverage = coveredInterfaces?.Any(
                    entry => entry.ProductId == productId) == true;
                foreach (var (deviceName, instanceName) in InstanceNames(root, productId))
                {
                    if (!IsPresentInstance(presentInstanceIds, deviceName, instanceName) ||
                        (productHasCoverage &&
                         TryGetInterfaceNumber(deviceName, out var interfaceNumber) &&
                         !coveredInterfaces!.Contains((productId, interfaceNumber))))
                    {
                        continue;
                    }

                    using var instance = root.OpenSubKey($@"{deviceName}\{instanceName}");
                    if (instance is null || !IsKeyboardInstance(instance) ||
                        filters.Any(filter => HasFilter(instance, filter) ||
                            (usbRoot is not null && HasLowerFilterOnParent(deviceName, filter, usbRoot))))
                    {
                        continue;
                    }

                    instances.Add(HidInstancePath(deviceName, instanceName));
                }
            }

            return instances;
        }
        finally
        {
            if (ownsRoot)
            {
                root.Dispose();
            }
            if (ownsUsbRoot)
            {
                usbRoot!.Dispose();
            }
        }
    }

    internal static string? TryGetHidRegistryInstanceId(string devicePath)
    {
        if (string.IsNullOrWhiteSpace(devicePath))
        {
            return null;
        }

        var parts = devicePath.Split('#');
        for (var index = 0; index + 2 < parts.Length; index++)
        {
            if (parts[index].EndsWith("hid", StringComparison.OrdinalIgnoreCase))
            {
                return HidInstancePath(parts[index + 1], parts[index + 2]);
            }
        }

        return null;
    }

    private static bool IsPresentInstance(
        IReadOnlySet<string>? presentInstanceIds,
        string deviceName,
        string instanceName) =>
        presentInstanceIds is null ||
        presentInstanceIds.Contains(HidInstancePath(deviceName, instanceName));

    private static string HidInstancePath(string deviceName, string instanceName) =>
        $@"HID\{deviceName}\{instanceName}";

    private static IEnumerable<(string DeviceName, string InstanceName)> InstanceNames(
        RegistryKey root,
        ushort productId)
    {
        // USB uses VID_1532&PID_xxxx; Bluetooth HID uses VID&021532_PID&xxxx
        // (USB vendor id) or VID&02068E_PID&xxxx (Razer's Bluetooth company id).
        var hex = productId.ToString("X4");
        var markers = new[]
        {
            $"VID_1532&PID_{hex}",
            $"VID&021532_PID&{hex}",
            $"VID&02068E_PID&{hex}",
        };
        foreach (var deviceName in root.GetSubKeyNames())
        {
            if (!markers.Any(marker => MatchesMarker(deviceName, marker)))
            {
                continue;
            }

            string[] instances;
            using (var device = root.OpenSubKey(deviceName))
            {
                if (device is null)
                {
                    continue;
                }

                instances = device.GetSubKeyNames();
            }

            foreach (var instanceName in instances)
            {
                yield return (deviceName, instanceName);
            }
        }
    }

    private static bool MatchesMarker(string deviceName, string marker)
    {
        var index = deviceName.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return index >= 0 &&
            (index + marker.Length == deviceName.Length ||
             deviceName[index + marker.Length] == '&');
    }
}
