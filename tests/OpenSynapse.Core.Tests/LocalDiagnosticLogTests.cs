using OpenSynapse.Core.Diagnostics;

namespace OpenSynapse.Core.Tests;

public sealed class LocalDiagnosticLogTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "OpenSynapse.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void WritesSingleLineUtf8Entries()
    {
        var path = Path.Combine(_directory, "opensynapse.log");
        var log = new LocalDiagnosticLog(path);

        Assert.True(log.TryWrite("device", "first line\r\nsecond line"));

        var lines = File.ReadAllLines(path);
        Assert.Single(lines);
        Assert.Contains("[device] first line | second line", lines[0]);
    }

    [Fact]
    public void KeepsOnlyOnePreviousFileWhenSizeLimitIsReached()
    {
        var path = Path.Combine(_directory, "opensynapse.log");
        var log = new LocalDiagnosticLog(path, maxFileBytes: 180);

        Assert.True(log.TryWrite("test", new string('A', 100)));
        Assert.True(log.TryWrite("test", new string('B', 100)));

        Assert.Contains(new string('A', 100), File.ReadAllText(log.PreviousFilePath));
        Assert.Contains(new string('B', 100), File.ReadAllText(log.FilePath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
