namespace OpenSynapse.Core.Lighting;

/// <summary>
/// Minimal clock surface driven by <see cref="StudioPlaybackTrigger"/>.
/// <see cref="StudioPlaybackClock"/> implements it; the trigger never owns or
/// duplicates timing state.
/// </summary>
public interface IStudioPlaybackClock
{
    void Trigger(double nowSeconds);

    void Toggle(double nowSeconds);
}

/// <summary>
/// One device-local key transition. Coordinates use the same row/column space
/// as the layer masks passed to <see cref="StudioPlaybackTrigger.Update"/>.
/// </summary>
public readonly record struct StudioTriggerInput(int Row, int Column, bool IsDown = true);

/// <summary>
/// Maps key-down events onto per-layer playback clocks.
/// <para>
/// OnPress reacts to any valid key-down. OnSelectedKeys reacts only when the
/// key is inside at least one of the layer's mask segments; a null mask means
/// the whole device. PlaybackEnd.Toggle toggles instead of restarting, and
/// PlaybackStart.Random ignores input unless End is Toggle because Sample
/// already starts it. Several region segments with the same layer id share one
/// clock and produce at most one action per input.
/// </para>
/// </summary>
public sealed class StudioPlaybackTrigger
{
    private readonly Func<Guid, IStudioPlaybackClock> _clockFor;
    private Snapshot _snapshot = new([], 0, 0);

    public StudioPlaybackTrigger(Func<Guid, IStudioPlaybackClock> clockFor)
    {
        _clockFor = clockFor ?? throw new ArgumentNullException(nameof(clockFor));
    }

    /// <summary>
    /// Rebuilds the layer bindings. Masks must match rows * columns; region
    /// renders that share a layer id are merged into one clock binding.
    /// </summary>
    public void Update(IEnumerable<StudioLayerRender> layers, int rows, int columns)
    {
        ArgumentNullException.ThrowIfNull(layers);
        if (rows < 1) throw new ArgumentOutOfRangeException(nameof(rows));
        if (columns < 1) throw new ArgumentOutOfRangeException(nameof(columns));
        if ((long)rows * columns > StudioCompositor.MaxPixels)
        {
            throw new ArgumentOutOfRangeException(nameof(rows),
                "The trigger canvas is outside the supported range.");
        }

        var grouped = new Dictionary<Guid, List<Segment>>();
        foreach (var layer in layers)
        {
            if (layer.Mask is { } mask && mask.Length != rows * columns)
            {
                throw new ArgumentException(
                    "A layer mask does not match the trigger canvas.", nameof(layers));
            }

            if (!grouped.TryGetValue(layer.Id, out var segments))
            {
                segments = [];
                grouped[layer.Id] = segments;
            }

            segments.Add(new Segment(layer.Params, layer.Mask));
        }

        var bindings = new LayerBinding[grouped.Count];
        var index = 0;
        foreach (var pair in grouped)
        {
            bindings[index++] = new LayerBinding(pair.Key, pair.Value.ToArray());
        }

        Volatile.Write(ref _snapshot, new Snapshot(bindings, rows, columns));
    }

    /// <summary>
    /// Applies one input transition and returns how many layer clocks it
    /// touched. Key-up events and out-of-range coordinates are ignored.
    /// </summary>
    public int Handle(StudioTriggerInput input, double nowSeconds)
    {
        var snapshot = Volatile.Read(ref _snapshot);
        if (!input.IsDown ||
            (uint)input.Row >= (uint)snapshot.Rows ||
            (uint)input.Column >= (uint)snapshot.Columns)
        {
            return 0;
        }

        var now = double.IsFinite(nowSeconds) ? Math.Max(0, nowSeconds) : 0;
        var count = 0;
        foreach (var layer in snapshot.Layers)
        {
            var action = Resolve(layer, input.Row, input.Column, snapshot.Columns);
            if (action == TriggerAction.None) continue;

            var clock = _clockFor(layer.Id);
            if (action == TriggerAction.Toggle) clock.Toggle(now);
            else clock.Trigger(now);
            count++;
        }

        return count;
    }

    private static TriggerAction Resolve(
        LayerBinding layer, int row, int column, int columns)
    {
        foreach (var segment in layer.Segments)
        {
            var parameters = segment.Parameters;
            var selected = segment.Mask is null ||
                segment.Mask[row * columns + column] > 0;
            var action = parameters.PlaybackStart switch
            {
                StudioPlaybackStart.OnPress => ActionForEnd(parameters.PlaybackEnd),
                StudioPlaybackStart.OnSelectedKeys when selected => ActionForEnd(parameters.PlaybackEnd),
                StudioPlaybackStart.Random when parameters.PlaybackEnd == StudioPlaybackEnd.Toggle =>
                    TriggerAction.Toggle,
                _ => TriggerAction.None,
            };
            if (action != TriggerAction.None) return action;
        }

        return TriggerAction.None;
    }

    private static TriggerAction ActionForEnd(StudioPlaybackEnd end) =>
        end == StudioPlaybackEnd.Toggle ? TriggerAction.Toggle : TriggerAction.Trigger;

    private sealed record Snapshot(LayerBinding[] Layers, int Rows, int Columns);

    private sealed record LayerBinding(Guid Id, Segment[] Segments);

    private readonly record struct Segment(StudioEffectParams Parameters, byte[]? Mask);

    private enum TriggerAction
    {
        None,
        Trigger,
        Toggle,
    }
}
