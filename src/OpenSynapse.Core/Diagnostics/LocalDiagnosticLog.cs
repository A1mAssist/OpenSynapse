using System.Text;
using OpenSynapse.Core.Storage;

namespace OpenSynapse.Core.Diagnostics;

public sealed class LocalDiagnosticLog
{
    public const long DefaultMaxFileBytes = 1024 * 1024;
    public const int DefaultRingBufferLines = 512;

    /// <summary>
    /// Successful HID requests are normally silent: a 60 Hz frame pump would otherwise
    /// append two lines per row. Set OPENSSYNAPSE_HID_TRACE=1 to opt in.
    /// </summary>
    public static bool TraceSuccessfulHidRequests { get; } =
        string.Equals(
            Environment.GetEnvironmentVariable("OPENSSYNAPSE_HID_TRACE"),
            "1",
            StringComparison.Ordinal);

    private readonly object _gate = new();
    private readonly long _maxFileBytes;
    private readonly int _ringBufferLines;
    private readonly DiagnosticLogLevel _minimumLevel;
    private readonly Queue<string> _recentLines = new();

    public LocalDiagnosticLog(
        string? filePath = null,
        long maxFileBytes = DefaultMaxFileBytes,
        DiagnosticLogLevel? minimumLevel = null,
        int ringBufferLines = DefaultRingBufferLines)
    {
        if (maxFileBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFileBytes));
        }
        if (ringBufferLines <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ringBufferLines));
        }

        FilePath = string.IsNullOrWhiteSpace(filePath)
            ? AppDataPaths.Combine("logs", "opensynapse.log")
            : Path.GetFullPath(filePath);
        PreviousFilePath = $"{FilePath}.previous";
        _maxFileBytes = maxFileBytes;
        _ringBufferLines = ringBufferLines;
        _minimumLevel = minimumLevel ?? ReadMinimumLevel();
    }

    public string FilePath { get; }

    public string PreviousFilePath { get; }

    public bool TryWrite(string source, string message) =>
        TryWrite(DiagnosticLogLevel.Info, source, message);

    public bool TryWrite(DiagnosticLogLevel level, string source, string message)
    {
        if (level < _minimumLevel ||
            string.IsNullOrWhiteSpace(source) ||
            string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var line = $"{DateTimeOffset.Now:O} [{level.ToString().ToUpperInvariant()}] " +
            $"[{SingleLine(source)}] {SingleLine(message)}{Environment.NewLine}";
        var lineBytes = Encoding.UTF8.GetByteCount(line);

        lock (_gate)
        {
            _recentLines.Enqueue(line.TrimEnd());
            while (_recentLines.Count > _ringBufferLines)
            {
                _recentLines.Dequeue();
            }

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

    public IReadOnlyList<string> SnapshotRecentLines()
    {
        lock (_gate)
        {
            return _recentLines.ToArray();
        }
    }

    public static DiagnosticLogLevel ReadMinimumLevel()
    {
        var configured = Environment.GetEnvironmentVariable("OPENSSYNAPSE_LOG_LEVEL");
        return Enum.TryParse<DiagnosticLogLevel>(configured, ignoreCase: true, out var level)
            ? level
            : DiagnosticLogLevel.Info;
    }

    private static string SingleLine(string value) =>
        value.Replace("\r\n", " | ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
}
