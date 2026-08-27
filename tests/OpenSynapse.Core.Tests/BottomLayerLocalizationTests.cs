using System.Text.RegularExpressions;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed partial class BottomLayerLocalizationTests
{
    [Fact]
    public void CoreAndWindowsSourceContainNoCjkText()
    {
        var repository = FindRepositoryRoot();
        var matches = new List<string>();

        foreach (var project in new[] { "OpenSynapse.Core", "OpenSynapse.Windows" })
        {
            var sourceDirectory = Path.Combine(repository, "src", project);
            foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                    file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var lineNumber = 0;
                foreach (var line in File.ReadLines(file))
                {
                    lineNumber++;
                    if (CjkText().IsMatch(line))
                    {
                        matches.Add($"{Path.GetRelativePath(repository, file)}:{lineNumber}");
                    }
                }
            }
        }

        Assert.True(matches.Count == 0,
            "Core and Windows source must use culture-invariant diagnostics. CJK text found at: " +
            string.Join(", ", matches));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenSynapse.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the OpenSynapse repository root.");
    }

    [GeneratedRegex("[\\u3400-\\u4DBF\\u4E00-\\u9FFF\\uF900-\\uFAFF，。：；！？（）【】“”‘’、]")]
    private static partial Regex CjkText();
}
