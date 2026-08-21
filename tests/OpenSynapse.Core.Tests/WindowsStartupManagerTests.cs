using OpenSynapse.Windows.Lifecycle;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class WindowsStartupManagerTests
{
    [Fact]
    public void StartupCommandAddsOnlyTheExplicitSilentArgument()
    {
        var executable = Path.Combine(Path.GetTempPath(), "OpenSynapse.App.exe");
        var quotedPath = $"\"{Path.GetFullPath(executable)}\"";

        Assert.Equal(quotedPath, WindowsStartupManager.FormatCommand(executable, silent: false));
        Assert.Equal($"{quotedPath} --silent", WindowsStartupManager.FormatCommand(executable, silent: true));
    }
}
