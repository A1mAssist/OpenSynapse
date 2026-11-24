using System.Text.Json;

namespace OpenSynapse.Core.Lighting;

/// <summary>Every render module, indexed by its stable preset module id.</summary>
public static class RenderModuleRegistry
{
    private static readonly IReadOnlyDictionary<string, IRenderModuleFactory> Modules =
        RenderModules.All.ToDictionary(module => module.Info.Id, StringComparer.Ordinal);

    public static IEnumerable<IRenderModuleFactory> All => Modules.Values;

    public static bool TryGet(string id, out IRenderModuleFactory module) =>
        Modules.TryGetValue(id, out module!);

    public static IReadOnlyList<RenderModuleParameter> Describe(string id, JsonElement values)
    {
        if (!TryGet(id, out var module)) throw new InvalidDataException($"Unknown module '{id}'.");
        var parameters = new RenderModuleParams(values, id);
        module.Create(parameters);
        return parameters.Fields;
    }
}
