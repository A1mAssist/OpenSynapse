using Xunit;

namespace OpenSynapse.Core.Tests;

/// <summary>
/// The mapping page is data-driven and shared by HID and BLE. These checks guard the
/// two regressions that are easy to reintroduce: a second BLE action vocabulary and a
/// fixed BossKey preset path.
/// </summary>
public sealed class MappingPageAuditTests
{
    [Fact]
    public void HidAndBleShareTheMappingViewWithoutADuplicateBleRowModel()
    {
        var root = RepoLayout.Root;
        var devicesPage = File.ReadAllText(Path.Combine(
            root, "src", "OpenSynapse.App", "Views", "DevicesPage.xaml"));
        var source = string.Join('\n', Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Select(File.ReadAllText));

        Assert.Equal(4, Count(devicesPage, "RazerMappingView"));
        Assert.Contains("IRazerMappingTransport", source);
        Assert.DoesNotContain("RazerBleMappingRowViewModel", source);
        Assert.DoesNotContain("RazerBleMappingGroupOption", source);
        Assert.DoesNotContain("ViperButtonAssignmentRowViewModel", source);
        Assert.Contains("RazerViperMappingActionCatalog", source);
        Assert.Contains("RazerKeyboardMappingActionCatalog", source);
        Assert.DoesNotContain("RazerBossKeyAction", source);
        Assert.DoesNotContain("BossKey", source);
        Assert.Contains("Viper.Mapping", devicesPage);
        Assert.Contains("MappingVisibility", devicesPage);
    }

    [Fact]
    public void MappingAssetsOnlyContainEvidencedDeviceImages()
    {
        var directory = Path.Combine(
            RepoLayout.Root, "src", "OpenSynapse.App", "Assets", "MappingLayouts");
        Assert.True(Directory.Exists(directory));
        var images = Directory.GetFiles(directory, "*.svg");
        Assert.Contains(images, path => Path.GetFileName(path) == "00B9.svg");
        Assert.DoesNotContain(
            Directory.GetFiles(directory, "*.json"),
            _ => true);
    }

    private static bool IsBuildOutput(string path)
    {
        var separator = Path.DirectorySeparatorChar;
        return path.Contains($"{separator}obj{separator}") ||
            path.Contains($"{separator}bin{separator}") ||
            path.Contains($"{separator}artifacts{separator}");
    }

    private static int Count(string value, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
