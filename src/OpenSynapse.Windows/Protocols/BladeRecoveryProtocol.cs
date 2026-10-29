using System.Globalization;
using System.Text;
using System.Text.Json;

namespace OpenSynapse.Windows.Protocols;

public sealed record BladeRecoveryMarker(
    int Version,
    int OwnerPid,
    string DevicePath,
    string FilterDevicePath,
    DateTimeOffset StartedAtUtc,
    string Hooks = "");

public static class BladeRecoveryProtocol
{
    public const int CurrentMarkerVersion = 3;
    private const int LegacyMarkerVersion = 2;
    public const int MaximumMarkerBytes = 16 * 1024;
    private const int MaximumHooks = 64;

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

            if (!root.TryGetProperty("version", out var versionElement) ||
                !versionElement.TryGetInt32(out var version) ||
                version is not (LegacyMarkerVersion or CurrentMarkerVersion))
            {
                throw new ArgumentException("Unsupported recovery marker version.", nameof(json));
            }
            var expected = new HashSet<string>(StringComparer.Ordinal)
                { "version", "ownerPid", "devicePath", "filterDevicePath", "startedAtUtc" };
            if (version >= CurrentMarkerVersion)
                expected.Add("hooks");
            foreach (var property in root.EnumerateObject())
            {
                if (!expected.Remove(property.Name))
                    throw new ArgumentException($"Unknown or duplicate recovery marker field: {property.Name}.", nameof(json));
            }
            if (expected.Count != 0)
                throw new ArgumentException("Recovery marker is missing required fields.", nameof(json));

            if (!root.GetProperty("ownerPid").TryGetInt32(out var ownerPid) || ownerPid <= 0)
                throw new ArgumentException("Recovery marker ownerPid must be positive.", nameof(json));
            var hooks = version >= CurrentMarkerVersion
                ? root.GetProperty("hooks").GetString() is { Length: > 0 } value ? value : string.Empty
                : string.Empty;
            if (version >= CurrentMarkerVersion)
                ParseHooks(hooks);
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

            return new(version, ownerPid, devicePath!, filterDevicePath!, startedAtUtc, hooks);
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
        if (marker.Version < CurrentMarkerVersion)
        {
            return JsonSerializer.Serialize(new
            {
                version = marker.Version,
                ownerPid = marker.OwnerPid,
                devicePath = marker.DevicePath,
                filterDevicePath = marker.FilterDevicePath,
                startedAtUtc = marker.StartedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            });
        }
        return JsonSerializer.Serialize(new
        {
            version = marker.Version,
            ownerPid = marker.OwnerPid,
            devicePath = marker.DevicePath,
            filterDevicePath = marker.FilterDevicePath,
            startedAtUtc = marker.StartedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            hooks = marker.Hooks,
        });
    }

    public static string FormatHooks(IReadOnlyList<(ushort ScanCode, ushort Flag)> hooks)
    {
        ArgumentNullException.ThrowIfNull(hooks);
        if (hooks.Count is 0 or > MaximumHooks)
            throw new ArgumentOutOfRangeException(nameof(hooks), "Recovery hook count is out of range.");
        return string.Join(';', hooks.Select(static hook => $"{hook.ScanCode:X4}:{hook.Flag}"));
    }

    public static IReadOnlyList<(ushort ScanCode, ushort Flag)> ParseHooks(string hooks)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hooks);
        var parts = hooks.Split(';');
        if (parts.Length is 0 or > MaximumHooks)
            throw new ArgumentException("Recovery hook count is out of range.", nameof(hooks));
        var parsed = new List<(ushort ScanCode, ushort Flag)>(parts.Length);
        var seen = new HashSet<(ushort ScanCode, ushort Flag)>();
        foreach (var part in parts)
        {
            if (part.Length != 6 || part[4] != ':' ||
                !ushort.TryParse(part.AsSpan(0, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var scanCode) ||
                !byte.TryParse(part.AsSpan(5, 1), NumberStyles.None, CultureInfo.InvariantCulture, out var flag) ||
                flag > 3 ||
                !seen.Add((scanCode, flag)))
            {
                throw new ArgumentException($"Recovery hook '{part}' is invalid.", nameof(hooks));
            }

            parsed.Add((scanCode, flag));
        }

        return parsed;
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
        if (marker.Version is not (LegacyMarkerVersion or CurrentMarkerVersion) ||
            marker.OwnerPid <= 0 || marker.StartedAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Recovery marker values are invalid.", nameof(marker));
        if (marker.Version >= CurrentMarkerVersion)
            ParseHooks(marker.Hooks);
        else if (marker.Hooks.Length != 0)
            throw new ArgumentException("Legacy recovery markers cannot carry hooks.", nameof(marker));
        ValidateBladeDevicePath(marker.DevicePath);
        ValidateFilterDevicePath(marker.FilterDevicePath);
    }

    private static void ValidateBladeDevicePath(string? devicePath)
    {
        const string prefix = @"\\?\hid#vid_1532&pid_";
        if (!HasRazerPid(devicePath, prefix))
        {
            throw new ArgumentException("Recovery marker devicePath is not a Razer HID path.", nameof(devicePath));
        }
    }

    private static void ValidateFilterDevicePath(string? devicePath)
    {
        const string prefix = @"\\?\RZCONTROL#VID_1532&PID_";
        if (!HasRazerPid(devicePath, prefix))
        {
            throw new ArgumentException("Recovery marker filterDevicePath is not a Razer filter endpoint.", nameof(devicePath));
        }
    }

    private static bool HasRazerPid(string? devicePath, string prefix) =>
        !string.IsNullOrWhiteSpace(devicePath) &&
        devicePath.Length > prefix.Length + 5 &&
        devicePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
        devicePath[prefix.Length + 4] == '&' &&
        ushort.TryParse(
            devicePath.AsSpan(prefix.Length, 4),
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture,
            out _);
}
