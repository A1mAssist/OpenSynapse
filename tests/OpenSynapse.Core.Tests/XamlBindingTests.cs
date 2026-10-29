using System.Text.RegularExpressions;
using Xunit;

namespace OpenSynapse.Core.Tests;

/// <summary>
/// A {Binding} path that no longer exists fails silently at runtime: the control simply
/// stays empty. This test reads the XAML and checks that every binding root is declared
/// on a view model, so moving a member between classes turns into a red test instead of
/// a blank page nobody notices.
/// </summary>
public sealed class XamlBindingTests
{
    private static readonly Regex BindingExpression =
        new(@"\{(?:x:)?Binding\s+([^}]+)\}", RegexOptions.Compiled);

    private static readonly Regex PublicMember =
        new(@"^\s*public\s+[^(=]*?([A-Za-z_]\w*)\s*(?:[{(=;]|$)", RegexOptions.Compiled);

    /// <summary>
    /// Roots that resolve through x:Name, a code-behind property or an item template type
    /// rather than a view model. Everything else has to be a real member.
    /// </summary>
    private static readonly string[] ResolvedOutsideViewModels =
    [
        "Hex",              // LightingColorOption record in MainWindow.Blade.cs
        "PrimaryBrush",     // code-behind brush helpers for the colour pickers
        "SecondaryBrush",
        "TertiaryBrush",
        "RpmInput",         // fan-curve editor text boxes
        "TemperatureInput",
    ];

    /// <summary>
    /// Workspace properties are reachable only through their parent, so the second
    /// segment is checked against the workspace's own members.
    /// </summary>
    private static readonly Dictionary<string, string> Workspaces = new(StringComparer.Ordinal)
    {
        ["Macros"] = "src/OpenSynapse.App/ViewModels/MacroWorkspace.cs",
        ["OpenRazer"] = "src/OpenSynapse.App/ViewModels/OpenRazerWorkspace.cs",
        ["Profiles"] = "src/OpenSynapse.App/ViewModels/ProfilesWorkspace.cs",
        ["Telemetry"] = "src/OpenSynapse.App/ViewModels/TelemetryWorkspace.cs",
        ["Fan"] = "src/OpenSynapse.App/ViewModels/FanWorkspace.cs",
        ["Display"] = "src/OpenSynapse.App/ViewModels/DisplayWorkspace.cs",
        ["Lighting"] = "src/OpenSynapse.App/ViewModels/LightingWorkspace.cs",
        ["Blade"] = "src/OpenSynapse.App/ViewModels/BladeWorkspace.cs",
        ["Firmware"] = "src/OpenSynapse.App/ViewModels/FirmwareWorkspace.cs",
        ["Diagnostics"] = "src/OpenSynapse.App/ViewModels/DiagnosticsWorkspace.cs",
        ["Refresh"] = "src/OpenSynapse.App/ViewModels/RefreshWorkspace.cs",
        ["Tray"] = "src/OpenSynapse.App/ViewModels/TrayWorkspace.cs",
        ["Viper"] = "src/OpenSynapse.App/ViewModels/ViperWorkspace.cs",
    };

    /// <summary>
    /// The headset panel sets its DataContext to the headset view model, whose own Lighting
    /// property is an OpenRazerKrakenViewModel. Those paths are not workspace roots.
    /// </summary>
    private static readonly string[] ResolvedOnNestedViewModels =
    [
        "Lighting.EffectOptions",
        "Lighting.SelectedEffectIndex",
        "Lighting.PrimaryVisibility",
        "Lighting.Primary",
        "Lighting.SecondaryVisibility",
        "Lighting.Secondary",
        "Lighting.CanApply",
    ];

    [Fact]
    public void EveryBindingRootResolvesToAViewModelMember()
    {
        var members = ViewModelMembers();
        var offenders = new List<string>();

        foreach (var file in RepoLayout.SourceFiles().Where(path => path.EndsWith(".xaml", StringComparison.Ordinal)))
        {
            var line = 0;
            foreach (var text in File.ReadLines(file))
            {
                line++;
                foreach (Match match in BindingExpression.Matches(text))
                {
                    foreach (var root in Paths(match.Groups[1].Value).Select(path => path.Split('.')[0]))
                    {
                        if (!members.Contains(root) &&
                            !ResolvedOutsideViewModels.Contains(root, StringComparer.Ordinal))
                        {
                            offenders.Add($"{RepoLayout.Relative(file)}:{line} {root}");
                        }
                    }
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "XAML 绑定到了不存在的成员（运行期会静默失效）：" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders.Distinct()));
    }

    [Fact]
    public void WorkspaceBindingsResolveInsideTheWorkspace()
    {
        var offenders = new List<string>();
        foreach (var (property, source) in Workspaces)
        {
            var members = MembersOf(source);
            foreach (var file in RepoLayout.SourceFiles().Where(path => path.EndsWith(".xaml", StringComparison.Ordinal)))
            {
                var line = 0;
                foreach (var text in File.ReadLines(file))
                {
                    line++;
                    foreach (Match match in BindingExpression.Matches(text))
                    {
                        foreach (var path in Paths(match.Groups[1].Value))
                        {
                            var segments = path.Split('.');
                            if (segments.Length < 2 ||
                                !string.Equals(segments[0], property, StringComparison.Ordinal))
                            {
                                continue;
                            }

                            if (ResolvedOnNestedViewModels.Contains(path, StringComparer.Ordinal))
                            {
                                continue;
                            }

                            if (!members.Contains(segments[1]))
                            {
                                offenders.Add($"{RepoLayout.Relative(file)}:{line} {path}");
                            }
                        }
                    }
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "绑定到了 workspace 里不存在的成员：" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders.Distinct()));
    }

    private static IEnumerable<string> Paths(string expression)
    {
        var head = expression.Split(',')[0].Trim();
        if (head.Length == 0)
        {
            yield break;
        }

        if (head.StartsWith("Path=", StringComparison.Ordinal))
        {
            head = head[5..].Trim();
        }

        // ElementName=, Source=, RelativeSource= and friends do not point at the data context.
        if (head.Length == 0 || head.Contains('=') || head.StartsWith('('))
        {
            yield break;
        }

        yield return head;
    }

    private static HashSet<string> ViewModelMembers()
    {
        var members = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in AppSourceFiles().Where(path =>
                     Path.GetFileName(path).Contains("ViewModel", StringComparison.Ordinal) &&
                     !Path.GetFileName(path).Contains("Workspace", StringComparison.Ordinal)))
        {
            members.UnionWith(MembersOf(file));
        }

        return members;
    }

    private static HashSet<string> MembersOf(string relativePath)
    {
        var path = Path.Combine(RepoLayout.Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"缺少 {relativePath}");

        var members = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(path))
        {
            var match = PublicMember.Match(line);
            if (match.Success)
            {
                members.Add(match.Groups[1].Value);
            }
        }

        return members;
    }

    private static IEnumerable<string> AppSourceFiles() =>
        Directory.EnumerateFiles(
                Path.Combine(RepoLayout.Root, "src", "OpenSynapse.App"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));
}
