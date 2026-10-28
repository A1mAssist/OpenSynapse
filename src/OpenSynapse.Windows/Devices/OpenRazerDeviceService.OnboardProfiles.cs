using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class OpenRazerDeviceService
{
    public async Task<byte> GetOnboardProfileCountAsync(
        OpenRazerDeviceConnection connection,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        var request = RazerOnboardProfileProtocol.CreateGetNumberOfProfilesRequest(MiddlewareTransactionId);
        var response = await ExecuteAsync(connection,
            new OpenRazerRequest("razer_onboard_get_profile_count", request), cancellationToken).ConfigureAwait(false);
        return RazerOnboardProfileProtocol.ParseNumberOfProfiles(response, request);
    }

    public async Task<byte> GetMaxOnboardProfilesAsync(
        OpenRazerDeviceConnection connection,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        var request = RazerOnboardProfileProtocol.CreateGetMaxProfilesSupportedRequest(MiddlewareTransactionId);
        var response = await ExecuteAsync(connection,
            new OpenRazerRequest("razer_onboard_get_max_profiles", request), cancellationToken).ConfigureAwait(false);
        return RazerOnboardProfileProtocol.ParseMaxProfilesSupported(response, request);
    }

    public async Task<byte> GetActiveOnboardProfileIdAsync(
        OpenRazerDeviceConnection connection,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        var request = RazerOnboardProfileProtocol.CreateGetActiveProfileIdRequest(MiddlewareTransactionId);
        var response = await ExecuteAsync(connection,
            new OpenRazerRequest("razer_onboard_get_active_profile", request), cancellationToken).ConfigureAwait(false);
        return RazerOnboardProfileProtocol.ParseProfileId(response, request);
    }

    public async Task<RazerProfileColor> GetOnboardProfileColorAsync(
        OpenRazerDeviceConnection connection,
        byte profileId,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        var request = RazerOnboardProfileProtocol.CreateGetProfileColorRequest(MiddlewareTransactionId, profileId);
        var response = await ExecuteAsync(connection,
            new OpenRazerRequest("razer_onboard_get_profile_color", request), cancellationToken).ConfigureAwait(false);
        return RazerOnboardProfileProtocol.ParseProfileColor(response, request);
    }

    public Task SetActiveOnboardProfileIdAsync(
        OpenRazerDeviceConnection connection,
        byte profileId,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        return ExecuteWithoutResultAsync(connection,
            new OpenRazerRequest("razer_onboard_set_active_profile",
                RazerOnboardProfileProtocol.CreateSetActiveProfileIdRequest(MiddlewareTransactionId, profileId)),
            cancellationToken);
    }

    public Task CreateOnboardProfileAsync(
        OpenRazerDeviceConnection connection,
        byte profileId,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        return ExecuteWithoutResultAsync(connection,
            new OpenRazerRequest("razer_onboard_create_profile",
                RazerOnboardProfileProtocol.CreateNewProfileRequest(MiddlewareTransactionId, profileId)),
            cancellationToken);
    }

    public Task DeleteOnboardProfileAsync(
        OpenRazerDeviceConnection connection,
        byte profileId,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        return ExecuteWithoutResultAsync(connection,
            new OpenRazerRequest("razer_onboard_delete_profile",
                RazerOnboardProfileProtocol.CreateDeleteProfileRequest(MiddlewareTransactionId, profileId)),
            cancellationToken);
    }

    public Task ResetOnboardProfileAsync(
        OpenRazerDeviceConnection connection,
        byte profileId,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        return ExecuteWithoutResultAsync(connection,
            new OpenRazerRequest("razer_onboard_reset_profile",
                RazerOnboardProfileProtocol.CreateSetProfileResetRequest(MiddlewareTransactionId, profileId)),
            cancellationToken);
    }

    public Task SetOnboardProfileColorAsync(
        OpenRazerDeviceConnection connection,
        byte profileId,
        byte red,
        byte green,
        byte blue,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        return ExecuteWithoutResultAsync(connection,
            new OpenRazerRequest("razer_onboard_set_profile_color",
                RazerOnboardProfileProtocol.CreateSetProfileColorRequest(
                    MiddlewareTransactionId, profileId, red, green, blue)),
            cancellationToken);
    }

    internal async Task ProbeOnboardProfileAsync(
        OpenRazerDeviceDefinition definition,
        string devicePath,
        ISet<OpenRazerBackendCapability> result,
        CancellationToken cancellationToken)
    {
        if (definition.Category is not (DeviceCategory.Mouse or DeviceCategory.Keyboard))
        {
            return;
        }

        var request = RazerOnboardProfileProtocol.CreateGetMaxProfilesSupportedRequest(MiddlewareTransactionId);
        var supported = await ProbeMiddlewareAsync(
            definition.ProductId,
            devicePath,
            definition.DeviceWait,
            "onboard.maxProfiles",
            request,
            response => RazerOnboardProfileProtocol.ParseMaxProfilesSupported(response, request) > 0,
            cancellationToken).ConfigureAwait(false);
        if (!supported)
        {
            return;
        }

        result.Add(OpenRazerBackendCapability.OnboardProfileRead);
        result.Add(OpenRazerBackendCapability.OnboardProfileWrite);
    }
}
