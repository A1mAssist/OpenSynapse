using OpenSynapse.Core.Devices;

namespace OpenSynapse.Windows.Lifecycle;

/// <summary>
/// The fixed command surface shared by the app and the machine-wide GPU service.
/// Keep this wire format backward compatible; a mismatch must fall back to the
/// existing UAC helper rather than sending an ambiguous command.
/// </summary>
public static class DedicatedGpuModeServiceProtocol
{
    public const string ServiceName = "OpenSynapseGpuMode";
    public const string PipeName = "OpenSynapse.GpuMode.v1";
    public const string ServiceArgument = "--gpu-service";
    public const string ServiceUserArgument = "--gpu-service-user";
    public const int Version = 1;
    public const string StatusOperation = "status";
    public const string SetOperation = "set";
    public const string RestartOperation = "restart";
    public const string EnsureMacroAgentOperation = "ensure-macro-agent";
    public const string StartMacroAgentOperation = "start-macro-agent";
    public const string GpuClockOffsetStatusOperation = "gpu-clock-offset-status";
    public const string SetGpuClockOffsetOperation = "set-gpu-clock-offset";
    public const string GpuClockLimitStatusOperation = "gpu-clock-limit-status";
    public const string SetGpuClockLimitOperation = "set-gpu-clock-limit";
    public const string CpuPowerLimitStatusOperation = "power-limit-status";
    public const string SetCpuPowerLimitOperation = "set-power-limit";
    public const string RestoreCpuPowerLimitOperation = "restore-power-limit";
    public const string CurveOptimizerStatusOperation = "curve-optimizer-status";
    public const string SetCurveOptimizerOperation = "set-curve-optimizer";
    public const int MaximumMessageLength = 4096;
}

public sealed record DedicatedGpuModeServiceRequest(
    int Version,
    string Operation,
    bool? Enabled,
    int? CoreOffsetMhz = null,
    int? MemoryOffsetMhz = null,
    int? ClockLimitMhz = null,
    double? Pl1Watts = null,
    double? Pl2Watts = null,
    int? CurveSteps = null);

/// <summary>
/// The operation a service request asks for. The decision lives here so the
/// request routing can be tested without a running Windows service.
/// </summary>
public enum DedicatedGpuModeServiceAction
{
    /// <summary>Return the current adapter state.</summary>
    Status,

    /// <summary>Turn the discrete adapter on or off.</summary>
    Set,

    /// <summary>Restart the discrete adapter so its driver loads again.</summary>
    Restart,

    /// <summary>
    /// Create the macro agent's scheduled task for the authorized user and
    /// start it. The task runs at the highest available integrity level so
    /// macro input reaches windows that a normal app cannot write to.
    /// </summary>
    EnsureMacroAgent,

    /// <summary>
    /// Start the macro agent if its task already exists.
    /// </summary>
    StartMacroAgent,

    /// <summary>Report whether the GPU driver accepts a clock-offset write.</summary>
    GpuClockOffsetStatus,

    /// <summary>Apply the GPU core and memory clock offsets.</summary>
    SetGpuClockOffset,

    /// <summary>Report whether the driver accepts a clock-limit read.</summary>
    GpuClockLimitStatus,

    /// <summary>Apply the GPU core clock ceiling.</summary>
    SetGpuClockLimit,

    /// <summary>Report the current CPU package power wall.</summary>
    CpuPowerLimitStatus,

    /// <summary>Apply user-requested CPU package power walls.</summary>
    SetCpuPowerLimit,

    /// <summary>Restore explicit walls, or the open-time firmware walls.</summary>
    RestoreCpuPowerLimit,

    /// <summary>Report whether the Ryzen SMU accepts a Curve Optimizer write.</summary>
    CurveOptimizerStatus,

    /// <summary>Apply an all-core Curve Optimizer offset.</summary>
    SetCurveOptimizer,

    /// <summary>The request does not match any known operation.</summary>
    Unsupported,
}

public sealed record DedicatedGpuModeServiceResponse(
    int Version,
    bool Handled,
    DedicatedGpuSwitchOutcome Outcome,
    int ExitCode,
    DedicatedGpuModeStatus? Status,
    string? Error,
    bool? ClockOffsetAvailable = null,
    int? CoreOffsetMhz = null,
    int? MemoryOffsetMhz = null,
    bool? ClockLimitAvailable = null,
    int? ClockLimitMhz = null,
    CpuPowerLimitReport? PowerLimit = null,
    bool? CurveOptimizerAvailable = null);

/// <summary>
/// What the driver currently holds. Null from the client means the installed service
/// predates the clock-offset operation, not that the driver refused it.
/// </summary>
public sealed record DedicatedGpuClockOffsetState(
    bool Available,
    int CoreMhz,
    int MemoryMhz);

/// <summary>
/// The clock ceiling the driver reports. Zero means no lock and maps to the UI's default.
/// Null from the client means the installed service predates the clock-limit operation.
/// </summary>
public sealed record DedicatedGpuClockLimitState(
    bool Available,
    int Megahertz);

/// <summary>Null from the client means the installed service predates the operation.</summary>
public sealed record DedicatedGpuCurveOptimizerState(bool Available, string? Error);

public sealed record DedicatedGpuCurveOptimizerResult(bool Applied, string? Error);

public static class DedicatedGpuModeServiceRequestParser
{
    /// <summary>
    /// Maps a request onto the action the service should take. The restart action is
    /// resolved before the set action, because a restart carries no enable flag and
    /// would otherwise be rejected as a malformed set.
    /// </summary>
    public static DedicatedGpuModeServiceAction Parse(DedicatedGpuModeServiceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Version != DedicatedGpuModeServiceProtocol.Version)
        {
            return DedicatedGpuModeServiceAction.Unsupported;
        }

        if (StringComparer.Ordinal.Equals(
                request.Operation,
                DedicatedGpuModeServiceProtocol.StatusOperation))
        {
            return DedicatedGpuModeServiceAction.Status;
        }

        if (StringComparer.Ordinal.Equals(
                request.Operation,
                DedicatedGpuModeServiceProtocol.RestartOperation))
        {
            return DedicatedGpuModeServiceAction.Restart;
        }

        if (StringComparer.Ordinal.Equals(
                request.Operation,
                DedicatedGpuModeServiceProtocol.EnsureMacroAgentOperation))
        {
            return DedicatedGpuModeServiceAction.EnsureMacroAgent;
        }

        if (StringComparer.Ordinal.Equals(
                request.Operation,
                DedicatedGpuModeServiceProtocol.StartMacroAgentOperation))
        {
            return DedicatedGpuModeServiceAction.StartMacroAgent;
        }

        if (StringComparer.Ordinal.Equals(
                request.Operation,
                DedicatedGpuModeServiceProtocol.GpuClockOffsetStatusOperation))
        {
            return DedicatedGpuModeServiceAction.GpuClockOffsetStatus;
        }

        if (StringComparer.Ordinal.Equals(
                request.Operation,
                DedicatedGpuModeServiceProtocol.SetGpuClockOffsetOperation) &&
            request.CoreOffsetMhz is not null &&
            request.MemoryOffsetMhz is not null)
        {
            return DedicatedGpuModeServiceAction.SetGpuClockOffset;
        }

        if (StringComparer.Ordinal.Equals(
                request.Operation,
                DedicatedGpuModeServiceProtocol.GpuClockLimitStatusOperation))
        {
            return DedicatedGpuModeServiceAction.GpuClockLimitStatus;
        }

        if (StringComparer.Ordinal.Equals(
                request.Operation,
                DedicatedGpuModeServiceProtocol.SetGpuClockLimitOperation) &&
            request.ClockLimitMhz is not null)
        {
            return DedicatedGpuModeServiceAction.SetGpuClockLimit;
        }

        if (StringComparer.Ordinal.Equals(
                request.Operation,
                DedicatedGpuModeServiceProtocol.CpuPowerLimitStatusOperation))
        {
            return DedicatedGpuModeServiceAction.CpuPowerLimitStatus;
        }

        if (StringComparer.Ordinal.Equals(
                request.Operation,
                DedicatedGpuModeServiceProtocol.SetCpuPowerLimitOperation) &&
            request.Pl1Watts is not null &&
            request.Pl2Watts is not null)
        {
            return DedicatedGpuModeServiceAction.SetCpuPowerLimit;
        }

        if (StringComparer.Ordinal.Equals(
                request.Operation,
                DedicatedGpuModeServiceProtocol.RestoreCpuPowerLimitOperation) &&
            (request.Pl1Watts is null) == (request.Pl2Watts is null))
        {
            return DedicatedGpuModeServiceAction.RestoreCpuPowerLimit;
        }

        if (StringComparer.Ordinal.Equals(
                request.Operation,
                DedicatedGpuModeServiceProtocol.CurveOptimizerStatusOperation))
        {
            return DedicatedGpuModeServiceAction.CurveOptimizerStatus;
        }

        if (StringComparer.Ordinal.Equals(
                request.Operation,
                DedicatedGpuModeServiceProtocol.SetCurveOptimizerOperation) &&
            request.CurveSteps is not null)
        {
            return DedicatedGpuModeServiceAction.SetCurveOptimizer;
        }

        if (StringComparer.Ordinal.Equals(
                request.Operation,
                DedicatedGpuModeServiceProtocol.SetOperation) &&
            request.Enabled is bool)
        {
            return DedicatedGpuModeServiceAction.Set;
        }

        return DedicatedGpuModeServiceAction.Unsupported;
    }
}
