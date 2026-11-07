using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidDeviceService
{
    /// <summary>
    /// The DeathAdder V4 Pro family (00BE/00BF) keeps its onboard profile commands in
    /// class 0x03. Every other device uses class 0x05, so a class 0x05 write to a
    /// DeathAdder V4 Pro comes back as "the device rejected the query (0x03)".
    /// </summary>
    private static readonly IReadOnlySet<ushort> AltProfileClassProducts =
        new HashSet<ushort> { 0x00BE, 0x00BF };

    private static bool UsesAltProfileClass(RazerHidDeviceConnection connection) =>
        AltProfileClassProducts.Contains(connection.Definition.ProductId);

    /// <summary>
    /// Only the class 0x05 dialect declares a profile reset command.
    /// </summary>
    public static bool SupportsOnboardProfileReset(ushort productId) =>
        !AltProfileClassProducts.Contains(productId);

    public async Task<byte> GetOnboardProfileCountAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        var request = UsesAltProfileClass(connection)
            ? RazerOnboardProfileProtocol.CreateAltGetNumberOfProfilesRequest(MiddlewareTransactionId)
            : RazerOnboardProfileProtocol.CreateGetNumberOfProfilesRequest(MiddlewareTransactionId);
        var response = await ExecuteAsync(connection,
            new RazerHidRequest("razer_onboard_get_profile_count", request), cancellationToken).ConfigureAwait(false);
        return RazerOnboardProfileProtocol.ParseNumberOfProfiles(response, request);
    }

    public async Task<byte> GetMaxOnboardProfilesAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        var request = UsesAltProfileClass(connection)
            ? RazerOnboardProfileProtocol.CreateAltGetMaxProfilesSupportedRequest(MiddlewareTransactionId)
            : RazerOnboardProfileProtocol.CreateGetMaxProfilesSupportedRequest(MiddlewareTransactionId);
        var response = await ExecuteAsync(connection,
            new RazerHidRequest("razer_onboard_get_max_profiles", request), cancellationToken).ConfigureAwait(false);
        return RazerOnboardProfileProtocol.ParseMaxProfilesSupported(response, request);
    }

    public async Task<byte> GetActiveOnboardProfileIdAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        var request = UsesAltProfileClass(connection)
            ? RazerOnboardProfileProtocol.CreateAltGetActiveProfileIdRequest(MiddlewareTransactionId)
            : RazerOnboardProfileProtocol.CreateGetActiveProfileIdRequest(MiddlewareTransactionId);
        var response = await ExecuteAsync(connection,
            new RazerHidRequest("razer_onboard_get_active_profile", request), cancellationToken).ConfigureAwait(false);
        return RazerOnboardProfileProtocol.ParseProfileId(response, request);
    }

    public async Task<RazerProfileColor> GetOnboardProfileColorAsync(
        RazerHidDeviceConnection connection,
        byte profileId,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        var request = RazerOnboardProfileProtocol.CreateGetProfileColorRequest(MiddlewareTransactionId, profileId);
        var response = await ExecuteAsync(connection,
            new RazerHidRequest("razer_onboard_get_profile_color", request), cancellationToken).ConfigureAwait(false);
        return RazerOnboardProfileProtocol.ParseProfileColor(response, request);
    }

    public Task SetActiveOnboardProfileIdAsync(
        RazerHidDeviceConnection connection,
        byte profileId,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        return ExecuteWithoutResultAsync(connection,
            new RazerHidRequest("razer_onboard_set_active_profile",
                UsesAltProfileClass(connection)
                    ? RazerOnboardProfileProtocol.CreateAltSetActiveProfileIdRequest(MiddlewareTransactionId, profileId)
                    : RazerOnboardProfileProtocol.CreateSetActiveProfileIdRequest(MiddlewareTransactionId, profileId)),
            cancellationToken);
    }

    public Task CreateOnboardProfileAsync(
        RazerHidDeviceConnection connection,
        byte profileId,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        return ExecuteWithoutResultAsync(connection,
            new RazerHidRequest("razer_onboard_create_profile",
                UsesAltProfileClass(connection)
                    ? RazerOnboardProfileProtocol.CreateAltCreateNewProfileRequest(MiddlewareTransactionId, profileId)
                    : RazerOnboardProfileProtocol.CreateNewProfileRequest(MiddlewareTransactionId, profileId)),
            cancellationToken);
    }

    public Task DeleteOnboardProfileAsync(
        RazerHidDeviceConnection connection,
        byte profileId,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        return ExecuteWithoutResultAsync(connection,
            new RazerHidRequest("razer_onboard_delete_profile",
                UsesAltProfileClass(connection)
                    ? RazerOnboardProfileProtocol.CreateAltDeleteProfileRequest(MiddlewareTransactionId, profileId)
                    : RazerOnboardProfileProtocol.CreateDeleteProfileRequest(MiddlewareTransactionId, profileId)),
            cancellationToken);
    }

    public Task ResetOnboardProfileAsync(
        RazerHidDeviceConnection connection,
        byte profileId,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        return ExecuteWithoutResultAsync(connection,
            new RazerHidRequest("razer_onboard_reset_profile",
                // The class 0x03 dialect has no reset command, so a DeathAdder V4 Pro
                // reports the unsupported command instead of writing the wrong table.
                UsesAltProfileClass(connection)
                    ? throw new NotSupportedException("This device does not support resetting a profile.")
                    : RazerOnboardProfileProtocol.CreateSetProfileResetRequest(MiddlewareTransactionId, profileId)),
            cancellationToken);
    }

    public Task SetOnboardProfileColorAsync(
        RazerHidDeviceConnection connection,
        byte profileId,
        byte red,
        byte green,
        byte blue,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        return ExecuteWithoutResultAsync(connection,
            new RazerHidRequest("razer_onboard_set_profile_color",
                RazerOnboardProfileProtocol.CreateSetProfileColorRequest(
                    MiddlewareTransactionId, profileId, red, green, blue)),
            cancellationToken);
    }

    internal async Task ProbeOnboardProfileAsync(
        RazerHidDeviceDefinition definition,
        string devicePath,
        ISet<RazerHidBackendCapability> result,
        CancellationToken cancellationToken)
    {
        if (definition.Category is not (DeviceCategory.Mouse or DeviceCategory.Keyboard))
        {
            return;
        }

        var request = AltProfileClassProducts.Contains(definition.ProductId)
            ? RazerOnboardProfileProtocol.CreateAltGetMaxProfilesSupportedRequest(MiddlewareTransactionId)
            : RazerOnboardProfileProtocol.CreateGetMaxProfilesSupportedRequest(MiddlewareTransactionId);
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

        result.Add(RazerHidBackendCapability.OnboardProfileRead);
        result.Add(RazerHidBackendCapability.OnboardProfileWrite);
    }
}
