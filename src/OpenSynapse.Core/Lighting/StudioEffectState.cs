namespace OpenSynapse.Core.Lighting;

/// <summary>
/// Per-layer scratch storage for effects that carry state across frames
/// (particles, trails, memory). Fields are created on first use and live as
/// long as the layer id stays in the frame source.
/// </summary>
public sealed class StudioEffectState
{
    private readonly Dictionary<string, float[]> _fields = new(StringComparer.Ordinal);

    public float[] Field(string name, int length)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
        if (_fields.TryGetValue(name, out var field) && field.Length == length)
        {
            return field;
        }

        field = new float[length];
        _fields[name] = field;
        return field;
    }

    public bool TryGet(string name, out float[] field) =>
        _fields.TryGetValue(name, out field!);

    public void Clear() => _fields.Clear();
}

/// <summary>Creates and reuses one state bag per layer id.</summary>
public sealed class StudioEffectStateCache
{
    private readonly Dictionary<Guid, StudioEffectState> _states = new();
    private readonly StudioEffectState _fallback = new();

    public StudioEffectState For(Guid id)
    {
        if (id == Guid.Empty)
        {
            return _fallback;
        }

        if (!_states.TryGetValue(id, out var state))
        {
            state = new StudioEffectState();
            _states[id] = state;
        }

        return state;
    }

    // shortcut: removed layers keep their bag until the source is rebuilt; clear when layer churn matters.
    public void Clear()
    {
        _states.Clear();
        _fallback.Clear();
    }
}
