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
        return DetectFormat(path) switch
        {
            ChromaFileFormat.Legacy => ImportLegacy(path, connected),
            ChromaFileFormat.Json => StudioChromaImport.Import(File.ReadAllText(path), connected),
            _ => throw new InvalidDataException(
                "Unsupported .ChromaEffects file. Expected Chroma JSON, a Synapse 3 XML profile, " +
                "or a ZIP archive containing XML profiles."),
        };
    }

    private static StudioImportResult ImportLegacy(string path, IReadOnlyList<StudioDeviceMatch> connected)
    {
        try
        {
            return StudioSynapse3Import.Import(path, connected);
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException(
                "The .ChromaEffects archive is invalid or unsupported: " + exception.Message, exception);
        }
    }

    /// <summary>
    /// Chroma's importer parses JSON first and falls back to its Synapse 3 ZIP
    /// reader. The extension is not evidence: legacy exports also use
    /// .ChromaEffects, so the leading bytes decide what the file is.
    /// </summary>
    private static ChromaFileFormat DetectFormat(string path)
    {
        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            return ChromaFileFormat.Legacy;

        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[512];
        var length = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        var bytes = header[..length];
        if (IsZip(bytes)) return ChromaFileFormat.Legacy;

        // UTF-16 XML starts with its BOM rather than '<'.
        if (bytes.Length >= 4)
        {
            if (bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == (byte)'<' && bytes[3] == 0)
                return ChromaFileFormat.Legacy;
            if (bytes[0] == 0xFE && bytes[1] == 0xFF && bytes[2] == 0 && bytes[3] == (byte)'<')
                return ChromaFileFormat.Legacy;
        }

        var index = 0;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) index = 3;
        while (index < bytes.Length &&
               bytes[index] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
            index++;
        if (index >= bytes.Length) return ChromaFileFormat.Unsupported;
        return bytes[index] switch
        {
            (byte)'<' => ChromaFileFormat.Legacy,
            (byte)'{' or (byte)'[' => ChromaFileFormat.Json,
            _ => ChromaFileFormat.Unsupported,
        };
    }

    private static bool IsZip(ReadOnlySpan<byte> header) =>
        header.Length >= 4 &&
        header[0] == (byte)'P' && header[1] == (byte)'K' &&
        header[2] is 3 or 5 or 7 && header[3] is 4 or 6 or 8;

    private enum ChromaFileFormat
    {
        Json,
        Legacy,
        Unsupported,
    }

    public static void Export(StudioProject project, string path, IReadOnlyCollection<string>? profileIds = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        StudioProjectCodec.Save(WithExtension(path), project, profileIds, export: true);
    }

    private static string WithExtension(string path) =>
        path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) ? path : path + Extension;
}
