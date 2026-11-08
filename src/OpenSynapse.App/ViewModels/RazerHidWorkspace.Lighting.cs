using System.Globalization;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidWorkspace
{
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
