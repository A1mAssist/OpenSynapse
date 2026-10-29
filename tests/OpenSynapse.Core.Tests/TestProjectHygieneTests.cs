using System.Text.RegularExpressions;
using Xunit;

namespace OpenSynapse.Core.Tests;

/// <summary>
/// The test project may not reach into the app by compiling its sources. The
/// list below is the debt that already exists; it may only shrink.
/// </summary>
public sealed class TestProjectHygieneTests
{
    private static readonly string[] AppSourcesCompiledIntoTheTestProject =
    [
    ];

    [Fact]
    public void TestProjectDoesNotCompileNewAppSources()
    {
        var included = AppSourcesInTestProject();
        var unexpected = included
            .Where(path => !AppSourcesCompiledIntoTheTestProject.Contains(path, StringComparer.OrdinalIgnoreCase))
            .ToList();

        Assert.True(unexpected.Count == 0,
            "测试项目不允许再硬链 App 源码，把它们移到 Core 或 Windows：" +
            Environment.NewLine + string.Join(Environment.NewLine, unexpected));
    }

    [Fact]
    public void AllowlistedAppSourcesStillExist()
    {
        var missing = AppSourcesCompiledIntoTheTestProject
            .Where(path => !File.Exists(Path.Combine(RepoLayout.Root, path.Replace('/', Path.DirectorySeparatorChar))))
            .ToList();

        Assert.True(missing.Count == 0,
            "这些文件已经不在 App 里了，把对应条目从白名单删掉：" +
            Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    private static List<string> AppSourcesInTestProject()
    {
        var csproj = Path.Combine(RepoLayout.Root, "tests", "OpenSynapse.Core.Tests", "OpenSynapse.Core.Tests.csproj");
        Assert.True(File.Exists(csproj), "缺少测试项目文件");

        return Regex.Matches(File.ReadAllText(csproj), @"<Compile\s+Include=""([^""]+)""")
            .Select(match => Normalize(match.Groups[1].Value))
            .Where(path => path.StartsWith("src/OpenSynapse.App/", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static string Normalize(string include)
    {
        var cleaned = include.Replace("\\\\", "\\").Replace('\\', '/');
        var index = cleaned.IndexOf("src/", StringComparison.Ordinal);
        return index < 0 ? cleaned : cleaned[index..];
    }
}
