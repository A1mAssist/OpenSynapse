using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Lighting;

/// <summary>
/// V2 frame source: every layer keeps its own playback clock, mask and effect
/// parameters, then the composite is mapped onto the sparse Blade matrix.
/// </summary>
public sealed class StudioBladeFrameSourceV2 : ISoftwareLightingFrameSource
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, StudioPlaybackClock> _clocks = new();
    private readonly Func<int, int, StudioEffectInput>? _inputProvider;
    private StudioLayerRender[] _layers = [];

    public StudioBladeFrameSourceV2()
    {
    }

    public StudioBladeFrameSourceV2(Func<int, int, StudioEffectInput>? inputProvider)
    {
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
            var clock = GetClock(layer.Id);
            var state = clock.Sample(now, layer.Params);
            // Speed is an effect parameter interpreted by the renderer; time stays absolute.
            timed[index] = (layer with { Opacity = state.Running ? layer.Opacity : 0 }, state.LocalSeconds);
        }

        var input = _inputProvider?.Invoke(
            BladeLightingLayout.LogicalRows, BladeLightingLayout.LogicalColumns) ?? default;
        var logical = StudioCompositor.RenderTimedLayers(
            timed,
            BladeLightingLayout.LogicalRows,
            BladeLightingLayout.LogicalColumns,
            input);
        var colors = new RazerRgb[logical.Length];
        for (var index = 0; index < logical.Length; index++)
        {
            colors[index] = new RazerRgb(logical[index].R, logical[index].G, logical[index].B);
        }

        return ValueTask.FromResult<IReadOnlyList<RazerRgb>>(BladeLightingLayout.MapToDeviceFrame(colors));
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
