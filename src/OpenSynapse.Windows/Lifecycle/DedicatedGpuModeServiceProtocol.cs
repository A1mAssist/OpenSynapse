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
    public const int MaximumMessageLength = 4096;
}

public sealed record DedicatedGpuModeServiceRequest(
    int Version,
    string Operation,
    bool? Enabled);

public sealed record DedicatedGpuModeServiceResponse(
    int Version,
    bool Handled,
    DedicatedGpuSwitchOutcome Outcome,
    int ExitCode,
    DedicatedGpuModeStatus? Status,
    string? Error);
