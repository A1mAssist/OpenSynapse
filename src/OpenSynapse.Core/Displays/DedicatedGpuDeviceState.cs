namespace OpenSynapse.Core.Displays;

/// <summary>
/// The PnP state of the discrete GPU adapter, as Windows reports it.
///
/// A disabled adapter and a faulted adapter are different situations. A disabled
/// adapter is one this app turned off on purpose; a faulted one failed to start and
/// Windows stopped it. The distinction matters because a faulted adapter cannot be
/// disabled any further, but it can be restarted to recover.
/// </summary>
public enum DedicatedGpuDeviceState
{
    /// <summary>The adapter is present and running.</summary>
    Enabled,

    /// <summary>The adapter was turned off through the device manager.</summary>
    Disabled,

    /// <summary>The adapter failed to start and Windows stopped it.</summary>
    Faulted,
}

public static class DedicatedGpuDeviceStateExtensions
{
    /// <summary>True when the adapter is running normally.</summary>
    public static bool IsEnabled(this DedicatedGpuDeviceState state) =>
        state == DedicatedGpuDeviceState.Enabled;

    /// <summary>True when the adapter was turned off on purpose.</summary>
    public static bool IsDisabled(this DedicatedGpuDeviceState state) =>
        state == DedicatedGpuDeviceState.Disabled;

    /// <summary>True when the adapter failed and may be restarted to recover.</summary>
    public static bool IsFaulted(this DedicatedGpuDeviceState state) =>
        state == DedicatedGpuDeviceState.Faulted;
}

public readonly record struct DedicatedGpuDeviceInfo(
    DedicatedGpuDeviceState State,
    int ProblemCode)
{
    /// <summary>
    /// Maps a Windows configuration-manager problem code onto an adapter state.
    /// A zero code means the adapter is healthy.
    /// </summary>
    public static DedicatedGpuDeviceState StateFromProblemCode(int problemCode) => problemCode switch
    {
        0 => DedicatedGpuDeviceState.Enabled,
        DedicatedGpuProblemCodes.Disabled => DedicatedGpuDeviceState.Disabled,
        _ => DedicatedGpuDeviceState.Faulted,
    };
}

/// <summary>
/// The Windows configuration-manager problem codes this app distinguishes.
/// </summary>
public static class DedicatedGpuProblemCodes
{
    /// <summary>CM_PROB_DISABLED: the device was turned off by the user or by an app.</summary>
    public const int Disabled = 22;

    /// <summary>CM_PROB_FAILED_POST_START: the driver reported a failure and Windows stopped the device.</summary>
    public const int FailedPostStart = 43;

    /// <summary>CM_PROB_NO_DEVICE: the device is not present.</summary>
    public const int NoDevice = 10;

    /// <summary>CM_PROB_NEED_RESTART: the device requires a system restart.</summary>
    public const int NeedRestart = 14;

    /// <summary>CM_PROB_HARDWARE_ERROR: the device reported a hardware error.</summary>
    public const int HardwareError = 5;

    /// <summary>CM_PROB_DRIVER_LOADED: the driver is loaded but the device is not started.</summary>
    public const int DriverLoaded = 8;
}
