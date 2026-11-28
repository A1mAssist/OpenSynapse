using System.Collections.Concurrent;
using OpenSynapse.Core.Devices;
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
    private bool _displayLightingAvailable = true;
    private readonly HashSet<string> _headsetDisplayLightingOff = new(StringComparer.OrdinalIgnoreCase);

    [NotProfileScoped]
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
        _chromaDisplaySnapshots.Clear();
        var connections = ChromaDevices.Select(row => row.Connection).ToArray();
        var turnedOff = 0;
        var failed = 0;
        await Task.WhenAll(connections.Select(async connection =>
        {
            try
            {
                var service = CreateChromaDisplayService(connection);
                RazerChromaStandardState? state = null;
                try
                {
                    using var captureTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(1500));
                    state = await service.ReadStateAsync(captureTimeout.Token).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    _diagnosticLog.TryWrite("lighting-display-chroma",
                        $"chroma {connection.ProductId:X4} snapshot unavailable: {exception.Message}");
                }
                RazerChromaEffect? effect = state?.Effect is { } value && value != RazerChromaEffect.CustomFrame
                    ? value
                    : null;
                _chromaDisplaySnapshots[SnapshotKey(connection)] = new ChromaDisplaySnapshot(
                    connection.ProductId,
                    connection.DevicePath,
                    effect,
                    state?.Colors.ToArray() ?? []);
                if (effect is null)
                {
                    _diagnosticLog.TryWrite(
                        "lighting-display-chroma",
                        $"chroma {connection.ProductId:X4} has no restorable effect; turning off");
                }

                using var writeTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(1500));
                await service.TurnOffForShutdownAsync(writeTimeout.Token).ConfigureAwait(false);
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
                try
                {
                    using var restoreTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(1500));
                    await RestoreChromaLightingAfterExternalAsync(connection, null, PowerSourceKind.Unknown, restoreTimeout.Token);
                    Interlocked.Increment(ref restored);
                }
                catch (Exception exception)
                {
                    Interlocked.Increment(ref failed);
                    _diagnosticLog.TryWrite("lighting-display-chroma",
                        $"chroma {snapshot.ProductId:X4} configured lighting restore failed: {exception.Message}");
                }
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

    /// <summary>
    /// Replays the lighting saved on the accessory cards after Studio or another
    /// external lighting surface releases the devices.
    /// </summary>
    internal async Task RestoreLightingAfterExternalAsync(
        PowerSourceKind powerSource,
        CancellationToken cancellationToken,
        IReadOnlyCollection<string>? instanceIds = null)
    {
        if (!_displayLightingAvailable) return;
        var targets = instanceIds?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pending = targets is null ? null : new HashSet<string>(targets, StringComparer.OrdinalIgnoreCase);
        var failures = new List<Exception>();
        foreach (var connection in HeadsetAudioConnections.Where(
                     connection => targets is null || targets.Contains(connection.DevicePath)))
        {
            pending?.Remove(connection.DevicePath);
            try
            {
                if (targets is null)
                    await ApplyHeadsetProfileAsync(connection, powerSource, cancellationToken).ConfigureAwait(true);
                else if (!await WithHeadsetAsync(connection, async (headset, token) =>
                         {
                             if (connection.Transport == RazerHeadsetAudioTransport.KrakenBase37)
                             {
                                 var lighting = _razerHid.CreateKrakenLighting(connection);
                                 if (lighting?.Connection.IsReady != true)
                                     throw new InvalidOperationException("The headset lighting interface is unavailable.");
                                 await lighting.ApplyPersistedProfileAsync(powerSource, token, requireRestore: true);
                             }
                             else
                                 await headset.RestoreLightingAfterExternalAsync(powerSource, token);
                             return true;
                         }, cancellationToken).ConfigureAwait(true))
                    throw new InvalidOperationException("The headset lighting session is unavailable.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failures.Add(exception);
                _diagnosticLog.TryWrite(
                    "headset-audio", $"lighting restore failed for {connection.DevicePath}: {exception.Message}");
            }
        }

        foreach (var row in KeyboardDevices.Where(row => targets is null ||
                     targets.Contains(StudioKeyboardCatalog.CreateConnection(row.Connection)?.InstanceId ??
                         row.Connection.DevicePath)).ToArray())
        {
            pending?.Remove(StudioKeyboardCatalog.CreateConnection(row.Connection)?.InstanceId ?? row.Connection.DevicePath);
            try
            {
                if (targets is null)
                    await row.Detail.ApplyPersistedProfileAsync(powerSource, cancellationToken).ConfigureAwait(true);
                else
                    await row.Detail.RestoreLightingAfterExternalAsync(powerSource, cancellationToken).ConfigureAwait(true);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failures.Add(exception);
                _diagnosticLog.TryWrite(
                    "keyboard", $"lighting restore failed for {row.Connection.DevicePath}: {exception.Message}");
            }
        }

        if (targets is null) return;
        foreach (var instanceId in pending!.ToArray())
        {
            try
            {
                var connection = ChromaDevices.FirstOrDefault(row =>
                    StringComparer.OrdinalIgnoreCase.Equals(row.Connection.DevicePath, instanceId))?.Connection;
                var argb = _razerHid.ArgbConnection is { IsReady: true } controller &&
                    StringComparer.OrdinalIgnoreCase.Equals(controller.InstanceId, instanceId) ? controller : null;
                if (connection is null && argb?.FeatureDevicePath is not { })
                    throw new InvalidOperationException($"Lighting instance {instanceId} is offline or unavailable.");
                await RestoreChromaLightingAfterExternalAsync(connection, argb, powerSource, cancellationToken)
                    .ConfigureAwait(true);
                pending!.Remove(instanceId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) { failures.Add(exception); }
        }
        if (failures.Count > 0)
            throw new InvalidOperationException("Accessory lighting restore failed.", new AggregateException(failures));
    }

    private async Task RestoreChromaLightingAfterExternalAsync(
        RazerChromaDeviceConnection? connection,
        RazerHidSpecialLightingConnection? argb,
        PowerSourceKind powerSource,
        CancellationToken cancellationToken)
    {
        var document = _getProfile?.Invoke() ??
            throw new InvalidOperationException("The Chroma lighting profile is unavailable.");
        var service = connection is not null ? CreateChromaDisplayService(connection) :
            new RazerChromaStandardService(new RazerFeatureTransport(), argb!.FeatureDevicePath!,
                TimeSpan.FromMilliseconds(20));
        var state = await service.ReadStateAsync(cancellationToken).ConfigureAwait(false);
        var descriptor = new DeviceDescriptor(
            connection?.DevicePath ?? argb!.InstanceId, connection?.Name ?? argb!.DisplayName,
            0x1532, connection?.ProductId ?? argb!.ProductId,
            DeviceAccessState.Available, DeviceCapabilityState.PendingValidation,
            91, 1, 2, "chroma", DeviceCategory.Lighting);
        var settings = ProfileResolver.ResolveWithDeviceKey(document, descriptor, powerSource,
            string.IsNullOrWhiteSpace(state.SerialNumber) ? ProfileResolver.GetDeviceKey(descriptor) : state.SerialNumber).Chroma;
        if (settings.BrightnessPercent is int brightness)
            await service.SetBrightnessAsync((byte)Math.Clamp(brightness, 0, 100), cancellationToken).ConfigureAwait(false);

        if (settings.ArgbFrames is { Count: > 0 } frames)
        {
            if (argb is null || _razerHid.SpecialLightingService is not { } special)
                throw new InvalidOperationException("The requested instance has no ARGB frame interface.");
            await special.SetArgbCustomFrameModeAsync(argb, cancellationToken).ConfigureAwait(false);
            foreach (var (portText, encoded) in frames)
            {
                if (!byte.TryParse(portText, out var port) || port > RazerHidArgbFrame.MaxChannel)
                    throw new InvalidOperationException("The saved ARGB port is invalid.");
                var bytes = Convert.FromBase64String(encoded);
                if (bytes.Length == 0 || bytes.Length % 3 != 0 || bytes.Length / 3 > RazerHidArgbFrame.MaxLeds)
                    throw new InvalidOperationException("The saved ARGB frame is invalid.");
                var colors = Enumerable.Range(0, bytes.Length / 3)
                    .Select(index => new RazerHidColor(bytes[index * 3], bytes[index * 3 + 1], bytes[index * 3 + 2])).ToArray();
                await special.SetArgbFrameAsync(argb, port, colors, cancellationToken).ConfigureAwait(false);
            }
            return;
        }

        if (!Enum.TryParse<RazerChromaEffect>(settings.Effect, true, out var effect) ||
            !Enum.IsDefined(effect) || effect == RazerChromaEffect.CustomFrame)
            throw new InvalidOperationException("The Chroma card has no restorable saved lighting effect.");
        byte[] effectColors = effect switch
        {
            RazerChromaEffect.Static => ParseColor(settings.PrimaryColor),
            RazerChromaEffect.Breathing or RazerChromaEffect.Starlight =>
                [.. ParseColor(settings.PrimaryColor), .. ParseColor(settings.SecondaryColor)],
            _ => [],
        };
        if (argb is not null)
            await service.SetEffectAsync(RazerChromaEffect.Off, [], cancellationToken).ConfigureAwait(false);
        await service.SetEffectAsync(effect, effectColors, cancellationToken).ConfigureAwait(false);

        static byte[] ParseColor(string? color)
        {
            var hex = color?.Trim().TrimStart('#');
            if (hex is not { Length: 6 }) throw new InvalidOperationException("The saved Chroma color is invalid.");
            return Convert.FromHexString(hex);
        }
    }

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
