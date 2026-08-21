using System.Globalization;
using System.Text;
using System.Text.Json;

namespace OpenSynapse.Windows.Protocols;

public sealed record BladeRecoveryMarker(
    int Version,
    int OwnerPid,
    string DevicePath,
    string FilterDevicePath,
    DateTimeOffset StartedAtUtc);

public static class BladeRecoveryProtocol
{
    public const int CurrentMarkerVersion = 2;
    public const int MaximumMarkerBytes = 16 * 1024;

    public static BladeRecoveryMarker ParseMarker(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        if (Encoding.UTF8.GetByteCount(json) > MaximumMarkerBytes)
            throw new ArgumentException("Recovery marker is too large.", nameof(json));

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8,
            });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("Recovery marker root must be an object.", nameof(json));

            var expected = new HashSet<string>(StringComparer.Ordinal)
                { "version", "ownerPid", "devicePath", "filterDevicePath", "startedAtUtc" };
            foreach (var property in root.EnumerateObject())
            {
                if (!expected.Remove(property.Name))
                    throw new ArgumentException($"Unknown or duplicate recovery marker field: {property.Name}.", nameof(json));
            }
            if (expected.Count != 0)
                throw new ArgumentException("Recovery marker is missing required fields.", nameof(json));

            if (!root.GetProperty("version").TryGetInt32(out var version) || version != CurrentMarkerVersion)
                throw new ArgumentException("Unsupported recovery marker version.", nameof(json));
            if (!root.GetProperty("ownerPid").TryGetInt32(out var ownerPid) || ownerPid <= 0)
                throw new ArgumentException("Recovery marker ownerPid must be positive.", nameof(json));
            var devicePath = root.GetProperty("devicePath").GetString();
            ValidateBladeDevicePath(devicePath);
            var filterDevicePath = root.GetProperty("filterDevicePath").GetString();
            ValidateFilterDevicePath(filterDevicePath);
            var timestampText = root.GetProperty("startedAtUtc").GetString();
            if (!DateTimeOffset.TryParse(
                    timestampText,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var startedAtUtc) ||
                startedAtUtc.Offset != TimeSpan.Zero)
            {
                throw new ArgumentException("Recovery marker startedAtUtc must be a UTC timestamp.", nameof(json));
            }

            return new(version, ownerPid, devicePath!, filterDevicePath!, startedAtUtc);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Recovery marker is not valid JSON.", nameof(json), exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new ArgumentException("Recovery marker field type is invalid.", nameof(json), exception);
        }
    }

    public static string SerializeMarker(BladeRecoveryMarker marker)
    {
        ValidateMarker(marker);
        return JsonSerializer.Serialize(new
        {
            version = marker.Version,
            ownerPid = marker.OwnerPid,
            devicePath = marker.DevicePath,
            filterDevicePath = marker.FilterDevicePath,
            startedAtUtc = marker.StartedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        });
    }

    public static async Task<BladeRecoveryMarker> ReadMarkerAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using var stream = new FileStream(
            Path.GetFullPath(path), FileMode.Open, FileAccess.Read, FileShare.Read,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length is <= 0 or > MaximumMarkerBytes)
            throw new InvalidDataException("Recovery marker length is invalid.");
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, leaveOpen: false);
        return ParseMarker(await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false));
    }

    public static async Task WriteMarkerAtomicAsync(
        string path,
        BladeRecoveryMarker marker,
        CancellationToken cancellationToken = default)
    {
        ValidateMarker(marker);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("Recovery marker path has no directory.", nameof(path));
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var bytes = Encoding.UTF8.GetBytes(SerializeMarker(marker));
            await using (var stream = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            // A recovery marker is an ownership claim, not replaceable state. The
            // create-only move closes the race between stale-marker cleanup and a
            // second process claiming the same device.
            File.Move(temporary, fullPath);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static string CreateObjectName(string purpose, Guid id)
    {
        if (purpose is not ("RecoveryReady" or "RecoveryShutdown" or "RecoveryKeys") || id == Guid.Empty)
            throw new ArgumentException("Unsupported recovery object purpose or id.");
        return $"Local\\OpenSynapse.{purpose}.{id:D}";
    }

    public static void ValidateObjectName(string name, string purpose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var prefix = $"Local\\OpenSynapse.{purpose}.";
        if (purpose is not ("RecoveryReady" or "RecoveryShutdown" or "RecoveryKeys") ||
            !name.StartsWith(prefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(name[prefix.Length..], "D", out var id) || id == Guid.Empty)
        {
            throw new ArgumentException("Recovery object name is outside the current-session namespace.", nameof(name));
        }
    }

    private static void ValidateMarker(BladeRecoveryMarker marker)
    {
        ArgumentNullException.ThrowIfNull(marker);
        if (marker.Version != CurrentMarkerVersion || marker.OwnerPid <= 0 || marker.StartedAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Recovery marker values are invalid.", nameof(marker));
        ValidateBladeDevicePath(marker.DevicePath);
        ValidateFilterDevicePath(marker.FilterDevicePath);
    }

    private static void ValidateBladeDevicePath(string? devicePath)
    {
        if (string.IsNullOrWhiteSpace(devicePath) || devicePath.Length > 4096 ||
            !devicePath.StartsWith(@"\\?\hid#", StringComparison.OrdinalIgnoreCase) ||
            !devicePath.Contains("vid_1532&pid_02c6&", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Recovery marker devicePath is not a Product 710 HID path.", nameof(devicePath));
        }
    }

    private static void ValidateFilterDevicePath(string? devicePath)
    {
        if (string.IsNullOrWhiteSpace(devicePath) || devicePath.Length > 4096 ||
            !devicePath.StartsWith(@"\\?\RZCONTROL#", StringComparison.OrdinalIgnoreCase) ||
            !devicePath.Contains("VID_1532&PID_02C6&MI_00#", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Recovery marker filterDevicePath is not the Product 710 filter endpoint.", nameof(devicePath));
        }
    }
}
