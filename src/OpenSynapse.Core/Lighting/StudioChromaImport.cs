namespace OpenSynapse.Core.Lighting;

/// <summary>A device currently reachable on this machine.</summary>
public sealed record StudioDeviceMatch(ushort ProductId, string InstanceId, string? Layout = null);

public enum StudioImportIssueKind
{
    MissingDevice,
    AmbiguousDevice,
    RemappedLeds,
    DeviceDropped,
}

public sealed record StudioImportIssue(
    StudioImportIssueKind Kind,
    ushort ProductId,
    string? InstanceId,
    string Message);

public sealed record StudioImportResult(StudioProject Project, IReadOnlyList<StudioImportIssue> Issues);

/// <summary>
/// Reconciles a .ChromaEffects import with the devices connected here. Region
/// entries are rewritten to the matched physical device; entries with no match
/// are removed and reported. LED references are kept as authored because the
/// imported file carries no layout to remap against.
/// </summary>
public static class StudioChromaImport
{
    public static StudioImportResult Import(string json, IReadOnlyList<StudioDeviceMatch> connected)
    {
        var project = StudioProjectCodec.Deserialize(json);
        var matches = connected ?? [];
        var issues = new List<StudioImportIssue>();

        foreach (var profile in project.Profiles)
        foreach (var layer in profile.EnumerateLayers())
        foreach (var region in layer.Regions)
        foreach (var device in region.Devices.ToList())
            Reconcile(device, region, matches, issues);

        return new StudioImportResult(project, issues);
    }

    private static void Reconcile(
        StudioRegionDevice device,
        StudioRegion region,
        IReadOnlyList<StudioDeviceMatch> connected,
        List<StudioImportIssue> issues)
    {
        var match = Resolve(connected, device, issues);
        if (match is null)
        {
            region.Devices.Remove(device);
            issues.Add(new StudioImportIssue(
                StudioImportIssueKind.MissingDevice,
                device.ProductId,
                device.InstanceId,
                $"No connected device matches product id {device.ProductId}{InstanceSuffix(device.InstanceId)}."));
            issues.Add(new StudioImportIssue(
                StudioImportIssueKind.DeviceDropped,
                device.ProductId,
                device.InstanceId,
                $"Dropped region device and its {device.Leds.Count} LED reference(s)."));
            return;
        }

        if (!string.Equals(device.InstanceId, match.InstanceId, StringComparison.OrdinalIgnoreCase))
            issues.Add(new StudioImportIssue(
                StudioImportIssueKind.RemappedLeds,
                match.ProductId,
                match.InstanceId,
                $"Remapped {device.Leds.Count} LED reference(s) from instance '{device.InstanceId ?? "(none)"}' to '{match.InstanceId}'."));

        device.InstanceId = match.InstanceId;
        device.ProductId = match.ProductId;
    }

    private static StudioDeviceMatch? Resolve(
        IReadOnlyList<StudioDeviceMatch> connected,
        StudioRegionDevice device,
        List<StudioImportIssue> issues)
    {
        if (!string.IsNullOrWhiteSpace(device.InstanceId))
        {
            // Instance ids identify one physical device, so the first exact match wins.
            foreach (var candidate in connected)
                if (string.Equals(candidate.InstanceId, device.InstanceId, StringComparison.OrdinalIgnoreCase))
                    return candidate;
        }

        var candidates = connected.Where(candidate => candidate.ProductId == device.ProductId).ToList();
        if (candidates.Count == 1) return candidates[0];
        if (candidates.Count == 0) return null;

        var picked = candidates[0];
        issues.Add(new StudioImportIssue(
            StudioImportIssueKind.AmbiguousDevice,
            device.ProductId,
            picked.InstanceId,
            $"Product id {device.ProductId} matches {candidates.Count} connected devices; using the first one."));
        return picked;
    }

    private static string InstanceSuffix(string? instanceId) =>
        string.IsNullOrWhiteSpace(instanceId) ? string.Empty : $" with instance '{instanceId}'";
}
