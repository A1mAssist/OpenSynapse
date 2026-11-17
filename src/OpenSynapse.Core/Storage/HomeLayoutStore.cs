using System.Text.Json;

namespace OpenSynapse.Core.Storage;

public sealed class HomeLayoutStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.General)
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public HomeLayoutStore(string? filePath = null)
    {
        FilePath = string.IsNullOrWhiteSpace(filePath)
            ? AppDataPaths.Combine("home-layout.json")
            : Path.GetFullPath(filePath);
    }

    public string FilePath { get; }

    public async Task<HomeLayoutSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(FilePath)) return HomeLayoutSettings.CreateDefault();
        try
        {
            var json = await File.ReadAllTextAsync(FilePath, cancellationToken);
            var settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, SerializerOptions);
            var layout = new HomeLayoutSettings();
            if (settings?.TryGetValue("cardOrder", out var order) == true) layout.CardOrder = order.Deserialize<List<string>>() ?? [];
            if (settings?.TryGetValue("hiddenCards", out var hidden) == true) layout.HiddenCards = new HashSet<string>(hidden.Deserialize<List<string>>() ?? [], StringComparer.OrdinalIgnoreCase);
            if (settings?.ContainsKey("cards") == true)
                return HomeLayoutSettings.FromDocument(JsonSerializer.Deserialize<HomeLayoutDocument>(json, SerializerOptions));
            return HomeLayoutSettings.Sanitize(layout);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or JsonException or NotSupportedException)
        {
            return HomeLayoutSettings.CreateDefault();
        }
    }

    public async Task SaveAsync(HomeLayoutSettings settings, CancellationToken cancellationToken = default)
    {
        settings = HomeLayoutSettings.Sanitize(settings);
        var directory = Path.GetDirectoryName(FilePath);
        if (string.IsNullOrEmpty(directory)) throw new ArgumentException("Home layout path must include a directory.", nameof(FilePath));
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{FilePath}.tmp-{Guid.NewGuid():N}";
        try
        {
            var json = JsonSerializer.Serialize(settings.ToDocument(), SerializerOptions);
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
            File.Move(temporaryPath, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
