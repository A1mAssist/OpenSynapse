namespace OpenSynapse.Core.Profiles;

public enum DeviceApplicationSwitchStatus
{
    Idle,
    WaitingForApplication,
    Applied,
    Restored,
    Suspended,
    DeviceOffline,
    PathUnavailable,
    ProfileMissing,
    ApplyFailed,
}

public sealed class DeviceApplicationSwitchState
{
    public string? ActiveRuleId { get; internal set; }
    public string? FallbackProfileName { get; internal set; }
    public bool HasManualOverride { get; internal set; }
    public DeviceApplicationSwitchStatus Status { get; internal set; }
    public string? AppliedProfileName { get; internal set; }
}
