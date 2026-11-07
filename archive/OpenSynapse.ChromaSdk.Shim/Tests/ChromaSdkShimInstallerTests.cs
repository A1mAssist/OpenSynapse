using OpenSynapse.Windows.Lighting;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class ChromaSdkShimInstallerTests
{
    [Fact]
    public void InstallKeepsTheOfficialDllAndRestorePutsItBack()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "OpenSynapse-chroma-shim-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var target = Path.Combine(directory, "RzChromaSDK64.dll");
            var source = Path.Combine(directory, "shim.dll");
            File.WriteAllBytes(target, new byte[273096]);
            File.WriteAllBytes(source, new byte[1679872]);

            Assert.Equal((int)ChromaSdkShimResult.Success,
                ChromaSdkShimInstaller.InstallTo(directory, source));
            Assert.True(ChromaSdkShimInstaller.IsInstalledAt(directory));
            Assert.Equal(1679872, new FileInfo(target).Length);
            Assert.Equal(273096, new FileInfo(target + ".opensynapse-backup").Length);

            Assert.Equal((int)ChromaSdkShimResult.Success,
                ChromaSdkShimInstaller.RestoreFrom(directory));
            Assert.False(ChromaSdkShimInstaller.IsInstalledAt(directory));
            Assert.Equal(273096, new FileInfo(target).Length);
            Assert.False(File.Exists(target + ".opensynapse-backup"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
