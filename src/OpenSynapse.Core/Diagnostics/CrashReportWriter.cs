using System.Runtime.InteropServices;
using System.Text;
using OpenSynapse.Core.Storage;

namespace OpenSynapse.Core.Diagnostics;

public static class CrashReportWriter
{
    public static string? TryWrite(string source, Exception exception, LocalDiagnosticLog? log)
    {
        try
        {
            var directory = AppDataPaths.Combine("logs");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(
                directory,
                $"crash-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}.txt");
            var report = new StringBuilder();
            report.AppendLine("OpenSynapse crash report");
            report.AppendLine($"Time: {DateTimeOffset.Now:O}");
            report.AppendLine($"Source: {source}");
            report.AppendLine($"OS: {Environment.OSVersion}");
            report.AppendLine($"Runtime: {RuntimeInformation.FrameworkDescription}");
            report.AppendLine($"Process: {Environment.ProcessPath}");
            report.AppendLine();
            report.AppendLine(exception.ToString());
            report.AppendLine();
            report.AppendLine("Recent log:");
            if (log is null)
            {
                report.AppendLine("(unavailable)");
            }
            else
            {
                foreach (var line in log.SnapshotRecentLines())
                {
                    report.AppendLine(line);
                }
            }

            File.WriteAllText(path, report.ToString(), Encoding.UTF8);
            log?.TryWrite(DiagnosticLogLevel.Error, "crash-report", $"Wrote {path}");
            return path;
        }
        catch (Exception reportException) when (
            reportException is IOException or UnauthorizedAccessException or
                NotSupportedException or ArgumentException)
        {
            return null;
        }
    }
}
