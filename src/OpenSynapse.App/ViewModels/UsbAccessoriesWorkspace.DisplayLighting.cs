using System.Collections.Concurrent;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class UsbAccessoriesWorkspace
{
    private sealed record ChromaDisplaySnapshot(
        ushort ProductId,
        string DevicePath,
        RazerChromaEffect? Effect,
        IReadOnlyList<byte> Colors);

    private readonly ConcurrentDictionary<string, ChromaDisplaySnapshot> _chromaDisplaySnapshots =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _displayLightingGate = new(1, 1);
    private readonly HashSet<string> _headsetDisplayLightingOff = new(StringComparer.OrdinalIgnoreCase);

    [NotProfileScoped]
    internal async Task SetDisplayLightingAvailableAsync(bool available)
    {
        await _displayLightingGate.WaitAsync().ConfigureAwait(true);
        try
        {
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
        _chromaDisplaySnapshots.Clear();
        var connections = ChromaDevices.Select(row => row.Connection).ToArray();
        var turnedOff = 0;
        var failed = 0;
        await Task.WhenAll(connections.Select(async connection =>
        {
            try
            {
                using var timeout =
                    new CancellationTokenSource(TimeSpan.FromMilliseconds(1500));
                var service = CreateChromaDisplayService(connection);
                var state = await service.ReadStateAsync(timeout.Token).ConfigureAwait(false);
                RazerChromaEffect? effect = state.Effect is { } value && value != RazerChromaEffect.CustomFrame
                    ? value
                    : null;
                _chromaDisplaySnapshots[SnapshotKey(connection)] = new ChromaDisplaySnapshot(
                    connection.ProductId,
                    connection.DevicePath,
                    effect,
                    state.Colors.ToArray());
                if (effect is null)
                {
                    _diagnosticLog.TryWrite(
                        "lighting-display-chroma",
                        $"chroma {connection.ProductId:X4} has no restorable effect; turning off");
                }

                await service.TurnOffForShutdownAsync(timeout.Token).ConfigureAwait(false);
                Interlocked.Increment(ref turnedOff);
            }
            catch (Exception exception)
            {
                Interlocked.Increment(ref failed);
                _diagnosticLog.TryWrite(
                    "lighting-display-chroma",
                    $"chroma {connection.ProductId:X4} off failed: {exception.Message}");
            }
        })).ConfigureAwait(true);

        _headsetDisplayLightingOff.Clear();
        foreach (var connection in HeadsetAudioConnections)
        {
            try
            {
                using var headsetTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(1500));
                if (await WithHeadsetAsync(connection,
                        (headset, token) => headset.TurnOffLightingForShutdownAsync(token), headsetTimeout.Token)
                    .ConfigureAwait(true))
                {
                    _headsetDisplayLightingOff.Add(connection.DevicePath);
                }
            }
            catch (Exception exception)
            {
                _diagnosticLog.TryWrite("lighting-display-chroma",
                    $"headset {connection.ProductId:X4} off failed: {exception.Message}");
            }
        }

        _diagnosticLog.TryWrite(
            "lighting-display-chroma",
            $"display-off: chroma {turnedOff}/{connections.Length} off, {failed} failure(s).");
    }

    private async Task RestoreDisplayLightingAsync()
    {
        var snapshots = _chromaDisplaySnapshots.Values.ToArray();
        var connections = ChromaDevices.ToDictionary(
            row => SnapshotKey(row.Connection),
            row => row.Connection,
            StringComparer.OrdinalIgnoreCase);
        var restored = 0;
        var failed = 0;
        var missing = 0;
        await Task.WhenAll(snapshots.Select(async snapshot =>
        {
            if (!connections.TryGetValue(Key(snapshot), out var connection))
            {
                Interlocked.Increment(ref missing);
                return;
            }

            if (snapshot.Effect is not { } effect)
            {
                _diagnosticLog.TryWrite(
                    "lighting-display-chroma",
                    $"chroma {snapshot.ProductId:X4} has no saved effect; leaving profile state");
                return;
            }

            try
            {
                using var timeout =
                    new CancellationTokenSource(TimeSpan.FromMilliseconds(1500));
                await CreateChromaDisplayService(connection)
                    .SetEffectAsync(effect, snapshot.Colors, timeout.Token)
                    .ConfigureAwait(false);
                Interlocked.Increment(ref restored);
            }
            catch (Exception exception)
            {
                Interlocked.Increment(ref failed);
                _diagnosticLog.TryWrite(
                    "lighting-display-chroma",
                    $"chroma {snapshot.ProductId:X4} restore failed: {exception.Message}");
            }
        })).ConfigureAwait(true);
        _chromaDisplaySnapshots.Clear();

        foreach (var connection in HeadsetAudioConnections.Where(
                     item => _headsetDisplayLightingOff.Contains(item.DevicePath)))
        {
            try
            {
                using var headsetTimeout =
                    new CancellationTokenSource(TimeSpan.FromMilliseconds(1500));
                await WithHeadsetAsync(connection, async (headset, token) =>
                {
                    await headset.ApplyPersistedProfileAsync(
                            PowerSourceKind.Unknown, token)
                        .ConfigureAwait(true);
                    return true;
                }, headsetTimeout.Token).ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                _diagnosticLog.TryWrite(
                    "lighting-display-chroma",
                    $"headset {connection.ProductId:X4} restore failed: {exception.Message}");
            }
        }
        _headsetDisplayLightingOff.Clear();

        _diagnosticLog.TryWrite(
            "lighting-display-chroma",
            $"display-on: chroma {restored} restored, {failed} failure(s), {missing} missing device(s).");
    }

    private RazerChromaStandardService CreateChromaDisplayService(
        RazerChromaDeviceConnection connection) =>
        new(
            new RazerFeatureTransport(),
            connection.DevicePath,
            TimeSpan.FromMilliseconds(20),
            message => _diagnosticLog.TryWrite("chroma", message),
            connection.Channel,
            connection.ProductId);

    private async Task<bool> WithHeadsetAsync(
        RazerHeadsetAudioConnection connection,
        Func<RazerHeadsetAudioViewModel, CancellationToken, Task<bool>> action,
        CancellationToken cancellationToken = default)
    {
        if (!connection.HasControlInterface ||
            !_headsetSessions.TryGetValue(connection.DevicePath, out var session)) return false;
        var result = false;
        var completed = await session.RunAsync(async (vm, token) => result = await action(vm, token), cancellationToken);
        return completed && result;
    }

    [NotProfileScoped]
    internal Task ApplyHeadsetProfileAsync(
        RazerHeadsetAudioConnection connection,
        PowerSourceKind powerSource,
        CancellationToken cancellationToken) =>
        WithHeadsetAsync(connection, async (headset, token) =>
        {
            await headset.ApplyPersistedProfileAsync(powerSource, token);
            return true;
        }, cancellationToken);

    internal Task StopHeadsetSoftwareEffectAsync(RazerHeadsetAudioConnection connection) =>
        WithHeadsetAsync(connection, async (headset, token) =>
        {
            token.ThrowIfCancellationRequested();
            await headset.StopSoftwareEffectForExternalAsync();
            return true;
        });

    private static string SnapshotKey(RazerChromaDeviceConnection connection) =>
        string.IsNullOrWhiteSpace(connection.DevicePath)
            ? $"pid:{connection.ProductId:X4}"
            : connection.DevicePath;

    private static string Key(ChromaDisplaySnapshot snapshot) =>
        string.IsNullOrWhiteSpace(snapshot.DevicePath)
            ? $"pid:{snapshot.ProductId:X4}"
            : snapshot.DevicePath;
}
