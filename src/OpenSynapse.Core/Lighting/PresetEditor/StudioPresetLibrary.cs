using OpenSynapse.Core.Storage;

namespace OpenSynapse.Core.Lighting;

public sealed record StudioPresetLibraryEntry(
    string Id,
    string Name,
    StudioPresetKind Kind,
    string Category,
    string? Path,
    bool IsBuiltIn,
    StudioPresetConfig Preset);

public sealed record StudioPresetLibraryValidation(
    StudioPresetLibraryEntry Entry,
    IReadOnlyList<StudioPresetValidationIssue> Issues);

/// <summary>Read-only built-ins plus the user preset directory.</summary>
public static class StudioPresetLibrary
{
    /// <summary>%LOCALAPPDATA%\A1mAssist\OpenSynapse\lighting-presets.</summary>
    public static string UserDirectory => AppDataPaths.Combine("lighting-presets");

    public static IReadOnlyList<StudioPresetLibraryEntry> Enumerate() =>
        Enumerate(out _);

    public static IReadOnlyList<StudioPresetLibraryEntry> Enumerate(out IReadOnlyList<string> errors)
    {
        var byId = new Dictionary<string, StudioPresetLibraryEntry>(StringComparer.Ordinal);
        foreach (var preset in StudioPresetCatalog.Default.All)
        {
            byId[preset.Id] = new StudioPresetLibraryEntry(
                preset.Id, preset.Name, preset.Kind, preset.Category, null, true, preset);
        }

        var problems = new List<string>();
        if (Directory.Exists(UserDirectory))
        {
            foreach (var path in Directory.EnumerateFiles(UserDirectory, "*" + StudioPresetCodec.Extension)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var preset = StudioPresetCodec.Load(path);
                    byId[preset.Id] = new StudioPresetLibraryEntry(
                        preset.Id, preset.Name, preset.Kind, preset.Category, path, false, preset);
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException or
                    FormatException or InvalidOperationException or UnauthorizedAccessException)
                {
                    problems.Add($"{path}: {exception.Message}");
                }
            }
        }

        errors = problems.AsReadOnly();
        return byId.Values
            .OrderBy(entry => entry.Kind)
            .ThenBy(entry => entry.Name, StringComparer.CurrentCulture)
            .ToArray();
    }

    public static StudioPresetDraft LoadDraft(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return StudioPresetDraft.FromConfig(StudioPresetCodec.Load(path));
    }

    public static string PathForId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return System.IO.Path.Combine(UserDirectory, id + StudioPresetCodec.Extension);
    }

    /// <summary>User copy first (it shadows a built-in with the same id), then embedded.</summary>
    public static StudioPresetConfig? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var trimmed = id.Trim();
        var path = PathForId(trimmed);
        if (File.Exists(path))
        {
            try
            {
                return StudioPresetCodec.Load(path);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or
                FormatException or InvalidOperationException or UnauthorizedAccessException or
                ArgumentException)
            {
                // A broken user copy must not hide the embedded preset of the same id.
            }
        }

        return StudioPresetCatalog.Default.Find(trimmed);
    }

    public static bool IsBuiltInId(string? id) =>
        !string.IsNullOrWhiteSpace(id) && StudioPresetCatalog.Default.Find(id) is not null &&
        !File.Exists(PathForId(id.Trim()));

    /// <summary>Compiles first, then replaces the file atomically and keeps one .bak.</summary>
    public static StudioPresetConfig Save(StudioPresetDraft draft, string path)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var preset = StudioPresetCompiler.Compile(draft);
        if (File.Exists(path))
        {
            File.Copy(path, path + ".bak", overwrite: true);
        }

        StudioPresetCodec.Save(path, preset);
        return preset;
    }

    public static IReadOnlyList<StudioPresetLibraryValidation> ValidateAll(bool renderSmoke = false)
    {
        var results = new List<StudioPresetLibraryValidation>();
        foreach (var entry in Enumerate())
        {
            var draft = StudioPresetDraft.FromConfig(entry.Preset);
            results.Add(new StudioPresetLibraryValidation(
                entry, StudioPresetValidator.Validate(draft, renderSmoke)));
        }

        return results;
    }
}
