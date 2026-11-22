using System.Globalization;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidWorkspace
{
    private readonly HashSet<string> _restoredLightingEndpoints =
        new(StringComparer.OrdinalIgnoreCase);

    private async Task RestoreInitialLightingAsync(
        IReadOnlyList<RazerHidDeviceConnection> connections,
        CancellationToken cancellationToken)
    {
        // Boot can enumerate the dock and the wireless mouse at different times.
        // Remember each endpoint instead of treating the first discovery as final,
        // otherwise a device that appears one refresh later never gets its profile.
        var liveEndpoints = connections
            .Where(connection => connection.IsReady)
            .Select(EndpointKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _restoredLightingEndpoints.IntersectWith(liveEndpoints);
        if (connections.Count == 0 || StudioLightingActive?.Invoke() == true)
        {
            return;
        }

        var pending = connections
            .Where(connection => connection.IsReady &&
                !_restoredLightingEndpoints.Contains(EndpointKey(connection)))
            .ToArray();
        if (pending.Length == 0)
        {
            return;
        }

        var pendingEndpoints = pending.Select(EndpointKey).ToArray();
        var pendingInstanceIds = pending
            .Select(connection => connection.InstanceId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var endpoint in pendingEndpoints)
        {
            _restoredLightingEndpoints.Add(endpoint);
        }

        var failedEndpoints = new List<string>();
        try
        {
            // A tray start has no selected device, so nothing used to replay the
            // saved lighting profiles until the user clicked a row.
            _diagnosticLog.TryWrite(
                "razer-hid",
                $"initial lighting restore for {pending.Length} new device(s): " +
                string.Join(",", pending.Select(connection => $"{connection.Definition.ProductId:X4}")));
            var failedInstanceIds = await _softwareLightingOwner.RestoreAsync(
                connections, SelectedDevice, cancellationToken, pendingInstanceIds).ConfigureAwait(true);
            if (failedInstanceIds.Count > 0)
            {
                failedEndpoints.AddRange(pending
                    .Where(connection => failedInstanceIds.Contains(connection.InstanceId))
                    .Select(EndpointKey));
                _diagnosticLog.TryWrite(
                    "razer-hid",
                    $"initial lighting restore failed for {failedEndpoints.Count} device(s); will retry");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _diagnosticLog.TryWrite(
                "razer-hid",
                $"initial lighting restore failed: {exception.Message}");
            failedEndpoints.AddRange(pendingEndpoints);
        }
        finally
        {
            // A failed or cancelled write must retry on the next refresh; a device
            // with no saved profile counts as restored.
            foreach (var endpoint in failedEndpoints)
            {
                _restoredLightingEndpoints.Remove(endpoint);
            }
        }
    }

    private static string EndpointKey(RazerHidDeviceConnection connection) =>
        $"{connection.InstanceId}|{connection.DevicePath}";

    private async Task<bool> SaveLightingAsync(
        RazerHidDeviceConnection connection,
        PowerSourceKind powerState,
        RazerHidLedZone zone,
        LightingProfile profile,
        CancellationToken cancellationToken)
    {
        var active = _getActiveProfile();
        var key = ProfileResolver.GetDeviceKey(connection.ToDescriptor());
        var zoneKey = ((byte)zone).ToString(CultureInfo.InvariantCulture);
        var defaultZoneKey = ((byte)connection.Definition.DefaultLedZone).ToString(CultureInfo.InvariantCulture);
        PowerProfileOverrides? power = powerState switch
        {
            PowerSourceKind.FullPowerAdapter => active.PluggedIn,
            PowerSourceKind.LowPowerAdapter => active.LowPowerAdapter,
            PowerSourceKind.Battery => active.OnBattery,
            _ => null,
        };

        Dictionary<string, LightingProfile> target;
        LightingProfile? previous = null;
        var hadPrevious = false;
        if (power is null)
        {
            if (!active.Devices.TryGetValue(key, out var settings))
            {
                settings = new DeviceProfileSettings();
                active.Devices[key] = settings;
            }

            MigrateLegacyLighting(settings.Lighting, settings.LightingZones, zoneKey, defaultZoneKey);
            hadPrevious = settings.LightingZones.TryGetValue(zoneKey, out previous);
            settings.LightingZones[zoneKey] = CloneLightingProfile(profile);
            target = settings.LightingZones;
        }
        else
        {
            if (!power.RazerHidLightingZones.TryGetValue(key, out var zones))
            {
                zones = new Dictionary<string, LightingProfile>(StringComparer.OrdinalIgnoreCase);
                power.RazerHidLightingZones[key] = zones;
            }

            if (power.RazerHidLighting.TryGetValue(key, out var legacy))
            {
                MigrateLegacyLighting(legacy, zones, zoneKey, defaultZoneKey);
            }
            hadPrevious = zones.TryGetValue(zoneKey, out previous);
            zones[zoneKey] = CloneLightingProfile(profile);
            target = zones;
        }

        if (await _saveProfileAsync(cancellationToken))
        {
            return true;
        }

        if (hadPrevious && previous is not null)
        {
            target[zoneKey] = previous;
        }
        else
        {
            target.Remove(zoneKey);
        }

        return false;
    }

    private static void MigrateLegacyLighting(
        LightingProfile legacy,
        IDictionary<string, LightingProfile> zones,
        string zoneKey,
        string defaultZoneKey)
    {
        if (legacy is null ||
            legacy.Parameters is null ||
            (string.IsNullOrWhiteSpace(legacy.Effect) && legacy.Parameters.Count == 0) ||
            zones.ContainsKey(zoneKey))
        {
            return;
        }

        var legacyZone = legacy.Parameters.TryGetValue("zone", out var rawZone)
            ? rawZone
            : defaultZoneKey;
        if (StringComparer.OrdinalIgnoreCase.Equals(legacyZone, zoneKey))
        {
            zones[zoneKey] = CloneLightingProfile(legacy);
        }
    }
}
