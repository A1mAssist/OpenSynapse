using System.IO.Compression;
using System.Text;
using OpenSynapse.Core.Storage;

namespace OpenSynapse.App.ViewModels;

public sealed partial class DiagnosticsWorkspace
{
    public string ExportLogBundle()
    {
        var logDirectory = AppDataPaths.Combine("logs");
        var exportDirectory = Path.Combine(logDirectory, "exports");
        Directory.CreateDirectory(exportDirectory);
        var path = Path.Combine(
            exportDirectory,
            $"OpenSynapse-logs-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}.zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        AddArchiveFile(archive, Path.Combine(logDirectory, "opensynapse.log"), "opensynapse.log");
        AddArchiveFile(
            archive,
            Path.Combine(logDirectory, "opensynapse.log.previous"),
            "opensynapse.log.previous");
        foreach (var crashReport in Directory.EnumerateFiles(logDirectory, "crash-*.txt"))
        {
            AddArchiveFile(archive, crashReport, Path.GetFileName(crashReport));
        }
        var reportEntry = archive.CreateEntry("diagnostics.txt", CompressionLevel.Optimal);
        using var writer = new StreamWriter(reportEntry.Open(), new UTF8Encoding(false));
        writer.Write(CreateReport());
        return path;
    }

    private static void AddArchiveFile(ZipArchive archive, string sourcePath, string entryName)
    {
        if (!File.Exists(sourcePath))
        {
            return;
        }
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var target = entry.Open();
        source.CopyTo(target);
    }
}
