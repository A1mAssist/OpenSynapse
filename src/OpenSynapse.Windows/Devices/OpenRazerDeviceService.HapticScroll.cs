using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

/// <summary>The six Synapse scroll-stage ids carried by protocol 25 class 0x15.</summary>
public static class OpenRazerHapticScrollStageIds
{
    public const byte Standard = (byte)RazerHapticoreScrollStageType.Standard;
    public const byte Distinct = (byte)RazerHapticoreScrollStageType.Distinct;
    public const byte UltraFine = (byte)RazerHapticoreScrollStageType.UltraFine;
    public const byte Adaptive = (byte)RazerHapticoreScrollStageType.Adaptive;
    // Synapse renamed the protocol's FreeScroll stage to Smooth Scroll.
    public const byte SmoothScroll = (byte)RazerHapticoreScrollStageType.FreeScroll;
    public const byte Custom = (byte)RazerHapticoreScrollStageType.Custom;
}

public sealed partial class OpenRazerDeviceService
{
    public async Task<RazerHapticoreScrollStageInformationState> GetHapticScrollStagesAsync(
        OpenRazerDeviceConnection connection,
        byte profileId,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        var request = RazerHapticoreScrollProtocol.CreateGetStageInformationRequest(
            MiddlewareTransactionId, profileId);
        var response = await ExecuteAsync(connection,
            new OpenRazerRequest("razer_hapticore_get_stage_information", request),
            cancellationToken).ConfigureAwait(false);
        return RazerHapticoreScrollProtocol.ParseStageInformation(response, request);
    }

    public async Task SetHapticScrollStagesAsync(
        OpenRazerDeviceConnection connection,
        byte profileId,
        byte activeStageIndex,
        IReadOnlyList<byte> stageIds,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        ArgumentNullException.ThrowIfNull(stageIds);
        if (stageIds.Count > RazerHapticoreScrollProtocol.MaxStage)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stageIds),
                stageIds.Count,
                $"At most {RazerHapticoreScrollProtocol.MaxStage} Hapticore scroll stages fit.");
        }

        var stageTypes = new RazerHapticoreScrollStageType[stageIds.Count];
        for (var index = 0; index < stageIds.Count; index++)
        {
            var stageType = (RazerHapticoreScrollStageType)stageIds[index];
            if (!Enum.IsDefined(stageType))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(stageIds),
                    stageIds[index],
                    "Unknown Hapticore scroll stage id.");
            }

            stageTypes[index] = stageType;
        }

        // Synapse applies the active index before replacing the profile's stage list.
        var activeRequest = RazerHapticoreScrollProtocol.CreateSetActiveStageRequest(
            MiddlewareTransactionId, profileId, activeStageIndex);
        var stagesRequest = RazerHapticoreScrollProtocol.CreateSetStageInformationRequest(
            MiddlewareTransactionId, profileId, activeStageIndex, (byte)stageTypes.Length, stageTypes);
        await ExecuteWithoutResultAsync(connection,
            new OpenRazerRequest("razer_hapticore_set_active_stage", activeRequest),
            cancellationToken).ConfigureAwait(false);
        await ExecuteWithoutResultAsync(connection,
            new OpenRazerRequest("razer_hapticore_set_stage_information", stagesRequest),
            cancellationToken).ConfigureAwait(false);
    }

    internal async Task ProbeHapticScrollAsync(
        OpenRazerDeviceDefinition definition,
        string devicePath,
        ISet<OpenRazerBackendCapability> result,
        CancellationToken cancellationToken)
    {
        if (definition.Category != DeviceCategory.Mouse)
        {
            return;
        }

        var request = RazerHapticoreScrollProtocol.CreateGetActiveStageRequest(
            MiddlewareTransactionId, 0x01);
        if (await ProbeMiddlewareAsync(definition.ProductId, devicePath, definition.DeviceWait,
                "mouse.hapticScroll", request, response =>
                {
                    _ = RazerHapticoreScrollProtocol.ParseActiveStage(response, request);
                    return true;
                }, cancellationToken).ConfigureAwait(false))
        {
            result.Add(OpenRazerBackendCapability.HapticScrollRead);
            result.Add(OpenRazerBackendCapability.HapticScrollWrite);
        }
    }
}
