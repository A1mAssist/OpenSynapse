using OpenSynapse.Core.Sensors;

namespace OpenSynapse.Core.Profiles;

public sealed class RazerRipsawProfileSettings
{
    public Dictionary<string, int> ChannelVolumes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, bool> ChannelMuted { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public void Validate()
    {
        if (ChannelVolumes is null || ChannelMuted is null)
            throw new InvalidDataException("Ripsaw channel maps must not be null.");
        foreach (var key in ChannelVolumes.Keys.Concat(ChannelMuted.Keys))
            if (!new[] { "Speaker", "Pc", "Hdmi", "Mic" }.Contains(key, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException($"Unknown Ripsaw monitoring channel: {key}.");
        foreach (var value in ChannelVolumes.Values)
            if (value is < 0 or > 100) throw new InvalidDataException("Ripsaw volume must be between 0 and 100.");
    }

    internal void ApplySafeDefaults()
    {
        var volumes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in ChannelVolumes ?? []) volumes[pair.Key] = pair.Value;
        ChannelVolumes = volumes;
        var muted = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in ChannelMuted ?? []) muted[pair.Key] = pair.Value;
        ChannelMuted = muted;
        Validate();
    }

    internal RazerRipsawProfileSettings Clone() => new()
    {
        ChannelVolumes = new(ChannelVolumes, StringComparer.OrdinalIgnoreCase),
        ChannelMuted = new(ChannelMuted, StringComparer.OrdinalIgnoreCase),
    };
}

public static partial class ProfileResolver
{
    public static RazerRipsawProfileSettings ResolveRipsaw(
        ProfileDocument document, PowerSourceKind powerSource, string deviceKey)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceKey);
        var active = document.GetActiveProfileDefinition();
        var power = powerSource switch
        {
            PowerSourceKind.FullPowerAdapter => active.PluggedIn,
            PowerSourceKind.LowPowerAdapter => active.LowPowerAdapter,
            PowerSourceKind.Battery => active.OnBattery,
            _ => null,
        };
        var result = new RazerRipsawProfileSettings();
        foreach (var source in new[] { active.Global.Ripsaw, FindDeviceSettings(active.Devices, deviceKey)?.Ripsaw, power?.Ripsaw })
        {
            if (source is null) continue;
            source.Validate();
            foreach (var pair in source.ChannelVolumes) result.ChannelVolumes[pair.Key] = pair.Value;
            foreach (var pair in source.ChannelMuted) result.ChannelMuted[pair.Key] = pair.Value;
        }
        return result;
    }
}
