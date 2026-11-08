using OpenSynapse.Core.Profiles;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerBleProfileApplier
{
    private static Task ApplyOnboardProfileAsync(
        RazerBleDeviceService service,
        MouseProfileSettings settings,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        if (settings.ActiveOnboardProfileId is not { } profileId)
        {
            return Task.CompletedTask;
        }

        return ApplySettingAsync(
            results,
            "ble.onboard-profile",
            async token => (int?)(await service.ReadProfilesAsync(token)).ActiveId,
            (value, token) => value is int id
                ? service.SetActiveProfileAsync((byte)id, token)
                : Task.CompletedTask,
            profileId,
            (left, right) => left == right,
            cancellationToken);
    }
}
