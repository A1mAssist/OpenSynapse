namespace OpenSynapse.Core.Profiles;

public readonly record struct DeviceBindingKey(ushort VendorId, ushort ProductId, string InstanceId)
{
    public bool Equals(DeviceBindingKey other) =>
        VendorId == other.VendorId &&
        ProductId == other.ProductId &&
        StringComparer.OrdinalIgnoreCase.Equals(InstanceId?.Trim(), other.InstanceId?.Trim());

    public override int GetHashCode() => HashCode.Combine(
        VendorId,
        ProductId,
        StringComparer.OrdinalIgnoreCase.GetHashCode(InstanceId?.Trim() ?? string.Empty));
}

public sealed record DeviceApplicationRule(
    string Id,
    DeviceBindingKey Device,
    string ProfileName,
    string ExecutablePath,
    bool Enabled);

public sealed record DeviceApplicationRuleDocument(
    int Version,
    bool Enabled,
    IReadOnlyList<DeviceApplicationRule> Rules)
{
    public int ApplicationBindingMigrationVersion { get; init; }
    public IReadOnlyList<PendingDeviceApplicationBinding> PendingBindings { get; init; } = [];

    public static DeviceApplicationRuleDocument CreateDefault() =>
        new(DeviceApplicationRuleStore.CurrentVersion, true, []);
}

public sealed record PendingDeviceApplicationBinding(string ExecutablePath, string ProfileName);
