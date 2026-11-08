using System.Text.Json;
using System.Text.Json.Serialization;
using OpenSynapse.Core.Displays;
using OpenSynapse.Core.Storage;

namespace OpenSynapse.Core.Profiles;

/// <summary>
/// Machine-level values that must not move with a profile: GPU routing, the Fn
/// key row, startup, overlay and language/update preferences.
/// </summary>
public sealed class MachineSettings
{
    public BladeGpuMode? GpuMode { get; set; }
    public bool? FnKeyPrimary { get; set; }
    public bool? StartWithWindows { get; set; }
    [JsonPropertyName("startupEnabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacyStartupEnabled
    {
        get => null;
        set => StartWithWindows ??= value;
    }
    public bool? OverlayEnabled { get; set; }
    public bool? TaskbarStripEnabled { get; set; }
    public string? Language { get; set; }
    public string? UpdateChannel { get; set; }
    public bool? AutoUpdateEnabled { get; set; }

    internal void ApplySafeDefaults()
    {
        if (GpuMode is BladeGpuMode mode && !Enum.IsDefined(mode))
        {
            throw new InvalidDataException($"Unsupported GPU mode: {mode}.");
        }
    }
}

public sealed class MachineSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.General)
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public MachineSettingsStore(string? filePath = null)
    {
        FilePath = string.IsNullOrWhiteSpace(filePath)
            ? AppDataPaths.Combine("machine-settings.json")
            : Path.GetFullPath(filePath);
    }

    public string FilePath { get; }

    public async Task<MachineSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(FilePath))
        {
            return new MachineSettings();
        }

        try
        {
            await using var stream = new FileStream(
                FilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                options: FileOptions.SequentialScan | FileOptions.Asynchronous);
            var settings = await JsonSerializer.DeserializeAsync<MachineSettings>(
                stream,
                SerializerOptions,
                cancellationToken);
            settings ??= new MachineSettings();
            settings.ApplySafeDefaults();
            return settings;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            return new MachineSettings();
        }
    }

    public async Task SaveAsync(MachineSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.ApplySafeDefaults();
        var directory = Path.GetDirectoryName(FilePath);
        if (string.IsNullOrEmpty(directory))
        {
            throw new ArgumentException("Machine settings path must include a directory.", nameof(FilePath));
        }

        Directory.CreateDirectory(directory);
        var temporaryPath = $"{FilePath}.tmp-{Guid.NewGuid():N}";
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                JsonSerializer.Serialize(settings, SerializerOptions),
                cancellationToken);
            File.Move(temporaryPath, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
