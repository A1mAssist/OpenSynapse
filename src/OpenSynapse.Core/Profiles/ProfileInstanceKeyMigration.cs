using OpenSynapse.Core.Devices;

namespace OpenSynapse.Core.Profiles;

public sealed record ProfileInstanceKeyMigrationResult(
    ProfileDocument Document,
    int MigratedEntries,
    IReadOnlyList<string> AmbiguousLegacyKeys);

/// <summary>
/// Moves pre-instance <c>VID:PID</c> profile entries to the physical
/// <c>VID:PID:InstanceId</c> key. A legacy entry is only moved when exactly one
/// connected device has that product id; otherwise two devices would inherit it.
/// </summary>
public static class ProfileInstanceKeyMigration
{
    public static ProfileInstanceKeyMigrationResult Migrate(
        ProfileDocument document,
        IReadOnlyList<DeviceDescriptor> connectedDevices)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(connectedDevices);
        document.EnsureProfileCatalog();

        var ambiguous = new List<string>();
        var migrated = 0;
        var groups = connectedDevices
            .Where(device => !string.IsNullOrWhiteSpace(device.InstanceId))
            .GroupBy(LegacyKey, StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            var instances = group
                .Select(ProfileResolver.GetDeviceKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (instances.Length != 1)
            {
                if (document.Profiles.Values.Any(profile => HasLegacyEntry(profile, group.Key)))
                {
                    ambiguous.Add(group.Key);
                }

                continue;
            }

            foreach (var profile in document.Profiles.Values)
            {
                migrated += MoveProfileEntries(profile, group.Key, instances[0]);
            }
        }

        return new ProfileInstanceKeyMigrationResult(document, migrated, ambiguous);
    }

    private static int MoveProfileEntries(ProfileDefinition profile, string legacy, string instance)
    {
        var moved = MoveDictionary(profile.Devices, legacy, instance);
        moved += MovePowerEntries(profile.PluggedIn, legacy, instance);
        moved += MovePowerEntries(profile.LowPowerAdapter, legacy, instance);
        moved += MovePowerEntries(profile.OnBattery, legacy, instance);
        return moved;
    }

    private static int MovePowerEntries(PowerProfileOverrides power, string legacy, string instance)
    {
        var moved = MoveDictionary(power.RazerHidLighting, legacy, instance);
        if (power.RazerHidLightingZones.TryGetValue(legacy, out var zones) &&
            !power.RazerHidLightingZones.ContainsKey(instance))
        {
            power.RazerHidLightingZones[instance] = zones;
            power.RazerHidLightingZones.Remove(legacy);
            moved++;
        }

        if (power.LegacyRazerHidLighting is not null)
        {
            moved += MoveDictionary(power.LegacyRazerHidLighting, legacy, instance);
        }

        return moved;
    }

    private static bool HasLegacyEntry(ProfileDefinition profile, string key) =>
        profile.Devices.ContainsKey(key) ||
        profile.PluggedIn.RazerHidLighting.ContainsKey(key) ||
        profile.PluggedIn.RazerHidLightingZones.ContainsKey(key) ||
        profile.LowPowerAdapter.RazerHidLighting.ContainsKey(key) ||
        profile.LowPowerAdapter.RazerHidLightingZones.ContainsKey(key) ||
        profile.OnBattery.RazerHidLighting.ContainsKey(key) ||
        profile.OnBattery.RazerHidLightingZones.ContainsKey(key);

    private static string LegacyKey(DeviceDescriptor device) =>
        $"{device.VendorId:X4}:{device.ProductId:X4}";

    private static int MoveDictionary<T>(
        Dictionary<string, T> values,
        string legacy,
        string instance)
        where T : class
    {
        if (!values.TryGetValue(legacy, out var value) || values.ContainsKey(instance))
        {
            return 0;
        }

        values[instance] = value;
        values.Remove(legacy);
        return 1;
    }
}
