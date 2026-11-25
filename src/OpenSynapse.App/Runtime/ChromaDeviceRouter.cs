using System.Collections.Concurrent;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.Runtime;

internal enum ChromaTargetTransport
{
    HidMatrix,
    HidStatic,
    KeyboardEffect,
    StandardEffect,
    Kraken,
    JolieFrame,
}

internal sealed record ChromaTarget(
    string InstanceId,
    string Name,
    ChromaDeviceKind Kind,
    int Rows,
    int Columns,
    ChromaTargetTransport Transport);

/// <summary>
/// Applies one Chroma frame to every connected device Razer's own Chroma device
/// manifest assigns to that channel. Matrix devices get the resized frame; every
/// other device gets the frame's average colour through its static path.
/// </summary>
internal sealed partial class ChromaDeviceRouter
{
    // A feature-report round trip on a sleeping wireless mouse can take a few
    // hundred milliseconds behind other reads. 150 ms turned that queueing into
    // a timeout, a target cooldown and a visible blink.
    private const int TargetTimeoutMilliseconds = 400;
    private const int DuplicateFrameWindowMilliseconds = 1_000;
    private const int TargetFailureThreshold = 4;
    // Writes are concurrent now, so a target that missed a frame no longer holds
    // the others back. Keep its pause short enough that it rejoins the next few
    // frames instead of sitting on a stale colour.
    private const int TargetCooldownMilliseconds = 250;

    private readonly RazerHidDeviceService? _hid;
    private readonly RazerHidSpecialLightingService? _special;
    private readonly Func<IReadOnlyList<RazerHidDeviceConnection>> _hidConnections;
    private readonly Func<IReadOnlyList<RazerKeyboardConnection>> _keyboardDevices;
    private readonly Func<IReadOnlyList<RazerChromaDeviceConnection>> _standardConnections;
    private readonly Func<IReadOnlyList<RazerHidSpecialLightingConnection>> _krakenConnections;
    private readonly Func<IReadOnlyList<RazerHeadsetAudioConnection>> _headsetConnections;
    private readonly Action<string>? _diagnostic;
    private readonly ConcurrentDictionary<string, RazerChromaStandardService> _standardServices = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, RazerChromaStandardState> _standardBaselines = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, RazerRgb[]> _lastFrames = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, RazerRgb> _lastColors = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _lastFrameAt = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _lastColorAt = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (int Failures, long CooldownUntil)> _targetFailures =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _touchedTargets = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _baselineFailures = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, RazerHidDeviceConnection> _customEffectPrepared =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, RazerHidLightingBaseline?> _hidBaselines =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _hidBaselineAttempted = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _joliePrepared = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, RazerChromaStandardState> _jolieBaselines = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _jolieBaselineAttempted = new(StringComparer.OrdinalIgnoreCase);
    private RazerMakoXChromaService? _makoService;
    private string? _makoPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _makoGate = new(1, 1);
    private readonly object _frameState = new();
    private (ChromaDeviceKind Kind, IReadOnlyList<RazerRgb> Frame, int Rows, int Columns)? _pendingFrame;
    private int _framePump;
    private volatile bool _resetCache;

    internal ChromaDeviceRouter(
        RazerHidDeviceService? hid,
        RazerHidSpecialLightingService? special,
        Func<IReadOnlyList<RazerHidDeviceConnection>> hidConnections,
        Func<IReadOnlyList<RazerKeyboardConnection>> keyboardDevices,
        Func<IReadOnlyList<RazerChromaDeviceConnection>> standardConnections,
        Func<IReadOnlyList<RazerHidSpecialLightingConnection>> krakenConnections,
        Func<IReadOnlyList<RazerHeadsetAudioConnection>> headsetConnections,
        Action<string>? diagnostic = null)
    {
        _hid = hid;
        _special = special;
        _hidConnections = hidConnections ?? throw new ArgumentNullException(nameof(hidConnections));
        _keyboardDevices = keyboardDevices ?? throw new ArgumentNullException(nameof(keyboardDevices));
        _standardConnections = standardConnections ?? throw new ArgumentNullException(nameof(standardConnections));
        _krakenConnections = krakenConnections ?? throw new ArgumentNullException(nameof(krakenConnections));
        _headsetConnections = headsetConnections ?? throw new ArgumentNullException(nameof(headsetConnections));
        _diagnostic = diagnostic;
    }

    internal IReadOnlySet<ChromaDeviceKind> SupportedKinds => BuildTargets()
        .Select(target => target.Kind)
        .ToHashSet();

    /// <summary>Sleep or a device power-cycle clears the firmware's last frame.</summary>
    internal void ResetCache() => _resetCache = true;

    /// <summary>
    /// Queues the newest frame and returns immediately. A game's SetEffect call must
    /// not wait for a wireless mouse's feature-report round trip; the pump coalesces
    /// frames so a slow target cannot build a backlog.
    /// </summary>
    internal Task<bool> ApplyAsync(
        ChromaDeviceKind kind,
        IReadOnlyList<RazerRgb> frame,
        int rows,
        int columns,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (rows <= 0 || columns <= 0 || frame.Count != rows * columns)
        {
            throw new ArgumentException("The Chroma frame does not match its dimensions.", nameof(frame));
        }

        lock (_frameState)
        {
            _pendingFrame = (kind, frame.ToArray(), rows, columns);
        }
        if (Interlocked.CompareExchange(ref _framePump, 1, 0) == 0)
        {
            _ = PumpFramesAsync();
        }
        return Task.FromResult(true);
    }

    private async Task PumpFramesAsync()
    {
        try
        {
            while (true)
            {
                (ChromaDeviceKind Kind, IReadOnlyList<RazerRgb> Frame, int Rows, int Columns) pending;
                lock (_frameState)
                {
                    if (_pendingFrame is not { } next)
                    {
                        Volatile.Write(ref _framePump, 0);
                        return;
                    }
                    pending = next;
                    _pendingFrame = null;
                }

                try
                {
                    await ApplyCoreAsync(
                            pending.Kind, pending.Frame, pending.Rows, pending.Columns, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // One bad frame must not stop the pump; the next frame replaces it.
                }
            }
        }
        finally
        {
            Volatile.Write(ref _framePump, 0);
        }
    }

    private async Task<bool> ApplyCoreAsync(
        ChromaDeviceKind kind,
        IReadOnlyList<RazerRgb> frame,
        int rows,
        int columns,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (rows <= 0 || columns <= 0 || frame.Count != rows * columns)
        {
            throw new ArgumentException("The Chroma frame does not match its dimensions.", nameof(frame));
        }

        var targets = BuildTargets().Where(target => target.Kind == kind).ToArray();
        if (targets.Length == 0) return false;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_resetCache)
            {
                _resetCache = false;
                _lastFrames.Clear();
                _lastFrameAt.Clear();
                _lastColors.Clear();
                _lastColorAt.Clear();
                _customEffectPrepared.Clear();
                _joliePrepared.Clear();
            }
            var liveIds = targets.Select(target => target.InstanceId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            TrimStale(_lastFrames, liveIds);
            TrimStale(_lastFrameAt, liveIds);
            TrimStale(_lastColors, liveIds);
            TrimStale(_lastColorAt, liveIds);
            TrimStale(_customEffectPrepared, liveIds);
            TrimStale(_joliePrepared, liveIds);
            TrimStale(_jolieBaselines, liveIds);
            TrimStale(_jolieBaselineAttempted, liveIds);
            if (_makoService is not null && (_makoPath is null || !liveIds.Contains(_makoPath)))
            {
                await _makoService.DisposeAsync().ConfigureAwait(false);
                _makoService = null;
                _makoPath = null;
            }

            var now = Environment.TickCount64;
            var tasks = new List<Task<bool>>(targets.Length);
            foreach (var target in targets)
            {
                if (_targetFailures.TryGetValue(target.InstanceId, out var failure) &&
                    failure.CooldownUntil > now)
                {
                    continue;
                }

                _touchedTargets.TryAdd(target.InstanceId, 0);
                // Write every target in the same frame window instead of stacking
                // one device's latency on top of the next.
                tasks.Add(ApplyTargetGuardedAsync(target, frame, rows, columns, cancellationToken));
            }
            if (tasks.Count == 0)
            {
                return false;
            }
            var results = await Task.WhenAll(tasks).ConfigureAwait(false);
            return results.Any(applied => applied);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> ApplyTargetGuardedAsync(
        ChromaTarget target,
        IReadOnlyList<RazerRgb> frame,
        int rows,
        int columns,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TargetTimeoutMilliseconds);
        try
        {
            var applied = await ApplyTargetAsync(target, frame, rows, columns, timeout.Token)
                .ConfigureAwait(false);
            if (!applied)
            {
                ForgetFrameState(target.InstanceId);
                return false;
            }
            _targetFailures.TryRemove(target.InstanceId, out _);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            ForgetFrameState(target.InstanceId);
            RecordTargetFailure(target, "timed out");
            return false;
        }
        catch (Exception exception) when (IsDeviceFailure(exception))
        {
            ForgetFrameState(target.InstanceId);
            RecordTargetFailure(target, exception.Message);
            return false;
        }
    }

    private async Task<bool> ApplyTargetAsync(
        ChromaTarget target,
        IReadOnlyList<RazerRgb> frame,
        int rows,
        int columns,
        CancellationToken cancellationToken)
    {
        if (target.Transport == ChromaTargetTransport.JolieFrame)
        {
            var resized = Resize(
                frame, rows, columns, 1, RazerMakoXChromaService.LedCount);
            var now = Environment.TickCount64;
            if (_lastFrames.TryGetValue(target.InstanceId, out var previous) &&
                previous.AsSpan().SequenceEqual(resized) &&
                now - _lastFrameAt.GetValueOrDefault(target.InstanceId) < DuplicateFrameWindowMilliseconds)
            {
                return true;
            }

            var connection = _headsetConnections().FirstOrDefault(candidate =>
                candidate.Transport == RazerHeadsetAudioTransport.Jolie64 &&
                StringComparer.OrdinalIgnoreCase.Equals(candidate.DevicePath, target.InstanceId));
            if (connection is null) return false;
            var service = await EnsureMakoServiceAsync(connection, cancellationToken).ConfigureAwait(false);
            if (_jolieBaselineAttempted.TryAdd(target.InstanceId, 0))
            {
                try
                {
                    _jolieBaselines[target.InstanceId] =
                        await service.ReadStateAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (IsDeviceFailure(exception))
                {
                    _diagnostic?.Invoke($"Chroma baseline for {target.Name} unavailable: {exception.Message}");
                }
            }

            if (_joliePrepared.TryAdd(target.InstanceId, 0))
            {
                await service.SetCustomFrameEffectAsync(cancellationToken).ConfigureAwait(false);
            }

            var colors = new byte[resized.Length * 3];
            for (var index = 0; index < resized.Length; index++)
            {
                colors[index * 3] = resized[index].Red;
                colors[(index * 3) + 1] = resized[index].Green;
                colors[(index * 3) + 2] = resized[index].Blue;
            }
            await service.SetFrameAsync(colors, cancellationToken).ConfigureAwait(false);
            _lastFrames[target.InstanceId] = resized;
            _lastFrameAt[target.InstanceId] = now;
            return true;
        }

        if (target.Transport == ChromaTargetTransport.HidMatrix)
        {
            var resized = Resize(frame, rows, columns, target.Rows, target.Columns);
            var now = Environment.TickCount64;
            if (_lastFrames.TryGetValue(target.InstanceId, out var previous) &&
                previous.AsSpan().SequenceEqual(resized) &&
                now - _lastFrameAt.GetValueOrDefault(target.InstanceId) < DuplicateFrameWindowMilliseconds)
            {
                return true;
            }

            var connection = FindHid(target.InstanceId);
            if (connection is null || _hid is null) return false;
            var colors = resized.Select(color => new RazerHidColor(color.Red, color.Green, color.Blue)).ToArray();
            if (!_customEffectPrepared.TryGetValue(target.InstanceId, out var prepared) ||
                !prepared.IsSameEndpoint(connection))
            {
                await CaptureHidBaselineAsync(target, connection, cancellationToken).ConfigureAwait(false);
                await _hid.SetCustomFrameEffectAsync(connection, cancellationToken).ConfigureAwait(false);
                _customEffectPrepared[target.InstanceId] = connection;
            }
            await _hid.SetCustomFrameAsync(connection, colors, cancellationToken).ConfigureAwait(false);
            _lastFrames[target.InstanceId] = resized;
            _lastFrameAt[target.InstanceId] = now;
            return true;
        }

        var representative = Average(frame);
        var colorNow = Environment.TickCount64;
        if (_lastColors.TryGetValue(target.InstanceId, out var last) &&
            last == representative &&
            colorNow - _lastColorAt.GetValueOrDefault(target.InstanceId) < DuplicateFrameWindowMilliseconds)
        {
            return true;
        }

        switch (target.Transport)
        {
            case ChromaTargetTransport.HidStatic:
            {
                var connection = FindHid(target.InstanceId);
                if (connection is null || _hid is null) return false;
                var zone = StudioHidOutputRouter.FindStaticZone(connection);
                if (zone is null) return false;
                await CaptureHidBaselineAsync(target, connection, cancellationToken).ConfigureAwait(false);
                await _hid.SetLightingAsync(
                    connection,
                    new RazerHidLightingSettings(
                        RazerHidLightingEffect.Static,
                        Primary: new RazerHidColor(representative.Red, representative.Green, representative.Blue),
                        Storage: connection.Definition.DefaultStorage,
                        Zone: zone),
                    cancellationToken).ConfigureAwait(false);
                break;
            }
            case ChromaTargetTransport.StandardEffect:
            {
                var connection = _standardConnections().FirstOrDefault(candidate =>
                    StringComparer.OrdinalIgnoreCase.Equals(candidate.DevicePath, target.InstanceId));
                if (connection is null) return false;
                var service = _standardServices.GetOrAdd(connection.DevicePath, path =>
                    new RazerChromaStandardService(
                        new RazerFeatureTransport(),
                        path,
                        TimeSpan.FromMilliseconds(20),
                        _diagnostic,
                        connection.Channel,
                        connection.ProductId));
                await CaptureBaselineAsync(target.InstanceId, service, cancellationToken).ConfigureAwait(false);
                await service.SetEffectAsync(
                    RazerChromaEffect.Static,
                    [representative.Red, representative.Green, representative.Blue],
                    cancellationToken).ConfigureAwait(false);
                break;
            }
            case ChromaTargetTransport.KeyboardEffect:
            {
                var keyboard = _keyboardDevices().FirstOrDefault(candidate =>
                    StringComparer.OrdinalIgnoreCase.Equals(candidate.DevicePath, target.InstanceId));
                if (keyboard is null) return false;
                var service = _standardServices.GetOrAdd(target.InstanceId, _ => new RazerChromaStandardService(
                    new RazerFeatureTransport(),
                    keyboard.DevicePath,
                    TimeSpan.FromMilliseconds(20),
                    _diagnostic,
                    keyboard.UsesLegacyChroma ? RazerChromaChannel.Legacy : RazerChromaChannel.Standard,
                    keyboard.ProductId));
                await CaptureBaselineAsync(target.InstanceId, service, cancellationToken).ConfigureAwait(false);
                await service.SetEffectAsync(
                    RazerChromaEffect.Static,
                    [representative.Red, representative.Green, representative.Blue],
                    cancellationToken).ConfigureAwait(false);
                break;
            }
            case ChromaTargetTransport.Kraken:
            {
                if (_special is null) return false;
                var connection = _krakenConnections().FirstOrDefault(candidate =>
                    StringComparer.OrdinalIgnoreCase.Equals(candidate.InstanceId, target.InstanceId));
                if (connection is null) return false;
                await _special.SetKrakenLightingAsync(
                    connection,
                    new RazerHidLightingSettings(
                        RazerHidLightingEffect.Static,
                        Primary: new RazerHidColor(representative.Red, representative.Green, representative.Blue)),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                break;
            }
            default:
                return false;
        }

        _lastColors[target.InstanceId] = representative;
        _lastColorAt[target.InstanceId] = colorNow;
        return true;
    }

    private void ForgetFrameState(string instanceId)
    {
        _lastFrames.TryRemove(instanceId, out _);
        _lastFrameAt.TryRemove(instanceId, out _);
        _lastColors.TryRemove(instanceId, out _);
        _lastColorAt.TryRemove(instanceId, out _);
        _customEffectPrepared.TryRemove(instanceId, out _);
        _joliePrepared.TryRemove(instanceId, out _);
    }

    private void RecordTargetFailure(ChromaTarget target, string message)
    {
        var failures = _targetFailures.TryGetValue(target.InstanceId, out var state)
            ? state.Failures + 1
            : 1;
        if (failures >= TargetFailureThreshold)
        {
            _targetFailures[target.InstanceId] = (
                0, Environment.TickCount64 + TargetCooldownMilliseconds);
            _diagnostic?.Invoke(
                $"Chroma target {target.Name} paused for {TargetCooldownMilliseconds} ms: {message}");
            return;
        }

        _targetFailures[target.InstanceId] = (failures, 0);
        // Log the first miss only; short cooldowns would otherwise flood the log.
        if (failures == 1)
        {
            _diagnostic?.Invoke($"Chroma target {target.Name} failed: {message}");
        }
    }

    private async Task CaptureBaselineAsync(
        string instanceId,
        RazerChromaStandardService service,
        CancellationToken cancellationToken)
    {
        if (_standardBaselines.ContainsKey(instanceId) ||
            _baselineFailures.GetValueOrDefault(instanceId) >= 3) return;
        try
        {
            var state = await service.ReadStateAsync(cancellationToken).ConfigureAwait(false);
            if (state.Effect is not null)
            {
                _standardBaselines[instanceId] = state;
                _baselineFailures.TryRemove(instanceId, out _);
            }
        }
        catch (Exception exception) when (IsDeviceFailure(exception))
        {
            _baselineFailures[instanceId] = _baselineFailures.GetValueOrDefault(instanceId) + 1;
            _diagnostic?.Invoke($"Chroma baseline for {instanceId} unavailable: {exception.Message}");
        }
    }

    private async Task CaptureHidBaselineAsync(
        ChromaTarget target,
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken)
    {
        if (_hid is null || !_hidBaselineAttempted.TryAdd(target.InstanceId, 0)) return;
        try
        {
            _hidBaselines[target.InstanceId] = await RazerHidLightingBaseline
                .CaptureAsync(_hid, connection, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            IsDeviceFailure(exception) || exception is OperationCanceledException)
        {
            // Keep the target in the restore set; a missing baseline falls back
            // to the firmware's Spectrum exit effect.
            _hidBaselines[target.InstanceId] = null;
            _diagnostic?.Invoke(
                $"Chroma baseline for {target.Name} unavailable: {exception.Message}");
            if (exception is OperationCanceledException) throw;
        }
    }

    private RazerHidDeviceConnection? FindHid(string instanceId) =>
        _hidConnections().FirstOrDefault(connection =>
            connection.IsReady &&
            StringComparer.OrdinalIgnoreCase.Equals(connection.InstanceId, instanceId)) ??
        _keyboardDevices()
            .Where(keyboard => StringComparer.OrdinalIgnoreCase.Equals(keyboard.DevicePath, instanceId))
            .Select(StudioKeyboardCatalog.CreateConnection)
            .FirstOrDefault(connection => connection is not null);

    private IReadOnlyList<ChromaTarget> BuildTargets()
    {
        var targets = new List<ChromaTarget>();
        foreach (var connection in _hidConnections())
        {
            if (!connection.IsReady) continue;
            if (connection.Definition.Category == DeviceCategory.Laptop) continue;
            var kind = ChromaDeviceCatalog.Resolve(connection.Definition.ProductId) ??
                MapCategory(connection.Definition.Category);
            if (kind is null) continue;

            if (connection.Definition.MatrixDimensions is { } dimensions &&
                connection.Capabilities.Contains(RazerHidBackendCapability.MatrixFrameWrite))
            {
                targets.Add(new ChromaTarget(
                    connection.InstanceId,
                    connection.Definition.DisplayName,
                    kind.Value,
                    dimensions.Rows,
                    dimensions.Columns,
                    ChromaTargetTransport.HidMatrix));
            }
            else if (StudioHidOutputRouter.FindStaticZone(connection) is not null)
            {
                targets.Add(new ChromaTarget(
                    connection.InstanceId,
                    connection.Definition.DisplayName,
                    kind.Value,
                    1,
                    1,
                    ChromaTargetTransport.HidStatic));
            }
        }

        foreach (var keyboard in _keyboardDevices())
        {
            var kind = ChromaDeviceCatalog.Resolve(keyboard.ProductId) ?? ChromaDeviceKind.Keyboard;
            var connection = StudioKeyboardCatalog.CreateConnection(keyboard);
            if (connection?.Definition.MatrixDimensions is { } dimensions)
            {
                targets.Add(new ChromaTarget(
                    keyboard.DevicePath,
                    connection.Definition.DisplayName,
                    kind,
                    dimensions.Rows,
                    dimensions.Columns,
                    ChromaTargetTransport.HidMatrix));
            }
            else
            {
                targets.Add(new ChromaTarget(
                    keyboard.DevicePath,
                    RazerKeyboardService.Products[keyboard.ProductId],
                    kind,
                    1,
                    1,
                    ChromaTargetTransport.KeyboardEffect));
            }
        }

        foreach (var connection in _standardConnections())
        {
            var kind = ChromaDeviceCatalog.Resolve(connection.ProductId) ??
                MapCategory(connection.Category) ?? ChromaDeviceKind.ChromaLink;
            targets.Add(new ChromaTarget(
                connection.DevicePath,
                connection.Name,
                kind,
                1,
                1,
                ChromaTargetTransport.StandardEffect));
        }

        foreach (var connection in _krakenConnections())
        {
            if (!connection.IsReady) continue;
            var kind = ChromaDeviceCatalog.Resolve(connection.ProductId) ??
                ChromaDeviceKind.Headset;
            targets.Add(new ChromaTarget(
                connection.InstanceId,
                connection.DisplayName,
                kind,
                1,
                1,
                ChromaTargetTransport.Kraken));
        }

        foreach (var connection in _headsetConnections())
        {
            if (connection.Transport != RazerHeadsetAudioTransport.Jolie64) continue;
            targets.Add(new ChromaTarget(
                connection.DevicePath,
                connection.Name,
                ChromaDeviceKind.Headset,
                1,
                RazerMakoXChromaService.LedCount,
                ChromaTargetTransport.JolieFrame));
        }

        return targets;
    }

    private async Task<RazerMakoXChromaService> EnsureMakoServiceAsync(
        RazerHeadsetAudioConnection connection,
        CancellationToken cancellationToken)
    {
        await _makoGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_makoService is not null &&
                StringComparer.OrdinalIgnoreCase.Equals(_makoPath, connection.DevicePath))
            {
                return _makoService;
            }

            if (_makoService is not null)
            {
                await _makoService.DisposeAsync().ConfigureAwait(false);
            }

            _makoService = new RazerMakoXChromaService(
                connection.DevicePath,
                _diagnostic,
                keepOpen: true);
            _makoPath = connection.DevicePath;
            return _makoService;
        }
        finally
        {
            _makoGate.Release();
        }
    }

    private static void TrimStale<T>(ConcurrentDictionary<string, T> map, IReadOnlySet<string> liveIds)
    {
        foreach (var key in map.Keys)
        {
            if (!liveIds.Contains(key)) map.TryRemove(key, out _);
        }
    }

    private static ChromaDeviceKind? MapCategory(DeviceCategory category) => category switch
    {
        DeviceCategory.Mouse => ChromaDeviceKind.Mouse,
        DeviceCategory.Keyboard => ChromaDeviceKind.Keyboard,
        DeviceCategory.Headset => ChromaDeviceKind.Headset,
        DeviceCategory.MouseMat => ChromaDeviceKind.Mousepad,
        DeviceCategory.Gamepad => ChromaDeviceKind.Keypad,
        DeviceCategory.Accessory => ChromaDeviceKind.ChromaLink,
        DeviceCategory.Monitor => ChromaDeviceKind.ChromaLink,
        DeviceCategory.Speaker => ChromaDeviceKind.ChromaLink,
        _ => null,
    };

    private static RazerRgb Average(IReadOnlyList<RazerRgb> frame)
    {
        if (frame.Count == 0) return default;
        long red = 0, green = 0, blue = 0;
        foreach (var color in frame)
        {
            red += color.Red;
            green += color.Green;
            blue += color.Blue;
        }
        return new RazerRgb(
            (byte)(red / frame.Count),
            (byte)(green / frame.Count),
            (byte)(blue / frame.Count));
    }

    private static RazerRgb[] Resize(
        IReadOnlyList<RazerRgb> source,
        int sourceRows,
        int sourceColumns,
        int rows,
        int columns)
    {
        var result = new RazerRgb[rows * columns];
        for (var row = 0; row < rows; row++)
        {
            var sourceRow = rows > sourceRows
                ? (row < sourceRows ? row : -1)
                : (rows == 1 ? 0 : (int)Math.Round(row * (sourceRows - 1d) / (rows - 1d)));
            if (sourceRow < 0) continue;
            for (var column = 0; column < columns; column++)
            {
                var sourceColumn = columns > sourceColumns
                    ? (column < sourceColumns ? column : -1)
                    : (columns == 1 ? 0 : (int)Math.Round(column * (sourceColumns - 1d) / (columns - 1d)));
                if (sourceColumn < 0) continue;
                result[row * columns + column] = source[sourceRow * sourceColumns + sourceColumn];
            }
        }
        return result;
    }

    private static bool IsDeviceFailure(Exception exception) =>
        exception is IOException or InvalidOperationException or TimeoutException or UnauthorizedAccessException;
}
