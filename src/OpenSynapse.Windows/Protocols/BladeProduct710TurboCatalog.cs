namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// The three turbo definitions referenced by Product 710's fixed default graph.
/// The graph contains only GUIDs; these event lists come from Synapse's matching
/// synapseDefaultTurbos records.
/// </summary>
public static class BladeProduct710TurboCatalog
{
    public static readonly Guid VolumeDownId =
        new("8a472b5b-04b1-4e88-9620-3bf0ff8ffce5");
    public static readonly Guid VolumeUpId =
        new("cb32ace3-869a-4803-9ca6-7cfb6026ac62");
    public static readonly Guid ProjectionSettingsId =
        new("3945ba01-5aca-499b-85fc-3d1176e384a1");

    private static readonly IReadOnlyList<BladeMappingAction> VolumeDown =
        Array.AsReadOnly<BladeMappingAction>(
        [
            new BladeKeyboardMappingAction(0x2E, true, true),
            new BladeKeyboardMappingAction(0x2E, false, true),
        ]);

    private static readonly IReadOnlyList<BladeMappingAction> VolumeUp =
        Array.AsReadOnly<BladeMappingAction>(
        [
            new BladeKeyboardMappingAction(0x30, true, true),
            new BladeKeyboardMappingAction(0x30, false, true),
        ]);

    private static readonly IReadOnlyList<BladeMappingAction> ProjectionSettings =
        Array.AsReadOnly<BladeMappingAction>(
        [
            new BladeKeyboardMappingAction(0x5B, true, true),
            new BladeKeyboardMappingAction(0x19, true, false),
            new BladeDelayMappingAction(10),
            new BladeKeyboardMappingAction(0x19, false, false),
            new BladeKeyboardMappingAction(0x5B, false, true),
        ]);

    public static bool TryGet(Guid id, out IReadOnlyList<BladeMappingAction> actions)
    {
        if (id == VolumeDownId)
        {
            actions = VolumeDown;
            return true;
        }
        if (id == VolumeUpId)
        {
            actions = VolumeUp;
            return true;
        }
        if (id == ProjectionSettingsId)
        {
            actions = ProjectionSettings;
            return true;
        }

        actions = Array.Empty<BladeMappingAction>();
        return false;
    }

    public static IReadOnlyList<BladeMappingAction> Get(Guid id) =>
        TryGet(id, out var actions)
            ? actions
            : throw new ArgumentOutOfRangeException(
                nameof(id),
                id,
                "Unknown Product 710 turbo GUID.");
}
