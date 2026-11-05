using OpenSynapse.Windows.Lifecycle;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class WindowsGpuPreferenceTests
{
    [Fact]
    public void CleanupTargetsOnlyOtherOpenSynapseAppPaths()
    {
        const string current = @"D:\Apps\OpenSynapse\OpenSynapse.App.exe";

        Assert.False(WindowsGpuPreference.IsStaleOpenSynapsePath(current, current));
        Assert.True(WindowsGpuPreference.IsStaleOpenSynapsePath(
            @"D:\Temp\OpenSynapse.App.exe",
            current));
        Assert.False(WindowsGpuPreference.IsStaleOpenSynapsePath(
            @"D:\Temp\Another.App.exe",
            current));
    }
}
