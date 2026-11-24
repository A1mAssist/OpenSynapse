using System.Text.Json;
using System.Text.Json.Nodes;
using OpenSynapse.Core.Lighting;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class PresetEditorTests
{
    [Fact]
    public void BundledCatalogLoadsWithoutErrors()
    {
        Assert.True(StudioPresetCatalog.Default.All.Count >= 513,
            $"Expected at least 513 presets, found {StudioPresetCatalog.Default.All.Count}.");
        Assert.Empty(StudioPresetCatalog.Default.Errors);
    }

    [Fact]
    public void BundledPresetsRoundTripThroughDraftAndCompiler()
    {
        foreach (var preset in StudioPresetCatalog.Default.All)
        {
            var draft = StudioPresetDraft.FromConfig(preset);
            var compiled = StudioPresetCompiler.Compile(draft);
            var reparsed = StudioPresetCodec.Parse(StudioPresetCodec.Serialize(compiled));

            Assert.Equal(preset.Id, reparsed.Id);
            Assert.Equal(preset.Name, reparsed.Name);
            Assert.Equal(preset.Kind, reparsed.Kind);
            Assert.Equal(preset.Category, reparsed.Category);
            Assert.Equal(preset.Note, reparsed.Note);
            Assert.Equal(preset.Layers.Count, reparsed.Layers.Count);
            for (var index = 0; index < preset.Layers.Count; index++)
            {
                var expected = preset.Layers[index];
                var actual = reparsed.Layers[index];
                Assert.Equal(expected.ModuleId, actual.ModuleId);
                Assert.Equal(expected.Blend, actual.Blend);
                Assert.Equal(expected.Opacity, actual.Opacity);
                Assert.Equal(expected.Speed, actual.Speed);
                Assert.Equal(Stops(expected.Palette), Stops(actual.Palette));
                Assert.True(JsonNode.DeepEquals(
                        JsonNode.Parse(expected.Parameters.GetRawText()),
                        JsonNode.Parse(actual.Parameters.GetRawText())),
                    $"{preset.Id} layer {index + 1} params changed.");
            }
        }
    }

    [Fact]
    public void BundledPresetsRenderAtDeviceAndWorldSizes()
    {
        foreach (var preset in StudioPresetCatalog.Default.All)
        {
            var input = StudioPresetPreviewSimulation.Create(1.7, preset.Layers, null);
            var device = StudioPresetEngine.Render(preset, 6, 22, 1.7, input);
            var world = StudioPresetEngine.Render(preset, 96, 96, 1.7, input);
            Assert.Equal(6 * 22, device.Length);
            Assert.Equal(96 * 96, world.Length);
        }
    }

    [Fact]
    public void EveryBundledPresetValidates()
    {
        var failures = StudioPresetLibrary.ValidateAll()
            .Where(result => result.Issues.Count > 0)
            .Select(result => $"{result.Entry.Id}: {string.Join("; ", result.Issues)}")
            .ToArray();
        Assert.Empty(failures);
    }

    [Fact]
    public void InvalidDraftsAreRejected()
    {
        var draft = MinimalDraft();
        draft.Id = "Bad Id";
        draft.Name = "";
        draft.Layers.Clear();
        var issues = StudioPresetValidator.Validate(draft);
        Assert.Contains(issues, issue => issue.Field == "id");
        Assert.Contains(issues, issue => issue.Field == "name");
        Assert.Contains(issues, issue => issue.Field == "layers");

        var unknown = MinimalDraft();
        unknown.Layers[0].ModuleId = "missing.module";
        Assert.Contains(StudioPresetValidator.Validate(unknown), issue => issue.Field == "module");

        var tooMany = MinimalDraft();
        while (tooMany.Layers.Count < 5) tooMany.Layers.Add(tooMany.Layers[0]);
        Assert.Contains(StudioPresetValidator.Validate(tooMany), issue => issue.Field == "layers");

        var badParameter = MinimalDraft();
        badParameter.Layers[0].ModuleId = "ambient.field";
        badParameter.Layers[0].Params["mode"] = "not-a-mode";
        Assert.Contains(StudioPresetValidator.Validate(badParameter), issue => issue.Field == "params");
    }

    [Fact]
    public void PaletteAcceptsStringAndObjectShapes()
    {
        const string named = """
            {"format":"opensynapse-chroma","version":1,"id":"palette-named","name":"Named","kind":"ambient","category":"test",
             "layers":[{"module":"ambient.field","palette":"sunset","params":{"mode":"pulse"}}]}
            """;
        const string objects = """
            {"format":"opensynapse-chroma","version":1,"id":"palette-object","name":"Object","kind":"ambient","category":"test",
             "layers":[{"module":"ambient.field","palette":{"stops":[{"stop":0,"color":"#000000"},{"stop":100,"color":"#FFFFFF"}]},"params":{"mode":"pulse"}}]}
            """;

        var namedPreset = StudioPresetCodec.Parse(named);
        var objectPreset = StudioPresetCodec.Parse(objects);
        Assert.Equal(Stops(StudioPalette.TryGet("sunset", out var sunset) ? sunset : StudioPalette.White),
            Stops(namedPreset.Layers[0].Palette));
        Assert.Equal("0:#000000|100:#FFFFFF", Stops(objectPreset.Layers[0].Palette));

        var draft = StudioPresetDraft.FromConfig(objectPreset);
        var reparsed = StudioPresetCompiler.Compile(draft);
        Assert.Equal(Stops(objectPreset.Layers[0].Palette), Stops(reparsed.Layers[0].Palette));
    }

    private static StudioPresetDraft MinimalDraft()
    {
        var preset = StudioPresetCodec.Parse("""
            {"format":"opensynapse-chroma","version":1,"id":"minimal","name":"Minimal","kind":"ambient","category":"test",
             "layers":[{"module":"ambient.field","params":{"mode":"pulse"}}]}
            """);
        return StudioPresetDraft.FromConfig(preset);
    }

    private static string Stops(StudioPalette palette) =>
        string.Join("|", palette.Stops.Select(stop => $"{stop.Stop}:{stop.Color.ToHex()}"));
}
