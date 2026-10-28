using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Devices;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class ProfileMigrationTests
{
    [Fact]
    public async Task Version1ProfileIsUpgradedOnImport()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"opensynapse-v1-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(
                path,
                """
                {
                  "version": 1,
                  "activeProfileName": "Default",
                  "profiles": {}
                }
                """);

            var document = await ProfileStore.ImportAsync(path);

            Assert.Equal(ProfileStore.CurrentVersion, document.Version);
            Assert.NotEmpty(document.Profiles);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Version1ProfileKeepsMacrosWhenUpgraded()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"opensynapse-v1-macro-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(
                path,
                """
                {
                  "version": 1,
                  "activeProfileName": "Default",
                  "profiles": {
                    "Default": {
                      "macros": [
                        {
                          "id": "macro-1",
                          "name": "Test macro",
                          "steps": [
                            {
                              "kind": "Input",
                              "input": { "kind": "Keyboard", "virtualKey": 65 },
                              "isDown": true,
                              "autoRelease": true
                            }
                          ]
                        }
                      ]
                    }
                  }
                }
                """);

            var document = await ProfileStore.ImportAsync(path);
            var macro = Assert.Single(document.Profiles[document.ActiveProfileName].Macros);
            Assert.Equal("macro-1", macro.Id);
            Assert.Equal("Test macro", macro.Name);
            Assert.Single(macro.Steps);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task UnknownProfileVersionIsRejectedOnImport()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"opensynapse-v99-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(
                path,
                """
                {
                  "version": 99,
                  "activeProfileName": "Default",
                  "profiles": {}
                }
                """);

            await Assert.ThrowsAsync<InvalidDataException>(
                () => ProfileStore.ImportAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// The performance mode is stored by name. The wire byte 0x01 was called Turbo
    /// before it was renamed to Gaming, and a profile file that no longer parses would
    /// be silently replaced by the defaults, so the old name still has to load.
    /// </summary>
    [Fact]
    public async Task AProfileWrittenBeforeTheGamingRenameStillLoads()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"opensynapse-turbo-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(
                path,
                """
                {
                  "version": 2,
                  "activeProfileName": "Default",
                  "profiles": {
                    "Default": {
                      "shortcuts": {
                        "performanceCycleModes": ["Balanced", "Turbo", "Custom"]
                      }
                    }
                  }
                }
                """);

            var document = await ProfileStore.ImportAsync(path);
            var shortcuts = document.Profiles[document.ActiveProfileName].Shortcuts;

            Assert.NotNull(shortcuts);
            Assert.Equal(
                [BladePerformanceMode.Balanced, BladePerformanceMode.Gaming, BladePerformanceMode.Custom],
                shortcuts!.PerformanceCycleModes);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
