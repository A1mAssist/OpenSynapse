using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Storage;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class AppDataPathsTests
{
    [Fact]
    public void ProfileStoreUsesTheDedicatedDataDirectory()
    {
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        var expectedRoot = Path.Combine(localAppData, "A1mAssist", "OpenSynapse");

        Assert.Equal(expectedRoot, AppDataPaths.Root);
        Assert.Equal(
            Path.Combine(expectedRoot, "profiles.json"),
            new ProfileStore().FilePath);
        Assert.NotEqual(
            Path.Combine(localAppData, "OpenSynapse", "profiles.json"),
            new ProfileStore().FilePath);
    }
}