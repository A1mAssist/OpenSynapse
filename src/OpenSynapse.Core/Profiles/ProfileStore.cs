using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenSynapse.Core.Profiles;

public sealed class ProfileStore
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.General)
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public ProfileStore(string? filePath = null)
    {
        FilePath = string.IsNullOrWhiteSpace(filePath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenSynapse",
                "profiles.json")
            : Path.GetFullPath(filePath);
    }

    public string FilePath { get; }

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
            if (document.Version != CurrentVersion)
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
        if (!File.Exists(FilePath))
        {
            return ProfileDocument.CreateDefault();
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
            var document = await JsonSerializer.DeserializeAsync<ProfileDocument>(
                stream,
                SerializerOptions,
                cancellationToken);

            if (document is null || document.Version != CurrentVersion)
            {
                return ProfileDocument.CreateDefault();
            }

            document.ApplySafeDefaults();
            return document;
        }
        catch (JsonException)
        {
            // Leave the source untouched so the user can recover or inspect it.
            return ProfileDocument.CreateDefault();
        }
        catch (NotSupportedException)
        {
            return ProfileDocument.CreateDefault();
        }
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
}
