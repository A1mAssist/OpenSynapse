using System.Text.RegularExpressions;

namespace OpenSynapse.Core.Tests;

/// <summary>
/// Locates the repository and exposes the source inventory the structure tests
/// measure. Walks up from the test binary until it finds the repository root.
/// </summary>
internal static class RepoLayout
{
    private static readonly Regex PublicMember = new(@"^    public\s", RegexOptions.Compiled);

    public static string Root { get; } = FindRoot();

    public static IReadOnlyList<string> SourceFiles() =>
        Enumerate("src", "*.cs").Concat(Enumerate("src", "*.xaml")).Order(StringComparer.Ordinal).ToList();

    public static IReadOnlyList<string> CodeFiles() =>
        Enumerate("src", "*.cs").Order(StringComparer.Ordinal).ToList();

    public static string Relative(string path) =>
        Path.GetRelativePath(Root, path).Replace('\\', '/');

    public static int CountLines(string path) => File.ReadAllLines(path).Length;

    public static int CountPublicMembers(string path) =>
        File.ReadLines(path).Count(line => PublicMember.IsMatch(line));

    public static IEnumerable<string> LinesMatching(string path, Regex pattern)
    {
        var number = 0;
        foreach (var line in File.ReadLines(path))
        {
            number++;
            if (pattern.IsMatch(line))
            {
                yield return $"{Relative(path)}:{number}: {line.Trim()}";
            }
        }
    }

    private static IEnumerable<string> Enumerate(string folder, string pattern) =>
        Directory.EnumerateFiles(Path.Combine(Root, folder), pattern, SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path));

    private static bool IsBuildOutput(string path)
    {
        var separator = Path.DirectorySeparatorChar;
        return path.Contains($"{separator}obj{separator}") ||
            path.Contains($"{separator}bin{separator}") ||
            path.Contains($"{separator}artifacts{separator}");
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenSynapse.sln")) ||
                Directory.Exists(Path.Combine(directory.FullName, ".git")) ||
                File.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Repository root not found above {AppContext.BaseDirectory}.");
    }
}
