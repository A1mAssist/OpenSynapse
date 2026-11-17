using System.Text.Json;
using OpenSynapse.Core.Storage;

namespace OpenSynapse.Core.Profiles;

public sealed class DeviceApplicationRuleStore
{
    public const int CurrentVersion = 1;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.General)
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public DeviceApplicationRuleStore(string? filePath = null)
    {
        FilePath = string.IsNullOrWhiteSpace(filePath)
            ? AppDataPaths.Combine("device-application-rules.json")
            : Path.GetFullPath(filePath);
    }

    public string FilePath { get; }
    public string? LastQuarantinedPath { get; private set; }

    public async Task<DeviceApplicationRuleDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        LastQuarantinedPath = null;
        if (!File.Exists(FilePath))
        {
            return DeviceApplicationRuleDocument.CreateDefault();
        }

        try
        {
            await using var stream = new FileStream(
                FilePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 4096, options: FileOptions.SequentialScan | FileOptions.Asynchronous);
            var document = await JsonSerializer.DeserializeAsync<DeviceApplicationRuleDocument>(
                stream, SerializerOptions, cancellationToken);
            if (document is null)
            {
                return QuarantineUnreadable();
            }

            DeviceApplicationRuleCatalog.Validate(document);
            return document;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or
            InvalidDataException or ArgumentException)
        {
            return QuarantineUnreadable();
        }
    }

    public async Task SaveAsync(
        DeviceApplicationRuleDocument document,
        CancellationToken cancellationToken = default)
    {
        DeviceApplicationRuleCatalog.Validate(document);
        var directory = Path.GetDirectoryName(FilePath);
        if (string.IsNullOrEmpty(directory))
        {
            throw new ArgumentException("Rule path must include a directory.", nameof(FilePath));
        }

        Directory.CreateDirectory(directory);
        var temporaryPath = $"{FilePath}.tmp-{Guid.NewGuid():N}";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 4096, options: FileOptions.WriteThrough | FileOptions.Asynchronous))
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
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private DeviceApplicationRuleDocument QuarantineUnreadable()
    {
        try
        {
            var path = $"{FilePath}.corrupt-{DateTime.Now:yyyyMMddHHmmss}";
            File.Move(FilePath, path);
            LastQuarantinedPath = path;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        return DeviceApplicationRuleDocument.CreateDefault();
    }
}
