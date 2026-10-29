using System.Xml.Linq;
using Xunit;

namespace OpenSynapse.Core.Tests;

/// <summary>
/// The assembly boundary: Core knows nothing about Windows or the UI, and the
/// Windows layer never reaches into the app. Both held before this test existed;
/// it exists so they keep holding.
/// </summary>
public sealed class LayeringTests
{
    [Fact]
    public void CoreDoesNotReferenceOuterLayers()
    {
        var offenders = Violations("OpenSynapse.Core", "OpenSynapse.Windows")
            .Concat(Violations("OpenSynapse.Core", "OpenSynapse.App"))
            .ToList();

        Assert.True(offenders.Count == 0,
            "Core 不允许引用 Windows/App：" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void WindowsDoesNotReferenceTheApp()
    {
        var offenders = Violations("OpenSynapse.Windows", "OpenSynapse.App").ToList();

        Assert.True(offenders.Count == 0,
            "Windows 不允许引用 App：" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void ProjectReferencesFollowTheLayerOrder()
    {
        var allowed = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["OpenSynapse.Core"] = [],
            ["OpenSynapse.Windows"] = ["OpenSynapse.Core"],
            ["OpenSynapse.App"] = ["OpenSynapse.Core", "OpenSynapse.Windows", "OpenSynapse.RecoveryHost", "OpenSynapse.ThxHelper"],
            ["OpenSynapse.RecoveryHost"] = ["OpenSynapse.Windows"],
            ["OpenSynapse.ThxHelper"] = ["OpenSynapse.Windows"],
        };

        var failures = new List<string>();
        foreach (var (project, permitted) in allowed)
        {
            var csproj = Path.Combine(RepoLayout.Root, "src", project, $"{project}.csproj");
            Assert.True(File.Exists(csproj), $"缺少 {project}.csproj");

            var references = XDocument.Load(csproj)
                .Descendants("ProjectReference")
                .Select(element => (string?)element.Attribute("Include"))
                .Where(value => value is not null)
                .Select(value => Path.GetFileNameWithoutExtension(value!.Replace('\\', '/')))
                .ToList();

            failures.AddRange(references
                .Where(reference => !permitted.Contains(reference, StringComparer.Ordinal))
                .Select(reference => $"{project} 引用了 {reference}，不在允许清单 {string.Join("/", permitted)} 内"));
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static IEnumerable<string> Violations(string project, string forbiddenNamespace)
    {
        var pattern = new System.Text.RegularExpressions.Regex(
            $@"^\s*using\s+{System.Text.RegularExpressions.Regex.Escape(forbiddenNamespace)}(\.|;)",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(RepoLayout.Root, "src", project),
                     "*.cs",
                     SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            foreach (var hit in RepoLayout.LinesMatching(file, pattern))
            {
                yield return hit;
            }
        }
    }
}
