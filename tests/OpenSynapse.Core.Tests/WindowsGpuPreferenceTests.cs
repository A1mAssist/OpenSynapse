using Microsoft.Win32;
using OpenSynapse.Windows.Lifecycle;

namespace OpenSynapse.Core.Tests;

public sealed class WindowsGpuPreferenceTests
{
    [Fact]
    public void RejectsMissingExecutablePath() =>
        Assert.Throws<ArgumentException>(() => WindowsGpuPreference.EnsureMinimumPower(" "));

    [Fact]
    public void PreservesExistingExplicitPreference()
    {
        var path = Path.Combine(Path.GetTempPath(), $"OpenSynapse-{Guid.NewGuid():N}.exe");
        const string keyPath = @"Software\Microsoft\DirectX\UserGpuPreferences";
        using var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true)!;
        key.SetValue(path, "GpuPreference=2;", RegistryValueKind.String);
        try
        {
            Assert.False(WindowsGpuPreference.EnsureMinimumPower(path));
            Assert.Equal("GpuPreference=2;", key.GetValue(path));
        }
        finally
        {
            key.DeleteValue(path, throwOnMissingValue: false);
        }
    }
}
