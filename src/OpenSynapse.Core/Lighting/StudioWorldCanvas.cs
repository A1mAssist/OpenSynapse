namespace OpenSynapse.Core.Lighting;

/// <summary>One device as it participates in the shared studio world.</summary>
public sealed record StudioWorldCanvasDevice(
    string InstanceId,
    ushort ProductId,
    StudioDeviceVisual Visual,
    double? X,
    double? Y,
    int Rotation,
    int MatrixRows,
    int MatrixColumns,
    int DeviceRows,
    int DeviceColumns,
    double? Width = null,
    double? Height = null);

/// <summary>Matrix-to-world sampling for one target, including an ARGB channel row.</summary>
public sealed class StudioWorldSampleMap
{
    public StudioWorldSampleMap(int rows, int columns, int[] worldIndices)
    {
        Rows = rows;
        Columns = columns;
        WorldIndices = worldIndices;
    }

    public int Rows { get; }

    public int Columns { get; }

    /// <summary>World frame index per target cell, or -1 when unmapped.</summary>
    public int[] WorldIndices { get; }
}

/// <summary>
/// Cached world and LED geometry. Legacy effects use the square world grid;
/// explicit Ambient mappings compose layers into separate device frames.
/// </summary>
public sealed partial class StudioWorldCanvas
{
    // Preview and hardware each render this grid. 96 keeps five heavy layers
    // around 2.5 ms/frame; finer grids cost more than the LED sampling gains.
    public const int GridSize = 96;
    private const double GridPadding = 4;
    private const double DefaultGap = 120;

    private readonly Dictionary<string, StudioWorldDevice> _byInstance;

    private StudioWorldCanvas(
        bool active,
        double minX,
        double minY,
        double width,
        double height,
        double scale,
        double offsetX,
        double offsetY,
        IReadOnlyList<StudioWorldDevice> devices,
        StudioLayerRender[] layers)
    {
        Active = active;
        MinX = minX;
        MinY = minY;
        Width = width;
        Height = height;
        Scale = scale;
        OffsetX = offsetX;
        OffsetY = offsetY;
        Devices = devices;
        Layers = layers;
        _byInstance = new Dictionary<string, StudioWorldDevice>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in devices) _byInstance[device.InstanceId] = device;
    }

    /// <summary>True when the legacy world or explicit per-layer mapping owns device rendering.</summary>
    public bool Active { get; }

    public int Rows => GridSize;

    public int Columns => GridSize;

    public double MinX { get; }

    public double MinY { get; }

    public double Width { get; }

    public double Height { get; }

    public double Scale { get; }

    public double OffsetX { get; }

    public double OffsetY { get; }

    public IReadOnlyList<StudioWorldDevice> Devices { get; }

    public StudioLayerRender[] Layers { get; }

    public static StudioWorldCanvas Empty { get; } = new(
        active: false, 0, 0, 1, 1, 1, 0, 0, [], []);

    public static StudioWorldCanvas Build(
        IReadOnlyList<StudioWorldCanvasDevice> devices,
        StudioProfile? profile)
    {
        ArgumentNullException.ThrowIfNull(devices);

        var usable = devices
            .Where(device => !string.IsNullOrWhiteSpace(device.InstanceId))
            .Where(device => device.MatrixRows > 0 && device.MatrixColumns > 0)
            .Where(device => device.DeviceRows > 0 && device.DeviceColumns > 0)
            .Where(device => (long)device.MatrixRows * device.MatrixColumns <= StudioCompositor.MaxPixels)
            .Where(device => double.IsFinite(device.Visual.Width) && double.IsFinite(device.Visual.Height) &&
                device.Visual.Width > 0 && device.Visual.Height > 0)
            .ToArray();
        if (usable.Length == 0) return Empty;

        var placements = ResolvePlacements(usable);
        var rects = new (double X, double Y, double Width, double Height)[usable.Length];
        for (var index = 0; index < usable.Length; index++)
        {
            var device = usable[index];
            var rotation = NormalizeRotation(device.Rotation);
            var (visualWidth, visualHeight) = StudioCanvasPlacement.VisualExtent(
                device.Width ?? device.Visual.Width, device.Height ?? device.Visual.Height, rotation);
            var placement = placements[index];
            rects[index] = (placement.X, placement.Y, visualWidth, visualHeight);
        }

        var minX = rects.Min(rect => rect.X);
        var minY = rects.Min(rect => rect.Y);
        var maxX = rects.Max(rect => rect.X + rect.Width);
        var maxY = rects.Max(rect => rect.Y + rect.Height);
        var worldWidth = Math.Max(1, maxX - minX);
        var worldHeight = Math.Max(1, maxY - minY);

        var available = GridSize - GridPadding * 2;
        var scale = Math.Min(available / worldWidth, available / worldHeight);
        if (!double.IsFinite(scale) || scale <= 0) scale = 1;
        var offsetX = (GridSize - worldWidth * scale) / 2;
        var offsetY = (GridSize - worldHeight * scale) / 2;

        var worldDevices = new StudioWorldDevice[usable.Length];
        for (var index = 0; index < usable.Length; index++)
        {
            worldDevices[index] = StudioWorldDevice.Create(
                usable[index],
                placements[index],
                rects[index],
                minX,
                minY,
                scale,
                offsetX,
                offsetY);
        }

        var hasChromaFile = false;
        var layers = BuildLayers(profile, worldDevices, ref hasChromaFile);
        var legacyWorld = worldDevices.Length >= 2 && !hasChromaFile;
        var mappedLayers = BuildMappedLayers(profile, worldDevices);
        var usesLayerMapping = mappedLayers.Any(layer =>
            IsMappedAmbient(layer.Render.Params) || IsKeyFade(layer.Render.Params));
        var active = legacyWorld || usesLayerMapping;
        return new StudioWorldCanvas(
            active, minX, minY, worldWidth, worldHeight, scale, offsetX, offsetY, worldDevices, layers)
        {
            UsesLayerMapping = usesLayerMapping,
            UsesLegacyWorld = legacyWorld,
            MappedLayers = mappedLayers,
        };
    }

    public bool HasDevice(string? instanceId) =>
        !string.IsNullOrWhiteSpace(instanceId) && _byInstance.ContainsKey(instanceId);

    public StudioWorldDevice? FindDevice(string? instanceId) =>
        !string.IsNullOrWhiteSpace(instanceId) && _byInstance.TryGetValue(instanceId, out var device)
            ? device
            : null;

    /// <summary>
    /// Builds the sampling table for one router target. A 1x1 target samples
    /// the device centre; a 1xN target uses <paramref name="rowOffset"/> so an
    /// ARGB channel can sample its own strip.
    /// </summary>
    public bool TryGetSampleMap(
        string? instanceId,
        int rows,
        int columns,
        int rowOffset,
        out StudioWorldSampleMap map)
    {
        map = null!;
        if (rows <= 0 || columns <= 0 || FindDevice(instanceId) is not { } device) return false;

        var indices = new int[rows * columns];
        if (rows == 1 && columns == 1 && device.MatrixRows * device.MatrixColumns > 1)
        {
            indices[0] = device.CenterWorldIndex;
            map = new StudioWorldSampleMap(rows, columns, indices);
            return true;
        }

        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var sourceRow = row + rowOffset;
                var sourceColumn = column;
                if (sourceRow >= device.MatrixRows || sourceColumn >= device.MatrixColumns)
                {
                    sourceRow = rows == 1
                        ? Math.Clamp(rowOffset, 0, device.MatrixRows - 1)
                        : MapIndex(row, rows, device.MatrixRows);
                    sourceColumn = MapIndex(column, columns, device.MatrixColumns);
                }

                indices[row * columns + column] = device.WorldIndex(sourceRow, sourceColumn);
            }
        }

        map = new StudioWorldSampleMap(rows, columns, indices);
        return true;
    }

    /// <summary>Maps a device key matrix coordinate into the world frame.</summary>
    public bool TryGetWorldIndex(
        string? instanceId,
        int matrixRow,
        int matrixColumn,
        out int worldIndex)
    {
        worldIndex = -1;
        if (FindDevice(instanceId) is not { } device) return false;
        if (device.MatrixRows * device.MatrixColumns == 1) worldIndex = device.CenterWorldIndex;
        else worldIndex = device.WorldIndex(matrixRow, matrixColumn);
        return worldIndex >= 0;
    }

    private static int MapIndex(int value, int count, int targetCount)
    {
        if (count <= 1 || targetCount <= 1) return 0;
        return Math.Clamp(
            (int)Math.Round(value * (targetCount - 1d) / (count - 1d)),
            0,
            targetCount - 1);
    }

    private static (double X, double Y)[] ResolvePlacements(
        IReadOnlyList<StudioWorldCanvasDevice> devices)
    {
        var result = new (double X, double Y)[devices.Count];
        var unplaced = new List<(int Index, double Width, double Height)>();
        for (var index = 0; index < devices.Count; index++)
        {
            var device = devices[index];
            var rotation = NormalizeRotation(device.Rotation);
            var (visualWidth, visualHeight) = StudioCanvasPlacement.VisualExtent(
                device.Width ?? device.Visual.Width, device.Height ?? device.Visual.Height, rotation);
            if (device.X is { } x && device.Y is { } y && double.IsFinite(x) && double.IsFinite(y))
            {
                result[index] = (
                    Math.Clamp(x, 0, StudioCanvasPlacement.MaxStart(visualWidth)),
                    Math.Clamp(y, 0, StudioCanvasPlacement.MaxStart(visualHeight)));
            }
            else
            {
                unplaced.Add((index, visualWidth, visualHeight));
            }
        }

        var defaults = StudioCanvasPlacement.CenterRow(
            unplaced.Select(entry => (entry.Width, entry.Height)).ToArray(),
            DefaultGap);
        for (var index = 0; index < unplaced.Count; index++)
        {
            result[unplaced[index].Index] = defaults[index];
        }

        return result;
    }

    private static StudioLayerRender[] BuildLayers(
        StudioProfile? profile,
        IReadOnlyList<StudioWorldDevice> devices,
        ref bool hasChromaFile)
    {
        if (profile is null) return [];

        var result = new List<StudioLayerRender>();
        foreach (var layer in profile.EnumerateLayers())
        {
            if (layer.IsGroup) continue;
            if (layer.Visible && layer.Params.Effect == StudioEffectKind.ChromaFile) hasChromaFile = true;

            if (layer.Regions.Count == 0)
            {
                result.Add(new StudioLayerRender(
                    layer.Params, null, layer.Opacity, layer.Blend, layer.Visible, layer.Id));
                continue;
            }

            foreach (var region in layer.Regions)
            {
                if (region.Devices.Count == 0) continue;
                if (layer.Visible && region.Params.Effect == StudioEffectKind.ChromaFile)
                    hasChromaFile = true;

                var mask = new byte[GridSize * GridSize];
                foreach (var target in region.Devices)
                {
                    foreach (var device in devices)
                    {
                        if (target.ProductId != device.ProductId) continue;
                        if (!string.IsNullOrWhiteSpace(target.InstanceId) &&
                            !StudioLedSelection.SameInstance(target.InstanceId, device.InstanceId))
                        {
                            continue;
                        }

                        if (target.Leds.Count == 0)
                        {
                            foreach (var worldIndex in device.AllWorldIndices)
                                mask[worldIndex] = StudioMaskBuilder.Full;
                            continue;
                        }

                        foreach (var led in target.Leds)
                        {
                            var worldIndex = device.WorldIndex(led.Row, led.Col);
                            if (worldIndex >= 0) mask[worldIndex] = StudioMaskBuilder.Full;
                        }
                    }
                }

                if (mask.Any(value => value != 0))
                {
                    result.Add(new StudioLayerRender(
                        region.Params, mask, layer.Opacity, layer.Blend, layer.Visible, region.PlaybackId, TimebaseId: layer.Id));
                }
            }
        }

        return result.ToArray();
    }

    internal static int NormalizeRotation(int rotation) => rotation is 90 or 180 or 270 ? rotation : 0;
}

/// <summary>One device's immutable world lookup tables.</summary>
public sealed partial class StudioWorldDevice
{
    private StudioWorldDevice(
        string instanceId,
        ushort productId,
        int matrixRows,
        int matrixColumns,
        int deviceRows,
        int deviceColumns,
        StudioLedLayout? layout,
        int[] worldIndices,
        int[] allWorldIndices,
        int centerWorldIndex)
    {
        InstanceId = instanceId;
        ProductId = productId;
        MatrixRows = matrixRows;
        MatrixColumns = matrixColumns;
        DeviceRows = deviceRows;
        DeviceColumns = deviceColumns;
        Layout = layout;
        WorldIndices = worldIndices;
        AllWorldIndices = allWorldIndices;
        CenterWorldIndex = centerWorldIndex;
    }

    public string InstanceId { get; }

    public ushort ProductId { get; }

    public int MatrixRows { get; }

    public int MatrixColumns { get; }

    public int DeviceRows { get; }

    public int DeviceColumns { get; }

    public StudioLedLayout? Layout { get; }

    public int[] WorldIndices { get; }

    public int[] AllWorldIndices { get; }

    public int CenterWorldIndex { get; }

    public int WorldIndex(int matrixRow, int matrixColumn)
    {
        if ((uint)matrixRow >= (uint)MatrixRows || (uint)matrixColumn >= (uint)MatrixColumns) return -1;
        return WorldIndices[matrixRow * MatrixColumns + matrixColumn];
    }

    internal static StudioWorldDevice Create(
        StudioWorldCanvasDevice source,
        (double X, double Y) placement,
        (double X, double Y, double Width, double Height) rect,
        double worldMinX,
        double worldMinY,
        double scale,
        double offsetX,
        double offsetY)
    {
        var rotation = StudioWorldCanvas.NormalizeRotation(source.Rotation);
        // A saved placement size wins over the manifest's natural size, so a
        // reopened project keeps the layout the user arranged.
        var visualWidth = source.Visual.Width;
        var visualHeight = source.Visual.Height;
        var width = source.Width ?? visualWidth;
        var height = source.Height ?? visualHeight;
        var centers = new Dictionary<(int Row, int Column), (double X, double Y)>();
        foreach (var led in source.Visual.Leds)
        {
            if (double.IsFinite(led.CenterX) && double.IsFinite(led.CenterY))
            {
                centers.TryAdd((led.MatrixRow, led.MatrixCol), (led.CenterX, led.CenterY));
            }
        }

        var worldIndices = new int[source.MatrixRows * source.MatrixColumns];
        var distinct = new HashSet<int>();
        for (var row = 0; row < source.MatrixRows; row++)
        {
            for (var column = 0; column < source.MatrixColumns; column++)
            {
                if (!centers.TryGetValue((row, column), out var center))
                {
                    center = (
                        (column + 0.5) / Math.Max(1, source.MatrixColumns) * visualWidth,
                        (row + 0.5) / Math.Max(1, source.MatrixRows) * visualHeight);
                }

                var sourceX = Math.Clamp(center.X, 0, visualWidth);
                var sourceY = Math.Clamp(center.Y, 0, visualHeight);
                var localX = visualWidth > 0 ? sourceX / visualWidth * width : sourceX;
                var localY = visualHeight > 0 ? sourceY / visualHeight * height : sourceY;
                var (worldX, worldY) = ToWorld(
                    localX, localY, width, height, rotation, rect.X, rect.Y, rect.Width, rect.Height);
                var worldIndex = Project(
                    worldX, worldY, worldMinX, worldMinY, scale, offsetX, offsetY);
                worldIndices[row * source.MatrixColumns + column] = worldIndex;
                distinct.Add(worldIndex);
            }
        }

        var (centerX, centerY) = ToWorld(
            width / 2, height / 2, width, height, rotation, rect.X, rect.Y, rect.Width, rect.Height);
        var centerWorldIndex = Project(
            centerX, centerY, worldMinX, worldMinY, scale, offsetX, offsetY);
        var device = new StudioWorldDevice(
            source.InstanceId,
            source.ProductId,
            source.MatrixRows,
            source.MatrixColumns,
            source.DeviceRows,
            source.DeviceColumns,
            source.Visual.Layout,
            worldIndices,
            distinct.ToArray(),
            centerWorldIndex);
        device.BuildLedCoordinates(source, rect);
        return device;
    }

    private static (double X, double Y) ToWorld(
        double localX,
        double localY,
        double width,
        double height,
        int rotation,
        double visualX,
        double visualY,
        double visualWidth,
        double visualHeight)
    {
        var dx = localX - width / 2;
        var dy = localY - height / 2;
        var (rotatedX, rotatedY) = rotation switch
        {
            90 => (-dy, dx),
            180 => (-dx, -dy),
            270 => (dy, -dx),
            _ => (dx, dy),
        };
        return (visualX + visualWidth / 2 + rotatedX, visualY + visualHeight / 2 + rotatedY);
    }

    private static int Project(
        double worldX,
        double worldY,
        double minX,
        double minY,
        double scale,
        double offsetX,
        double offsetY)
    {
        var column = (int)Math.Floor(offsetX + (worldX - minX) * scale);
        var row = (int)Math.Floor(offsetY + (worldY - minY) * scale);
        column = Math.Clamp(column, 0, StudioWorldCanvas.GridSize - 1);
        row = Math.Clamp(row, 0, StudioWorldCanvas.GridSize - 1);
        return row * StudioWorldCanvas.GridSize + column;
    }
}
