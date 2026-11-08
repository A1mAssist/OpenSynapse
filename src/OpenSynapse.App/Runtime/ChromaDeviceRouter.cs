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
internal sealed class ChromaDeviceRouter
{
    private readonly RazerHidDeviceService? _hid;
    private readonly RazerHidSpecialLightingService? _special;
    private readonly Func<IReadOnlyList<RazerHidDeviceConnection>> _hidConnections;
    private readonly Func<IReadOnlyList<RazerKeyboardConnection>> _keyboardDevices;
    private readonly Func<IReadOnlyList<RazerChromaDeviceConnection>> _standardConnections;
    private readonly Func<IReadOnlyList<RazerHidSpecialLightingConnection>> _krakenConnections;
    private readonly Action<string>? _diagnostic;
    private readonly ConcurrentDictionary<string, RazerChromaStandardService> _standardServices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RazerChromaStandardState> _standardBaselines = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _baselineAttempted = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RazerRgb[]> _lastFrames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RazerRgb> _lastColors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RazerHidDeviceConnection> _customEffectPrepared =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);

    internal ChromaDeviceRouter(
        RazerHidDeviceService? hid,
        RazerHidSpecialLightingService? special,
        Func<IReadOnlyList<RazerHidDeviceConnection>> hidConnections,
        Func<IReadOnlyList<RazerKeyboardConnection>> keyboardDevices,
        Func<IReadOnlyList<RazerChromaDeviceConnection>> standardConnections,
        Func<IReadOnlyList<RazerHidSpecialLightingConnection>> krakenConnections,
        Action<string>? diagnostic = null)
    {
        _hid = hid;
        _special = special;
        _hidConnections = hidConnections ?? throw new ArgumentNullException(nameof(hidConnections));
        _keyboardDevices = keyboardDevices ?? throw new ArgumentNullException(nameof(keyboardDevices));
        _standardConnections = standardConnections ?? throw new ArgumentNullException(nameof(standardConnections));
        _krakenConnections = krakenConnections ?? throw new ArgumentNullException(nameof(krakenConnections));
        _diagnostic = diagnostic;
    }

    internal IReadOnlySet<ChromaDeviceKind> SupportedKinds => BuildTargets()
        .Select(target => target.Kind)
        .ToHashSet();

    internal async Task<bool> ApplyAsync(
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
            var liveIds = targets.Select(target => target.InstanceId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var staleId in _lastFrames.Keys.Where(id => !liveIds.Contains(id)).ToArray())
            {
                _lastFrames.Remove(staleId);
            }
            foreach (var staleId in _lastColors.Keys.Where(id => !liveIds.Contains(id)).ToArray())
            {
                _lastColors.Remove(staleId);
            }
            foreach (var staleId in _customEffectPrepared.Keys.Where(id => !liveIds.Contains(id)).ToArray())
            {
                _customEffectPrepared.Remove(staleId);
            }

            var applied = false;
            foreach (var target in targets)
            {
                try
                {
                    applied |= await ApplyTargetAsync(target, frame, rows, columns, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception exception) when (IsDeviceFailure(exception))
                {
                    _diagnostic?.Invoke($"Chroma target {target.Name} failed: {exception.Message}");
                }
            }
            return applied;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> ApplyTargetAsync(
        ChromaTarget target,
        IReadOnlyList<RazerRgb> frame,
        int rows,
        int columns,
        CancellationToken cancellationToken)
    {
        if (target.Transport == ChromaTargetTransport.HidMatrix)
        {
            var resized = Resize(frame, rows, columns, target.Rows, target.Columns);
            if (_lastFrames.TryGetValue(target.InstanceId, out var previous) &&
                previous.AsSpan().SequenceEqual(resized))
            {
                return true;
            }

            var connection = FindHid(target.InstanceId);
            if (connection is null || _hid is null) return false;
            var colors = resized.Select(color => new RazerHidColor(color.Red, color.Green, color.Blue)).ToArray();
            if (!_customEffectPrepared.TryGetValue(target.InstanceId, out var prepared) ||
                !ReferenceEquals(prepared, connection))
            {
                await _hid.SetCustomFrameEffectAsync(connection, cancellationToken).ConfigureAwait(false);
                _customEffectPrepared[target.InstanceId] = connection;
            }
            await _hid.SetCustomFrameAsync(connection, colors, cancellationToken).ConfigureAwait(false);
            _lastFrames[target.InstanceId] = resized;
            return true;
        }

        var representative = Average(frame);
        if (_lastColors.TryGetValue(target.InstanceId, out var last) &&
            last == representative)
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
                        _diagnostic));
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
        return true;
    }

    private async Task CaptureBaselineAsync(
        string instanceId,
        RazerChromaStandardService service,
        CancellationToken cancellationToken)
    {
        if (!_baselineAttempted.Add(instanceId)) return;
        try
        {
            var state = await service.ReadStateAsync(cancellationToken).ConfigureAwait(false);
            if (state.Effect is not null)
            {
                _standardBaselines[instanceId] = state;
            }
        }
        catch (Exception exception) when (IsDeviceFailure(exception))
        {
            _diagnostic?.Invoke($"Chroma baseline for {instanceId} unavailable: {exception.Message}");
        }
    }

    /// <summary>Restores the effect each whole-device target had before the session.</summary>
    internal async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var (instanceId, state) in _standardBaselines.ToArray())
            {
                if (state.Effect is not { } effect ||
                    !_standardServices.TryGetValue(instanceId, out var service))
                {
                    continue;
                }

                try
                {
                    await service.SetEffectAsync(effect, state.Colors, cancellationToken).ConfigureAwait(false);
                    if (state.BrightnessPercent is byte brightness)
                    {
                        await service.SetBrightnessAsync(brightness, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (Exception exception) when (IsDeviceFailure(exception))
                {
                    _diagnostic?.Invoke($"Chroma restore for {instanceId} failed: {exception.Message}");
                }
            }
        }
        finally
        {
            _standardBaselines.Clear();
            _baselineAttempted.Clear();
            _gate.Release();
        }
    }

    private RazerHidDeviceConnection? FindHid(string instanceId) =>
        _hidConnections().FirstOrDefault(connection =>
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

        return targets;
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
            var sourceRow = rows == 1 ? 0 : (int)Math.Round(row * (sourceRows - 1d) / (rows - 1d));
            for (var column = 0; column < columns; column++)
            {
                var sourceColumn = columns == 1
                    ? 0
                    : (int)Math.Round(column * (sourceColumns - 1d) / (columns - 1d));
                result[row * columns + column] = source[sourceRow * sourceColumns + sourceColumn];
            }
        }
        return result;
    }

    private static bool IsDeviceFailure(Exception exception) =>
        exception is IOException or InvalidOperationException or TimeoutException or UnauthorizedAccessException;
}
