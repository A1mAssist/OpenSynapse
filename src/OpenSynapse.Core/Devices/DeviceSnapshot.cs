namespace OpenSynapse.Core.Devices;

public sealed record DeviceSnapshot(
    IReadOnlyList<DeviceDescriptor> Devices,
    DateTimeOffset CapturedAt,
    string? ErrorMessage = null)
{
    public static DeviceSnapshot Empty(DateTimeOffset? capturedAt = null) =>
        new(Array.Empty<DeviceDescriptor>(), capturedAt ?? DateTimeOffset.UtcNow);
}
