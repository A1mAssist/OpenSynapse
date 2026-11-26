namespace OpenSynapse.Core.Lighting;

public enum StudioOutputScopeMode
{
    All,
    Selected,
}

public sealed record StudioOutputTarget(string InstanceId, string Name);

/// <summary>Explicit hardware targets; offline identities remain selected until removed.</summary>
public sealed class StudioOutputScope
{
    public StudioOutputScopeMode Mode { get; set; } = StudioOutputScopeMode.All;

    public List<StudioOutputTarget> Targets { get; } = new();

    public bool Includes(string instanceId) =>
        Mode == StudioOutputScopeMode.All ||
        (Mode == StudioOutputScopeMode.Selected && !string.IsNullOrWhiteSpace(instanceId) &&
         Targets.Any(target => string.Equals(target.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase)));

    public StudioOutputScope Clone()
    {
        var clone = new StudioOutputScope { Mode = Mode };
        clone.Targets.AddRange(Targets.Select(target => target with { }));
        return clone;
    }
}
