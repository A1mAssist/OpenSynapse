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

    [Fact]
    public async Task Version2RazerHidLightingKeyIsMigratedAndRewritten()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"opensynapse-razerhid-{Guid.NewGuid():N}");
        var sourcePath = Path.Combine(directory, "profiles.json");
        var savedPath = Path.Combine(directory, "saved.json");
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(
                sourcePath,
                """
                {
                  "version": 2,
                  "activeProfileName": "Default",
                  "profiles": {
                    "Default": {
                      "pluggedIn": {
                        "openRazerLighting": {
                          "1532:0200": { "effect": "static" }
                        }
                      }
                    }
                  }
                }
                """);

            var document = await ProfileStore.ImportAsync(sourcePath);
            Assert.Equal(ProfileStore.CurrentVersion, document.Version);
            Assert.Equal("static", document.PluggedIn.RazerHidLighting["1532:0200"].Effect);

            await new ProfileStore(savedPath).SaveAsync(document);
            var saved = await File.ReadAllTextAsync(savedPath);
            Assert.Contains("\"razerHidLighting\"", saved);
            Assert.DoesNotContain("\"openRazerLighting\"", saved);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task LegacyRazerHidLightingOnlyFillsMissingDevices()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"opensynapse-razerhid-merge-{Guid.NewGuid():N}.json");
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
                      "pluggedIn": {
                        "razerHidLighting": {
                          "1532:0200": { "effect": "spectrum" }
                        },
                        "openRazerLighting": {
                          "1532:0200": { "effect": "static" },
                          "1532:0201": { "effect": "breathing" }
                        }
                      }
                    }
                  }
                }
                """);

            var document = await ProfileStore.ImportAsync(path);

            Assert.Equal("spectrum", document.PluggedIn.RazerHidLighting["1532:0200"].Effect);
            Assert.Equal("breathing", document.PluggedIn.RazerHidLighting["1532:0201"].Effect);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Version2TopLevelRazerHidLightingKeyStillLoads()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"opensynapse-razerhid-top-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(
                path,
                """
                {
                  "version": 2,
                  "activeProfileName": "Default",
                  "pluggedIn": {
                    "openRazerLighting": {
                      "1532:0200": { "effect": "off" }
                    }
                  }
                }
                """);

            var document = await ProfileStore.ImportAsync(path);

            Assert.Equal("off", document.PluggedIn.RazerHidLighting["1532:0200"].Effect);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
