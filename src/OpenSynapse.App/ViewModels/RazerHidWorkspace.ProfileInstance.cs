using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidWorkspace
{
    private async Task MigrateProfileInstanceKeysAsync(
        IReadOnlyList<RazerHidDeviceConnection> connections,
        CancellationToken cancellationToken)
    {
        var result = ProfileInstanceKeyMigration.Migrate(
            _getProfile(),
            connections.Select(connection => connection.ToDescriptor()).ToArray());
        if (result.MigratedEntries > 0 &&
            !await _saveProfileAsync(cancellationToken).ConfigureAwait(true))
        {
            _setDeviceOperationError(AppStrings.FormatText(
                "ProfileSaveError",
                "device instance keys"));
        }
        if (result.AmbiguousLegacyKeys.Count > 0)
        {
            _setDeviceOperationError(AppStrings.FormatText(
                "RazerHidProfileInstanceAmbiguous",
                string.Join(", ", result.AmbiguousLegacyKeys)));
        }
    }
}
