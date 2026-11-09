using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.Windows.Lifecycle;

public static partial class OpenSynapseServiceClient
{
    /// <summary>
    /// Asks the machine-wide service to attach the staged RzDev filter INF for one
    /// product. Null means the service is unavailable, so the caller keeps its UAC
    /// fallback. Applied is false when nothing was staged, which is not a failure.
    /// </summary>
    internal static async Task<RazerFilterBindResult?> TryBindRazerFilterAsync(
        ushort productId,
        CancellationToken cancellationToken = default)
    {
        var request = new OpenSynapseServiceRequest(
            OpenSynapseServiceProtocol.Version,
            OpenSynapseServiceProtocol.BindFilterOperation,
            Enabled: null,
            ProductId: productId);

        var response = await SendForResponseAsync(
                request, cancellationToken, TimeSpan.FromMinutes(2))
            .ConfigureAwait(false);
        if (response?.Handled != true)
        {
            return null;
        }

        return new RazerFilterBindResult(
            response.ExitCode switch
            {
                0 => RazerFilterBindStatus.Applied,
                1 => RazerFilterBindStatus.NoStagedInf,
                2 when response.Error is null => RazerFilterBindStatus.NoStagedInf,
                _ => RazerFilterBindStatus.Failed,
            },
            response.Error);
    }

    /// <summary>
    /// Asks the machine-wide service to download and install the official
    /// per-device RzDev package without a UAC prompt. Null means the service is
    /// unavailable or predates this operation, so the caller keeps its UAC fallback.
    /// </summary>
    internal static async Task<RazerDriverInstallResult?> TryInstallRazerDeviceDriverAsync(
        ushort productId,
        CancellationToken cancellationToken = default)
    {
        var request = new OpenSynapseServiceRequest(
            OpenSynapseServiceProtocol.Version,
            OpenSynapseServiceProtocol.InstallRazerDeviceDriverOperation,
            Enabled: null,
            ProductId: productId);

        var response = await SendForResponseAsync(
                request, cancellationToken, TimeSpan.FromMinutes(6))
            .ConfigureAwait(false);
        if (response?.Handled != true)
        {
            return null;
        }

        var success = response.Outcome == DedicatedGpuSwitchOutcome.Success;
        return new RazerDriverInstallResult(
            success,
            success ? RazerDriverInstallFailure.None : RazerDriverInstallFailure.ExitCode,
            response.ExitCode,
            RequiresRestart: success && response.ExitCode is 3010 or 1641,
            Error: response.Error);
    }

    /// <summary>
    /// Asks the machine-wide service to install the WHQL Razer Common driver.
    /// Null means the service is unavailable, so the caller keeps its UAC fallback.
    /// </summary>
    public static async Task<bool?> TryInstallRazerCommonAsync(
        CancellationToken cancellationToken = default)
    {
        var request = new OpenSynapseServiceRequest(
            OpenSynapseServiceProtocol.Version,
            OpenSynapseServiceProtocol.InstallRazerCommonOperation,
            Enabled: null);

        var response = await SendForResponseAsync(
                request, cancellationToken, TimeSpan.FromMinutes(3))
            .ConfigureAwait(false);
        return response?.Handled == true
            ? response.Outcome == DedicatedGpuSwitchOutcome.Success
            : null;
    }
}
