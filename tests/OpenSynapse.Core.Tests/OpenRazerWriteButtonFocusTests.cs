using System.Text.RegularExpressions;
using Xunit;

namespace OpenSynapse.Core.Tests;

/// <summary>
/// A save button that disables itself while it writes loses focus, and WinUI then moves
/// focus to the next control on the tab chain. Next to the mouse controls sits a DPI
/// NumberBox, and a focused NumberBox pops its spin-button flyout over the neighbouring
/// value, so the Y DPI read-out looked like it changed when it never did.
/// </summary>
public sealed class OpenRazerWriteButtonFocusTests
{
    [Fact]
    public void WriteButtonsStayEnabledWhileTheyWrite()
    {
        var repository = FindRepositoryRoot();
        var appRoot = Path.Combine(repository, "src", "OpenSynapse.App");
        var viewModelPath = Path.Combine(appRoot, "ViewModels", "OpenRazerDeviceViewModel.cs");
        var xaml = File.ReadAllText(Path.Combine(appRoot, "MainWindow.xaml"));
        var panel = ReadPanel(xaml, "OpenRazerDevicePanel");

        var properties = ReadBooleanProperties(viewModelPath);
        var offenders = new List<string>();

        foreach (Match match in Regex.Matches(
                     panel,
                     "<Button\\b[^>]*?IsEnabled=\"\\{Binding (?<name>Can\\w+)\\}\"[^>]*>"))
        {
            var name = match.Groups["name"].Value;
            if (!properties.TryGetValue(name, out var body))
            {
                // The panel also hosts controls from other view models; they are out of scope.
                continue;
            }

            // The property may delegate to another one, so follow the chain before judging it.
            var resolved = Resolve(properties, name, new HashSet<string>(StringComparer.Ordinal));
            if (resolved.Contains("Busy", StringComparison.Ordinal))
            {
                offenders.Add($"{name} disables its save button while the write is running.");
            }
        }

        Assert.True(offenders.Count == 0,
            "Save buttons that turn themselves off during a write steal focus: " + string.Join(", ", offenders));
    }

    private static string ReadPanel(string xaml, string name)
    {
        var start = xaml.IndexOf($"x:Name=\"{name}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find the {name} section.");

        // The panel ends where the next top-level panel begins.
        var next = xaml.IndexOf("x:Name=\"", start + 1, StringComparison.Ordinal);
        return next < 0 ? xaml[start..] : xaml[start..next];
    }

    private static Dictionary<string, string> ReadBooleanProperties(string path)
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(
                     File.ReadAllText(path),
                     "public bool (?<name>Can\\w+)\\s*=>(?<body>[^;]+);"))
        {
            properties[match.Groups["name"].Value] = match.Groups["body"].Value;
        }

        return properties;
    }

    private static string Resolve(
        IReadOnlyDictionary<string, string> properties,
        string name,
        ISet<string> visited)
    {
        if (!visited.Add(name) || !properties.TryGetValue(name, out var body))
        {
            return string.Empty;
        }

        var resolved = body;
        foreach (Match match in Regex.Matches(body, "(?<![.\\w])(?<name>Can\\w+)\\b"))
        {
            resolved += " " + Resolve(properties, match.Groups["name"].Value, visited);
        }

        return resolved;
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
}
