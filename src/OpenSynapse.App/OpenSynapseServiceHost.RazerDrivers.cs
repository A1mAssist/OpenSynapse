using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Lifecycle;

namespace OpenSynapse.App;

internal static partial class OpenSynapseServiceHost
{
    private static async Task<OpenSynapseServiceResponse> ExecuteRazerDriverAsync(
        OpenSynapseServiceAction action,
        OpenSynapseServiceRequest request)
    {
        switch (action)
        {
            case OpenSynapseServiceAction.InstallRazerCommon:
                var commonResult = await RazerCommonDriverInstaller
                    .InstallAsServiceAsync(CancellationToken.None)
                    .ConfigureAwait(false);
                return new OpenSynapseServiceResponse(
                    OpenSynapseServiceProtocol.Version,
                    Handled: true,
                    commonResult.Success
                        ? DedicatedGpuSwitchOutcome.Success
                        : DedicatedGpuSwitchOutcome.Failed,
                    ExitCode: commonResult.Success ? 0 : 1,
                    Status: null,
                    Error: commonResult.Success
                        ? null
                        : commonResult.Error ?? commonResult.Failure.ToString());

            case OpenSynapseServiceAction.InstallRazerDeviceDriver:
                var deviceResult = await RazerDriverInstaller
                    .InstallAsServiceAsync((ushort)request.ProductId!.Value, CancellationToken.None)
                    .ConfigureAwait(false);
                return new OpenSynapseServiceResponse(
                    OpenSynapseServiceProtocol.Version,
                    Handled: true,
                    deviceResult.Success
                        ? DedicatedGpuSwitchOutcome.Success
                        : DedicatedGpuSwitchOutcome.Failed,
                    ExitCode: deviceResult.Success
                        ? (deviceResult.RequiresRestart ? 3010 : 0)
                        : 1,
                    Status: null,
                    Error: deviceResult.Success
                        ? null
                        : deviceResult.Error ?? deviceResult.Failure.ToString());

            case OpenSynapseServiceAction.BindFilter:
                var bindResult = await RazerFilterInfBinder
                    .ApplyDirectAsync((ushort)request.ProductId!.Value, CancellationToken.None)
                    .ConfigureAwait(false);
                return new OpenSynapseServiceResponse(
                    OpenSynapseServiceProtocol.Version,
                    Handled: true,
                    bindResult.Applied
                        ? DedicatedGpuSwitchOutcome.Success
                        : DedicatedGpuSwitchOutcome.Failed,
                    ExitCode: bindResult.Status switch
                    {
                        RazerFilterBindStatus.Applied => 0,
                        RazerFilterBindStatus.NoStagedInf => 1,
                        _ => 2,
                    },
                    Status: null,
                    Error: bindResult.Error);

            case OpenSynapseServiceAction.StartDriverService:
                var driverStarted = WindowsServiceControl.TryStart(
                    request.ServiceName!,
                    out var driverError);
                return new OpenSynapseServiceResponse(
                    OpenSynapseServiceProtocol.Version,
                    Handled: true,
                    driverStarted
                        ? DedicatedGpuSwitchOutcome.Success
                        : DedicatedGpuSwitchOutcome.Failed,
                    ExitCode: driverStarted ? 0 : 1,
                    Status: null,
                    Error: driverError);

            default:
                return new OpenSynapseServiceResponse(
                    OpenSynapseServiceProtocol.Version,
                    Handled: false,
                    DedicatedGpuSwitchOutcome.Failed,
                    ExitCode: 1,
                    Status: null,
                    Error: "Unsupported Razer driver operation.");
        }
    }
}
