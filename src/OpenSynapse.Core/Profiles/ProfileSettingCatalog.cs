using System.Collections.Concurrent;
using OpenSynapse.Core.Devices;

namespace OpenSynapse.Core.Profiles;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ProfileSettingAttribute : Attribute
{
    public ProfileSettingAttribute(string key, DeviceCategory category, bool powerScoped = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Key = key;
        Category = category;
        PowerScoped = powerScoped;
    }

    public string Key { get; }
    public DeviceCategory Category { get; }
    public bool PowerScoped { get; }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class NotProfileScopedAttribute : Attribute
{
}

public sealed record ProfileSettingDescriptor(string Key, DeviceCategory Category, bool PowerScoped);

/// <summary>
/// Registry of device settings that are persisted through the profile pipeline.
/// Every new Apply/Set method on a device view model must be registered here or
/// marked <see cref="NotProfileScopedAttribute"/>; the audit test enforces it.
/// </summary>
public static class ProfileSettingCatalog
{
    private static readonly ConcurrentDictionary<string, ProfileSettingDescriptor> Registered =
        new(StringComparer.Ordinal);

    public static IReadOnlyCollection<ProfileSettingDescriptor> All => Registered.Values.ToArray();

    public static void Register(ProfileSettingDescriptor setting)
    {
        ArgumentNullException.ThrowIfNull(setting);
        ArgumentException.ThrowIfNullOrWhiteSpace(setting.Key);
        if (!Registered.TryAdd(setting.Key, setting))
        {
            throw new InvalidOperationException($"Profile setting '{setting.Key}' is already registered.");
        }
    }

    public static ProfileSettingDescriptor? Find(string key) =>
        Registered.TryGetValue(key, out var setting) ? setting : null;
}
