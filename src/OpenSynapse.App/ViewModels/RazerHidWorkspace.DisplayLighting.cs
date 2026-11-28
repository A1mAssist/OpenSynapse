using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidWorkspace
{
    private sealed record DisplayZoneSnapshot(RazerHidLedZone Zone, bool? Enabled);

    private sealed record DisplayDeviceSnapshot(
        ushort ProductId,
        IReadOnlyList<DisplayZoneSnapshot> Zones,
        RazerHidLightingBaseline? Baseline,
        bool NeedsFallback,
        bool HadSoftwareLighting);

    private readonly Dictionary<string, DisplayDeviceSnapshot> _displayLightingSnapshots =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _displayLightingGate = new(1, 1);
    private bool _displayLightingAvailable = true;

    internal async Task SetDisplayLightingAvailableAsync(bool available)
    {
        await _displayLightingGate.WaitAsync().ConfigureAwait(true);
        try
        {
            _displayLightingAvailable = available;
            if (available)
            {
                await RestoreDisplayLightingAsync().ConfigureAwait(false);
            }
            else
            {
                await TurnOffDisplayLightingAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _displayLightingGate.Release();
        }
    }

    private async Task TurnOffDisplayLightingAsync()
    {
        var service = _deviceService;
        if (service is null)
        {
            return;
        }

        var connections = CurrentConnections;
        var hadSoftwareLighting = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var selected = SelectedDevice;
        foreach (var connection in connections)
        {
            hadSoftwareLighting[connection.InstanceId] =
                _softwareLightingOwner.HasRunning(connection.InstanceId) ||
                (selected is not null &&
                 StringComparer.OrdinalIgnoreCase.Equals(
                     selected.InstanceId, connection.InstanceId) &&
                 selected.HasRunningSoftwareLighting);
        }

        try
        {
            using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await _softwareLightingOwner.StopAllAsync(stopTimeout.Token, restore: false)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _diagnosticLog.TryWrite(
                "lighting-display-razorhid",
                $"software runtime stop failed: {exception.Message}");
        }
        catch (OperationCanceledException)
        {
            _diagnosticLog.TryWrite(
                "lighting-display-razorhid",
                "software runtime stop timed out.");
        }

        if (selected is { } selectedDevice)
        {
            try
            {
                using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await selectedDevice.StopLightingRuntimeAsync(stopTimeout.Token, restore: false)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _diagnosticLog.TryWrite(
                    "lighting-display-razorhid",
                    $"{selectedDevice.InstanceId}: selected runtime stop failed: {exception.Message}");
            }
            catch (OperationCanceledException)
            {
                _diagnosticLog.TryWrite(
                    "lighting-display-razorhid",
                    $"{selectedDevice.InstanceId}: selected runtime stop timed out.");
            }
        }

        _displayLightingSnapshots.Clear();
        var turnedOff = 0;
        var failed = 0;
        foreach (var connection in connections.Where(connection =>
                     connection.IsReady &&
                     connection.Definition.Category != DeviceCategory.Laptop))
        {
            var storage = connection.Definition.DefaultStorage;
            var needsBaseline = connection.LightingZones.Any(pair =>
                !pair.Value.CanWriteState &&
                pair.Value.LightingEffects.Contains(RazerHidLightingEffect.Off));

            RazerHidLightingBaseline? baseline = null;
            if (needsBaseline)
            {
                try
                {
                    using var captureTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(700));
                    baseline = await RazerHidLightingBaseline.CaptureAsync(service, connection, captureTimeout.Token)
                        .ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    _diagnosticLog.TryWrite(
                        "lighting-display-razorhid",
                        $"1532:{connection.Definition.ProductId:X4} baseline capture failed: {exception.Message}");
                }
            }

            var zones = new List<DisplayZoneSnapshot>();
            var needsFallback = false;
            foreach (var (zone, capabilities) in connection.LightingZones)
            {
                bool? enabled = null;
                if (capabilities.CanWriteState && capabilities.CanReadState)
                {
                    try
                    {
                        using var captureTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                        enabled = await service.GetLedStateAsync(connection, storage, zone, captureTimeout.Token)
                            .ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        _diagnosticLog.TryWrite("lighting-display-razorhid",
                            $"1532:{connection.Definition.ProductId:X4} {zone} state capture failed: {exception.Message}");
                    }
                }
                // A slow or unsupported read must not consume the budget for the Off write.
                using var writeTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(1500));
                var token = writeTimeout.Token;
                try
                {
                    if (capabilities.CanWriteState)
                    {
                        await service.SetLedStateAsync(
                                connection, storage, zone, false, token)
                            .ConfigureAwait(false);
                        zones.Add(new DisplayZoneSnapshot(zone, enabled));
                        needsFallback |= enabled is null;
                        turnedOff++;
                    }
                    else if (capabilities.LightingEffects.Contains(RazerHidLightingEffect.Off))
                    {
                        await service.SetLightingAsync(
                                connection,
                                new RazerHidLightingSettings(
                                    RazerHidLightingEffect.Off,
                                    Storage: storage,
                                    Zone: zone),
                                token)
                            .ConfigureAwait(false);
                        zones.Add(new DisplayZoneSnapshot(zone, null));
                        needsFallback |= baseline?.CanRestoreEffectFor(zone) != true;
                        turnedOff++;
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    failed++;
                    _diagnosticLog.TryWrite(
                        "lighting-display-razorhid",
                        $"1532:{connection.Definition.ProductId:X4} {zone} off failed: {exception.Message}");
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    failed++;
                    _diagnosticLog.TryWrite(
                        "lighting-display-razorhid",
                        $"1532:{connection.Definition.ProductId:X4} {zone} off timed out.");
                }
            }

            if (zones.Count > 0 ||
                baseline is not null ||
                hadSoftwareLighting.GetValueOrDefault(connection.InstanceId))
            {
                _displayLightingSnapshots[connection.InstanceId] = new DisplayDeviceSnapshot(
                    connection.Definition.ProductId,
                    zones,
                    baseline,
                    needsFallback,
                    hadSoftwareLighting.GetValueOrDefault(connection.InstanceId));
            }
        }

        _diagnosticLog.TryWrite(
            "lighting-display-razorhid",
            $"display-off: razorhid {turnedOff} zone(s) off, {failed} failure(s), " +
            $"{_displayLightingSnapshots.Count} device snapshot(s).");
    }

    private async Task RestoreDisplayLightingAsync()
    {
        var service = _deviceService;
        if (service is null || _displayLightingSnapshots.Count == 0)
        {
            _displayLightingSnapshots.Clear();
            return;
        }

        var connections = CurrentConnections;
        var restored = 0;
        var failed = 0;
        var missing = 0;
        var configuredRestore = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stateRestores = new List<(RazerHidDeviceConnection Connection, DisplayZoneSnapshot Zone)>();
        foreach (var (instanceId, snapshot) in _displayLightingSnapshots.ToArray())
        {
            if (snapshot.HadSoftwareLighting || snapshot.NeedsFallback)
                configuredRestore.Add(instanceId);
            var connection = connections.FirstOrDefault(candidate =>
                candidate.IsReady &&
                StringComparer.OrdinalIgnoreCase.Equals(candidate.InstanceId, instanceId));
            if (connection is null)
            {
                missing++;
                continue;
            }

            using var deviceTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(1500));
            var token = deviceTimeout.Token;
            foreach (var zone in snapshot.Zones.Where(item => item.Enabled is not null))
                stateRestores.Add((connection, zone));

            if (snapshot.Baseline is not null)
            {
                try
                {
                    var failures = await snapshot.Baseline
                        .RestoreAsync(service, connection, token)
                        .ConfigureAwait(false);
                    if (failures.Count == 0)
                    {
                        restored++;
                    }
                    else
                    {
                        configuredRestore.Add(instanceId);
                        failed += failures.Count;
                        foreach (var failure in failures)
                        {
                            _diagnosticLog.TryWrite(
                                "lighting-display-razorhid",
                                $"1532:{snapshot.ProductId:X4} baseline restore failed: {failure.Message}");
                        }
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    configuredRestore.Add(instanceId);
                    failed++;
                    _diagnosticLog.TryWrite(
                        "lighting-display-razorhid",
                        $"1532:{snapshot.ProductId:X4} baseline restore failed: {exception.Message}");
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    configuredRestore.Add(instanceId);
                    failed++;
                    _diagnosticLog.TryWrite(
                        "lighting-display-razorhid",
                        $"1532:{snapshot.ProductId:X4} baseline restore timed out.");
                }
            }
        }

        _displayLightingSnapshots.Clear();
        if (configuredRestore.Count > 0)
        {
            try
            {
                using var restoreTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2 * configuredRestore.Count));
                await RunOnDispatcherAsync(async () =>
                    failed += (await _softwareLightingOwner.RestoreAsync(
                        connections, SelectedDevice, restoreTimeout.Token, configuredRestore, requireRestore: true)).Count)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _diagnosticLog.TryWrite(
                    "lighting-display-razorhid",
                    $"software lighting restore failed: {exception.Message}");
            }
            catch (OperationCanceledException)
            {
                _diagnosticLog.TryWrite(
                    "lighting-display-razorhid",
                    "software lighting restore timed out.");
            }
        }

        // Apply captured switches last so replaying the card cannot turn on a previously disabled zone.
        foreach (var (connection, zone) in stateRestores)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(1500));
                await service.SetLedStateAsync(connection, connection.Definition.DefaultStorage,
                    zone.Zone, zone.Enabled!.Value, timeout.Token).ConfigureAwait(false);
                restored++;
            }
            catch (Exception exception)
            {
                failed++;
                _diagnosticLog.TryWrite("lighting-display-razorhid",
                    $"1532:{connection.Definition.ProductId:X4} {zone.Zone} restore failed: {exception.Message}");
            }
        }

        _diagnosticLog.TryWrite(
            "lighting-display-razorhid",
            $"display-on: razorhid {restored} restored, {failed} failure(s), {missing} missing device(s).");
    }

    private Task RunOnDispatcherAsync(Func<Task> action)
    {
        if (_dispatcherQueue is null || _dispatcherQueue.HasThreadAccess)
        {
            return action();
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    await action();
                    completion.TrySetResult();
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            }))
        {
            completion.TrySetException(
                new InvalidOperationException("The main window dispatcher is unavailable."));
        }

        return completion.Task;
    }
}
