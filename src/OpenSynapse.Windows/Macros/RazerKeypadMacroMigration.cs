using OpenSynapse.Core.Macros;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.Windows.Macros;

public static class RazerKeypadMacroMigration
{
    public static Task<bool> MigrateAsync(
        IEnumerable<ProfileDefinition> profiles,
        IReadOnlyList<string> connectedInstanceIds,
        Func<CancellationToken, Task<bool>> save,
        CancellationToken cancellationToken = default) =>
        MigrateDevicesAsync(profiles, connectedInstanceIds.Select(id =>
            (RazerKeypadInputCatalog.ProductId, id)).ToArray(), save, cancellationToken);

    public static async Task<bool> MigrateDevicesAsync(
        IEnumerable<ProfileDefinition> profiles,
        IReadOnlyList<(ushort ProductId, string InstanceId)> connectedDevices,
        Func<CancellationToken, Task<bool>> save,
        CancellationToken cancellationToken = default)
    {
        var connected = connectedDevices.Where(device => !string.IsNullOrWhiteSpace(device.InstanceId))
            .GroupBy(device => device.ProductId).ToDictionary(group => group.Key,
                group => group.Select(device => device.InstanceId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        var changes = new List<(MacroDefinition Macro, MacroTrigger Trigger, MacroSoftwareBinding Binding, string Instance)>();
        foreach (var macro in profiles.Distinct().SelectMany(profile => profile.Macros.Concat(profile.SystemMacros)).Distinct())
        {
            foreach (var trigger in macro.AllTriggers)
            {
                var binding = BindingFor(macro, trigger);
                if (binding is not { Layer: <= 1 } || !RazerKeypadInputCatalog.UsesHostMapping(binding.ProductId) ||
                    !RazerKeypadInputCatalog.GetButtonIds(binding.ProductId).Contains(binding.ButtonId))
                {
                    continue;
                }
                connected.TryGetValue(binding.ProductId, out var instances);
                var instance = !string.IsNullOrWhiteSpace(binding.InstanceId)
                    ? binding.InstanceId.Trim()
                    : instances?.Length == 1 ? instances[0] : null;
                if (instance is null ||
                    trigger.Input.Kind == MacroInputKind.RazerKey &&
                    trigger.Input.DeviceId == binding.ProductId &&
                    trigger.Input.RazerKeyCode == binding.ButtonId &&
                    StringComparer.OrdinalIgnoreCase.Equals(trigger.Input.InstanceId, instance) &&
                    StringComparer.OrdinalIgnoreCase.Equals(binding.InstanceId, instance))
                {
                    continue;
                }
                changes.Add((macro, trigger, binding, instance));
            }
        }
        if (changes.Count == 0)
        {
            return true;
        }

        var backups = changes.Select(change => change.Macro).Distinct()
            .Select(macro => new MacroBindingSnapshot(macro)).ToArray();
        var saved = false;
        try
        {
            foreach (var (macro, trigger, binding, instance) in changes)
            {
                trigger.Input = new MacroInput
                {
                    Kind = MacroInputKind.RazerKey,
                    DeviceId = binding.ProductId,
                    InstanceId = instance,
                    DeviceName = trigger.Input.DeviceName,
                    RazerKeyCode = binding.ButtonId,
                };
                trigger.SoftwareBinding = binding.Clone();
                trigger.SoftwareBinding.InstanceId = instance;
                if (ReferenceEquals(trigger, macro.Trigger))
                {
                    macro.SoftwareBinding = trigger.SoftwareBinding.Clone();
                }
            }
            saved = await save(cancellationToken).ConfigureAwait(false);
            return saved;
        }
        finally
        {
            if (!saved)
            {
                foreach (var backup in backups) backup.Restore();
            }
        }
    }

    /// <summary>Unresolved legacy bindings stay saved, but cannot fire from another keyboard.</summary>
    public static MacroDefinition ForRuntime(MacroDefinition macro)
    {
        if (!macro.AllTriggers.Any(trigger =>
                RazerKeypadInputCatalog.UsesHostMapping(BindingFor(macro, trigger)?.ProductId ?? 0) ||
                trigger.Input.Kind == MacroInputKind.RazerKey && RazerKeypadInputCatalog.UsesHostMapping(trigger.Input.DeviceId)))
        {
            return macro;
        }
        var copy = MacroDocumentService.Clone(macro);
        if (copy.Trigger is { SoftwareBinding: null } primary && copy.SoftwareBinding is { } primaryBinding)
        {
            primary.SoftwareBinding = primaryBinding.Clone();
        }
        var triggers = copy.AllTriggers.Where(trigger =>
        {
            var binding = BindingFor(copy, trigger);
            var input = trigger.Input;
            var keypadInput = input.Kind == MacroInputKind.RazerKey && RazerKeypadInputCatalog.UsesHostMapping(input.DeviceId);
            if (!RazerKeypadInputCatalog.UsesHostMapping(binding?.ProductId ?? 0) && !keypadInput) return true;
            if (!keypadInput || string.IsNullOrWhiteSpace(input.InstanceId) ||
                input.RazerKeyCode is < 0 or > byte.MaxValue ||
                !RazerKeypadInputCatalog.GetButtonIds(input.DeviceId).Contains((byte)input.RazerKeyCode)) return false;
            return binding is null || binding.ProductId == input.DeviceId &&
                binding.Layer <= 1 && input.RazerKeyCode == binding.ButtonId &&
                (string.IsNullOrWhiteSpace(binding.InstanceId) ||
                 StringComparer.OrdinalIgnoreCase.Equals(input.InstanceId, binding.InstanceId));
        }).ToArray();
        copy.Trigger = triggers.FirstOrDefault();
        copy.AdditionalTriggers = triggers.Skip(1).ToList();
        copy.SoftwareBinding = copy.Trigger?.SoftwareBinding?.Clone();
        return copy;
    }

    private static MacroSoftwareBinding? BindingFor(MacroDefinition macro, MacroTrigger trigger) =>
        trigger.SoftwareBinding ?? (ReferenceEquals(trigger, macro.Trigger) ? macro.SoftwareBinding : null);
}
