using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

/// <summary>
/// Keeps software frame-pump view models alive between applies. Native effects are
/// one-shot; only a running frame loop needs an owner that outlives the refresh.
/// </summary>
internal sealed class RazerHidSoftwareLightingOwner : IAsyncDisposable
{
    private readonly Dictionary<string, RazerHidDeviceViewModel> _devices =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<RazerHidDeviceConnection, RazerHidDeviceViewModel> _create;

    internal RazerHidSoftwareLightingOwner(
        Func<RazerHidDeviceConnection, RazerHidDeviceViewModel> create) =>
        _create = create ?? throw new ArgumentNullException(nameof(create));

    internal RazerHidDeviceViewModel? Take(string instanceId) =>
        _devices.Remove(instanceId, out var device) ? device : null;

    internal async Task RestoreAsync(
        IReadOnlyList<RazerHidDeviceConnection> connections,
        RazerHidDeviceViewModel? selected,
        CancellationToken cancellationToken)
    {
        foreach (var connection in connections)
        {
            if (!connection.IsReady)
            {
                continue;
            }

            RazerHidDeviceViewModel device;
            if (selected is { } current &&
                StringComparer.OrdinalIgnoreCase.Equals(current.InstanceId, connection.InstanceId))
            {
                device = current;
                if (_devices.Remove(connection.InstanceId, out var duplicate))
                {
                    await duplicate.DisposeAsync().ConfigureAwait(false);
                }
            }
            else if (_devices.TryGetValue(connection.InstanceId, out var cached))
            {
                device = cached;
            }
            else
            {
                device = _create(connection);
            }

            await device.ApplyConfiguredLightingAsync(cancellationToken).ConfigureAwait(false);
            if (ReferenceEquals(device, selected))
            {
                continue;
            }

            if (device.HasRunningSoftwareLighting)
            {
                _devices[connection.InstanceId] = device;
            }
            else
            {
                _devices.Remove(connection.InstanceId);
                await device.DisposeAsync().ConfigureAwait(false);
            }
        }

        await PruneAsync(connections, cancellationToken).ConfigureAwait(false);
    }

    internal async Task PruneAsync(
        IReadOnlyList<RazerHidDeviceConnection> connections,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var livePaths = connections
            .Where(connection => connection.IsReady)
            .ToDictionary(connection => connection.InstanceId, connection => connection.DevicePath,
                StringComparer.OrdinalIgnoreCase);
        foreach (var (instanceId, device) in _devices.ToArray())
        {
            if (livePaths.TryGetValue(instanceId, out var devicePath) &&
                StringComparer.OrdinalIgnoreCase.Equals(devicePath, device.Connection.DevicePath))
            {
                continue;
            }

            _devices.Remove(instanceId);
            await device.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var device in _devices.Values)
        {
            await device.DisposeAsync().ConfigureAwait(false);
        }
        _devices.Clear();
    }
}
