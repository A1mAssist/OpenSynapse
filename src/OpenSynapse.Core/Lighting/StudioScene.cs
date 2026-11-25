namespace OpenSynapse.Core.Lighting;

/// <summary>Capability bucket a scene layer targets; never a product name.</summary>
public enum StudioSceneRole
{
    Keyboard,
    Mousepad,
    Mouse,
    Headset,
    ChromaLink,
    Argb,
    SingleZone,
}

/// <summary>
/// One layer of a whole-set scene. <see cref="TimebaseId"/> is fixed in the
/// catalog so every device derives the same phase from the same guid.
/// </summary>
public sealed record StudioSceneLayer(
    IReadOnlyList<StudioSceneRole> Roles,
    StudioEffectKind Effect,
    StudioRgb Primary,
    StudioRgb Secondary,
    int DurationMs,
    double Speed,
    double Opacity,
    StudioBlendMode Blend,
    Guid TimebaseId,
    string? PresetId = null,
    IReadOnlyList<StudioRgb>? Stops = null,
    int Angle = 0,
    double Width = 50);

/// <summary>A one-click effect shared by every controllable device.</summary>
public sealed record StudioScene(
    string Id,
    string NameKey,
    string DescriptionKey,
    IReadOnlyList<StudioSceneLayer> Layers);
