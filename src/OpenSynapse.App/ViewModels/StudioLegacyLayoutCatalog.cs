using OpenSynapse.Core.Lighting;

namespace OpenSynapse.App.ViewModels;

/// <summary>
/// Official Synapse 2 key matrices for devices whose live manifests are gone.
/// Their LED coordinates match the OpenRazer frame dimensions exactly.
/// </summary>
internal static class StudioLegacyLayoutCatalog
{
    private static readonly Lazy<IReadOnlyDictionary<ushort, StudioLedLayout>> Layouts = new(Load);

    public static bool TryGet(ushort productId, out StudioLedLayout layout)
    {
        var found = Layouts.Value.TryGetValue(productId, out var value);
        layout = value!;
        return found;
    }

    private static IReadOnlyDictionary<ushort, StudioLedLayout> Load()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory, "Assets", "LightingLayouts", "synapse2-legacy.json");
        if (!File.Exists(path)) return new Dictionary<ushort, StudioLedLayout>();

        try
        {
            return StudioLegacyLayoutParser.Parse(File.ReadAllText(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            System.Text.Json.JsonException or InvalidOperationException or FormatException or
            ArgumentException or KeyNotFoundException)
        {
            return new Dictionary<ushort, StudioLedLayout>();
        }
    }
}
