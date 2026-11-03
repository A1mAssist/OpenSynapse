using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Lighting;

/// <summary>
/// Renders the studio layer stack directly onto one device matrix. Every layer
/// keeps its own playback clock, and the resulting frame uses the device's own
/// row/column layout instead of the Blade physical map.
/// </summary>
public sealed class StudioDeviceFrameSource : ISoftwareLightingFrameSource
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, StudioPlaybackClock> _clocks = new();
    private readonly int _rows;
    private readonly int _columns;
    private readonly Func<int, int, StudioEffectInput>? _inputProvider;
    private StudioLayerRender[] _layers = [];

    public StudioDeviceFrameSource(
        int rows, int columns, Func<int, int, StudioEffectInput>? inputProvider = null)
    {
        if (rows < 1) throw new ArgumentOutOfRangeException(nameof(rows));
        if (columns < 1) throw new ArgumentOutOfRangeException(nameof(columns));
        if ((long)rows * columns > StudioCompositor.MaxPixels)
        {
            throw new ArgumentOutOfRangeException(nameof(rows),
                "The device matrix is outside the supported range.");
        }

        _rows = rows;
        _columns = columns;
        _inputProvider = inputProvider;
    }

    public void Update(IEnumerable<StudioLayerRender> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        Volatile.Write(ref _layers, layers.ToArray());
    }

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
            var state = GetClock(layer.Id).Sample(now, layer.Params);
            timed[index] = (layer with { Opacity = state.Running ? layer.Opacity : 0 }, state.LocalSeconds);
        }

        var input = _inputProvider?.Invoke(_rows, _columns) ?? default;
        var logical = StudioCompositor.RenderTimedLayers(timed, _rows, _columns, input);
        var colors = new RazerRgb[logical.Length];
        for (var index = 0; index < logical.Length; index++)
        {
            colors[index] = new RazerRgb(logical[index].R, logical[index].G, logical[index].B);
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
