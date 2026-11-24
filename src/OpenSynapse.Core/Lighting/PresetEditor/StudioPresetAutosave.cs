using System.Text.Json;
using System.Text.Json.Nodes;
using OpenSynapse.Core.Storage;

namespace OpenSynapse.Core.Lighting;

public readonly record struct StudioPresetAutosaveState(
    StudioPresetDraft Draft,
    string? SourcePath,
    DateTimeOffset SavedAt);

/// <summary>One recovery slot for the preset editor; best-effort writes never block editing.</summary>
public static class StudioPresetAutosave
{
    public static string Path => AppDataPaths.Combine("lighting-presets", ".autosave.json");

    public static void Save(StudioPresetDraft draft, string? sourcePath)
    {
        try
        {
            var json = new JsonObject
            {
                ["savedAt"] = DateTimeOffset.Now,
                ["source"] = sourcePath,
                ["draft"] = JsonNode.Parse(StudioPresetCompiler.Serialize(draft)),
            };
            var directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            StudioProjectCodec.WriteAtomically(Path, json.ToJsonString());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            JsonException or InvalidOperationException)
        {
            // Autosave is optional; the editor keeps working when the disk is read-only.
        }
    }

    public static bool TryLoad(out StudioPresetAutosaveState state)
    {
        state = default;
        try
        {
            if (!File.Exists(Path)) return false;
            using var document = JsonDocument.Parse(File.ReadAllText(Path));
            var root = document.RootElement;
            if (!root.TryGetProperty("draft", out var draftElement)) return false;
            var savedAt = root.TryGetProperty("savedAt", out var saved) &&
                saved.TryGetDateTimeOffset(out var parsed)
                ? parsed
                : DateTimeOffset.MinValue;
            var source = root.TryGetProperty("source", out var sourceElement) &&
                sourceElement.ValueKind == JsonValueKind.String
                ? sourceElement.GetString()
                : null;
            state = new StudioPresetAutosaveState(
                StudioPresetDraft.FromJson(draftElement.GetRawText()), source, savedAt);
            return true;
        }
        catch (Exception exception) when (exception is IOException or JsonException or
            InvalidOperationException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(Path)) File.Delete(Path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A stale recovery file is harmless.
        }
    }
}
