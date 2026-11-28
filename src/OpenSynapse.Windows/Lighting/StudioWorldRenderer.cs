using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Protocols;
using System.Diagnostics;

namespace OpenSynapse.Windows.Lighting;

/// <summary>
/// Owns one timeline and input snapshot per role. Legacy projects sample a
/// shared world frame; explicit Ambient mappings retain separate device frames.
/// Preview input cannot move hardware clocks.
/// </summary>
public sealed partial class StudioWorldRenderer
{
    private readonly object _gate = new();
    private readonly Func<int, int, StudioEffectInput>? _inputProvider;
    private readonly Stopwatch _outputClock = Stopwatch.StartNew();
    private StudioWorldCanvas? _canvas;
    private StudioDeviceFrameSource? _outputSource;
    private StudioDeviceFrameSource? _previewSource;
    private LayerPlayback? _outputPlayback;
    private LayerPlayback? _previewPlayback;
    private IReadOnlyDictionary<string, StudioDeviceFrame>? _outputDevices;
    private IReadOnlyDictionary<string, StudioDeviceFrame>? _previewDevices;
    private (TimeSpan Elapsed, RazerRgb[] Frame)? _outputCache;
    private (TimeSpan Elapsed, RazerRgb[] Frame)? _previewCache;
    private long _outputFrameSlot = -1;
    private string _signature = string.Empty;
    private long _generation;

    public StudioWorldRenderer(Func<int, int, StudioEffectInput>? inputProvider = null)
    {
        _inputProvider = inputProvider;
    }

    public bool Active
    {
        get
        {
            lock (_gate) return _canvas?.Active == true;
        }
    }

    /// <summary>
    /// Changes only when the active state or the set of world devices changes.
    /// Routers use it to replace a per-device source with a world source.
    /// </summary>
    public long Generation
    {
        get
        {
            lock (_gate) return _generation;
        }
    }

    public StudioWorldCanvas? Canvas
    {
        get
        {
            lock (_gate) return _canvas;
        }
    }

    public void Update(StudioWorldCanvas? canvas)
    {
        lock (_gate)
        {
            var nextSignature = Signature(canvas);
            if (!string.Equals(nextSignature, _signature, StringComparison.Ordinal))
            {
                _signature = nextSignature;
                _generation++;
            }

            if (canvas is not { Active: true })
            {
                _canvas = null;
                _outputSource = null;
                _previewSource = null;
                _outputCache = null;
                _previewCache = null;
                _outputPlayback = null;
                _previewPlayback = null;
                _outputDevices = null;
                _previewDevices = null;
                return;
            }

            _canvas = canvas;
            _outputSource ??= new StudioDeviceFrameSource(canvas.Rows, canvas.Columns, _inputProvider);
            _previewSource ??= new StudioDeviceFrameSource(canvas.Rows, canvas.Columns, _inputProvider);
            if (canvas.UsesLayerMapping)
            {
                _outputPlayback ??= new LayerPlayback(_outputSource);
                _previewPlayback ??= new LayerPlayback(_previewSource);
                _outputPlayback.Update(canvas);
                _previewPlayback.Update(canvas);
            }
            var activeLayers = canvas.UsesLayerMapping
                ? canvas.MappedLayers.Select(layer => layer.Render with { Mask = null }).ToArray()
                : canvas.Layers;
            _outputSource.Update(activeLayers);
            _previewSource.Update(activeLayers);
            if (!canvas.UsesLayerMapping)
            {
                _outputPlayback = null;
                _previewPlayback = null;
            }
            _outputCache = null;
            _previewCache = null;
            _outputDevices = null;
            _previewDevices = null;
        }
    }

    /// <summary>Starts a fresh hardware timeline without recreating the preview source.</summary>
    public void RestartOutputClock()
    {
        lock (_gate)
        {
            _outputClock.Restart();
            _outputFrameSlot = -1;
            _outputCache = null;
        }
    }

    /// <summary>
    /// Creates a router source when this world owns the target; null means use the old per-device path.
    /// An explicit matrixRowOffset identifies a channel slice, including single-LED channel zero.
    /// </summary>
    public StudioDeviceFrameSource? CreateSource(
        string? instanceId,
        int matrixRows,
        int matrixColumns,
        int deviceRows,
        int deviceColumns,
        StudioLedLayout? layout,
        int? matrixRowOffset = null)
    {
        lock (_gate)
        {
            if (_canvas is not { Active: true } canvas || !canvas.HasDevice(instanceId)) return null;
            return new StudioWorldDeviceFrameSource(
                this,
                instanceId!,
                matrixRows,
                matrixColumns,
                deviceRows,
                deviceColumns,
                layout,
                matrixRowOffset);
        }
    }

    /// <summary>
    /// Hardware frame at the shared world clock. Blade's runtime and the HID
    /// pump have independent timebases, so the world owns one 25 fps timeline.
    /// </summary>
    public RazerRgb[] RenderOutputFrame(TimeSpan _)
    {
        lock (_gate)
        {
            var frameSeconds = 1d / StudioEffectRenderer.NativeFramesPerSecond;
            var now = _outputClock.Elapsed;
            var slot = (long)Math.Floor(now.TotalSeconds / frameSeconds);
            if (_outputCache is { } cached)
            {
                // Four routers plus Blade's runtime hit the same tick. Hold the
                // previous slot briefly after a boundary so one tick cannot split.
                var sinceSlotStart = now.TotalSeconds - slot * frameSeconds;
                if (slot == _outputFrameSlot ||
                    (slot == _outputFrameSlot + 1 && sinceSlotStart < 0.015))
                {
                    return cached.Frame;
                }
            }

            var elapsed = TimeSpan.FromSeconds(slot * frameSeconds);
            var frame = RenderFrameLocked(elapsed, preview: false);
            _outputFrameSlot = slot;
            return frame;
        }
    }

    public RazerRgb[] RenderPreviewFrame(TimeSpan elapsed)
    {
        lock (_gate) return RenderFrameLocked(elapsed, preview: true);
    }

    /// <summary>Preview frames in each device's logical matrix dimensions.</summary>
    public IReadOnlyDictionary<string, StudioDeviceFrame> RenderDeviceFrames(TimeSpan elapsed)
    {
        lock (_gate)
        {
            var frames = new Dictionary<string, StudioDeviceFrame>(StringComparer.OrdinalIgnoreCase);
            if (_canvas is not { } canvas) return frames;
            var world = RenderFrameLocked(elapsed, preview: true);
            if (canvas.UsesLayerMapping) return _previewDevices!;
            foreach (var device in canvas.Devices)
            {
                var colors = new StudioRgb[device.MatrixRows * device.MatrixColumns];
                for (var index = 0; index < colors.Length; index++)
                {
                    var worldIndex = device.WorldIndices[index];
                    if ((uint)worldIndex >= (uint)world.Length) continue;
                    var color = world[worldIndex];
                    colors[index] = new StudioRgb(color.Red, color.Green, color.Blue);
                }

                frames[device.InstanceId] = new StudioDeviceFrame(
                    device.MatrixRows, device.MatrixColumns, colors);
            }

            return frames;
        }
    }

    public int HandlePreviewInput(string? instanceId, StudioTriggerInput input, double seconds)
    {
        lock (_gate)
        {
            if (_canvas?.UsesLayerMapping == true)
                return _previewPlayback!.HandleInput(instanceId, input, seconds, 0);
            if (_previewSource is null || !TryMapInput(instanceId, input, rowOffset: 0, out var mapped)) return 0;
            return _previewSource.HandleInput(mapped, seconds);
        }
    }

    internal int HandleOutputInput(
        string? instanceId,
        StudioTriggerInput input,
        double seconds,
        int rowOffset)
    {
        lock (_gate)
        {
            if (_canvas?.UsesLayerMapping == true)
                return _outputPlayback!.HandleInput(instanceId, input, _outputClock.Elapsed.TotalSeconds, rowOffset);
            if (_outputSource is null || !TryMapInput(instanceId, input, rowOffset, out var mapped)) return 0;
            return _outputSource.HandleInput(mapped, _outputClock.Elapsed.TotalSeconds);
        }
    }

    internal int TriggerOutputPlayback(
        string? instanceId,
        StudioTriggerInput input,
        double seconds,
        int rowOffset)
    {
        lock (_gate)
        {
            if (_canvas?.UsesLayerMapping == true)
                return _outputPlayback!.HandleInput(instanceId, input, _outputClock.Elapsed.TotalSeconds,
                    rowOffset, playbackOnly: true);
            if (_outputSource is null || !TryMapInput(instanceId, input, rowOffset, out var mapped)) return 0;
            return _outputSource.TriggerPlayback(mapped, _outputClock.Elapsed.TotalSeconds);
        }
    }

    internal void RestoreOutputHeldKeys(
        string? instanceId,
        IEnumerable<(int Row, int Column)> keys,
        int rowOffset)
    {
        lock (_gate)
        {
            if (_canvas?.UsesLayerMapping == true)
            {
                _outputPlayback!.RestoreHeldKeys(instanceId, keys, rowOffset);
                return;
            }
            if (_outputSource is null || _canvas is not { } canvas) return;
            var mapped = new List<(int Row, int Column)>();
            foreach (var (row, column) in keys)
            {
                if (!canvas.TryGetWorldIndex(instanceId, row + rowOffset, column, out var worldIndex)) continue;
                mapped.Add((worldIndex / canvas.Columns, worldIndex % canvas.Columns));
            }

            if (mapped.Count > 0) _outputSource.RestoreHeldKeys(mapped);
        }
    }

    internal bool TryGetSampleMap(
        string? instanceId,
        int rows,
        int columns,
        int rowOffset,
        out StudioWorldSampleMap map)
    {
        lock (_gate)
        {
            if (_canvas is not { } canvas)
            {
                map = null!;
                return false;
            }

            return canvas.TryGetSampleMap(instanceId, rows, columns, rowOffset, out map);
        }
    }

    private RazerRgb[] RenderFrameLocked(TimeSpan elapsed, bool preview)
    {
        var source = preview ? _previewSource : _outputSource;
        if (source is null) return [];

        var cache = preview ? _previewCache : _outputCache;
        if (cache is { } cached && cached.Elapsed == elapsed) return cached.Frame;

        RazerRgb[] frame;
        if (_canvas is { UsesLayerMapping: true } canvas)
        {
            var input = _inputProvider?.Invoke(canvas.Rows, canvas.Columns) ?? default;
            var devices = (preview ? _previewPlayback : _outputPlayback)!.Render(elapsed.TotalSeconds, input);
            if (preview) _previewDevices = devices;
            else _outputDevices = devices;
            // Compatibility world preview only. Device output never samples this projection.
            frame = new RazerRgb[canvas.Rows * canvas.Columns];
            foreach (var device in canvas.Devices)
            {
                var colors = devices[device.InstanceId].Frame;
                for (var index = 0; index < colors.Length; index++)
                {
                    var color = colors[index];
                    frame[device.WorldIndices[index]] = new(color.R, color.G, color.B);
                }
            }
        }
        else
        {
            frame = source.RenderAsync(elapsed, CancellationToken.None)
                .AsTask().GetAwaiter().GetResult().ToArray();
        }
        if (preview) _previewCache = (elapsed, frame);
        else _outputCache = (elapsed, frame);
        return frame;
    }

    /// <summary>Logical target frame, before the existing HID/sparse layout conversion.</summary>
    internal RazerRgb[] RenderOutputDeviceFrame(string instanceId, int rows, int columns, int? rowOffset,
        TimeSpan elapsed)
    {
        lock (_gate)
        {
            var result = new RazerRgb[rows * columns];
            var world = RenderOutputFrame(elapsed);
            if (_canvas is not { } canvas) return result;
            if (!canvas.UsesLayerMapping)
            {
                if (!canvas.TryGetSampleMap(instanceId, rows, columns, rowOffset ?? 0, out var map)) return result;
                for (var index = 0; index < result.Length; index++)
                    if ((uint)map.WorldIndices[index] < (uint)world.Length) result[index] = world[map.WorldIndices[index]];
                return result;
            }
            if (_outputDevices is null || !_outputDevices.TryGetValue(instanceId, out var frame)) return result;
            for (var row = 0; row < rows; row++)
            {
                for (var column = 0; column < columns; column++)
                {
                    var sourceRow = Math.Clamp(row + (rowOffset ?? 0), 0, frame.Rows - 1);
                    var sourceColumn = Math.Clamp(column, 0, frame.Columns - 1);
                    if (rows == 1 && columns == 1 && rowOffset is null)
                    {
                        sourceRow = (frame.Rows - 1) / 2;
                        sourceColumn = (frame.Columns - 1) / 2;
                    }
                    var color = frame.Frame[sourceRow * frame.Columns + sourceColumn];
                    result[row * columns + column] = new(color.R, color.G, color.B);
                }
            }
            return result;
        }
    }

    private bool TryMapInput(
        string? instanceId,
        StudioTriggerInput input,
        int rowOffset,
        out StudioTriggerInput mapped)
    {
        mapped = default;
        if (_canvas is not { } canvas) return false;
        if (!canvas.TryGetWorldIndex(instanceId, input.Row + rowOffset, input.Column, out var worldIndex))
        {
            return false;
        }

        mapped = input with
        {
            Row = worldIndex / canvas.Columns,
            Column = worldIndex % canvas.Columns,
        };
        return true;
    }

    private static string Signature(StudioWorldCanvas? canvas)
    {
        if (canvas is not { Active: true }) return "off";
        var devices = canvas.Devices
            .OrderBy(device => device.InstanceId, StringComparer.OrdinalIgnoreCase)
            .Select(device =>
                $"{device.InstanceId}:{device.ProductId}:{device.MatrixRows}x{device.MatrixColumns}");
        return $"on|{canvas.Rows}x{canvas.Columns}|{string.Join(';', devices)}";
    }
}

/// <summary>One router target mapped from the renderer's logical device frame.</summary>
internal sealed class StudioWorldDeviceFrameSource : StudioDeviceFrameSource
{
    private readonly StudioWorldRenderer _world;
    private readonly string _instanceId;
    private readonly int _matrixRows;
    private readonly int _matrixColumns;
    private readonly int _deviceRows;
    private readonly int _deviceColumns;
    private readonly StudioLedLayout? _layout;
    private readonly int? _rowOffset;

    public StudioWorldDeviceFrameSource(
        StudioWorldRenderer world,
        string instanceId,
        int matrixRows,
        int matrixColumns,
        int deviceRows,
        int deviceColumns,
        StudioLedLayout? layout,
        int? rowOffset)
        : base(1, 1)
    {
        _world = world;
        _instanceId = instanceId;
        _matrixRows = Math.Max(1, matrixRows);
        _matrixColumns = Math.Max(1, matrixColumns);
        _deviceRows = Math.Max(1, deviceRows);
        _deviceColumns = Math.Max(1, deviceColumns);
        _layout = layout;
        _rowOffset = rowOffset is { } offset ? Math.Max(0, offset) : null;
    }

    // Update(canvas) owns the complete layer stack and device-specific mapping plan.
    public override void Update(IEnumerable<StudioLayerRender> layers)
    {
    }

    public override ValueTask<IReadOnlyList<RazerRgb>> RenderAsync(
        TimeSpan elapsed,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var logical = _world.RenderOutputDeviceFrame(
            _instanceId, _matrixRows, _matrixColumns, _rowOffset, elapsed);
        return ValueTask.FromResult<IReadOnlyList<RazerRgb>>(Sample(logical));
    }

    public override int HandleInput(StudioTriggerInput input, double nowSeconds) =>
        _world.HandleOutputInput(_instanceId, input, nowSeconds, _rowOffset ?? 0);

    public override int TriggerPlayback(StudioTriggerInput input, double nowSeconds) =>
        _world.TriggerOutputPlayback(_instanceId, input, nowSeconds, _rowOffset ?? 0);

    internal override void RestoreHeldKeys(IEnumerable<(int Row, int Column)> keys) =>
        _world.RestoreOutputHeldKeys(_instanceId, keys, _rowOffset ?? 0);

    private RazerRgb[] Sample(IReadOnlyList<RazerRgb> logical)
    {
        var colors = new RazerRgb[_deviceRows * _deviceColumns];
        if (_layout is null)
        {
            var count = Math.Min(logical.Count, colors.Length);
            for (var index = 0; index < count; index++)
                colors[index] = logical[index];

            return colors;
        }

        foreach (var led in _layout.Leds)
        {
            if ((uint)led.MatrixRow >= (uint)_matrixRows ||
                (uint)led.MatrixCol >= (uint)_matrixColumns ||
                (uint)led.DeviceRow >= (uint)_deviceRows ||
                (uint)led.DeviceCol >= (uint)_deviceColumns)
            {
                continue;
            }

            colors[led.DeviceRow * _deviceColumns + led.DeviceCol] =
                logical[led.MatrixRow * _matrixColumns + led.MatrixCol];
        }

        return colors;
    }
}
