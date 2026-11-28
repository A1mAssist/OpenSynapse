namespace OpenSynapse.Core.Lighting;

/// <summary>A layer segment with device-local masks and cached Ambient coordinates.</summary>
public sealed record StudioWorldLayer(StudioLayerRender Render, StudioWorldLayerTarget[] Targets);

public sealed record StudioWorldLayerTarget(
    StudioWorldDevice Device,
    byte[]? Mask,
    (double U, double V)[]? Coordinates);

public sealed partial class StudioWorldCanvas
{
    public bool UsesLayerMapping { get; private init; }

    /// <summary>Whether ordinary and LegacyAuto effects retain their old world-grid mapping.</summary>
    public bool UsesLegacyWorld { get; private init; }

    public StudioWorldLayer[] MappedLayers { get; private init; } = [];

    internal static bool IsMappedAmbient(StudioEffectParams parameters) =>
        parameters.Effect == StudioEffectKind.Ambient &&
        parameters.AmbientMapping is StudioAmbientMapping.PerDevice or StudioAmbientMapping.SharedCanvas;

    private static bool IsKeyFade(StudioEffectParams parameters) =>
        parameters.Effect == StudioEffectKind.Reactive && parameters.ReactiveStyle == StudioReactiveStyle.KeyFade;

    private static StudioWorldLayer[] BuildMappedLayers(
        StudioProfile? profile, IReadOnlyList<StudioWorldDevice> devices)
    {
        if (profile is null) return [];
        var result = new List<StudioWorldLayer>();
        foreach (var layer in profile.EnumerateLayers())
        {
            if (layer.IsGroup) continue;
            var segments = new List<(Guid Id, StudioEffectParams Params, StudioWorldLayerTarget[] Targets)>();
            if (layer.Regions.Count == 0)
            {
                segments.Add((layer.Id, layer.Params, devices.Select(device =>
                    new StudioWorldLayerTarget(device, null, null)).ToArray()));
            }
            else
            {
                foreach (var region in layer.Regions)
                {
                    // An imported region with no resolved target must stay empty.
                    if (region.Devices.Count == 0) continue;
                    var targets = new List<StudioWorldLayerTarget>();
                    foreach (var device in devices)
                    {
                        var mask = StudioMaskBuilder.BuildRegion(region, device.ProductId,
                            device.InstanceId, device.MatrixRows, device.MatrixColumns);
                        if (mask.Any(value => value != 0)) targets.Add(new(device, mask, null));
                    }
                    if (targets.Count > 0) segments.Add((region.PlaybackId, region.Params, targets.ToArray()));
                }
            }

            var participants = segments.SelectMany(segment => segment.Targets)
                .Select(target => target.Device).Distinct().ToArray();
            if (participants.Length == 0) continue;
            // Bounds belong to the entire layer, including devices in other regions.
            var minX = participants.Min(device => device.LedBounds.MinX);
            var minY = participants.Min(device => device.LedBounds.MinY);
            var maxX = participants.Max(device => device.LedBounds.MaxX);
            var maxY = participants.Max(device => device.LedBounds.MaxY);
            foreach (var (id, parameters, targets) in segments)
            {
                for (var index = 0; index < targets.Length; index++)
                {
                    var target = targets[index];
                    if (!IsMappedAmbient(parameters)) continue;
                    var coordinates = parameters.AmbientMapping == StudioAmbientMapping.PerDevice
                        ? target.Device.LocalCoordinates
                        : target.Device.LedPositions.Select(point => (
                            Normalize(point.X, minX, maxX), Normalize(point.Y, minY, maxY))).ToArray();
                    targets[index] = target with { Coordinates = coordinates };
                }

                var worldMask = new byte[GridSize * GridSize];
                foreach (var target in targets)
                    for (var pixel = 0; pixel < target.Device.WorldIndices.Length; pixel++)
                        if (target.Mask is null || target.Mask[pixel] > 0)
                            worldMask[target.Device.WorldIndices[pixel]] = StudioMaskBuilder.Full;
                result.Add(new(new StudioLayerRender(parameters, worldMask,
                    layer.Opacity, layer.Blend, layer.Visible, id, TimebaseId: layer.Id), targets));
            }
        }
        return result.ToArray();
    }

    /// <summary>
    /// Paints each device in original stack order, using one input snapshot and
    /// one time per layer. World pixels are sampled before device-local masking.
    /// </summary>
    public IReadOnlyDictionary<string, StudioDeviceFrame> RenderTimedDeviceFrames(
        IReadOnlyList<(StudioLayerRender Layer, double Seconds)> layers,
        StudioEffectInput input = default,
        IReadOnlyDictionary<string, StudioKeyState>? deviceKeys = null)
    {
        if (layers.Count != MappedLayers.Length)
            throw new ArgumentException("The timed layers do not match this canvas.", nameof(layers));
        var frames = Devices.ToDictionary(device => device.InstanceId,
            device => new StudioDeviceFrame(device.MatrixRows, device.MatrixColumns,
                new StudioRgb[device.MatrixRows * device.MatrixColumns]), StringComparer.OrdinalIgnoreCase);
        for (var index = layers.Count - 1; index >= 0; index--)
        {
            var (layer, seconds) = layers[index];
            if (!layer.Enabled || layer.Opacity <= 0) continue;
            var time = double.IsFinite(seconds) ? Math.Max(0, seconds) : 0;
            var mapped = IsMappedAmbient(layer.Params);
            // Reactive belongs to physical keys even when device drawings overlap.
            var world = UsesLegacyWorld && !mapped && !IsKeyFade(layer.Params)
                ? StudioEffectRenderer.Render(layer.Params, Rows, Columns, time,
                    input with { TriggerMask = layer.Mask }, layer.State)
                : null;
            foreach (var target in MappedLayers[index].Targets)
            {
                var device = target.Device;
                if (layer.Params.Effect == StudioEffectKind.ChromaFile && device.MatrixRows <= 1) continue;
                var localInput = input;
                if (deviceKeys?.TryGetValue(device.InstanceId, out var keys) == true)
                    localInput = input with { Keys = keys };
                localInput = localInput with { TriggerMask = target.Mask };
                var effect = world ?? (mapped
                    ? StudioEffectRenderer.RenderMappedAmbient(layer.Params, device.MatrixRows,
                        device.MatrixColumns, target.Coordinates!, localInput)
                    : StudioEffectRenderer.Render(layer.Params, device.MatrixRows, device.MatrixColumns,
                        time, localInput, layer.State?.Scope(device.InstanceId)));
                var frame = frames[device.InstanceId].Frame;
                var alpha = Math.Clamp(layer.Opacity / 100, 0, 1);
                for (var pixel = 0; pixel < frame.Length; pixel++)
                {
                    var mask = target.Mask is null ? 1 : target.Mask[pixel] / 100d;
                    var color = effect[world is null ? pixel : device.WorldIndices[pixel]];
                    frame[pixel] = StudioCompositor.Blend(frame[pixel], color, alpha * mask, layer.Blend);
                }
            }
        }
        return frames;
    }

    internal static double Normalize(double value, double min, double max) =>
        max > min ? Math.Clamp((value - min) / (max - min), 0, 1) : 0.5;
}

public sealed partial class StudioWorldDevice
{
    public (double U, double V)[] LocalCoordinates { get; private set; } = [];
    public (double X, double Y)[] LedPositions { get; private set; } = [];
    public (double MinX, double MinY, double MaxX, double MaxY) LedBounds { get; private set; }

    private void BuildLedCoordinates(StudioWorldCanvasDevice source,
        (double X, double Y, double Width, double Height) rect)
    {
        var centers = source.Visual.Leds
            .Where(led => (uint)led.MatrixRow < (uint)MatrixRows && (uint)led.MatrixCol < (uint)MatrixColumns)
            .Where(led => double.IsFinite(led.CenterX) && double.IsFinite(led.CenterY))
            // SVG fallback centres span the chassis; use logical coordinates for those cells.
            .Where(led => !source.Visual.UsesSvg || source.Visual.Paths.Count == 0 || led.PathId is not null)
            .GroupBy(led => (led.MatrixRow, led.MatrixCol))
            .ToDictionary(group => group.Key, group => group.First());
        var minX = centers.Count > 0 ? centers.Values.Min(led => led.CenterX) : 0;
        var minY = centers.Count > 0 ? centers.Values.Min(led => led.CenterY) : 0;
        var maxX = centers.Count > 0 ? centers.Values.Max(led => led.CenterX) : MatrixColumns - 1;
        var maxY = centers.Count > 0 ? centers.Values.Max(led => led.CenterY) : MatrixRows - 1;
        var width = source.Width ?? source.Visual.Width;
        var height = source.Height ?? source.Visual.Height;
        LocalCoordinates = new (double, double)[MatrixRows * MatrixColumns];
        LedPositions = new (double, double)[LocalCoordinates.Length];
        for (var row = 0; row < MatrixRows; row++)
        {
            for (var column = 0; column < MatrixColumns; column++)
            {
                var u = StudioWorldCanvas.Normalize(column, 0, MatrixColumns - 1);
                var v = StudioWorldCanvas.Normalize(row, 0, MatrixRows - 1);
                var x = minX + u * (maxX - minX);
                var y = minY + v * (maxY - minY);
                if (centers.TryGetValue((row, column), out var led))
                {
                    x = led.CenterX;
                    y = led.CenterY;
                    u = StudioWorldCanvas.Normalize(x, minX, maxX);
                    v = StudioWorldCanvas.Normalize(y, minY, maxY);
                }
                var index = row * MatrixColumns + column;
                LocalCoordinates[index] = (u, v);
                // Missing geometry uses matrix centres, never the chassis/palm-rest bounds.
                var logicalPitch = width / MatrixColumns;
                var localX = centers.Count > 0 ? x / source.Visual.Width * width : (column + 0.5) * logicalPitch;
                var localY = centers.Count > 0 ? y / source.Visual.Height * height : (row + 0.5) * logicalPitch;
                LedPositions[index] = ToWorld(localX, localY, width, height,
                    StudioWorldCanvas.NormalizeRotation(source.Rotation), rect.X, rect.Y, rect.Width, rect.Height);
            }
        }
        var physical = centers.Count > 0
            ? centers.Keys.Select(cell => LedPositions[cell.MatrixRow * MatrixColumns + cell.MatrixCol]).ToArray()
            : LedPositions;
        LedBounds = (physical.Min(point => point.X), physical.Min(point => point.Y),
            physical.Max(point => point.X), physical.Max(point => point.Y));
    }
}
