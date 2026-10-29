using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenSynapse.Core.Macros;

/// <summary>
/// File, JSON and identity handling for macro documents. The ViewModel keeps the
/// observable collections and status text; everything here works on plain models.
/// </summary>
public static class MacroDocumentService
{
    private static readonly JsonSerializerOptions CloneJsonOptions = new(JsonSerializerDefaults.General)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static IReadOnlyDictionary<string, MacroDefinition> IndexById(
        IEnumerable<MacroDefinition> macros) =>
        macros
            .GroupBy(macro => macro.Id, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.Ordinal);

    public static Task ExportAsync(
        MacroDefinition macro,
        IReadOnlyDictionary<string, MacroDefinition> macros,
        string filePath,
        CancellationToken cancellationToken) =>
        MacroFileStore.ExportAsync(macro, macros, filePath, cancellationToken);

    public static Task<IReadOnlyList<MacroDefinition>> ImportAsync(
        string filePath,
        CancellationToken cancellationToken) =>
        MacroFileStore.ImportBundleAsync(filePath, cancellationToken);

    /// <summary>
    /// Gives imported macros fresh ids, rewrites references that pointed at the
    /// bundle's old ids, names unnamed macros and clamps everything to safe values.
    /// </summary>
    public static void PrepareImported(
        IReadOnlyList<MacroDefinition> imported,
        Func<string> newId,
        string unnamedName)
    {
        var idMap = imported.ToDictionary(
            macro => macro.Id,
            _ => newId(),
            StringComparer.Ordinal);
        foreach (var macro in imported)
        {
            macro.Id = idMap[macro.Id];
            foreach (var step in MacroStepTree.Enumerate(macro.Steps))
            {
                if (step.Kind == MacroStepKind.MacroReference &&
                    step.MacroId is { } reference &&
                    idMap.TryGetValue(reference, out var replacement))
                {
                    step.MacroId = replacement;
                }
            }

            if (string.IsNullOrWhiteSpace(macro.Name) ||
                StringComparer.Ordinal.Equals(macro.Name, "Imported macro"))
            {
                macro.Name = unnamedName;
            }

            macro.ApplySafeDefaults();
        }
    }

    public static MacroDefinition Clone(MacroDefinition macro)
    {
        var json = JsonSerializer.Serialize(macro, CloneJsonOptions);
        return JsonSerializer.Deserialize<MacroDefinition>(json, CloneJsonOptions)
            ?? throw new InvalidDataException("Macro copy failed.");
    }

    public static List<MacroStep> CloneSteps(IReadOnlyList<MacroStep> steps)
    {
        var json = JsonSerializer.Serialize(steps, CloneJsonOptions);
        return JsonSerializer.Deserialize<List<MacroStep>>(json, CloneJsonOptions) ?? [];
    }
}
