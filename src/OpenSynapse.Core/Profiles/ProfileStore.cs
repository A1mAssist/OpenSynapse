using System.Text.Json;
using System.Text.Json.Serialization;
using OpenSynapse.Core.Displays;
using OpenSynapse.Core.Storage;

namespace OpenSynapse.Core.Profiles;

public sealed class ProfileStore
{
    public const int CurrentVersion = 4;
    private readonly MachineSettingsStore _machineSettings;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.General)
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        // The performance mode is written by name, so it needs a converter that still
        // reads the name the older builds wrote. It goes first because the converters
        // are tried in order and the generic one would take it.
        Converters =
        {
            new BladePerformanceModeJsonConverter(),
            new JsonStringEnumConverter(),
        },
    };

    public ProfileStore(string? filePath = null, MachineSettingsStore? machineSettings = null)
    {
        FilePath = string.IsNullOrWhiteSpace(filePath)
            ? AppDataPaths.Combine("profiles.json")
            : Path.GetFullPath(filePath);
        _machineSettings = machineSettings ?? new MachineSettingsStore(Path.Combine(
            Path.GetDirectoryName(FilePath) ?? AppDataPaths.Root,
            "machine-settings.json"));
    }

    public string FilePath { get; }

    /// <summary>
    /// Where the last unreadable profile was moved. The app starts on defaults and can
    /// save them without destroying the original file.
    /// </summary>
    public string? LastQuarantinedPath { get; private set; }

    public static Task ExportAsync(
        ProfileDocument document,
        string filePath,
        CancellationToken cancellationToken = default) =>
        new ProfileStore(filePath).SaveAsync(document, cancellationToken);

    public static async Task<ProfileDocument> ImportAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var fullPath = Path.GetFullPath(filePath);
        try
        {
            await using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                options: FileOptions.SequentialScan | FileOptions.Asynchronous);
            var document = await JsonSerializer.DeserializeAsync<ProfileDocument>(
                stream,
                SerializerOptions,
                cancellationToken);
            if (document is null)
            {
                throw new InvalidDataException("Profile file is empty.");
            }
            if (!TryUpgrade(document, moveLegacyMachineSettings: false, out _))
            {
                throw new InvalidDataException(
                    $"Profile version {document.Version} is not supported; expected {CurrentVersion}.");
            }

            document.ApplySafeDefaults();
            return document;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Profile file contains invalid JSON.", exception);
        }
        catch (NotSupportedException exception)
        {
            throw new InvalidDataException("Profile file contains unsupported data.", exception);
        }
    }

    public async Task<ProfileDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        LastQuarantinedPath = null;
        if (!File.Exists(FilePath))
        {
            return ProfileDocument.CreateDefault();
        }

        try
        {
            ProfileDocument? document;
            await using (var stream = new FileStream(
                FilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                options: FileOptions.SequentialScan | FileOptions.Asynchronous))
            {
                document = await JsonSerializer.DeserializeAsync<ProfileDocument>(
                    stream,
                    SerializerOptions,
                    cancellationToken);
            }

            if (document is null)
            {
                return QuarantineUnreadable();
            }
            var sourceVersion = document.Version;
            if (!TryUpgrade(document, moveLegacyMachineSettings: true, out var legacyGpuMode))
            {
                return QuarantineUnreadable();
            }

            document.ApplySafeDefaults();
            if (sourceVersion == 3)
            {
                await CompleteV3MigrationAsync(document, legacyGpuMode, cancellationToken);
            }
            return document;
        }
        catch (JsonException)
        {
            return QuarantineUnreadable();
        }
        catch (NotSupportedException)
        {
            return QuarantineUnreadable();
        }
        catch (InvalidDataException)
        {
            return QuarantineUnreadable();
        }
    }

    /// <summary>
    /// A profile the loader cannot read is moved aside instead of being silently replaced
    /// by the defaults on the next save. The user keeps the original bytes to inspect or
    /// recover; the app keeps working on a clean document.
    /// </summary>
    private ProfileDocument QuarantineUnreadable()
    {
        try
        {
            var path = $"{FilePath}.corrupt-{DateTime.Now:yyyyMMddHHmmss}";
            File.Move(FilePath, path);
            LastQuarantinedPath = path;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A locked file stays in place; the next save may replace it, same as before.
        }

        return ProfileDocument.CreateDefault();
    }

    public async Task SaveAsync(ProfileDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Version != CurrentVersion)
        {
            throw new ArgumentException($"Profile version must be {CurrentVersion}.", nameof(document));
        }

        document.ApplySafeDefaults();
        var directory = Path.GetDirectoryName(FilePath);
        if (string.IsNullOrEmpty(directory))
        {
            throw new ArgumentException("Profile path must include a directory.", nameof(FilePath));
        }

        Directory.CreateDirectory(directory);
        var temporaryPath = $"{FilePath}.tmp-{Guid.NewGuid():N}";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                options: FileOptions.WriteThrough | FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, document, SerializerOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(FilePath))
            {
                File.Replace(temporaryPath, FilePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, FilePath);
            }
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (IOException)
            {
                // The durable target has already been committed; a stale temp is harmless.
            }
        }
    }

    private async Task CompleteV3MigrationAsync(
        ProfileDocument document,
        BladeGpuMode? legacyGpuMode,
        CancellationToken cancellationToken)
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var backupPath = $"{FilePath}.v3-backup-{DateTime.Now:yyyyMMddHHmmss}";
                File.Copy(FilePath, backupPath, overwrite: true);
            }

            if (legacyGpuMode is BladeGpuMode gpuMode)
            {
                var machine = await _machineSettings.LoadAsync(cancellationToken);
                if (machine.GpuMode is null)
                {
                    machine.GpuMode = gpuMode;
                    await _machineSettings.SaveAsync(machine, cancellationToken);
                }
            }

            await SaveAsync(document, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The v3 file is still intact and the migrated document is usable in memory.
        }
    }

    private static bool TryUpgrade(
        ProfileDocument document,
        bool moveLegacyMachineSettings,
        out BladeGpuMode? legacyGpuMode)
    {
        legacyGpuMode = null;
        if (document.Version == CurrentVersion)
        {
            return true;
        }

        if (document.Version is 1 or 2)
        {
            document.Version = CurrentVersion;
            return true;
        }

        if (document.Version == 3)
        {
            document.EnsureProfileCatalog();
            foreach (var profile in document.Profiles.Values)
            {
                MigrateLegacyLightingZones(profile);
            }
            if (moveLegacyMachineSettings)
            {
                foreach (var profile in document.Profiles.Values)
                {
                    var blade = profile.Global.Blade;
                    if (blade.GpuMode is not BladeGpuMode mode)
                    {
                        continue;
                    }
                    legacyGpuMode ??= mode;
                    blade.GpuMode = null;
                }
            }
            document.Version = CurrentVersion;
            return true;
        }

        return false;
    }

    private static void MigrateLegacyLightingZones(ProfileDefinition profile)
    {
        MigratePower(profile.PluggedIn);
        MigratePower(profile.LowPowerAdapter);
        MigratePower(profile.OnBattery);
        if (profile.Devices is not null)
        {
            foreach (var settings in profile.Devices.Values)
            {
                if (settings is null)
                {
                    continue;
                }

                var lighting = settings.Lighting;
                if (lighting is null || lighting.Parameters is null ||
                    !lighting.Parameters.TryGetValue("zone", out var zone) ||
                    string.IsNullOrWhiteSpace(zone))
                {
                    continue;
                }

                settings.LightingZones ??= new Dictionary<string, LightingProfile>(
                    StringComparer.OrdinalIgnoreCase);
                settings.LightingZones.TryAdd(zone, lighting);
                settings.Lighting = new LightingProfile();
            }
        }

        static void MigratePower(PowerProfileOverrides? power)
        {
            if (power?.RazerHidLighting is null)
            {
                return;
            }

            foreach (var (key, lighting) in power.RazerHidLighting.ToArray())
            {
                if (lighting?.Parameters is null ||
                    !lighting.Parameters.TryGetValue("zone", out var zone) ||
                    string.IsNullOrWhiteSpace(zone))
                {
                    continue;
                }

                power.RazerHidLightingZones ??= new Dictionary<string, Dictionary<string, LightingProfile>>(
                    StringComparer.OrdinalIgnoreCase);
                if (!power.RazerHidLightingZones.TryGetValue(key, out var zones))
                {
                    zones = new Dictionary<string, LightingProfile>(StringComparer.OrdinalIgnoreCase);
                    power.RazerHidLightingZones[key] = zones;
                }
                zones.TryAdd(zone, lighting);
                power.RazerHidLighting.Remove(key);
            }
        }
    }
}
