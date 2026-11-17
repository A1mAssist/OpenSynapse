using OpenSynapse.Core.Devices;

namespace OpenSynapse.Core.Profiles;

public sealed record DeviceApplicationRuleMigrationResult(
    DeviceApplicationRuleDocument Document,
    int Added,
    bool Completed);

public static class DeviceApplicationRuleMigration
{
    public static async Task<DeviceApplicationRuleMigrationResult> MigrateAsync(
        DeviceApplicationRuleStore store,
        ProfileDocument profiles,
        IReadOnlyList<DeviceBindingKey> connectedDevices,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(connectedDevices);
        var document = await store.LoadAsync(cancellationToken);
        if (profiles.ApplicationBindings.Count == 0)
            return new(document, 0, true);
        var result = Migrate(document, profiles, connectedDevices);
        if (!ReferenceEquals(result.Document, document))
        {
            await store.SaveAsync(result.Document, cancellationToken);
        }

        return result;
    }

    public static DeviceApplicationRuleMigrationResult Migrate(
        DeviceApplicationRuleDocument document,
        ProfileDocument profiles,
        IReadOnlyList<DeviceBindingKey> connectedDevices)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(connectedDevices);
        DeviceApplicationRuleCatalog.Validate(document);
        if (document.ApplicationBindingMigrationVersion == 1)
            return new(document, 0, true);
        return Migrate(document, profiles, connectedDevices
            .Where(device => !string.IsNullOrWhiteSpace(device.InstanceId))
            .Distinct()
            .ToArray(),
            static _ => true);
    }

    public static DeviceApplicationRuleMigrationResult MigrateConnectedDevices(
        DeviceApplicationRuleDocument document,
        ProfileDocument profiles,
        IReadOnlyList<DeviceDescriptor> connectedDevices)
    {
        ArgumentNullException.ThrowIfNull(connectedDevices);
        return Migrate(document, profiles, connectedDevices
            .Select(device => new DeviceBindingKey(device.VendorId, device.ProductId, device.Id))
            .Distinct()
            .ToArray(),
            device => connectedDevices.Any(candidate =>
                new DeviceBindingKey(candidate.VendorId, candidate.ProductId, candidate.Id).Equals(device) &&
                candidate.Category is DeviceCategory.Keyboard or DeviceCategory.Mouse &&
                candidate.Access == DeviceAccessState.Available &&
                candidate.Capability is not (DeviceCapabilityState.Unsupported or DeviceCapabilityState.Blocked) &&
                !string.IsNullOrWhiteSpace(candidate.Id)));
    }

    private static DeviceApplicationRuleMigrationResult Migrate(
        DeviceApplicationRuleDocument document,
        ProfileDocument profiles,
        IReadOnlyList<DeviceBindingKey> connectedDevices,
        Func<DeviceBindingKey, bool> isCompatible)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(profiles);
        DeviceApplicationRuleCatalog.Validate(document);
        var devices = connectedDevices
            .Where(isCompatible)
            .Distinct()
            .ToArray();
        var pending = document.PendingBindings.ToList();
        var result = document;
        var added = 0;

        foreach (var binding in profiles.ApplicationBindings)
        {
            if (string.IsNullOrWhiteSpace(binding.Key) || string.IsNullOrWhiteSpace(binding.Value) ||
                !profiles.Profiles.TryGetValue(binding.Value, out _))
            {
                continue;
            }

            var path = DeviceApplicationRuleCatalog.NormalizePath(binding.Key);
            var profile = profiles.Profiles.Keys.First(name =>
                StringComparer.OrdinalIgnoreCase.Equals(name, binding.Value));
            if (!pending.Any(item => DeviceApplicationRuleCatalog.PathsEqual(item.ExecutablePath, path) &&
                    StringComparer.OrdinalIgnoreCase.Equals(item.ProfileName, profile)))
            {
                pending.Add(new(path, profile));
            }
        }

        if (devices.Length > 0)
        {
            foreach (var binding in pending)
            {
                if (!profiles.Profiles.ContainsKey(binding.ProfileName))
                {
                    continue;
                }

                foreach (var device in devices)
                {
                    if (result.Rules.Any(rule => rule.Device.Equals(device) &&
                        DeviceApplicationRuleCatalog.PathsEqual(rule.ExecutablePath, binding.ExecutablePath)))
                    {
                        continue;
                    }

                    result = DeviceApplicationRuleCatalog.AddOrUpdate(result,
                        new DeviceApplicationRule(Guid.NewGuid().ToString("N"), device,
                            binding.ProfileName, binding.ExecutablePath, true));
                    added++;
                }
            }
        }

        IReadOnlyList<PendingDeviceApplicationBinding> remaining = devices.Length == 0
            ? pending
            : pending.Where(binding => !profiles.Profiles.ContainsKey(binding.ProfileName)).ToArray();
        if (added == 0 && remaining.SequenceEqual(document.PendingBindings))
        {
            return new(document, 0, remaining.Count == 0);
        }

        result = result with
        {
            PendingBindings = remaining,
            ApplicationBindingMigrationVersion = remaining.Count == 0 ? 1 : 0,
        };
        return new(result, added, result.PendingBindings.Count == 0);
    }
}
