namespace OpenSynapse.Core.Profiles;

public static class DeviceApplicationRuleCatalog
{
    public static DeviceApplicationRuleDocument AddOrUpdate(
        DeviceApplicationRuleDocument document,
        DeviceApplicationRule rule)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(rule);
        var normalized = rule with
        {
            Id = rule.Id?.Trim() ?? string.Empty,
            ProfileName = rule.ProfileName?.Trim() ?? string.Empty,
            ExecutablePath = Path.GetFullPath(rule.ExecutablePath.Trim()),
        };
        var rules = document.Rules.ToList();
        var index = rules.FindIndex(existing => SameBinding(existing, normalized));
        if (index >= 0)
        {
            normalized = normalized with
            {
                Id = rules[index].Id,
                Device = rules[index].Device,
                ExecutablePath = rules[index].ExecutablePath,
            };
            rules[index] = normalized;
        }
        else
        {
            rules.Add(normalized);
        }

        var result = document with
        {
            Rules = rules,
            PendingBindings = document.PendingBindings
                .Where(pending => !PathsEqual(pending.ExecutablePath, normalized.ExecutablePath) ||
                    !StringComparer.OrdinalIgnoreCase.Equals(pending.ProfileName, normalized.ProfileName))
                .ToArray(),
        };
        Validate(result);
        return result;
    }

    public static DeviceApplicationRuleDocument Remove(DeviceApplicationRuleDocument document, string id)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return document with
        {
            Rules = document.Rules.Where(rule => !string.Equals(rule.Id, id, StringComparison.Ordinal)).ToArray(),
        };
    }

    public static DeviceApplicationRuleDocument SetEnabled(
        DeviceApplicationRuleDocument document,
        string id,
        bool enabled)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var found = false;
        var rules = document.Rules.Select(rule =>
        {
            if (!string.Equals(rule.Id, id, StringComparison.Ordinal))
            {
                return rule;
            }

            found = true;
            return rule with { Enabled = enabled };
        }).ToArray();
        return found
            ? document with { Rules = rules }
            : throw new KeyNotFoundException($"Rule '{id}' does not exist.");
    }

    public static DeviceApplicationRuleDocument RenameProfileReferences(
        DeviceApplicationRuleDocument document,
        string oldName,
        string newName)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(oldName);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        return document with
        {
            Rules = document.Rules.Select(rule => StringComparer.OrdinalIgnoreCase.Equals(rule.ProfileName, oldName)
                ? rule with { ProfileName = newName.Trim() }
                : rule).ToArray(),
            PendingBindings = document.PendingBindings.Select(binding =>
                StringComparer.OrdinalIgnoreCase.Equals(binding.ProfileName, oldName)
                    ? binding with { ProfileName = newName.Trim() }
                    : binding).ToArray(),
        };
    }

    public static DeviceApplicationRule? FindForExecutable(
        DeviceApplicationRuleDocument document,
        DeviceBindingKey device,
        string? executablePath)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!document.Enabled || string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }

        var path = NormalizePath(executablePath);
        return document.Rules.FirstOrDefault(rule =>
            rule.Enabled && rule.Device.Equals(device) && PathsEqual(rule.ExecutablePath, path));
    }

    public static void Validate(DeviceApplicationRuleDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Version != DeviceApplicationRuleStore.CurrentVersion)
        {
            throw new InvalidDataException($"Unsupported rule document version: {document.Version}.");
        }
        if (document.Rules is null || document.PendingBindings is null)
        {
            throw new InvalidDataException("Rule collections cannot be null.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var bindings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in document.Rules)
        {
            if (rule is null || string.IsNullOrWhiteSpace(rule.Id) ||
                string.IsNullOrWhiteSpace(rule.ProfileName) ||
                string.IsNullOrWhiteSpace(rule.Device.InstanceId))
            {
                throw new InvalidDataException("Every rule needs an ID, device instance ID and profile.");
            }
            if (!ids.Add(rule.Id) || !bindings.Add(BindingKey(rule.Device, NormalizePath(rule.ExecutablePath))))
            {
                throw new InvalidDataException("Rule IDs and per-device executable bindings must be unique.");
            }
            if (!string.Equals(rule.ExecutablePath, Path.GetFullPath(rule.ExecutablePath.Trim()), StringComparison.Ordinal))
            {
                throw new InvalidDataException("Executable paths must be absolute and normalized.");
            }
        }

        var pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var binding in document.PendingBindings)
        {
            if (binding is null || string.IsNullOrWhiteSpace(binding.ProfileName) ||
                string.IsNullOrWhiteSpace(binding.ExecutablePath) ||
                !pending.Add($"{NormalizePath(binding.ExecutablePath)}\0{binding.ProfileName}"))
            {
                throw new InvalidDataException("Pending bindings must be valid and unique.");
            }
        }
    }

    internal static bool PathsEqual(string left, string right) =>
        StringComparer.OrdinalIgnoreCase.Equals(left, right);

    internal static string NormalizePath(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        return Path.GetFullPath(executablePath.Trim());
    }

    private static bool SameBinding(DeviceApplicationRule left, DeviceApplicationRule right) =>
        left.Device.Equals(right.Device) && PathsEqual(left.ExecutablePath, right.ExecutablePath);

    private static string BindingKey(DeviceBindingKey device, string path) =>
        $"{device.VendorId:X4}:{device.ProductId:X4}:{device.InstanceId.Trim()}\0{path}";
}
