using OpenSynapse.Windows.Lifecycle;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class LegacyInstallSafetyTests
{
    [Theory]
    [InlineData(0, false, false)]
    [InlineData(0x2000, false, true)]
    [InlineData(0x2100, false, true)]
    [InlineData(0x3000, false, false)]
    [InlineData(0x4000, false, false)]
    [InlineData(0x2000, true, false)]
    public void UserUninstallerRequiresKnownUnelevatedIntegrity(int level, bool privileged, bool allowed) =>
        Assert.Equal(allowed, LegacyPerUserInstallMigrator.CanRunLegacyUninstaller(level, privileged));

    [Fact]
    public void ElevatedMigrationNeverInvokesTheUserUninstallerOrProcessCallbacks()
    {
        using var files = new Files();
        var calls = 0;
        Assert.False(LegacyPerUserInstallMigrator.Migrate(files.Source, files.Executable, files.Destination,
            _ => calls++, _ => calls++, _ => { calls++; return true; }, isElevated: true));
        Assert.Equal(0, calls);
        Assert.True(File.Exists(Path.Combine(files.Source, "current", "OpenSynapse.App.exe")));
    }

    [Theory]
    [InlineData("profiles.json")]
    [InlineData("profiles/sub/device.json")]
    public void ConflictingSourceVersionIsPreservedBeforeUninstall(string relative)
    {
        using var files = new Files();
        files.Write(files.Source, relative, "legacy newer edit");
        files.Write(files.Destination, relative, "new app edit");
        Assert.True(files.Migrate(root =>
        {
            Assert.Equal("new app edit", File.ReadAllText(Path.Combine(files.Destination, relative)));
            Assert.Equal("legacy newer edit", File.ReadAllText(Path.Combine(files.Destination, relative + ".legacy-backup")));
            Directory.Delete(root, recursive: true);
        }));
        Assert.False(Directory.Exists(files.Source));
    }

    [Fact]
    public void InterruptedMigrationAndLaterLegacyEditsKeepBothBackupVersions()
    {
        using var files = new Files();
        files.Write(files.Source, "profiles.json", "legacy v1");
        files.Write(files.Destination, "profiles.json", "new app");
        Assert.False(files.Migrate(_ => throw new IOException("uninstall interrupted")));
        files.Write(files.Source, "profiles.json", "legacy v2");
        Assert.True(files.Migrate(root => Directory.Delete(root, recursive: true)));
        Assert.Equal("new app", File.ReadAllText(Path.Combine(files.Destination, "profiles.json")));
        Assert.Equal("legacy v1", File.ReadAllText(Path.Combine(files.Destination, "profiles.json.legacy-backup")));
        Assert.Equal("legacy v2", File.ReadAllText(Path.Combine(files.Destination, "profiles.json.legacy-backup.1")));
    }

    [Fact]
    public void RepeatedMigrationDoesNotDuplicateAnIdenticalConflictBackup()
    {
        using var files = new Files();
        files.Write(files.Source, "profiles.json", "legacy");
        files.Write(files.Destination, "profiles.json", "new app");
        Assert.False(files.Migrate(_ => throw new IOException("interrupted")));
        Assert.False(files.Migrate(_ => throw new IOException("interrupted")));
        Assert.Single(Directory.GetFiles(files.Destination, "*.legacy-backup*"));
    }

    [Fact]
    public void BackupFailureStopsBeforeUninstallAndKeepsTheOnlySourceCopy()
    {
        using var files = new Files();
        files.Write(files.Source, "profiles.json", "legacy");
        files.Write(files.Destination, "profiles.json", "new app");
        Directory.CreateDirectory(Path.Combine(files.Destination, "profiles.json.legacy-backup"));
        var uninstall = false;
        Assert.False(files.Migrate(_ => uninstall = true));
        Assert.False(uninstall);
        Assert.Equal("legacy", File.ReadAllText(Path.Combine(files.Source, "profiles.json")));
        Assert.Equal("new app", File.ReadAllText(Path.Combine(files.Destination, "profiles.json")));
    }

    [Fact]
    public void TransitionalCleanupDoesNotDeleteAConflictingUnbackedSource()
    {
        using var files = new Files();
        var transitional = Path.Combine(files.Root, "transitional");
        files.Write(transitional, "profiles.json", "only copy");
        files.Write(files.Destination, "profiles.json", "different");
        Assert.False(LegacyPerUserInstallMigrator.CleanupTransitionalRoot(transitional, files.Destination));
        Assert.Equal("only copy", File.ReadAllText(Path.Combine(transitional, "profiles.json")));
    }

    [Fact]
    public void TransitionalCleanupAcceptsAVerifiedConflictBackupWithoutReplacingDestination()
    {
        using var files = new Files();
        var transitional = Path.Combine(files.Root, "transitional");
        files.Write(transitional, "profiles.json", "legacy");
        files.Write(files.Destination, "profiles.json", "different");
        LegacyMigrationData.PreserveFile(Path.Combine(transitional, "profiles.json"), Path.Combine(files.Destination, "profiles.json"));
        Assert.True(LegacyPerUserInstallMigrator.CleanupTransitionalRoot(transitional, files.Destination));
        Assert.False(Directory.Exists(transitional));
        Assert.Equal("different", File.ReadAllText(Path.Combine(files.Destination, "profiles.json")));
        Assert.Equal("legacy", File.ReadAllText(Path.Combine(files.Destination, "profiles.json.legacy-backup")));
    }

    [Fact]
    public void ConcurrentCopiesPreserveOneWholeFileWithoutPartialDestinationsOrDuplicateBackups()
    {
        using var files = new Files();
        files.Write(files.Source, "profiles.json", new string('x', 100000));
        Directory.CreateDirectory(files.Destination);
        var source = Path.Combine(files.Source, "profiles.json");
        var destination = Path.Combine(files.Destination, "profiles.json");
        Parallel.For(0, 8, _ => LegacyMigrationData.PreserveFile(source, destination));
        Assert.Equal(File.ReadAllText(source), File.ReadAllText(destination));
        Assert.Single(Directory.GetFiles(files.Destination));
    }

    private sealed class Files : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "OpenSynapseTests", "migration-" + Guid.NewGuid().ToString("N"));
        internal string Source => Path.Combine(Root, "legacy");
        internal string Destination => Path.Combine(Root, "data");
        internal string Executable => @"C:\Program Files\OpenSynapse\current\OpenSynapse.App.exe";
        internal Files() => Write(Source, "current/OpenSynapse.App.exe", "old binary");
        internal void Write(string root, string relative, string value)
        {
            var path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, value);
        }
        internal bool Migrate(Action<string> uninstall) => LegacyPerUserInstallMigrator.Migrate(
            Source, Executable, Destination, _ => { }, uninstall, _ => true);
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
