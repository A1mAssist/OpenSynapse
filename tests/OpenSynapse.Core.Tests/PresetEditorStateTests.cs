using System.Text.Json;
using System.Text.Json.Nodes;
using OpenSynapse.Core.Lighting;
using OpenSynapse.Core.Storage;
using Xunit;

namespace OpenSynapse.Core.Tests;

[CollectionDefinition("PresetEditorState", DisableParallelization = true)]
public sealed class PresetEditorStateCollection;

[Collection("PresetEditorState")]
public sealed class PresetEditorStateTests
{
    [Fact]
    public void DraftFromJsonRecoversDamagedValuesAndCloneIsIndependent()
    {
        const string json = """
            {
              "format": "opensynapse-chroma",
              "version": 1,
              "id": "damaged-draft",
              "name": "Damaged",
              "kind": "ambient",
              "layers": [
                {
                  "module": "missing.module",
                  "params": [1, 2, 3],
                  "palette": "not-a-real-palette"
                }
              ]
            }
            """;

        var draft = StudioPresetDraft.FromJson(json);
        Assert.Single(draft.Layers);
        var layer = draft.Layers[0];
        Assert.Equal("missing.module", layer.ModuleId);
        Assert.Empty(layer.Params);
        Assert.Empty(layer.Palette);
        Assert.Equal(StudioBlendMode.Normal, layer.Blend);
        Assert.Equal(1, layer.Opacity);
        Assert.Equal(1, layer.Speed);

        var source = MinimalDraft();
        source.Layers[0].Params["mode"] = "pulse";
        var sourceStopCount = source.Layers[0].Palette.Count;
        var sourceFirstStop = source.Layers[0].Palette[0];
        var clone = source.Clone();

        source.Layers[0].Params["mode"] = "source";
        source.Layers[0].Palette.Add(new StudioColorStop(10, StudioRgb.White));
        Assert.Equal("pulse", clone.Layers[0].Params["mode"]!.GetValue<string>());
        Assert.Equal(sourceStopCount, clone.Layers[0].Palette.Count);

        clone.Layers[0].Params["mode"] = "clone";
        clone.Layers[0].Palette.Add(new StudioColorStop(90, StudioRgb.Black));
        clone.Layers[0].Palette[0] = new StudioColorStop(42, StudioRgb.Black);
        Assert.Equal("source", source.Layers[0].Params["mode"]!.GetValue<string>());
        Assert.Equal(sourceStopCount + 1, source.Layers[0].Palette.Count);
        Assert.Equal(sourceFirstStop, source.Layers[0].Palette[0]);
    }

    [Fact]
    public void AutosaveRoundTripsDraftAndClearRemovesFile()
    {
        var previousInstance = Environment.GetEnvironmentVariable("OPENSSYNAPSE_UI_TEST_INSTANCE");
        var instance = "preset-editor-tests-" + Guid.NewGuid().ToString("N");
        var temp = CreateTempDirectory();
        try
        {
            Environment.SetEnvironmentVariable("OPENSSYNAPSE_UI_TEST_INSTANCE", instance);
            StudioPresetAutosave.Clear();

            var sourcePath = Path.Combine(temp, "autosave-source.oschroma");
            var draft = MinimalDraft();
            draft.Id = "autosave-draft";
            draft.Name = "Autosave Draft";
            draft.Layers[0].Params["speed"] = 0.25;
            StudioPresetAutosave.Save(draft, sourcePath);

            Assert.True(StudioPresetAutosave.TryLoad(out var state));
            Assert.Equal(draft.Id, state.Draft.Id);
            Assert.Equal(draft.Name, state.Draft.Name);
            Assert.Equal(draft.Layers[0].ModuleId, state.Draft.Layers[0].ModuleId);
            Assert.True(JsonNode.DeepEquals(draft.Layers[0].Params, state.Draft.Layers[0].Params));
            Assert.Equal(sourcePath, state.SourcePath);

            StudioPresetAutosave.Clear();
            Assert.False(File.Exists(StudioPresetAutosave.Path));
            Assert.False(StudioPresetAutosave.TryLoad(out _));
        }
        finally
        {
            StudioPresetAutosave.Clear();
            Environment.SetEnvironmentVariable("OPENSSYNAPSE_UI_TEST_INSTANCE", previousInstance);
            DeleteDirectory(temp);
            DeleteDirectory(Path.Combine(
                Path.GetTempPath(), AppDataPaths.VendorName, AppDataPaths.FolderName, "QA", instance));
        }
    }

    [Fact]
    public void LibrarySaveCreatesFileAndBackupAndUsesExtension()
    {
        var temp = CreateTempDirectory();
        try
        {
            var path = Path.Combine(temp, "library-save.oschroma");
            var draft = MinimalDraft();
            draft.Id = "library-save";
            draft.Name = "First";

            StudioPresetLibrary.Save(draft, path);
            Assert.True(File.Exists(path));
            Assert.False(File.Exists(path + ".bak"));

            draft.Name = "Second";
            StudioPresetLibrary.Save(draft, path);
            Assert.True(File.Exists(path + ".bak"));
            Assert.Equal("Second", StudioPresetCodec.Load(path).Name);
            Assert.Equal("First", StudioPresetCodec.Load(path + ".bak").Name);

            var libraryPath = StudioPresetLibrary.PathForId("library-save");
            Assert.Equal(StudioPresetCodec.Extension, Path.GetExtension(libraryPath));
            Assert.Equal("library-save.oschroma", Path.GetFileName(libraryPath));
        }
        finally
        {
            DeleteDirectory(temp);
        }
    }

    [Fact]
    public void ValidatorReportsIssuesAndAcceptsRenderSmoke()
    {
        var emptyId = MinimalDraft();
        emptyId.Id = "";
        Assert.Contains(StudioPresetValidator.Validate(emptyId), issue => issue.Field == "id");

        var fiveLayers = MinimalDraft();
        while (fiveLayers.Layers.Count < 5) fiveLayers.Layers.Add(fiveLayers.Layers[0]);
        Assert.Contains(StudioPresetValidator.Validate(fiveLayers), issue => issue.Field == "layers");

        var unknownModule = MinimalDraft();
        unknownModule.Layers[0].ModuleId = "missing.module";
        Assert.Contains(StudioPresetValidator.Validate(unknownModule), issue => issue.Field == "module");

        var invalidParameter = MinimalDraft();
        invalidParameter.Layers[0].Params["mode"] = "not-a-mode";
        Assert.Contains(StudioPresetValidator.Validate(invalidParameter), issue => issue.Field == "params");

        var emptyPalette = MinimalDraft();
        emptyPalette.Layers[0].Palette.Clear();
        Assert.Contains(StudioPresetValidator.Validate(emptyPalette), issue => issue.Field == "palette");

        var opacity = MinimalDraft();
        opacity.Layers[0].Opacity = 1.5;
        Assert.Contains(StudioPresetValidator.Validate(opacity), issue => issue.Field == "opacity");

        var speed = MinimalDraft();
        speed.Layers[0].Speed = 0;
        Assert.Contains(StudioPresetValidator.Validate(speed), issue => issue.Field == "speed");

        Assert.Empty(StudioPresetValidator.Validate(MinimalDraft()));
        Assert.Empty(StudioPresetValidator.Validate(MinimalDraft(), renderSmoke: true));
    }

    [Fact]
    public void PreviewSimulationRendersMusicScreenAndKeyPresets()
    {
        var music = RequirePreset(preset => preset.Kind == StudioPresetKind.Music, "music");
        var screen = RequirePreset(
            preset => preset.Kind is StudioPresetKind.Screen or StudioPresetKind.System,
            "screen/system");
        var key = RequirePreset(
            preset => preset.Layers.Any(layer => layer.ModuleId.StartsWith("key.", StringComparison.Ordinal)),
            "key");

        AssertNonBlack(music, 6, 22, 1.3);
        AssertNonBlack(music, 96, 96, 1.3);
        AssertNonBlack(screen, 6, 22, 1.3);
        AssertNonBlack(screen, 96, 96, 1.3);

        var heat = new StudioKeyHeat(6, 22);
        heat.Press(2, 7);
        var keyInput = StudioPresetPreviewSimulation.Create(0.05, key.Layers, heat.Snapshot());
        var keyFrame = StudioPresetEngine.Render(key, 6, 22, 0.05, keyInput);
        Assert.Equal(6 * 22, keyFrame.Length);
        Assert.True(keyFrame.Any(IsNonBlack), $"Bundled key preset '{key.Id}' rendered black.");
    }

    [Fact]
    public void WorldCanvasPreviewRendersBundledPreset()
    {
        var preset = StudioPresetCatalog.Default.Find("ambient-breathe-cdf2f4");
        Assert.True(preset is not null, "Bundled ambient field preset 'ambient-breathe-cdf2f4' was not found.");

        var profile = new StudioProfile("Preset Preview");
        var layer = StudioLayerItem.CreateLayer("Preset", StudioEffectKind.Preset);
        layer.Params.Preset = preset!.Id;
        layer.Params.PresetDefinition = preset;
        profile.Items.Add(layer);

        var canvas = StudioWorldCanvas.Build(
            [
                CreateGridDevice("grid", 1, 6, 22),
                CreateGridDevice("zone", 2, 1, 1),
            ],
            profile);

        Assert.Equal(96, canvas.Rows);
        Assert.Equal(96, canvas.Columns);
        Assert.NotEmpty(canvas.Layers);

        var input = StudioPresetPreviewSimulation.Create(1.3, preset.Layers, null);
        var frame = StudioCompositor.RenderLayers(canvas.Layers, 96, 96, 1.3, input);
        Assert.Equal(96 * 96, frame.Length);
        Assert.True(frame.Any(IsNonBlack), "World canvas rendered an all-black preview.");
    }

    [Fact]
    public void CompilerRejectsInvalidDraftsAndSerializesStrictShape()
    {
        var emptyId = MinimalDraft();
        emptyId.Id = "";
        Assert.Throws<InvalidDataException>(() => StudioPresetCompiler.Compile(emptyId));

        var emptyName = MinimalDraft();
        emptyName.Name = " ";
        Assert.Throws<InvalidDataException>(() => StudioPresetCompiler.Compile(emptyName));

        var noLayers = MinimalDraft();
        noLayers.Layers.Clear();
        Assert.Throws<InvalidDataException>(() => StudioPresetCompiler.Compile(noLayers));

        var unknownModule = MinimalDraft();
        unknownModule.Layers[0].ModuleId = "missing.module";
        Assert.Throws<InvalidDataException>(() => StudioPresetCompiler.Compile(unknownModule));

        var invalidParameter = MinimalDraft();
        invalidParameter.Layers[0].Params["mode"] = "not-a-mode";
        Assert.Throws<InvalidDataException>(() => StudioPresetCompiler.Compile(invalidParameter));

        var json = StudioPresetCompiler.Serialize(MinimalDraft());
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("opensynapse-chroma", root.GetProperty("format").GetString());
        Assert.Equal(1, root.GetProperty("version").GetInt32());

        var allowedTopLevel = new HashSet<string>(StringComparer.Ordinal)
        {
            "format", "version", "id", "name", "kind", "category", "note", "layers",
        };
        var allowedLayer = new HashSet<string>(StringComparer.Ordinal)
        {
            "module", "blend", "opacity", "speed", "palette", "params",
        };
        var allowedStop = new HashSet<string>(StringComparer.Ordinal) { "stop", "color" };
        foreach (var property in root.EnumerateObject())
            Assert.Contains(property.Name, allowedTopLevel);

        var layer = root.GetProperty("layers")[0];
        foreach (var property in layer.EnumerateObject())
            Assert.Contains(property.Name, allowedLayer);

        var palette = layer.GetProperty("palette");
        foreach (var property in palette.EnumerateObject())
            Assert.Equal("stops", property.Name);

        var stops = palette.GetProperty("stops");
        Assert.Equal(JsonValueKind.Array, stops.ValueKind);
        Assert.True(stops.GetArrayLength() > 0, "Serialized palette has no color stops.");
        foreach (var stop in stops.EnumerateArray())
        {
            Assert.Equal(JsonValueKind.Object, stop.ValueKind);
            foreach (var property in stop.EnumerateObject())
                Assert.Contains(property.Name, allowedStop);
        }
    }

    [Fact]
    public void LibraryFindFallsBackToEmbeddedPreset()
    {
        var preset = StudioPresetLibrary.Find("ambient-breathe-cdf2f4");
        Assert.NotNull(preset);
        Assert.Equal("ambient-breathe-cdf2f4", preset!.Id);
        Assert.Null(StudioPresetLibrary.Find(" "));
        Assert.Null(StudioPresetLibrary.Find($"missing-{Guid.NewGuid():N}"));
    }

    private static StudioPresetDraft MinimalDraft() =>
        StudioPresetDraft.FromConfig(StudioPresetCodec.Parse("""
            {"format":"opensynapse-chroma","version":1,"id":"minimal","name":"Minimal","kind":"ambient","category":"test",
             "layers":[{"module":"ambient.field","params":{"mode":"pulse"}}]}
            """));

    private static StudioPresetConfig RequirePreset(
        Func<StudioPresetConfig, bool> predicate,
        string description)
    {
        var preset = StudioPresetCatalog.Default.All.FirstOrDefault(predicate);
        Assert.True(preset is not null, $"No bundled {description} preset was found.");
        return preset!;
    }

    private static void AssertNonBlack(
        StudioPresetConfig preset,
        int rows,
        int columns,
        double seconds)
    {
        var input = StudioPresetPreviewSimulation.Create(seconds, preset.Layers, null);
        var frame = StudioPresetEngine.Render(preset, rows, columns, seconds, input);
        Assert.Equal(rows * columns, frame.Length);
        Assert.True(frame.Any(IsNonBlack), $"Bundled preset '{preset.Id}' rendered black at {rows}x{columns}.");
    }

    private static bool IsNonBlack(StudioRgb color) => color != StudioRgb.Black;

    private static StudioWorldCanvasDevice CreateGridDevice(
        string instanceId,
        ushort productId,
        int rows,
        int columns)
    {
        var leds = new List<StudioLedMap>(rows * columns);
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
                leds.Add(new StudioLedMap(row, column, row, column));
        }

        var layout = StudioLedLayout.Create(rows, columns, rows, columns, leds);
        var visual = StudioDeviceVisualBuilder.BuildGrid(productId, instanceId, instanceId, layout);
        return new StudioWorldCanvasDevice(
            instanceId, productId, visual, null, null, 0, rows, columns, rows, columns);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "OpenSynapseTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
