using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Protocols;
using System.Diagnostics;

namespace OpenSynapse.Windows.Lighting;

/// <summary>
/// Owns the shared world frame. One world source paints the whole canvas on a
/// fixed square grid; every router target samples its own LEDs from that frame.
/// Preview uses a separate source so preview input cannot move hardware clocks.
/// </summary>
public sealed class StudioWorldRenderer
{
    private readonly object _gate = new();
    private readonly Func<int, int, StudioEffectInput>? _inputProvider;
    private readonly Stopwatch _outputClock = Stopwatch.StartNew();
    private StudioWorldCanvas? _canvas;
    private StudioDeviceFrameSource? _outputSource;
    private StudioDeviceFrameSource? _previewSource;
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
                return;
            }

            _canvas = canvas;
            _outputSource ??= new StudioDeviceFrameSource(canvas.Rows, canvas.Columns, _inputProvider);
            _previewSource ??= new StudioDeviceFrameSource(canvas.Rows, canvas.Columns, _inputProvider);
            _outputSource.Update(canvas.Layers);
            _previewSource.Update(canvas.Layers);
            _outputCache = null;
            _previewCache = null;
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

    /// <summary>Creates a router source when this world owns the target; null means use the old per-device path.</summary>
    public StudioDeviceFrameSource? CreateSource(
        string? instanceId,
        int matrixRows,
        int matrixColumns,
        int deviceRows,
        int deviceColumns,
        StudioLedLayout? layout,
        int matrixRowOffset = 0)
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

        var frame = source.RenderAsync(elapsed, CancellationToken.None)
            .AsTask().GetAwaiter().GetResult().ToArray();
        if (preview) _previewCache = (elapsed, frame);
        else _outputCache = (elapsed, frame);
        return frame;
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

/// <summary>One router target sampled from the shared world frame.</summary>
internal sealed class StudioWorldDeviceFrameSource : StudioDeviceFrameSource
{
    private readonly StudioWorldRenderer _world;
    private readonly string _instanceId;
    private readonly int _matrixRows;
    private readonly int _matrixColumns;
    private readonly int _deviceRows;
    private readonly int _deviceColumns;
    private readonly StudioLedLayout? _layout;
    private readonly int _rowOffset;

    public StudioWorldDeviceFrameSource(
        StudioWorldRenderer world,
        string instanceId,
        int matrixRows,
        int matrixColumns,
        int deviceRows,
        int deviceColumns,
        StudioLedLayout? layout,
        int rowOffset)
        : base(1, 1)
    {
        _world = world;
        _instanceId = instanceId;
        _matrixRows = Math.Max(1, matrixRows);
        _matrixColumns = Math.Max(1, matrixColumns);
        _deviceRows = Math.Max(1, deviceRows);
        _deviceColumns = Math.Max(1, deviceColumns);
        _layout = layout;
        _rowOffset = Math.Max(0, rowOffset);
    }

    // The world renderer already owns the world-space layer stack.
    public override void Update(IEnumerable<StudioLayerRender> layers)
    {
    }

    public override ValueTask<IReadOnlyList<RazerRgb>> RenderAsync(
        TimeSpan elapsed,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var world = _world.RenderOutputFrame(elapsed);
        return ValueTask.FromResult<IReadOnlyList<RazerRgb>>(Sample(world));
    }

    public override int HandleInput(StudioTriggerInput input, double nowSeconds) =>
        _world.HandleOutputInput(_instanceId, input, nowSeconds, _rowOffset);

    public override int TriggerPlayback(StudioTriggerInput input, double nowSeconds) =>
        _world.TriggerOutputPlayback(_instanceId, input, nowSeconds, _rowOffset);

    internal override void RestoreHeldKeys(IEnumerable<(int Row, int Column)> keys) =>
        _world.RestoreOutputHeldKeys(_instanceId, keys, _rowOffset);

    private RazerRgb[] Sample(IReadOnlyList<RazerRgb> world)
    {
        var colors = new RazerRgb[_deviceRows * _deviceColumns];
        if (world.Count == 0 ||
            !_world.TryGetSampleMap(_instanceId, _matrixRows, _matrixColumns, _rowOffset, out var map))
        {
            return colors;
        }

        if (_layout is null)
        {
            var count = Math.Min(map.WorldIndices.Length, colors.Length);
            for (var index = 0; index < count; index++)
            {
                var worldIndex = map.WorldIndices[index];
                if ((uint)worldIndex < (uint)world.Count) colors[index] = world[worldIndex];
            }

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

            var worldIndex = map.WorldIndices[led.MatrixRow * _matrixColumns + led.MatrixCol];
            if ((uint)worldIndex < (uint)world.Count)
            {
                colors[led.DeviceRow * _deviceColumns + led.DeviceCol] = world[worldIndex];
            }
        }

        return colors;
    }
}
