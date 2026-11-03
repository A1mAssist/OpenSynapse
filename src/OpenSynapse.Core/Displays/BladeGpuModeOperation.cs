namespace OpenSynapse.Core.Displays;

/// <summary>
/// Result of one user-visible GPU mode operation. Auto is resolved before the
/// controller executes, so Target is always a concrete hardware route.
/// </summary>
public enum BladeGpuModeApplyOutcome
{
    Success,
    RestartRequired,
    Blocked,
    Cancelled,
    NoDevice,
    Failed,
    RolledBack,
    RollbackFailed,
}

public enum BladeGpuModeBlockReason
{
    None,
    NotDeclared,
    NoMux,
    ExternalDisplay,
    DisplayTopology,
    InternalDisplayUnsupported,
    DiscreteRouteLocked,
    DeviceFaulted,
    NoDevice,
    Unsupported,
}

public readonly record struct BladeGpuModeApplyResult(
    BladeGpuModeApplyOutcome Outcome,
    BladeGpuMode Target,
    BladeGpuModeBlockReason BlockReason = BladeGpuModeBlockReason.None,
    int ErrorCode = 0)
{
    public bool Changed => Outcome is BladeGpuModeApplyOutcome.Success or
        BladeGpuModeApplyOutcome.RestartRequired or
        BladeGpuModeApplyOutcome.RolledBack;
}
