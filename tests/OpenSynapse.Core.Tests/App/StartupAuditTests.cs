using Xunit;

namespace OpenSynapse.Core.Tests;

/// <summary>
/// The startup registration launches with --silent. Activation paints a frame before
/// any handler can hide the window, so the hidden path must never call Activate().
/// </summary>
public sealed class StartupAuditTests
{
    [Fact]
    public void SilentStartupNeverActivatesTheMainWindow()
    {
        var app = Source("App.xaml.cs");
        Assert.Contains("window.StartHiddenLaunch();", app);
        var window = Source("MainWindow.xaml.cs");
        var start = window.IndexOf("internal void StartHiddenLaunch()", StringComparison.Ordinal);
        Assert.True(start >= 0, "the hidden launch entry point must exist");
        var body = window[start..window.IndexOf("\n    }", start, StringComparison.Ordinal)];
        Assert.Contains("Activated -= OnActivated;", body);
        Assert.DoesNotContain("Activate()", body);
    }

    private static string Source(string file) =>
        File.ReadAllText(Path.Combine(RepoLayout.Root, "src/OpenSynapse.App", file));
}