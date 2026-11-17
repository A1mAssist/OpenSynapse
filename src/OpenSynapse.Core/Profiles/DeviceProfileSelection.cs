namespace OpenSynapse.Core.Profiles;

/// <summary>A read-only profile selection that does not change the user's active profile.</summary>
public static class DeviceProfileSelection
{
    public static ProfileDocument For(ProfileDocument document, string profileName) =>
        document.CloneWithActiveProfile(profileName);
}
