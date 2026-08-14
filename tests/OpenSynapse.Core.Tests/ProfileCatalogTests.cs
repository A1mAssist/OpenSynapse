using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;

namespace OpenSynapse.Core.Tests;

public sealed class ProfileCatalogTests
{
    [Fact]
    public void DefaultProfileIsAvailableAndCannotBeDeleted()
    {
        var document = ProfileDocument.CreateDefault();

        Assert.Equal(new[] { "Default" }, ProfileCatalog.GetNames(document));
        var exception = Assert.Throws<InvalidOperationException>(() =>
            ProfileCatalog.Delete(document, "Default"));
        Assert.Contains("last profile", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CloneIsDeepAndCanBeSelected()
    {
        var document = ProfileDocument.CreateDefault();
        document.Global.Viper.DpiX = 800;
        document.Global.Blade.RefreshRateHertz = 240;

        ProfileCatalog.Clone(document, "Default", "Gaming");
        ProfileCatalog.Select(document, "Gaming");
        document.Global.Viper.DpiX = 1600;
        document.Global.Blade.RefreshRateHertz = 60;

        ProfileCatalog.Select(document, "Default");
        Assert.Equal(800, document.Global.Viper.DpiX);
        Assert.Equal(240, document.Global.Blade.RefreshRateHertz);
        ProfileCatalog.Select(document, "Gaming");
        Assert.Equal(1600, document.Global.Viper.DpiX);
        Assert.Equal(60, document.Global.Blade.RefreshRateHertz);
    }

    [Fact]
    public void RenameAndDeleteActiveProfileSelectsAnotherProfile()
    {
        var document = ProfileDocument.CreateDefault();
        ProfileCatalog.Clone(document, "Default", "Gaming");
        ProfileCatalog.Select(document, "Gaming");

        ProfileCatalog.Rename(document, "Gaming", "Game");
        Assert.Equal("Game", document.ActiveProfileName);

        ProfileCatalog.Delete(document, "Game");
        Assert.Equal("Default", document.ActiveProfileName);
        Assert.Equal(new[] { "Default" }, ProfileCatalog.GetNames(document));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad/name")]
    [InlineData("bad:name")]
    public void RejectsInvalidProfileNames(string name)
    {
        var document = ProfileDocument.CreateDefault();

        Assert.ThrowsAny<ArgumentException>(() => ProfileCatalog.Create(document, name));
    }

    [Fact]
    public void RejectsCaseInsensitiveDuplicates()
    {
        var document = ProfileDocument.CreateDefault();
        ProfileCatalog.Create(document, "Gaming");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ProfileCatalog.Create(document, "gaming"));
        Assert.Contains("already exists", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DocumentClonePreservesProfilesAndBindings()
    {
        var document = ProfileDocument.CreateDefault();
        ProfileCatalog.Clone(document, "Default", "Gaming");
        ProfileCatalog.Select(document, "Gaming");
        ApplicationProfileBinding.Bind(document, @"C:\Games\game.exe", "Gaming");

        var clone = document.Clone();

        Assert.Equal("Gaming", clone.ActiveProfileName);
        Assert.Equal(new[] { "Default", "Gaming" }, ProfileCatalog.GetNames(clone));
        Assert.Equal("Gaming", ApplicationProfileBinding.Resolve(clone, @"C:\Games\GAME.EXE"));
    }

    [Fact]
    public void DocumentCloneDeepCopiesExpandedDeviceSettings()
    {
        var document = ProfileDocument.CreateDefault();
        document.Global.Blade.CpuBoostMode = (byte)BladeCpuBoostMode.Boost;
        document.Global.Blade.GpuBoostMode = (byte)BladeGpuBoostMode.High;
        document.Global.Blade.LogoMode = (byte)BladeLogoMode.Static;
        document.Global.Viper.DpiStages = new ViperDpiStagesProfile
        {
            ActiveStage = 1,
            Stages = [new() { Number = 1, X = 800, Y = 800 }],
        };
        document.Devices["1532:02C6"] = new DeviceProfileSettings
        {
            Lighting = new LightingProfile
            {
                Effect = "static",
                Parameters = new(StringComparer.OrdinalIgnoreCase) { ["color"] = "99DD72" },
            },
        };

        var clone = document.Clone();
        clone.Global.Viper.DpiStages!.Stages[0].X = 1600;
        clone.Devices["1532:02C6"].Lighting.Parameters["color"] = "FFFFFF";

        Assert.Equal((byte)BladeCpuBoostMode.Boost, clone.Global.Blade.CpuBoostMode);
        Assert.Equal((byte)BladeGpuBoostMode.High, clone.Global.Blade.GpuBoostMode);
        Assert.Equal((byte)BladeLogoMode.Static, clone.Global.Blade.LogoMode);
        Assert.Equal(800, document.Global.Viper.DpiStages.Stages[0].X);
        Assert.Equal("99DD72", document.Devices["1532:02C6"].Lighting.Parameters["color"]);
    }
}
