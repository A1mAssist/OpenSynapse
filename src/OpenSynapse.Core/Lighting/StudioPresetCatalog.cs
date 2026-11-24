namespace OpenSynapse.Core.Lighting;

public readonly record struct StudioPresetSource(string Name, string Json);

/// <summary>Embedded presets with optional development overrides; invalid entries are reported in Errors.</summary>
public sealed class StudioPresetCatalog
{
    private const string ResourceMarker = ".Lighting.Presets.";
    private readonly IReadOnlyDictionary<string, StudioPresetConfig> _byId;

    private StudioPresetCatalog(IReadOnlyList<StudioPresetConfig> all,
        IReadOnlyDictionary<string, StudioPresetConfig> byId, IReadOnlyList<string> errors)
    {
        All = all;
        _byId = byId;
        Errors = errors;
    }

    public static StudioPresetCatalog Default { get; } = LoadDefault();
    public IReadOnlyList<StudioPresetConfig> All { get; }
    public IReadOnlyList<string> Errors { get; }

    public bool TryGet(string? id, out StudioPresetConfig preset)
    {
        if (!string.IsNullOrWhiteSpace(id) &&
            _byId.TryGetValue(id.Trim().ToLowerInvariant(), out var found))
        {
            preset = found;
            return true;
        }
        preset = null!;
        return false;
    }

    public StudioPresetConfig? Find(string? id) => TryGet(id, out var preset) ? preset : null;

    public static StudioPresetCatalog Load(IEnumerable<StudioPresetSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var byId = new Dictionary<string, StudioPresetConfig>(StringComparer.Ordinal);
        var errors = new List<string>();
        foreach (var source in sources)
        {
            try
            {
                var preset = source.Name.EndsWith(StudioPresetCodec.Extension, StringComparison.OrdinalIgnoreCase)
                    ? StudioPresetCodec.Parse(source.Json) : StudioPresetCodec.ParseCatalog(source.Json);
                byId[preset.Id] = preset;
            }
            catch (Exception exception) when (exception is System.Text.Json.JsonException or InvalidDataException or
                FormatException or InvalidOperationException or ArgumentException)
            {
                errors.Add($"{source.Name}: {exception.Message}");
            }
        }
        var all = Array.AsReadOnly(byId.Values.OrderBy(preset => preset.Id, StringComparer.Ordinal).ToArray());
        return new StudioPresetCatalog(all, byId, errors.AsReadOnly());
    }

    private static StudioPresetCatalog LoadDefault()
    {
        var sources = new List<StudioPresetSource>();
        var assembly = typeof(StudioPresetCatalog).Assembly;
        foreach (var resource in assembly.GetManifestResourceNames()
                     .Where(name => name.Contains(ResourceMarker, StringComparison.Ordinal) &&
                         name.EndsWith(StudioPresetCodec.Extension, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(name => name, StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(resource);
            if (stream is null) continue;
            using var reader = new StreamReader(stream);
            sources.Add(new StudioPresetSource(resource, reader.ReadToEnd()));
        }
        var overrideDirectory = Path.Combine(AppContext.BaseDirectory, "presets");
        if (Directory.Exists(overrideDirectory))
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(overrideDirectory)
                             .Where(path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                                 path.EndsWith(StudioPresetCodec.Extension, StringComparison.OrdinalIgnoreCase))
                             .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                    sources.Add(new StudioPresetSource(file, File.ReadAllText(file)));
            }
            catch (IOException)
            {
                // Optional development overrides must not prevent embedded presets loading.
            }
            catch (UnauthorizedAccessException)
            {
                // Same as above: never let a stray folder break the catalog.
            }
        }
        return Load(sources);
    }
}
