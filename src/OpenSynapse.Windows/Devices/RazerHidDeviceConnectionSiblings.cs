namespace OpenSynapse.Windows.Devices;

/// <summary>
/// One physical keyboard or mouse publishes a wired and a 2.4 GHz product id. When the
/// cable is connected both enumerate, so the device list collapses a genuine pair.
/// </summary>
internal static class RazerHidDeviceConnectionSiblings
{
    public static RazerHidDeviceConnection[] Collapse(RazerHidDeviceConnection[] connections) =>
        connections
            .GroupBy(
                connection => BaseDeviceName(connection.Definition.DisplayName),
                StringComparer.OrdinalIgnoreCase)
            .SelectMany(group =>
            {
                // Only a genuine wired/wireless pair collapses; two identical units of the
                // same model are separate devices and must both stay.
                var wired = group.Any(connection =>
                    connection.Definition.DisplayName.EndsWith(" (Wired)", StringComparison.OrdinalIgnoreCase));
                var wireless = group.Any(connection =>
                    connection.Definition.DisplayName.EndsWith(" (Wireless)", StringComparison.OrdinalIgnoreCase));
                return wired && wireless
                    ? new[] { group.FirstOrDefault(connection => connection.IsReady) ?? group.First() }
                    : group.ToArray();
            })
            .ToArray();

    private static string BaseDeviceName(string displayName) =>
        displayName.EndsWith(" (Wired)", StringComparison.OrdinalIgnoreCase)
            ? displayName[..^" (Wired)".Length]
            : displayName.EndsWith(" (Wireless)", StringComparison.OrdinalIgnoreCase)
                ? displayName[..^" (Wireless)".Length]
                : displayName;
}
