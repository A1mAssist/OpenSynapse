using System.Text.Json;
using OpenSynapse.Core.Storage;

namespace OpenSynapse.Windows.Devices;

public sealed record RazerSurfaceCalibrationIdentity(string PhysicalDeviceId, string InstanceId, ushort ProductId,
    string? ControlDevicePath = null);

public sealed record RazerSurfaceCalibrationSurface(string Id, string Name,
    RazerSurfaceCalibrationConfiguration Configuration, DateTimeOffset CreatedAt);

public sealed record RazerSurfaceCalibrationRecord(
    RazerSurfaceCalibrationIdentity Identity,
    RazerProximityCalibrationKind Engine,
    RazerSurfaceCalibrationConfiguration Configuration,
    DateTimeOffset SavedAt,
    int DataVersion = 1)
{
    public IReadOnlyList<RazerSurfaceCalibrationSurface> CustomSurfaces { get; init; } = [];
    public string? SelectedSurfaceId { get; init; }
    public RazerSurfaceCalibrationState? AppliedState { get; init; }
}

/// <summary>Only successfully applied results belong here; in-flight candidates stay in the session.</summary>
public sealed class RazerSurfaceCalibrationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General)
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };
    private static readonly object Gate = new();
    private readonly string _path;

    public RazerSurfaceCalibrationStore(string? filePath = null) =>
        _path = Path.GetFullPath(filePath ?? AppDataPaths.Combine("surface-calibration.json"));

    public static RazerSurfaceCalibrationStore Shared { get; } = new();

    public async Task<RazerSurfaceCalibrationReadback?> RestoreAsync(RazerSurfaceCalibrationIdentity identity,
        IRazerSurfaceCalibrationClient client, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var operation = RazerSurfaceCalibrationOperations.TryAcquire(identity);
        if (operation is null) return null;
        var saved = Load(identity);
        if (saved is null) return null;
        if (saved.Engine != client.Profile.Kind) throw new InvalidDataException("The saved calibration belongs to a different sensor engine.");
        var current = await client.ReadAsync(cancellationToken).ConfigureAwait(false);
        var match = RazerSurfaceCalibrationReadbackPolicy.NormalizeSampledReadback(
            client.CompareReadback(saved.Configuration, current), saved.Configuration, current);
        if (match == RazerSurfaceCalibrationReadback.Consistent) return match;
        RequireRestorableSnapshot(client.Profile, current);
        var original = current;
        try { await client.ApplyAsync(saved.Configuration, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception)
        {
            await RestoreOriginalAsync(client, original, exception).ConfigureAwait(false);
            throw;
        }
        try { current = await client.ReadAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException exception)
        {
            await RestoreOriginalAsync(client, original, exception).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return RazerSurfaceCalibrationReadback.Unavailable;
        }
        match = RazerSurfaceCalibrationReadbackPolicy.NormalizeSampledReadback(
            client.CompareReadback(saved.Configuration, current), saved.Configuration, current);
        if (match == RazerSurfaceCalibrationReadback.Mismatch)
            await RestoreOriginalAsync(client, original, new InvalidDataException("The restored calibration does not match the device readback.")).ConfigureAwait(false);
        return match;
    }

    private static async Task RestoreOriginalAsync(IRazerSurfaceCalibrationClient client,
        RazerSurfaceCalibrationState original, Exception failure)
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await client.RestoreAsync(original, cleanup.Token).WaitAsync(cleanup.Token).ConfigureAwait(false); }
        catch (Exception cleanupError)
        {
            throw new AggregateException("Calibration restoration failed and the original device state could not be fully recovered.", failure, cleanupError);
        }
    }

    internal static void RequireRestorableSnapshot(RazerProximitySensorProfile profile, RazerSurfaceCalibrationState original)
    {
        if (original.SensorState is null or > 1)
            throw new InvalidOperationException("The original sensor state cannot be read; calibration cannot safely restore it.");
        if (profile.Kind == RazerProximityCalibrationKind.SmartPixart && original.LiftMode is null or < 1 or > 6)
            throw new InvalidOperationException("The original tracking mode is unknown; calibration cannot safely restore it.");
        if (profile.Kind == RazerProximityCalibrationKind.SmartPixart && original.LiftMode is 4 or 6 &&
            (original.LiftToTrackData.Length == 0 || original.TrackToLiftData.Length == 0))
            throw new InvalidOperationException("The original asymmetric calibration curves cannot be read; calibration cannot safely restore them.");
    }

    public RazerSurfaceCalibrationRecord? Load(RazerSurfaceCalibrationIdentity identity)
    {
        ValidateIdentity(identity);
        lock (Gate)
        {
            return Read().SingleOrDefault(record => SameDevice(record.Identity, identity));
        }
    }

    public void Save(RazerSurfaceCalibrationRecord record)
    {
        Validate(record);
        lock (Gate)
        {
            // Never turn an unreadable store into an empty one and overwrite other devices.
            var records = Read();
            var previous = records.SingleOrDefault(item => SameDevice(item.Identity, record.Identity));
            if (previous is not null)
            {
                var surfaces = previous.CustomSurfaces.ToDictionary(surface => surface.Id, StringComparer.Ordinal);
                foreach (var surface in record.CustomSurfaces) surfaces[surface.Id] = surface;
                record = record with { CustomSurfaces = surfaces.Values.ToArray() };
            }
            ValidateSelection(record);
            records.RemoveAll(item => SameDevice(item.Identity, record.Identity));
            records.Add(record);
            var directory = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(directory);
            var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
            try
            {
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(stream, records, JsonOptions);
                    stream.Flush(flushToDisk: true);
                }
                if (File.Exists(_path)) File.Replace(temporaryPath, _path, null);
                else File.Move(temporaryPath, _path);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }
    }

    private List<RazerSurfaceCalibrationRecord> Read()
    {
        if (!File.Exists(_path)) return [];
        var records = JsonSerializer.Deserialize<List<RazerSurfaceCalibrationRecord>>(File.ReadAllText(_path), JsonOptions)
            ?? throw new InvalidDataException("The calibration store contains no records.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            Validate(record);
            ValidateSelection(record);
            if (!seen.Add($"{record.Identity.ProductId:X4}:{record.Identity.PhysicalDeviceId}"))
                throw new InvalidDataException("The calibration store contains duplicate physical identities.");
        }
        return records;
    }

    private static void Validate(RazerSurfaceCalibrationRecord record)
    {
        if (record is null || record.DataVersion != 1 || record.Configuration is null || !Enum.IsDefined(record.Engine))
            throw new InvalidDataException("The calibration record version or engine is not supported.");
        ValidateIdentity(record.Identity);
        ValidateConfiguration(record.Configuration);
        if (record.CustomSurfaces is null || record.CustomSurfaces.Any(surface =>
                surface is null || string.IsNullOrWhiteSpace(surface.Id) || string.IsNullOrWhiteSpace(surface.Name) ||
                surface.Configuration?.RawData is null) ||
            record.CustomSurfaces.Select(surface => surface.Id).Distinct(StringComparer.Ordinal).Count() != record.CustomSurfaces.Count)
            throw new InvalidDataException("A saved surface must have a unique ID, a name and its original data.");
        foreach (var surface in record.CustomSurfaces) ValidateConfiguration(surface.Configuration);
    }

    private static void ValidateConfiguration(RazerSurfaceCalibrationConfiguration configuration)
    {
        if (!Enum.IsDefined(configuration.Mode) || configuration.Mode == RazerSurfaceCalibrationMode.Unknown)
            throw new InvalidDataException("An unconfirmed configuration cannot be saved.");
        if (configuration.Mode is RazerSurfaceCalibrationMode.Manual or RazerSurfaceCalibrationMode.Mini && configuration.RawData is null)
            throw new InvalidDataException("A custom calibration must retain its original samples.");
        if (configuration.RawData is { } raw && (raw.Parameters is null || raw.CalibrationData is null ||
                raw.LiftToTrackData is null || raw.TrackToLiftData is null || raw.Z is null || raw.Q is null))
            throw new InvalidDataException("The calibration data contains a missing raw array.");
    }

    private static void ValidateSelection(RazerSurfaceCalibrationRecord record)
    {
        if (record.SelectedSurfaceId is { } selected && !record.CustomSurfaces.Any(surface => surface.Id == selected))
            throw new InvalidDataException("The selected calibration surface does not exist.");
    }

    private static void ValidateIdentity(RazerSurfaceCalibrationIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.PhysicalDeviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.InstanceId);
    }

    private static bool SameDevice(RazerSurfaceCalibrationIdentity left, RazerSurfaceCalibrationIdentity right) =>
        left.ProductId == right.ProductId &&
        StringComparer.OrdinalIgnoreCase.Equals(left.PhysicalDeviceId, right.PhysicalDeviceId);
}
