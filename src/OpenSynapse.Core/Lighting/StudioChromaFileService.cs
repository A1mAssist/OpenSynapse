namespace OpenSynapse.Core.Lighting;

/// <summary>
/// Reads and writes .ChromaEffects files. Import reconciles the file with the
/// devices reachable here; export replaces the target through a same-directory
/// temp file so a crash never truncates an existing project.
/// </summary>
public static class StudioChromaFileService
{
    public const string Extension = ".ChromaEffects";

    public static StudioImportResult Import(string path, IReadOnlyList<StudioDeviceMatch> connected)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return StudioChromaImport.Import(File.ReadAllText(path), connected);
    }

    public static void Export(StudioProject project, string path)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        StudioProjectCodec.Save(WithExtension(path), project);
    }

    private static string WithExtension(string path) =>
        path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) ? path : path + Extension;
}
