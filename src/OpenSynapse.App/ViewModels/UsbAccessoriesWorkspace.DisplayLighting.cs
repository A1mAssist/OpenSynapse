using System.Collections.Concurrent;
using OpenSynapse.Core.Sensors;
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
    private bool _headsetDisplayLightingOff;

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

        try
        {
            using var headsetTimeout =
                new CancellationTokenSource(TimeSpan.FromMilliseconds(1500));
            _headsetDisplayLightingOff = await WithSelectedHeadsetAsync(
                    headset => headset.TurnOffLightingForShutdownAsync(headsetTimeout.Token))
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _headsetDisplayLightingOff = false;
            _diagnosticLog.TryWrite(
                "lighting-display-chroma",
                $"headset off failed: {exception.Message}");
        }
        if (_headsetDisplayLightingOff)
        {
            _diagnosticLog.TryWrite(
                "lighting-display-chroma",
                "headset off request sent.");
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

        if (_headsetDisplayLightingOff)
        {
            _headsetDisplayLightingOff = false;
            var applied = false;
            try
            {
                using var headsetTimeout =
                    new CancellationTokenSource(TimeSpan.FromMilliseconds(1500));
                applied = await WithSelectedHeadsetAsync(async headset =>
                {
                    await headset.ApplyPersistedProfileAsync(
                            PowerSourceKind.Unknown, headsetTimeout.Token)
                        .ConfigureAwait(false);
                    return true;
                }).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _diagnosticLog.TryWrite(
                    "lighting-display-chroma",
                    $"headset restore failed: {exception.Message}");
            }
            if (!applied)
            {
                _diagnosticLog.TryWrite(
                    "lighting-display-chroma",
                    "headset lighting restore skipped: no selected or connected headset");
            }
        }

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

    private async Task<bool> WithSelectedHeadsetAsync(
        Func<RazerHeadsetAudioViewModel, Task<bool>> action)
    {
        var selected = SelectedHeadsetAudio;
        if (selected is not null)
        {
            return await action(selected).ConfigureAwait(false);
        }

        var row = _selectedHeadsetAudioDevice ?? HeadsetAudioDevices.FirstOrDefault();
        if (row is null)
        {
            return false;
        }

        var temporary = new RazerHeadsetAudioViewModel(
            row.Connection,
            message => _diagnosticLog.TryWrite("headset-audio", message),
            null,
            _getProfile,
            _saveProfile);
        try
        {
            return await action(temporary).ConfigureAwait(false);
        }
        finally
        {
            await temporary.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static string SnapshotKey(RazerChromaDeviceConnection connection) =>
        string.IsNullOrWhiteSpace(connection.DevicePath)
            ? $"pid:{connection.ProductId:X4}"
            : connection.DevicePath;

    private static string Key(ChromaDisplaySnapshot snapshot) =>
        string.IsNullOrWhiteSpace(snapshot.DevicePath)
            ? $"pid:{snapshot.ProductId:X4}"
            : snapshot.DevicePath;
}
