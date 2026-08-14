using System.Text;

namespace OpenSynapse.Core.Diagnostics;

public sealed class LocalDiagnosticLog
{
    public const long DefaultMaxFileBytes = 1024 * 1024;

    private readonly object _gate = new();
    private readonly long _maxFileBytes;

    public LocalDiagnosticLog(string? filePath = null, long maxFileBytes = DefaultMaxFileBytes)
    {
        if (maxFileBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFileBytes));
        }

        FilePath = string.IsNullOrWhiteSpace(filePath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenSynapse",
                "logs",
                "opensynapse.log")
            : Path.GetFullPath(filePath);
        PreviousFilePath = $"{FilePath}.previous";
        _maxFileBytes = maxFileBytes;
    }

    public string FilePath { get; }

    public string PreviousFilePath { get; }

    public bool TryWrite(string source, string message)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var line = $"{DateTimeOffset.Now:O} [{SingleLine(source)}] {SingleLine(message)}{Environment.NewLine}";
        var lineBytes = Encoding.UTF8.GetByteCount(line);

        lock (_gate)
        {
            try
            {
                var directory = Path.GetDirectoryName(FilePath);
                if (string.IsNullOrEmpty(directory))
                {
                    return false;
                }

                Directory.CreateDirectory(directory);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length + lineBytes > _maxFileBytes)
                {
                    File.Move(FilePath, PreviousFilePath, overwrite: true);
                }

                File.AppendAllText(FilePath, line, Encoding.UTF8);
                return true;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                return false;
            }
        }
    }

    private static string SingleLine(string value) =>
        value.Replace("\r\n", " | ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
}
