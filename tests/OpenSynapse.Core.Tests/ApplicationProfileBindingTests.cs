using OpenSynapse.Core.Profiles;

namespace OpenSynapse.Core.Tests;

public sealed class ApplicationProfileBindingTests
{
    [Fact]
    public void BoundApplicationSwitchesProfileAndUnboundApplicationRestoresFallback()
    {
        var document = ProfileDocument.CreateDefault();
        ProfileCatalog.Clone(document, "Default", "Gaming");
        ApplicationProfileBinding.Bind(document, @"C:\Games\game.exe", "Gaming");
        var switcher = new ApplicationProfileSwitcher();

        Assert.True(switcher.Update(document, @"c:\games\GAME.EXE"));
        Assert.Equal("Gaming", document.ActiveProfileName);
        Assert.False(switcher.Update(document, @"C:\Games\game.exe"));

        Assert.True(switcher.Update(document, @"C:\Windows\explorer.exe"));
        Assert.Equal("Default", document.ActiveProfileName);
    }

    [Fact]
    public void ClonedSwitcherCanRetryFallbackAfterCallerRollsBack()
    {
        var document = ProfileDocument.CreateDefault();
        ProfileCatalog.Clone(document, "Default", "Gaming");
        ApplicationProfileBinding.Bind(document, @"C:\Games\game.exe", "Gaming");
        var switcher = new ApplicationProfileSwitcher();

        Assert.True(switcher.Update(document, @"C:\Games\game.exe"));
        var previousDocument = document.Clone();
        var previousSwitcher = switcher.Clone();

        Assert.True(switcher.Update(document, null));
        document = previousDocument;
        switcher = previousSwitcher;

        Assert.True(switcher.Update(document, null));
        Assert.Equal("Default", document.ActiveProfileName);
    }

    [Fact]
    public void RenameUpdatesBindingsAndDeleteRemovesThem()
    {
        var document = ProfileDocument.CreateDefault();
        ProfileCatalog.Clone(document, "Default", "Gaming");
        ApplicationProfileBinding.Bind(document, @"C:\Games\game.exe", "Gaming");

        ProfileCatalog.Rename(document, "Gaming", "Game");
        Assert.Equal("Game", ApplicationProfileBinding.Resolve(document, @"C:\Games\game.exe"));

        ProfileCatalog.Delete(document, "Game");
        Assert.Null(ApplicationProfileBinding.Resolve(document, @"C:\Games\game.exe"));
    }

    [Fact]
    public void BindingRejectsMissingProfile()
    {
        var document = ProfileDocument.CreateDefault();

        Assert.Throws<KeyNotFoundException>(() =>
            ApplicationProfileBinding.Bind(document, @"C:\Games\game.exe", "Missing"));
    }
}
