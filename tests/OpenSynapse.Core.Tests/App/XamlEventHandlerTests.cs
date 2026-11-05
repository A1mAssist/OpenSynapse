using System.Text.RegularExpressions;
using Xunit;

namespace OpenSynapse.Core.Tests;

/// <summary>
/// The app cannot be launched under UI automation while a user instance is running, so this
/// guards the cheapest runtime failure: an event handler named in XAML but missing in code.
/// </summary>
public sealed class XamlEventHandlerTests
{
    [Fact]
    public void EveryXamlEventHandlerExistsInCode()
    {
        var repository = FindRepositoryRoot();
        var appRoot = Path.Combine(repository, "src", "OpenSynapse.App");
        var referenced = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in Enumerate(appRoot, "*.xaml"))
        {
            foreach (Match match in Regex.Matches(
                         File.ReadAllText(file),
                         "\\b(?:Click|Toggled|Checked|Unchecked|SelectionChanged|TextChanged|ValueChanged|Tapped|DoubleTapped|Loaded|Unloaded)=\"([A-Za-z_][A-Za-z0-9_]*)\""))
            {
                referenced.Add(match.Groups[1].Value);
            }
        }

        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Enumerate(appRoot, "*.cs"))
        {
            foreach (Match match in Regex.Matches(
                         File.ReadAllText(file),
                         "\\b(?:void|Task)\\s+([A-Za-z_][A-Za-z0-9_]*)\\s*\\("))
            {
                declared.Add(match.Groups[1].Value);
            }
        }

        var missing = referenced.Where(name => !declared.Contains(name)).OrderBy(name => name).ToArray();

        Assert.True(missing.Length == 0,
            "XAML references handlers that no method implements: " + string.Join(", ", missing));
    }

    private static IEnumerable<string> Enumerate(string root, string pattern) =>
        Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
            .Where(file =>
                !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !file.Contains($"{Path.DirectorySeparatorChar}.isolated-build{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

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
}
