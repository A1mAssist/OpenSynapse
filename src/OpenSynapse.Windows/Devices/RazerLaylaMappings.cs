using System.Text.Json;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Macros;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public enum RazerLaylaAction { Disabled, Mute, PlayPause, NextTrack, PreviousTrack, VolumeUp, VolumeDown, CyclePlayback, Macro }
public sealed record RazerLaylaBinding(RazerLaylaAction Action, string? MacroId = null);

public static class RazerLaylaMappings
{
    public const string SettingKey = "layla.mappings";
    public static string DeviceKey(RazerBleDevice device) => $"ble:{device.Id}";
    public static DeviceDescriptor Descriptor(RazerBleDevice device) => new(
        DeviceKey(device), device.Name, 0x1532, RazerLaylaProtocol.ProductId,
        DeviceAccessState.Available, DeviceCapabilityState.PendingValidation, 0, 0, 0, "ble", DeviceCategory.Accessory);

    public static Dictionary<RazerLaylaInput, RazerLaylaBinding> Defaults() => new()
    {
        [RazerLaylaInput.Click] = new(RazerLaylaAction.Mute),
        [RazerLaylaInput.DoubleClick] = new(RazerLaylaAction.PlayPause),
        [RazerLaylaInput.TripleClick] = new(RazerLaylaAction.NextTrack),
        [RazerLaylaInput.Hold] = new(RazerLaylaAction.Disabled),
        [RazerLaylaInput.SourceClick] = new(RazerLaylaAction.CyclePlayback),
        [RazerLaylaInput.SourceDoubleClick] = new(RazerLaylaAction.Disabled),
        [RazerLaylaInput.Clockwise] = new(RazerLaylaAction.VolumeUp),
        [RazerLaylaInput.Anticlockwise] = new(RazerLaylaAction.VolumeDown),
    };

    public static Dictionary<RazerLaylaInput, RazerLaylaBinding> Read(ProfileDocument document, RazerBleDevice device)
    {
        // Public resolver synchronizes Devices with the selected profile.
        _ = ProfileResolver.Resolve(document, Descriptor(device), PowerSourceKind.Unknown);
        if (!document.Devices.TryGetValue(DeviceKey(device), out var settings) ||
            !settings.Extras.TryGetValue(SettingKey, out var saved)) return Defaults();
        var result = JsonSerializer.Deserialize<Dictionary<RazerLaylaInput, RazerLaylaBinding>>(saved.Value ?? "null")
            ?? throw new InvalidDataException("Control Pod mappings are empty.");
        Validate(result);
        return result;
    }

    public static IReadOnlyList<MacroDefinition> Macros(ProfileDocument document) =>
        document.Profiles.TryGetValue(document.ActiveProfileName, out var active)
            ? active.Macros.Concat(active.SystemMacros).Select(MacroDocumentService.Clone).ToArray() : [];

    public static DeviceSettingValue Encode(IReadOnlyDictionary<RazerLaylaInput, RazerLaylaBinding> mappings)
    {
        Validate(mappings);
        return new() { Kind = "json", Value = JsonSerializer.Serialize(mappings) };
    }

    public static async Task<bool> SaveAsync(ProfileDocument document, RazerBleDevice device,
        IReadOnlyDictionary<RazerLaylaInput, RazerLaylaBinding> mappings,
        Func<CancellationToken, Task<bool>> save, CancellationToken token = default)
    {
        var encoded = Encode(mappings);
        _ = ProfileResolver.Resolve(document, Descriptor(device), PowerSourceKind.Unknown);
        var entries = document.Devices;
        var key = DeviceKey(device);
        var existed = entries.TryGetValue(key, out var settings);
        settings ??= new DeviceProfileSettings();
        var hadValue = settings.Extras.TryGetValue(SettingKey, out var previous);
        settings.Extras[SettingKey] = encoded;
        entries[key] = settings;
        var saved = false;
        try { saved = await save(token).ConfigureAwait(false); return saved; }
        finally
        {
            if (!saved)
            {
                if (!existed) entries.Remove(key);
                else if (hadValue) settings.Extras[SettingKey] = previous!;
                else settings.Extras.Remove(SettingKey);
            }
        }
    }

    public static void Validate(IReadOnlyDictionary<RazerLaylaInput, RazerLaylaBinding> mappings)
    {
        if (mappings.Count != 8 || Enum.GetValues<RazerLaylaInput>().Any(input => !mappings.ContainsKey(input)))
            throw new InvalidDataException("Control Pod requires all eight input mappings.");
        foreach (var binding in mappings.Values)
            if (binding is null || !Enum.IsDefined(binding.Action) ||
                binding.Action == RazerLaylaAction.Macro && string.IsNullOrWhiteSpace(binding.MacroId))
                throw new InvalidDataException("Control Pod has an invalid action or missing macro.");
    }
}
