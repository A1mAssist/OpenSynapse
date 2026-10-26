using OpenSynapse.Windows.Lifecycle;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class LegacyPerUserInstallMigratorTests
{
    [Fact]
    public void CopiesUserDataBeforeRemovingTheLegacyInstall()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"OpenSynapse-migration-test-{Guid.NewGuid():N}");
        var data = Path.Combine(
            Path.GetTempPath(),
            $"OpenSynapse-migration-data-{Guid.NewGuid():N}");
        var current = Path.Combine(root, "current");
        var logs = Path.Combine(root, "logs");
        var newExecutable = @"C:\Program Files\OpenSynapse\current\OpenSynapse.App.exe";
        string? repairedStartup = null;
        try
        {
            Directory.CreateDirectory(current);
            Directory.CreateDirectory(logs);
            File.WriteAllText(Path.Combine(root, "Update.exe"), string.Empty);
            File.WriteAllText(Path.Combine(current, "OpenSynapse.App.exe"), string.Empty);
            File.WriteAllText(Path.Combine(root, "OpenSynapse.exe"), string.Empty);
            File.WriteAllText(Path.Combine(root, "profiles.json"), "profile-data");
            File.WriteAllText(Path.Combine(root, "automatic-updates.txt"), "1");
            File.WriteAllText(Path.Combine(root, "blade-mapping-session.json.lock"), string.Empty);
            File.WriteAllText(Path.Combine(logs, "latest.log"), "log-data");

            var migrated = LegacyPerUserInstallMigrator.Migrate(
                root,
                newExecutable,
                data,
                stopLegacyProcesses: _ => { },
                uninstallLegacy: legacyRoot => Directory.Delete(legacyRoot, recursive: true),
                repairStartup: executablePath =>
                {
                    repairedStartup = executablePath;
                    return true;
                });

            Assert.True(migrated);
            Assert.Equal("profile-data", File.ReadAllText(Path.Combine(data, "profiles.json")));
            Assert.Equal("1", File.ReadAllText(Path.Combine(data, "automatic-updates.txt")));
            Assert.Equal("log-data", File.ReadAllText(Path.Combine(data, "logs", "latest.log")));
            Assert.False(Directory.Exists(current));
            Assert.False(File.Exists(Path.Combine(root, "Update.exe")));
            Assert.False(File.Exists(Path.Combine(root, "OpenSynapse.exe")));
            Assert.False(File.Exists(Path.Combine(data, "blade-mapping-session.json.lock")));
            Assert.Equal(newExecutable, repairedStartup);
        }
        finally
        {
            DeleteDirectory(root);
            DeleteDirectory(data);
        }
    }

    [Fact]
    public void KeepsCopiedUserDataWhenTheLegacyUninstallerFails()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"OpenSynapse-migration-test-{Guid.NewGuid():N}");
        var data = Path.Combine(
            Path.GetTempPath(),
            $"OpenSynapse-migration-data-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "current"));
            File.WriteAllText(Path.Combine(root, "Update.exe"), string.Empty);
            File.WriteAllText(
                Path.Combine(root, "current", "OpenSynapse.App.exe"),
                string.Empty);
            File.WriteAllText(Path.Combine(root, "profiles.json"), "profile-data");

            var migrated = LegacyPerUserInstallMigrator.Migrate(
                root,
                @"C:\Program Files\OpenSynapse\current\OpenSynapse.App.exe",
                data,
                stopLegacyProcesses: _ => { },
                uninstallLegacy: legacyRoot =>
                {
                    Directory.Delete(legacyRoot, recursive: true);
                    throw new InvalidOperationException("simulated uninstall failure");
                },
                repairStartup: _ => true);

            Assert.False(migrated);
            Assert.Equal("profile-data", File.ReadAllText(Path.Combine(data, "profiles.json")));
        }
        finally
        {
            DeleteDirectory(root);
            DeleteDirectory(data);
        }
    }

    [Fact]
    public void ParsesQuotedSilentStartupRegistration()
    {
        var parsed = WindowsStartupManager.TryParseRegistration(
            "\"C:\\Users\\Test\\AppData\\Local\\OpenSynapse\\current\\OpenSynapse.App.exe\" --silent",
            out var executablePath,
            out var silent);

        Assert.True(parsed);
        Assert.Equal(
            @"C:\Users\Test\AppData\Local\OpenSynapse\current\OpenSynapse.App.exe",
            executablePath);
        Assert.True(silent);
    }

    [Fact]
    public void CleanupRemovesInstallerArtifactsAndKeepsUserData()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"OpenSynapse-cleanup-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "packages"));
            Directory.CreateDirectory(Path.Combine(root, "logs"));
            File.WriteAllText(Path.Combine(root, "Update.exe"), "installer");
            File.WriteAllText(Path.Combine(root, "OpenSynapse.exe"), "installer");
            File.WriteAllText(
                Path.Combine(root, "packages", "OpenSynapse-1.5.5-full.nupkg"),
                "installer");
            File.WriteAllText(Path.Combine(root, "packages", ".velopack_lock"), string.Empty);
            File.WriteAllText(Path.Combine(root, "profiles.json"), "user-data");
            File.WriteAllText(Path.Combine(root, "behavior.json"), "user-data");
            File.WriteAllText(Path.Combine(root, "language.txt"), "zh-CN");
            File.WriteAllText(Path.Combine(root, "introduction-v1.done"), "1");
            File.WriteAllText(Path.Combine(root, "logs", "opensynapse.log"), "log");

            var removed = LegacyPerUserInstallMigrator.CleanupStaleArtifacts(root);

            Assert.True(removed);
            Assert.False(File.Exists(Path.Combine(root, "Update.exe")));
            Assert.False(File.Exists(Path.Combine(root, "OpenSynapse.exe")));
            Assert.False(Directory.Exists(Path.Combine(root, "packages")));
            Assert.True(File.Exists(Path.Combine(root, "profiles.json")));
            Assert.True(File.Exists(Path.Combine(root, "behavior.json")));
            Assert.True(File.Exists(Path.Combine(root, "language.txt")));
            Assert.True(File.Exists(Path.Combine(root, "introduction-v1.done")));
            Assert.True(File.Exists(Path.Combine(root, "logs", "opensynapse.log")));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void CleanupKeepsTheUninstallerWhileTheLegacyAppStillRuns()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"OpenSynapse-cleanup-live-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "current"));
            Directory.CreateDirectory(Path.Combine(root, "packages"));
            File.WriteAllText(Path.Combine(root, "Update.exe"), "installer");
            File.WriteAllText(
                Path.Combine(root, "current", "OpenSynapse.App.exe"),
                "installer");

            var removed = LegacyPerUserInstallMigrator.CleanupOrphanedInstallerArtifacts(root);

            Assert.False(removed);
            Assert.True(File.Exists(Path.Combine(root, "Update.exe")));
            Assert.True(Directory.Exists(Path.Combine(root, "packages")));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void CleanupRemovesTheUninstallerOnceTheLegacyAppIsGone()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"OpenSynapse-cleanup-gone-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "packages"));
            File.WriteAllText(Path.Combine(root, "Update.exe"), "installer");
            File.WriteAllText(
                Path.Combine(root, "packages", "OpenSynapse-1.5.5-full.nupkg"),
                "installer");
            File.WriteAllText(Path.Combine(root, "profiles.json"), "user-data");

            var removed = LegacyPerUserInstallMigrator.CleanupOrphanedInstallerArtifacts(root);

            Assert.True(removed);
            Assert.False(File.Exists(Path.Combine(root, "Update.exe")));
            Assert.False(Directory.Exists(Path.Combine(root, "packages")));
            Assert.True(File.Exists(Path.Combine(root, "profiles.json")));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void CleanupKeepsTheLiveLegacyInstallWhenOnlyTheUninstallerIsMissing()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"OpenSynapse-cleanup-no-uninstaller-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "current"));
            Directory.CreateDirectory(Path.Combine(root, "packages"));
            File.WriteAllText(
                Path.Combine(root, "current", "OpenSynapse.App.exe"),
                "installer");
            File.WriteAllText(
                Path.Combine(root, "packages", "OpenSynapse-1.5.5-full.nupkg"),
                "installer");

            var removed = LegacyPerUserInstallMigrator.CleanupOrphanedInstallerArtifacts(root);

            Assert.False(removed);
            Assert.True(Directory.Exists(Path.Combine(root, "packages")));
            Assert.True(File.Exists(Path.Combine(root, "current", "OpenSynapse.App.exe")));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void TransitionalCleanupKeepsFilesThatWereNotMigratedYet()
    {
        var transitional = Path.Combine(
            Path.GetTempPath(),
            $"OpenSynapse-transitional-test-{Guid.NewGuid():N}");
        var destination = Path.Combine(
            Path.GetTempPath(),
            $"OpenSynapse-transitional-data-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(transitional);
            Directory.CreateDirectory(destination);
            File.WriteAllText(Path.Combine(transitional, "profiles.json"), "migrated");
            File.WriteAllText(Path.Combine(transitional, "behavior.json"), "pending");
            File.WriteAllText(Path.Combine(destination, "profiles.json"), "migrated");

            var removed = LegacyPerUserInstallMigrator.CleanupTransitionalRoot(
                transitional,
                destination);

            Assert.True(removed);
            Assert.False(File.Exists(Path.Combine(transitional, "profiles.json")));
            Assert.True(File.Exists(Path.Combine(transitional, "behavior.json")));
            Assert.True(Directory.Exists(transitional));
        }
        finally
        {
            DeleteDirectory(transitional);
            DeleteDirectory(destination);
        }
    }

    [Fact]
    public void TransitionalCleanupRemovesTheDirectoryOnceEveryFileIsMigrated()
    {
        var transitional = Path.Combine(
            Path.GetTempPath(),
            $"OpenSynapse-transitional-done-{Guid.NewGuid():N}");
        var destination = Path.Combine(
            Path.GetTempPath(),
            $"OpenSynapse-transitional-done-data-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(transitional, "logs"));
            Directory.CreateDirectory(Path.Combine(destination, "logs"));
            File.WriteAllText(Path.Combine(transitional, "profiles.json"), "migrated");
            File.WriteAllText(Path.Combine(transitional, "logs", "opensynapse.log"), "log");
            File.WriteAllText(Path.Combine(destination, "profiles.json"), "migrated");
            File.WriteAllText(
                Path.Combine(destination, "logs", "opensynapse.log"),
                "log");

            var removed = LegacyPerUserInstallMigrator.CleanupTransitionalRoot(
                transitional,
                destination);

            Assert.True(removed);
            Assert.False(Directory.Exists(transitional));
        }
        finally
        {
            DeleteDirectory(transitional);
            DeleteDirectory(destination);
        }
    }

    [Fact]
    public void LegacyRootCheckRespectsTheDirectoryBoundary()
    {
        var legacyRoot = LegacyPerUserInstallMigrator.GetLegacyRoot();

        Assert.True(LegacyPerUserInstallMigrator.IsUnderLegacyRoot(
            Path.Combine(legacyRoot, "current", "OpenSynapse.App.exe")));
        Assert.False(LegacyPerUserInstallMigrator.IsUnderLegacyRoot(
            Path.Combine(legacyRoot, "..", "OpenSynapseData", "profiles.json")));
        Assert.False(LegacyPerUserInstallMigrator.IsUnderLegacyRoot(
            @"C:\Program Files\OpenSynapse\current\OpenSynapse.App.exe"));
        Assert.False(LegacyPerUserInstallMigrator.IsUnderLegacyRoot(null));
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}