using OpenSynapse.Core.Profiles;

namespace OpenSynapse.App.ViewModels;

public sealed partial class ProfilesWorkspace
{
    internal async Task LoadAsync(CancellationToken cancellationToken)
    {
        ProfileDocument loaded;
        try
        {
            loaded = await _profileStore.LoadAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _setProfile(ProfileDocument.CreateDefault());
            RefreshProfileState();
            RefreshStartupState();
            ProfileStatusText = AppStrings.FormatText("DefaultProfileLoaded", ActiveProfileName);
            _reportApplicationError(AppStrings.FormatText("ProfileLoadError", exception.Message));
            return;
        }

        _setProfile(loaded);
        if (_profileStore.LastQuarantinedPath is { } quarantined)
        {
            _reportApplicationError(AppStrings.FormatText("ProfileCorruptQuarantined", quarantined));
        }
        var migrated = EnsureActiveProfileExtensions();
        RefreshProfileState();
        RefreshStartupState();
        ProfileStatusText = AppStrings.FormatText("ProfileLoaded", ActiveProfileName);
        if (!migrated)
        {
            return;
        }

        // A failed migration write must not discard the document we already loaded.
        try
        {
            await _profileStore.SaveAsync(_getProfile(), cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _reportApplicationError(AppStrings.FormatText("ProfileSaveError", exception.Message));
        }
    }
}
