using System.Text.Json;
using OpenSynapse.Core.Storage;

namespace OpenSynapse.Windows.Devices;

public sealed record AnalogJoystickRecoveryEntry(
    string InstanceId,
    byte PreviousMode,
    byte MappingMode,
    DateTimeOffset Timestamp);

/// <summary>
/// A device left in controller mode by a crash must be switched back on the next
/// start, before the user has a chance to type.
/// </summary>
public sealed class AnalogJoystickRecoveryStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.General)
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly object _gate = new();
    private readonly string _filePath;

    public AnalogJoystickRecoveryStore(string? filePath = null)
    {
        _filePath = string.IsNullOrWhiteSpace(filePath)
            ? AppDataPaths.Combine("analog-joystick-recovery.json")
            : Path.GetFullPath(filePath);
    }

    public static AnalogJoystickRecoveryStore Shared { get; } = new();

    public IReadOnlyList<AnalogJoystickRecoveryEntry> ReadAll()
    {
        lock (_gate)
        {
            return ReadCore();
        }
    }

    public void Save(AnalogJoystickRecoveryEntry entry)
    {
        lock (_gate)
        {
            var entries = ReadCore();
            entries.RemoveAll(item =>
                StringComparer.OrdinalIgnoreCase.Equals(item.InstanceId, entry.InstanceId));
            entries.Add(entry);
            WriteCore(entries);
        }
    }

    public void Clear(string instanceId)
    {
        lock (_gate)
        {
            var entries = ReadCore();
            if (entries.RemoveAll(item =>
                    StringComparer.OrdinalIgnoreCase.Equals(item.InstanceId, instanceId)) == 0)
            {
                return;
            }

            WriteCore(entries);
        }
    }

    private List<AnalogJoystickRecoveryEntry> ReadCore()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return [];
            }

            return JsonSerializer.Deserialize<List<AnalogJoystickRecoveryEntry>>(
                File.ReadAllText(_filePath), SerializerOptions) ?? [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            JsonException or NotSupportedException or ArgumentException)
        {
            return [];
        }
    }

    private void WriteCore(List<AnalogJoystickRecoveryEntry> entries)
    {
        if (entries.Count == 0)
        {
            try
            {
                File.Delete(_filePath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }

            return;
        }

        var directory = Path.GetDirectoryName(_filePath);
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        var temporaryPath = $"{_filePath}.tmp-{Guid.NewGuid():N}";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(entries, SerializerOptions));
            if (File.Exists(_filePath))
            {
                File.Replace(temporaryPath, _filePath, destinationBackupFileName: null,
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, _filePath);
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
}
