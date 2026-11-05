using System.Text.RegularExpressions;
using Xunit;

namespace OpenSynapse.Core.Tests;

/// <summary>
/// A missing {StaticResource} key throws XamlParseException while the page is built,
/// which terminates the app. Catch it here instead of on a user's device page.
/// </summary>
public sealed class XamlResourceTests
{
    private static readonly Regex KeyPattern = new(@"x:Key=""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex ReferencePattern = new(@"\{StaticResource\s+([^}\s]+)\s*\}", RegexOptions.Compiled);

    [Fact]
    public void EveryStaticResourceKeyIsDefined()
    {
        var xamlFiles = RepoLayout.SourceFiles()
            .Where(path => path.EndsWith(".xaml", StringComparison.Ordinal))
            .ToArray();
        var keys = xamlFiles
            .SelectMany(path => KeyPattern.Matches(File.ReadAllText(path))
                .Select(match => match.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);

        var offenders = xamlFiles
            .SelectMany(path => ReferencePattern.Matches(File.ReadAllText(path))
                .Select(match => (Path: path, Key: match.Groups[1].Value)))
            .Where(item => !keys.Contains(item.Key))
            .Select(item => $"{RepoLayout.Relative(item.Path)} {item.Key}")
            .Distinct()
            .ToArray();

        Assert.True(offenders.Length == 0,
            "XAML 引用了未定义的 StaticResource（运行期 XamlParseException 会崩进程）：" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }
}
