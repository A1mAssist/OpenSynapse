using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;

namespace OpenSynapse.Core.Profiles;

public sealed record DeviceApplicationSwitchDevice(
    DeviceBindingKey Binding,
    DeviceDescriptor Descriptor);

public sealed record DeviceApplicationSwitchChange(
    DeviceBindingKey Device,
    string ProfileName,
    string? RuleId);

/// <summary>Computes one target per physical device; callers own the device write gate.</summary>
public sealed class DeviceApplicationSwitchCoordinator
{
    private readonly Dictionary<string, DeviceApplicationSwitchState> _states = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, DeviceApplicationSwitchState> States => _states;

    public IReadOnlyList<DeviceApplicationSwitchChange> Update(
        DeviceApplicationRuleDocument rules,
        IReadOnlyList<DeviceApplicationSwitchDevice> connected,
        ProfileDocument profiles,
        string? executablePath,
        bool manualChange = false,
        bool foregroundChanged = false)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(connected);
        ArgumentNullException.ThrowIfNull(profiles);
        var online = connected.ToDictionary(
            device => Key(device.Binding), device => device, StringComparer.OrdinalIgnoreCase);
        var changes = new List<DeviceApplicationSwitchChange>();

        foreach (var (key, state) in _states)
        {
            if (!online.ContainsKey(key))
            {
                state.ActiveRuleId = null;
                state.AppliedProfileName = null;
                state.FallbackProfileName = null;
                state.HasManualOverride = false;
                state.Status = DeviceApplicationSwitchStatus.DeviceOffline;
            }
        }

        if (!rules.Enabled)
        {
            foreach (var state in _states.Values)
            {
                state.ActiveRuleId = null;
                state.AppliedProfileName = null;
                state.FallbackProfileName = null;
                state.HasManualOverride = false;
                state.Status = DeviceApplicationSwitchStatus.Suspended;
            }
            return changes;
        }

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            foreach (var device in connected)
            {
                var state = State(device.Binding);
                state.Status = DeviceApplicationSwitchStatus.PathUnavailable;
            }
            return changes;
        }

        foreach (var device in connected)
        {
            var state = State(device.Binding);
            var rule = DeviceApplicationRuleCatalog.FindForExecutable(rules, device.Binding, executablePath);
            if (manualChange && state.ActiveRuleId is not null)
            {
                state.HasManualOverride = true;
            }
            else if (foregroundChanged)
            {
                state.HasManualOverride = false;
            }

            string? target;
            string? ruleId;
            if (state.HasManualOverride)
            {
                target = null;
                ruleId = state.ActiveRuleId;
                state.Status = DeviceApplicationSwitchStatus.Applied;
            }
            else if (rule is not null)
            {
                target = rule.ProfileName;
                ruleId = rule.Id;
                if (state.ActiveRuleId is null)
                {
                    state.FallbackProfileName = profiles.ActiveProfileName;
                }
                state.Status = profiles.Profiles.ContainsKey(target)
                    ? DeviceApplicationSwitchStatus.WaitingForApplication
                    : DeviceApplicationSwitchStatus.ProfileMissing;
            }
            else if (state.ActiveRuleId is not null)
            {
                target = state.FallbackProfileName;
                ruleId = null;
                state.Status = DeviceApplicationSwitchStatus.Restored;
                if (target is null || !profiles.Profiles.ContainsKey(target))
                {
                    state.Status = DeviceApplicationSwitchStatus.ProfileMissing;
                    target = null;
                }
            }
            else
            {
                state.Status = DeviceApplicationSwitchStatus.Idle;
                continue;
            }

            if (target is null || StringComparer.OrdinalIgnoreCase.Equals(state.AppliedProfileName, target))
            {
                continue;
            }

            if (state.Status == DeviceApplicationSwitchStatus.WaitingForApplication ||
                state.Status == DeviceApplicationSwitchStatus.Restored)
            {
                changes.Add(new(device.Binding, target, ruleId));
            }
        }
        return changes;
    }

    public void Complete(DeviceBindingKey device, string profileName, string? ruleId, bool succeeded)
    {
        var state = State(device);
        if (!succeeded)
        {
            state.Status = DeviceApplicationSwitchStatus.ApplyFailed;
            return;
        }

        state.AppliedProfileName = profileName;
        state.ActiveRuleId = ruleId;
        if (ruleId is null)
        {
            state.FallbackProfileName = null;
            state.Status = DeviceApplicationSwitchStatus.Restored;
        }
        else
        {
            state.Status = DeviceApplicationSwitchStatus.Applied;
        }
    }

    public void MarkManualOverride(DeviceBindingKey device) => State(device).HasManualOverride = true;

    public void MarkManualOverrides(IEnumerable<DeviceBindingKey> devices)
    {
        foreach (var device in devices)
            if (_states.TryGetValue(Key(device), out var state) && state.ActiveRuleId is not null)
                state.HasManualOverride = true;
    }

    private DeviceApplicationSwitchState State(DeviceBindingKey device)
    {
        var key = Key(device);
        if (!_states.TryGetValue(key, out var state))
        {
            _states[key] = state = new DeviceApplicationSwitchState();
        }
        return state;
    }

    private static string Key(DeviceBindingKey device) =>
        $"{device.VendorId:X4}:{device.ProductId:X4}:{device.InstanceId.Trim()}";
}
