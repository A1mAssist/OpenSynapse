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

    internal bool HasRunning(string instanceId) =>
        _devices.TryGetValue(instanceId, out var device) && device.HasRunningSoftwareLighting;

    internal async Task<IReadOnlySet<string>> RestoreAsync(
        IReadOnlyList<RazerHidDeviceConnection> connections,
        RazerHidDeviceViewModel? selected,
        CancellationToken cancellationToken,
        IReadOnlySet<string>? instanceIds = null,
        bool requireRestore = false)
    {
        var failed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var connection in connections)
        {
            if (!connection.IsReady ||
                (instanceIds is not null && !instanceIds.Contains(connection.InstanceId)))
            {
                continue;
            }

            RazerHidDeviceViewModel device;
            if (selected is { } current &&
                StringComparer.OrdinalIgnoreCase.Equals(current.InstanceId, connection.InstanceId) &&
                StringComparer.OrdinalIgnoreCase.Equals(current.Connection.DevicePath, connection.DevicePath))
            {
                device = current;
                if (_devices.Remove(connection.InstanceId, out var duplicate))
                {
                    await duplicate.DisposeAsync().ConfigureAwait(true);
                }
            }
            else if (_devices.TryGetValue(connection.InstanceId, out var cached) &&
                StringComparer.OrdinalIgnoreCase.Equals(cached.Connection.DevicePath, connection.DevicePath))
            {
                device = cached;
            }
            else
            {
                if (_devices.Remove(connection.InstanceId, out var stale))
                    await stale.DisposeAsync().ConfigureAwait(true);
                device = _create(connection);
            }

            // A failed Chroma write may have raised this flag; the connection is
            // still current, so give the saved profile one chance to land.
            device.ClearRequiresRescan();
            try
            {
                if (!await (requireRestore
                        ? device.RestoreConfiguredLightingAfterExternalAsync(cancellationToken)
                        : device.ApplyConfiguredLightingAsync(cancellationToken)).ConfigureAwait(true))
                {
                    failed.Add(connection.InstanceId);
                }
            }
            finally
            {
                if (!ReferenceEquals(device, selected))
                {
                    if (device.HasRunningSoftwareLighting)
                        _devices[connection.InstanceId] = device;
                    else
                    {
                        _devices.Remove(connection.InstanceId);
                        await device.DisposeAsync().ConfigureAwait(true);
                    }
                }
            }
        }

        await PruneAsync(connections, cancellationToken, instanceIds).ConfigureAwait(false);
        return failed;
    }

    internal async Task PruneAsync(
        IReadOnlyList<RazerHidDeviceConnection> connections,
        CancellationToken cancellationToken,
        IReadOnlySet<string>? instanceIds = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var livePaths = connections
            .Where(connection => connection.IsReady)
            .ToDictionary(connection => connection.InstanceId, connection => connection.DevicePath,
                StringComparer.OrdinalIgnoreCase);
        foreach (var (instanceId, device) in _devices.ToArray())
        {
            if (instanceIds is not null && !instanceIds.Contains(instanceId)) continue;
            if (livePaths.TryGetValue(instanceId, out var devicePath) &&
                StringComparer.OrdinalIgnoreCase.Equals(devicePath, device.Connection.DevicePath))
            {
                continue;
            }

            _devices.Remove(instanceId);
            await device.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Stops every frame pump without disposing its view model. Shutdown needs the
    /// last frame to stop before the firmware Off write can take effect.
    /// </summary>
    internal async Task StopAllAsync(
        CancellationToken cancellationToken,
        bool restore = true,
        IReadOnlySet<string>? instanceIds = null)
    {
        foreach (var device in _devices.Values.ToArray())
        {
            if (instanceIds is not null && !instanceIds.Contains(device.InstanceId)) continue;
            await device.StopLightingRuntimeAsync(cancellationToken, restore).ConfigureAwait(true);
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
