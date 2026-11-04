using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Lighting;

/// <summary>
/// Renders the studio layer stack on the manifest matrix and maps the result
/// into the device's HID row/column frame. Every layer keeps its own playback
/// clock; without a layout the legacy direct device-matrix path is preserved.
/// </summary>
public sealed class StudioDeviceFrameSource : ISoftwareLightingFrameSource
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, StudioPlaybackClock> _clocks = new();
    private readonly int _matrixRows;
    private readonly int _matrixColumns;
    private readonly int _deviceRows;
    private readonly int _deviceColumns;
    private readonly StudioLedLayout? _layout;
    private readonly Func<int, int, StudioEffectInput>? _inputProvider;
    private readonly StudioPlaybackTrigger _trigger;
    private StudioLayerRender[] _layers = [];

    public StudioDeviceFrameSource(
        int rows, int columns, Func<int, int, StudioEffectInput>? inputProvider = null)
        : this(rows, columns, rows, columns, null, inputProvider)
    {
    }

    public StudioDeviceFrameSource(
        int matrixRows,
        int matrixColumns,
        int deviceRows,
        int deviceColumns,
        StudioLedLayout? layout,
        Func<int, int, StudioEffectInput>? inputProvider = null)
    {
        if (matrixRows < 1) throw new ArgumentOutOfRangeException(nameof(matrixRows));
        if (matrixColumns < 1) throw new ArgumentOutOfRangeException(nameof(matrixColumns));
        if ((long)matrixRows * matrixColumns > StudioCompositor.MaxPixels)
        {
            throw new ArgumentOutOfRangeException(nameof(matrixRows),
                "The logical matrix is outside the supported range.");
        }

        if (deviceRows < 1) throw new ArgumentOutOfRangeException(nameof(deviceRows));
        if (deviceColumns < 1) throw new ArgumentOutOfRangeException(nameof(deviceColumns));
        if ((long)deviceRows * deviceColumns > StudioCompositor.MaxPixels)
        {
            throw new ArgumentOutOfRangeException(nameof(deviceRows),
                "The device matrix is outside the supported range.");
        }

        if (layout is null && (deviceRows != matrixRows || deviceColumns != matrixColumns))
        {
            throw new ArgumentException("A different device matrix needs a manifest layout.", nameof(layout));
        }

        _matrixRows = matrixRows;
        _matrixColumns = matrixColumns;
        _deviceRows = deviceRows;
        _deviceColumns = deviceColumns;
        _layout = layout;
        _inputProvider = inputProvider;
        _trigger = new StudioPlaybackTrigger(GetClock);
    }

    public void Update(IEnumerable<StudioLayerRender> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        var snapshot = layers.ToArray();
        _trigger.Update(snapshot, _matrixRows, _matrixColumns);
        Volatile.Write(ref _layers, snapshot);
    }

    /// <summary>
    /// Applies one key transition in matrix coordinates. The trigger shares this
    /// source's clocks, so rendered frames see the same playback state.
    /// </summary>
    public int HandleInput(StudioTriggerInput input, double nowSeconds) =>
        _trigger.Handle(input, nowSeconds);

    public ValueTask<IReadOnlyList<RazerRgb>> RenderAsync(
        TimeSpan elapsed,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var layers = Volatile.Read(ref _layers);
        var now = Math.Max(0, elapsed.TotalSeconds);
        var timed = new (StudioLayerRender Layer, double Seconds)[layers.Length];
        for (var index = 0; index < layers.Length; index++)
        {
            var layer = layers[index];
            var state = GetClock(layer.Id).Sample(now, layer.Params, _matrixRows, _matrixColumns);
            timed[index] = (layer with { Opacity = state.Running ? layer.Opacity : 0 }, state.LocalSeconds);
        }

        var input = _inputProvider?.Invoke(_matrixRows, _matrixColumns) ?? default;
        var logical = StudioCompositor.RenderTimedLayers(timed, _matrixRows, _matrixColumns, input);
        var colors = new RazerRgb[_deviceRows * _deviceColumns];
        if (_layout is null)
        {
            for (var index = 0; index < logical.Length; index++)
            {
                colors[index] = new RazerRgb(logical[index].R, logical[index].G, logical[index].B);
            }
        }
        else
        {
            foreach (var led in _layout.Leds)
            {
                if ((uint)led.MatrixRow >= (uint)_matrixRows ||
                    (uint)led.MatrixCol >= (uint)_matrixColumns ||
                    (uint)led.DeviceRow >= (uint)_deviceRows ||
                    (uint)led.DeviceCol >= (uint)_deviceColumns)
                {
                    continue;
                }

                var color = logical[led.MatrixRow * _matrixColumns + led.MatrixCol];
                colors[led.DeviceRow * _deviceColumns + led.DeviceCol] =
                    new RazerRgb(color.R, color.G, color.B);
            }
        }

        return ValueTask.FromResult<IReadOnlyList<RazerRgb>>(colors);
    }

    private StudioPlaybackClock GetClock(Guid id)
    {
        lock (_gate)
        {
            if (_clocks.TryGetValue(id, out var clock)) return clock;
            var phase = id == Guid.Empty ? 0 : (id.GetHashCode() & 0x7FFF) / 32768d;
            clock = new StudioPlaybackClock(phase);
            _clocks[id] = clock;
            return clock;
        }
    }
}
