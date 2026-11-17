using System.Collections.Concurrent;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidWorkspace
{
    // A device whose mode was already repaired must not be rewritten on every device-tree
    // change; three HID round trips at the 31 ms device wait is not free.
    private readonly ConcurrentDictionary<string, byte> _driverModeChecked =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Re-asserts driver mode on the readout heartbeat. SyncAsync throttles the
    /// actual device read to once per 30 seconds.
    /// </summary>
    internal Task KeepAliveDriverModeAsync(CancellationToken cancellationToken) =>
        SyncPrivateKeyDriverModeAsync(cancellationToken);

    private void PruneDriverModeChecked(IReadOnlyList<RazerHidDeviceConnection> connections)
    {
        var presentInstances = connections
            .Select(static connection => connection.InstanceId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var instanceId in _driverModeChecked.Keys)
        {
            if (!presentInstances.Contains(instanceId))
            {
                _driverModeChecked.TryRemove(instanceId, out _);
            }
        }
    }
}
