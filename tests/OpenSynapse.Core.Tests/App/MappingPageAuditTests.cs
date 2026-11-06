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
        Assert.Contains("RazerMappingImageLayout", assets);
        Assert.Contains("IsLeft", assets);
        Assert.DoesNotContain("FindLayout", assets);
        Assert.DoesNotContain("0x00B8", assets);
        Assert.DoesNotContain("#EDEDED", view);
    }

    [Fact]
    public void MappingArtIsNotPreGenerated()
    {
        var directory = Path.Combine(
            RepoLayout.Root, "src", "OpenSynapse.App", "Assets", "MappingLayouts");
        var images = Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory).ToArray()
            : Array.Empty<string>();
        Assert.Empty(images);

        var assets = File.ReadAllText(Path.Combine(
            RepoLayout.Root, "src", "OpenSynapse.App", "ViewModels", "RazerMappingAssetCatalog.cs"));
        Assert.DoesNotContain("FindDeviceImage", assets);
        Assert.DoesNotContain("FindLayout", assets);
    }

    [Fact]
    public void MappingArtIsResolvedPerProductAtRuntime()
    {
        var root = RepoLayout.Root;
        var service = string.Join('\n', new[]
        {
            "RazerMappingAssetService.cs",
            "RazerMappingAssetService.Manifest.cs",
            "RazerMappingAssetService.Images.cs",
        }.Select(name => File.ReadAllText(Path.Combine(
            root, "src", "OpenSynapse.App", "ViewModels", name))));
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
        Assert.Contains("prd.svg", service);
        Assert.Contains("CreateImageOnlyAsync", service);
        Assert.Contains("RazerAvifDecoder", service);
        Assert.Contains("StudioManifestService", service);
        Assert.Contains("RazerMappingBundleParser", service);
        Assert.DoesNotContain("offlineCatalogPath", service);
        Assert.DoesNotContain("studio-manifests.zip", service);
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
