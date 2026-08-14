namespace OpenSynapse.Core.Devices;

public enum DeviceAccessState
{
    Available,
    BusyOrUnavailable,
}

public enum DeviceCapabilityState
{
    PendingValidation,
    Unsupported,
    Blocked,
}

public sealed record DeviceDescriptor(
    string Id,
    string Name,
    ushort VendorId,
    ushort ProductId,
    DeviceAccessState Access,
    DeviceCapabilityState Capability,
    ushort FeatureReportByteLength,
    ushort UsagePage,
    ushort Usage,
    string ProtocolFamily);
