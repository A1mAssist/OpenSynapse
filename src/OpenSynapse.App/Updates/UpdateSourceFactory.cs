using Velopack.Sources;

namespace OpenSynapse.App.Updates;

internal enum UpdateSourcePreference
{
    Automatic = 0,
    Gitee = 1,
    GitHub = 2,
}

internal static class UpdateSourceFactory
{
    private const string GitHubRepositoryUrl = "https://github.com/A1mAssist/OpenSynapse";
    private const string GiteeOwner = "A1mAssist";
    private const string GiteeRepository = "OpenSynapse";

    public static IUpdateSource Create(UpdateSourcePreference preference) =>
        preference switch
        {
            UpdateSourcePreference.Gitee => CreateGitee(),
            UpdateSourcePreference.GitHub => CreateGitHub(),
            _ => new AutomaticUpdateSource(CreateGitee(), CreateGitHub()),
        };

    private static GiteeSource CreateGitee() =>
        new(GiteeOwner, GiteeRepository, includePrerelease: false);

    private static GithubSource CreateGitHub() =>
        new(GitHubRepositoryUrl, accessToken: null, prerelease: false);
}
