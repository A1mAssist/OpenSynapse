using OpenSynapse.Core.Devices;

namespace OpenSynapse.Windows.Lifecycle;

/// <summary>
/// The fixed command surface shared by the app and the machine-wide OpenSynapse service.
/// Keep this wire format backward compatible. Only confirmed non-execution permits
/// UAC fallback; losing a dispatched PnP write response does not cancel the write.
/// </summary>
public static class OpenSynapseServiceProtocol
{
    public const string ServiceName = "OpenSynapseService";
    // The pre-rename machine-wide service. EnsureInstalled deletes it once the new
    // name is installed and verified, so an interrupted upgrade keeps the old path.
    public const string LegacyServiceName = "OpenSynapseGpuMode";
    public const string PipeName = "OpenSynapse.Service.v1";
    public const string ServiceArgument = "--open-synapse-service";
    public const string ServiceUserArgument = "--open-synapse-service-user";
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
    public const string StartChromaBrokerOperation = "start-chroma-broker";
    public const string StopChromaBrokerOperation = "stop-chroma-broker";
    public const string InstallChromaDllOperation = "install-chroma-dll";
    public const string ReadCpuTemperatureOperation = "read-cpu-temperature";
    public const string InstallRazerCommonOperation = "install-razer-common";
    public const string InstallRazerDeviceDriverOperation = "install-razer-device-driver";
    public const string BindFilterOperation = "bind-filter";
    public const string StartDriverServiceOperation = "start-driver-service";
    public const string ApplyUpdateOperation = "apply-update";

    /// <summary>
    /// The services the privileged host will start. They are fixed here so a local
    /// caller cannot turn the service into a generic service launcher.
    /// </summary>
    public const string RazerCommonServiceName = "RzCommon";
    public const string ThxServiceName = "VSSrv";
    public const string ThxBladeServiceName11 = "thx_rznb11_service";
    public const string ThxBladeServiceName12 = "thx_rznb12_service";

    public static bool IsManagedDriverService(string? name)
    {
        if (string.Equals(name, RazerCommonServiceName, StringComparison.Ordinal) ||
            string.Equals(name, ThxServiceName, StringComparison.Ordinal) ||
            string.Equals(name, ThxBladeServiceName11, StringComparison.Ordinal) ||
            string.Equals(name, ThxBladeServiceName12, StringComparison.Ordinal))
        {
            return true;
        }

        // The per-device RzDev filter service is the only generated name allowed
        // through. Keep the fixed prefix and four hex digits so this cannot become
        // a generic service launcher.
        if (name is null ||
            name.Length != 10 ||
            !name.StartsWith("RzDev_", StringComparison.Ordinal))
        {
            return false;
        }

        for (var index = 6; index < name.Length; index++)
        {
            if (!char.IsAsciiHexDigit(name[index]))
            {
                return false;
            }
        }

        return true;
    }

    public const int MaximumMessageLength = 4096;
}

public sealed record OpenSynapseServiceRequest(
    int Version,
    string Operation,
    bool? Enabled,
    int? CoreOffsetMhz = null,
    int? MemoryOffsetMhz = null,
    int? ClockLimitMhz = null,
    double? Pl1Watts = null,
    double? Pl2Watts = null,
    int? CurveSteps = null,
    int? ParentProcessId = null,
    string? ServiceName = null,
    int? ProductId = null,
    string? PackageFileName = null,
    int? WaitProcessId = null,
    string? PackagePath = null,
    string? PackageVersion = null);

/// <summary>
/// The operation a service request asks for. The decision lives here so the
/// request routing can be tested without a running Windows service.
/// </summary>
public enum OpenSynapseServiceAction
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

    /// <summary>Start the Chroma shared-memory broker for the calling app process.</summary>
    StartChromaBroker,

    /// <summary>Stop the Chroma shared-memory broker.</summary>
    StopChromaBroker,

    /// <summary>
    /// Download and install the official RzChromaSDK64.dll. The service owns the
    /// URL and the hash, so a local caller cannot point it at another file.
    /// </summary>
    InstallChromaDll,

    /// <summary>
    /// Reads the CPU package temperature through PawnIO. Only an elevated caller can
    /// open the driver, so the app asks the service once per sample.
    /// </summary>
    ReadCpuTemperature,

    /// <summary>Start a Razer driver service that is installed but stopped.</summary>
    StartDriverService,

    /// <summary>Apply a downloaded Velopack update through the machine service.</summary>
    ApplyUpdate,

    /// <summary>
    /// Download, verify, and install the WHQL Razer Common driver. The service
    /// owns the package URL, so the caller supplies no path.
    /// </summary>
    InstallRazerCommon,

    /// <summary>
    /// Download, verify, and install the official per-device RzDev filter package
    /// through the elevated service, so a standard user gets no UAC prompt.
    /// </summary>
    InstallRazerDeviceDriver,

    /// <summary>
    /// Attach the staged RzDev filter INF for one product to the present device
    /// nodes. The service resolves the INF from the Windows INF directory, so the
    /// caller passes a product id, never a path.
    /// </summary>
    BindFilter,

    /// <summary>The request does not match any known operation.</summary>
    Unsupported,
}

public sealed record OpenSynapseServiceResponse(
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
    bool? CurveOptimizerAvailable = null,
    double? CpuTemperatureCelsius = null);

/// <summary>
/// The service's answer to an update request. ViaService separates "the service
/// declined the request, so the UAC fallback is available" from "the service ran
/// and failed, so there is no safe automatic fallback".
/// </summary>
public readonly record struct OpenSynapseUpdateAttempt(
    bool ViaService,
    bool Started,
    string? Error);

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

public static class OpenSynapseServiceRequestParser
{
    /// <summary>
    /// Maps a request onto the action the service should take. The restart action is
    /// resolved before the set action, because a restart carries no enable flag and
    /// would otherwise be rejected as a malformed set.
    /// </summary>
    public static OpenSynapseServiceAction Parse(OpenSynapseServiceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Version != OpenSynapseServiceProtocol.Version)
        {
            return OpenSynapseServiceAction.Unsupported;
        }

        if (StringComparer.Ordinal.Equals(
                request.Operation,
                OpenSynapseServiceProtocol.StatusOperation))
        {
            return OpenSynapseServiceAction.Status;
        }

        if (StringComparer.Ordinal.Equals(
                request.Operation,
                OpenSynapseServiceProtocol.RestartOperation))
        {
            return OpenSynapseServiceAction.Restart;
        }

        if (StringComparer.Ordinal.Equals(
                request.Operation,
                OpenSynapseServiceProtocol.EnsureMacroAgentOperation))
        {
            return OpenSynapseServiceAction.EnsureMacroAgent;
        }

        if (StringComparer.Ordinal.Equals(
                request.Operation,
                OpenSynapseServiceProtocol.StartMacroAgentOperation))
        {
            return OpenSynapseServiceAction.StartMacroAgent;
        }

        if (StringComparer.Ordinal.Equals(
                request.Operation,
                OpenSynapseServiceProtocol.GpuClockOffsetStatusOperation))
        {
            return OpenSynapseServiceAction.GpuClockOffsetStatus;
        }

        if (StringComparer.Ordinal.Equals(
                request.Operation,
                OpenSynapseServiceProtocol.SetGpuClockOffsetOperation) &&
            request.CoreOffsetMhz is not null &&
            request.MemoryOffsetMhz is not null)
        {
            return OpenSynapseServiceAction.SetGpuClockOffset;
        }

        if (StringComparer.Ordinal.Equals(
                request.Operation,
                OpenSynapseServiceProtocol.GpuClockLimitStatusOperation))
        {
            return OpenSynapseServiceAction.GpuClockLimitStatus;
        }

        if (StringComparer.Ordinal.Equals(
                request.Operation,
                OpenSynapseServiceProtocol.SetGpuClockLimitOperation) &&
            request.ClockLimitMhz is not null)
        {
            return OpenSynapseServiceAction.SetGpuClockLimit;
        }

        if (StringComparer.Ordinal.Equals(
                request.Operation,
                OpenSynapseServiceProtocol.CpuPowerLimitStatusOperation))
        {
            return OpenSynapseServiceAction.CpuPowerLimitStatus;
        }

        if (StringComparer.Ordinal.Equals(
                request.Operation,
                OpenSynapseServiceProtocol.SetCpuPowerLimitOperation) &&
            request.Pl1Watts is not null &&
            request.Pl2Watts is not null)
        {
            return OpenSynapseServiceAction.SetCpuPowerLimit;
        }

        if (StringComparer.Ordinal.Equals(
                request.Operation,
                OpenSynapseServiceProtocol.RestoreCpuPowerLimitOperation) &&
            (request.Pl1Watts is null) == (request.Pl2Watts is null))
        {
            return OpenSynapseServiceAction.RestoreCpuPowerLimit;
        }

        if (StringComparer.Ordinal.Equals(
                request.Operation,
                OpenSynapseServiceProtocol.CurveOptimizerStatusOperation))
        {
            return OpenSynapseServiceAction.CurveOptimizerStatus;
        }

        if (StringComparer.Ordinal.Equals(
                request.Operation,
                OpenSynapseServiceProtocol.SetCurveOptimizerOperation) &&
            request.CurveSteps is not null)
        {
            return OpenSynapseServiceAction.SetCurveOptimizer;
        }

        if (StringComparer.Ordinal.Equals(
                request.Operation,
                OpenSynapseServiceProtocol.StartChromaBrokerOperation) &&
            request.ParentProcessId is > 0)
        {
            return OpenSynapseServiceAction.StartChromaBroker;
        }

        if (StringComparer.Ordinal.Equals(
                request.Operation,
                OpenSynapseServiceProtocol.StopChromaBrokerOperation))
        {
            return OpenSynapseServiceAction.StopChromaBroker;
        }

        if (StringComparer.Ordinal.Equals(
                request.Operation,
                OpenSynapseServiceProtocol.InstallChromaDllOperation))
        {
            return OpenSynapseServiceAction.InstallChromaDll;
    }

    if (StringComparer.Ordinal.Equals(
        request.Operation,
        OpenSynapseServiceProtocol.ReadCpuTemperatureOperation))
    {
        return OpenSynapseServiceAction.ReadCpuTemperature;
        }

        if (StringComparer.Ordinal.Equals(
                request.Operation,
                OpenSynapseServiceProtocol.BindFilterOperation) &&
            request.ProductId is > 0 and <= 0xFFFF)
        {
            return OpenSynapseServiceAction.BindFilter;
        }

        if (StringComparer.Ordinal.Equals(
                request.Operation,
                OpenSynapseServiceProtocol.InstallRazerCommonOperation))
        {
            return OpenSynapseServiceAction.InstallRazerCommon;
        }

        if (StringComparer.Ordinal.Equals(
                request.Operation,
                OpenSynapseServiceProtocol.InstallRazerDeviceDriverOperation) &&
            request.ProductId is > 0 and <= 0xFFFF)
        {
            return OpenSynapseServiceAction.InstallRazerDeviceDriver;
        }

        if (StringComparer.Ordinal.Equals(
                request.Operation,
                OpenSynapseServiceProtocol.StartDriverServiceOperation) &&
            OpenSynapseServiceProtocol.IsManagedDriverService(request.ServiceName))
        {
            return OpenSynapseServiceAction.StartDriverService;
        }

        if (StringComparer.Ordinal.Equals(
                request.Operation,
                OpenSynapseServiceProtocol.ApplyUpdateOperation) &&
            !string.IsNullOrWhiteSpace(request.PackageFileName) &&
            !string.IsNullOrWhiteSpace(request.PackagePath) &&
            !string.IsNullOrWhiteSpace(request.PackageVersion) &&
            request.WaitProcessId is > 0)
        {
            return OpenSynapseServiceAction.ApplyUpdate;
        }

        if (StringComparer.Ordinal.Equals(
                request.Operation,
                OpenSynapseServiceProtocol.SetOperation) &&
            request.Enabled is bool)
        {
            return OpenSynapseServiceAction.Set;
        }

        return OpenSynapseServiceAction.Unsupported;
    }
}
