namespace OpenSynapse.Windows.Devices;

public enum RazerKeypadMappingUnavailableReason
{
    DriverMissing,
    DriverInUse,
    AmbiguousDevice,
    RecoveryMissing,
    Stopped,
}

public sealed class RazerKeypadMappingUnavailableException(
    RazerKeypadMappingUnavailableReason reason, string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException)
{
    public RazerKeypadMappingUnavailableReason Reason { get; } = reason;
}
