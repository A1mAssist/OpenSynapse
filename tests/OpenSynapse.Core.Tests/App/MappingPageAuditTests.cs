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
    public void MappingStageUsesEvidencedCalloutsAndKeepsTheTableFallback()
    {
        var root = RepoLayout.Root;
        var view = File.ReadAllText(Path.Combine(
            root, "src", "OpenSynapse.App", "Views", "RazerMappingView.xaml"));
        var code = File.ReadAllText(Path.Combine(
            root, "src", "OpenSynapse.App", "Views", "RazerMappingView.xaml.cs"));
        var assets = File.ReadAllText(Path.Combine(
            root, "src", "OpenSynapse.App", "ViewModels", "RazerMappingAssetCatalog.cs"));

        Assert.Contains("LeftCalloutControl", view);
        Assert.Contains("RightCalloutControl", view);
        Assert.Contains("ConnectorCanvas", view);
        Assert.Contains("StageVisibility", view);
        Assert.Contains("TableVisibility", view);
        Assert.Contains("CalloutClick", code);
        Assert.Contains("left ? 53 : -53", code);
        Assert.Contains("FindLayout", assets);
        Assert.Contains("0x00B8", assets);
        Assert.Contains("IsLeft", assets);
        Assert.DoesNotContain("00B9", assets);
        Assert.DoesNotContain("#EDEDED", view);
    }

    [Fact]
    public void MappingAssetsOnlyContainEvidencedDeviceImages()
    {
        var directory = Path.Combine(
            RepoLayout.Root, "src", "OpenSynapse.App", "Assets", "MappingLayouts");
        Assert.True(Directory.Exists(directory));
        var images = Directory
            .EnumerateFiles(directory)
            .Where(path =>
                path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.Contains(images, path => Path.GetFileName(path) == "00B9.svg");
        Assert.Contains(images, path => Path.GetFileName(path) == "00B8.png");
        Assert.DoesNotContain(
            Directory.GetFiles(directory, "*.json"),
            _ => true);
    }

    [Fact]
    public void MappingArtIsResolvedPerProductAtRuntime()
    {
        var root = RepoLayout.Root;
        var service = File.ReadAllText(Path.Combine(
            root, "src", "OpenSynapse.App", "ViewModels", "RazerMappingAssetService.cs"));
        var bundle = File.ReadAllText(Path.Combine(
            root, "src", "OpenSynapse.Core", "Devices", "RazerMappingBundle.cs"));
        var view = File.ReadAllText(Path.Combine(
            root, "src", "OpenSynapse.App", "Views", "RazerMappingView.xaml"));
        var loader = File.ReadAllText(Path.Combine(
            root, "src", "OpenSynapse.App", "ViewModels", "RazerMappingImageLoader.cs"));

        Assert.Contains("RazerAvifDecoder", loader);
        Assert.DoesNotContain("LiftDarkBackdrop", loader);
        Assert.Contains("asset-manifest.json", service);
        Assert.Contains("prd-3x", service);
        Assert.Contains("StudioManifestService", service);
        Assert.Contains("RazerMappingBundleParser", service);
        Assert.Contains("offlineCatalogPath:", service);
        Assert.Contains("studio-manifests.zip", service);
        Assert.Contains("{ 0, 129 }", service);
        Assert.Contains("var image = await LoadBundleAsync", service);
        Assert.Contains("drawLines", bundle);
        Assert.Contains("buttonPos", bundle);
        Assert.Contains("KeyboardCanvas", view);
        Assert.Contains("KeyboardStageVisibility", view);
        Assert.DoesNotContain("MappingProductIds", service);
        Assert.DoesNotContain("PidWhitelist", service);
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
