using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;

namespace OpenSynapse.Core.Tests;

public sealed class ProfileStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "OpenSynapseProfileTests", Guid.NewGuid().ToString("N"));
    private readonly string _path;

    public ProfileStoreTests()
    {
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "profiles.json");
    }

    [Fact]
    public async Task MissingFileLoadsSafeDefaults()
    {
        var document = await new ProfileStore(_path).LoadAsync();

        Assert.Equal(ProfileStore.CurrentVersion, document.Version);
        Assert.Empty(document.Devices);
        Assert.Empty(document.ApplicationBindings);
        Assert.Equal("off", document.Global.Lighting.Effect);
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public async Task SavesAndLoadsVersionedDocument()
    {
        var expected = ProfileDocument.CreateDefault();
        expected.Global.Viper.DpiX = 1600;
        expected.Global.Viper.DpiY = 1600;
        expected.Global.Blade.RefreshRateHertz = 240;
        expected.Global.Lighting.Effect = "static";
        expected.Global.Lighting.Parameters["color"] = "#99DD72";
        expected.ApplicationBindings["game.exe"] = "gaming";

        var store = new ProfileStore(_path);
        await store.SaveAsync(expected);

        var json = await File.ReadAllTextAsync(_path);
        Assert.Contains("\"version\": 1", json, StringComparison.Ordinal);

        var actual = await store.LoadAsync();
        Assert.Equal(1600, actual.Global.Viper.DpiX);
        Assert.Equal(240, actual.Global.Blade.RefreshRateHertz);
        Assert.Equal("static", actual.Global.Lighting.Effect);
        Assert.Equal("#99DD72", actual.Global.Lighting.Parameters["color"]);
        Assert.Equal("gaming", actual.ApplicationBindings["game.exe"]);
    }

    [Fact]
    public async Task ReplacesExistingFileWithoutLeavingTemporaryFiles()
    {
        var store = new ProfileStore(_path);
        await store.SaveAsync(ProfileDocument.CreateDefault());

        var changed = ProfileDocument.CreateDefault();
        changed.Global.Viper.PollingRateHertz = 1000;
        await store.SaveAsync(changed);

        var loaded = await store.LoadAsync();
        Assert.Equal(1000, loaded.Global.Viper.PollingRateHertz);
        Assert.Empty(Directory.GetFiles(_directory, "profiles.json.tmp-*"));
    }

    [Fact]
    public async Task CorruptJsonFallsBackWithoutOverwritingSource()
    {
        const string corruptJson = "{\"version\":1,\"global\":";
        await File.WriteAllTextAsync(_path, corruptJson);

        var document = await new ProfileStore(_path).LoadAsync();

        Assert.Equal(ProfileStore.CurrentVersion, document.Version);
        Assert.Equal("off", document.Global.Lighting.Effect);
        Assert.Equal(corruptJson, await File.ReadAllTextAsync(_path));
    }

    [Fact]
    public async Task PersistsNamedProfilesAndActiveSelection()
    {
        var document = ProfileDocument.CreateDefault();
        document.Global.Viper.DpiX = 800;
        ProfileCatalog.Clone(document, "Default", "Gaming");
        ProfileCatalog.Select(document, "Gaming");
        document.Global.Viper.DpiX = 1600;

        var store = new ProfileStore(_path);
        await store.SaveAsync(document);
        var loaded = await store.LoadAsync();

        Assert.Equal("Gaming", loaded.ActiveProfileName);
        Assert.Equal(1600, loaded.Global.Viper.DpiX);
        ProfileCatalog.Select(loaded, "Default");
        Assert.Equal(800, loaded.Global.Viper.DpiX);
    }

    [Fact]
    public async Task ExportsAndImportsNamedProfilesAndBindings()
    {
        var exportPath = Path.Combine(_directory, "export.json");
        var document = ProfileDocument.CreateDefault();
        ProfileCatalog.Clone(document, "Default", "Gaming");
        ApplicationProfileBinding.Bind(document, @"C:\Games\game.exe", "Gaming");
        ProfileCatalog.Select(document, "Gaming");
        document.Global.Viper.PollingRateHertz = 1000;

        await ProfileStore.ExportAsync(document, exportPath);
        var imported = await ProfileStore.ImportAsync(exportPath);

        Assert.Equal("Gaming", imported.ActiveProfileName);
        Assert.Equal(1000, imported.Global.Viper.PollingRateHertz);
        Assert.Equal("Gaming", ApplicationProfileBinding.Resolve(imported, @"c:\games\GAME.EXE"));
    }

    [Fact]
    public async Task ExportsAndImportsExpandedDeviceSettings()
    {
        var exportPath = Path.Combine(_directory, "expanded.json");
        var document = ProfileDocument.CreateDefault();
        document.Global.Blade.CpuBoostMode = (byte)BladeCpuBoostMode.High;
        document.Global.Blade.GpuBoostMode = (byte)BladeGpuBoostMode.Medium;
        document.Global.Blade.LogoMode = (byte)BladeLogoMode.Off;
        document.Global.Viper.DpiStages = new ViperDpiStagesProfile
        {
            ActiveStage = 1,
            Stages = [new() { Number = 1, X = 1600, Y = 1800 }],
        };
        document.Devices["1532:02C6"] = new DeviceProfileSettings
        {
            Lighting = new LightingProfile
            {
                Effect = "static",
                Parameters = new(StringComparer.OrdinalIgnoreCase) { ["color"] = "99DD72" },
            },
        };

        await ProfileStore.ExportAsync(document, exportPath);
        var imported = await ProfileStore.ImportAsync(exportPath);

        Assert.Equal((byte)BladeCpuBoostMode.High, imported.Global.Blade.CpuBoostMode);
        Assert.Equal((byte)BladeGpuBoostMode.Medium, imported.Global.Blade.GpuBoostMode);
        Assert.Equal((byte)BladeLogoMode.Off, imported.Global.Blade.LogoMode);
        Assert.Equal((byte)1, imported.Global.Viper.DpiStages!.ActiveStage);
        Assert.Equal((byte)1, imported.Global.Viper.DpiStages.Stages[0].Number);
        Assert.Equal(1600, imported.Global.Viper.DpiStages.Stages[0].X);
        Assert.Equal(1800, imported.Global.Viper.DpiStages.Stages[0].Y);
        Assert.Equal("static", imported.Devices["1532:02C6"].Lighting.Effect);
        Assert.Equal("99DD72", imported.Devices["1532:02C6"].Lighting.Parameters["color"]);
    }

    [Fact]
    public async Task ImportRejectsCorruptAndUnknownVersionFiles()
    {
        var importPath = Path.Combine(_directory, "import.json");
        await File.WriteAllTextAsync(importPath, "{not-json");
        await Assert.ThrowsAsync<InvalidDataException>(() => ProfileStore.ImportAsync(importPath));

        const string unknownVersion = "{\"version\":999}";
        await File.WriteAllTextAsync(importPath, unknownVersion);
        await Assert.ThrowsAsync<InvalidDataException>(() => ProfileStore.ImportAsync(importPath));
        Assert.Equal(unknownVersion, await File.ReadAllTextAsync(importPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
