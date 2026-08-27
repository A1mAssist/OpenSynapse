using System.Text.Json.Nodes;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class DeviceMetadataTests
{
    [Fact]
    public void LegacyManifestWithoutCategoryDerivesTheKnownFamilyCategory()
    {
        var json = BuiltInManifestJson("blade-710");
        var root = JsonNode.Parse(json)!.AsObject();
        root.Remove("category");

        var registry = RazerDeviceRegistry.LoadJson([root.ToJsonString()]);

        Assert.Equal(DeviceCategory.Laptop, registry.Manifests.Single().Category);
    }

    [Theory]
    [InlineData("laptop", DeviceCategory.Laptop)]
    [InlineData("mouse", DeviceCategory.Mouse)]
    [InlineData("keyboard", DeviceCategory.Keyboard)]
    [InlineData("headset", DeviceCategory.Headset)]
    public void ManifestCategoryIsParsed(string value, DeviceCategory expected)
    {
        var json = BuiltInManifestJson("blade-710");
        var root = JsonNode.Parse(json)!.AsObject();
        root["category"] = value;

        var registry = RazerDeviceRegistry.LoadJson([root.ToJsonString()]);

        Assert.Equal(expected, registry.Manifests.Single().Category);
    }

    [Fact]
    public void UnknownManifestCategoryIsRejected()
    {
        var root = JsonNode.Parse(BuiltInManifestJson("blade-710"))!.AsObject();
        root["category"] = "console";

        var exception = Assert.Throws<InvalidOperationException>(() =>
            RazerDeviceRegistry.LoadJson([root.ToJsonString()]));

        Assert.Contains("Unknown device category", exception.Message);
    }

    [Fact]
    public void ExternalManifestCannotChangeItsProtocolFamilyCategory()
    {
        var root = JsonNode.Parse(BuiltInManifestJson("viper-184"))!.AsObject();
        root["id"] = "viper-compatible-test";
        root["productIds"] = new JsonArray("00FF");
        root["category"] = "laptop";
        var directory = Path.Combine(Path.GetTempPath(), $"OpenSynapse-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "device.json"), root.ToJsonString());

            var result = RazerDeviceRegistry.Load(directory);

            Assert.Contains(result.Errors, error => error.Contains("category does not match", StringComparison.Ordinal));
            Assert.DoesNotContain(result.Registry.Manifests, manifest => manifest.Id == "viper-compatible-test");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void OpenRazerStandardManifestMayDeclareOnlyItsVerifiedCapabilities()
    {
        var root = JsonNode.Parse(BuiltInManifestJson("blade-710"))!.AsObject();
        root["id"] = "openrazer-standard-test";
        root["displayName"] = "OpenRazer test device";
        root["productIds"] = new JsonArray("0BAD");
        root["protocolFamily"] = "openrazer-standard";
        root["category"] = "keyboard";
        root["collection"]!["featureReportLength"] = 91;
        root["capabilities"] = new JsonObject();

        var registry = RazerDeviceRegistry.LoadJson([root.ToJsonString()]);

        var manifest = Assert.Single(registry.Manifests);
        Assert.Equal("openrazer-standard", manifest.ProtocolFamily);
        Assert.Empty(manifest.Capabilities);
        Assert.Equal(DeviceCategory.Keyboard, manifest.Category);
    }

    [Fact]
    public void OpenRazerStandardManifestCannotUseTheLinuxLogicalLengthAsWindowsFeatureLength()
    {
        var root = JsonNode.Parse(BuiltInManifestJson("blade-710"))!.AsObject();
        root["id"] = "openrazer-linux-length-test";
        root["productIds"] = new JsonArray("0BAD");
        root["protocolFamily"] = "openrazer-standard";
        root["category"] = "keyboard";
        root["collection"]!["featureReportLength"] = 90;
        root["capabilities"] = new JsonObject();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            RazerDeviceRegistry.LoadJson([root.ToJsonString()]));

        Assert.Contains("permits only 91-byte feature reports", exception.Message);
    }

    [Theory]
    [InlineData("02C6")]
    [InlineData("00B8")]
    public void OpenRazerStandardCannotClaimCompletedDevice(string productId)
    {
        var root = JsonNode.Parse(BuiltInManifestJson("blade-710"))!.AsObject();
        root["id"] = "openrazer-reserved-test";
        root["displayName"] = "OpenRazer reserved test device";
        root["productIds"] = new JsonArray(productId);
        root["protocolFamily"] = "openrazer-standard";
        root["category"] = "keyboard";
        root["collection"]!["featureReportLength"] = 91;
        root["capabilities"] = new JsonObject();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            RazerDeviceRegistry.LoadJson([root.ToJsonString()]));

        Assert.Contains("is reserved for protocol family", exception.Message);
    }

    [Fact]
    public void CompletedDevicesRemainOwnedByTheirOriginalProtocolFamilies()
    {
        Assert.Equal("blade-710", RazerDeviceRegistry.BuiltIn.Find(0x1532, 0x02C6)?.ProtocolFamily);
        Assert.Equal("viper-184", RazerDeviceRegistry.BuiltIn.Find(0x1532, 0x00B8)?.ProtocolFamily);
    }

    [Fact]
    public void ExternalOpenRazerStandardManifestIsRejectedUntilAudited()
    {
        var root = JsonNode.Parse(BuiltInManifestJson("blade-710"))!.AsObject();
        root["id"] = "external-openrazer-test";
        root["displayName"] = "External OpenRazer test device";
        root["productIds"] = new JsonArray("0BAD");
        root["protocolFamily"] = "openrazer-standard";
        root["category"] = "keyboard";
        root["collection"]!["featureReportLength"] = 91;
        root["capabilities"] = new JsonObject();
        var directory = Path.Combine(Path.GetTempPath(), $"OpenSynapse-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "device.json"), root.ToJsonString());

            var result = RazerDeviceRegistry.Load(directory);

            Assert.Contains(result.Errors, error => error.Contains("permits only reviewed built-in manifests", StringComparison.Ordinal));
            Assert.DoesNotContain(result.Registry.Manifests, manifest => manifest.Id == "external-openrazer-test");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void BladeCapabilitySummaryKeepsTheExistingCounts()
    {
        var descriptor = Descriptor("blade-710", DeviceCategory.Laptop);
        var telemetry = new RazerDeviceTelemetry(
            BladeKeyboardBrightness: 1,
            BladePerformanceMode: BladePerformanceMode.Balanced,
            BladeFanMode: BladeFanMode.Automatic,
            BladeFanTargetRpm: 1,
            BladeChargeLimitPercent: 80,
            ViperBatteryPercent: null,
            ViperPollingRateHertz: null,
            ViperDpiX: null,
            ViperDpiY: null,
            ViperIdleSeconds: null,
            Errors: [],
            CapturedAt: DateTimeOffset.UtcNow,
            BladeCpuBoostMode: BladeCpuBoostMode.Medium,
            BladeGpuBoostMode: BladeGpuBoostMode.Medium,
            BladeMaxFanMode: BladeMaxFanMode.Disabled,
            BladeCurrentFanCpuRpm: 1,
            BladeCurrentFanGpuRpm: 1,
            BladeAdvancedFanCpuModeRaw: 1,
            BladeAdvancedFanGpuModeRaw: 1,
            BladeLogoMode: BladeLogoMode.Off,
            BladeStartupAnimationEnabled: true,
            BladeNativeDisplayMode: BladeNativeDisplayMode.Uhd,
            BladeSkuHardwareConfiguration: new(true, true, false, 1),
            BladeOneTimeFullChargeEnabled: false,
            BladeLocalDimmingEnabled: true);

        var summary = DeviceCapabilitySummaryCalculator.Calculate(descriptor, telemetry);

        Assert.Equal(new DeviceCapabilitySummary(17, 17), summary);
    }

    [Fact]
    public void ViperCapabilitySummaryKeepsTheExistingCounts()
    {
        var descriptor = Descriptor("viper-184", DeviceCategory.Mouse);
        var telemetry = new RazerDeviceTelemetry(
            BladeKeyboardBrightness: null,
            BladePerformanceMode: null,
            BladeFanMode: null,
            BladeFanTargetRpm: null,
            BladeChargeLimitPercent: null,
            ViperBatteryPercent: 90,
            ViperPollingRateHertz: 1000,
            ViperDpiX: 800,
            ViperDpiY: 800,
            ViperIdleSeconds: 600,
            Errors: [],
            CapturedAt: DateTimeOffset.UtcNow,
            ViperDpiStages: new(1, []),
            ViperLowBatteryThresholdRaw: 20);

        var summary = DeviceCapabilitySummaryCalculator.Calculate(descriptor, telemetry);

        Assert.Equal(new DeviceCapabilitySummary(6, 6), summary);
    }

    private static DeviceDescriptor Descriptor(string family, DeviceCategory category) => new(
        family,
        family,
        0x1532,
        family == "blade-710" ? (ushort)0x02C6 : (ushort)0x0084,
        DeviceAccessState.Available,
        DeviceCapabilityState.PendingValidation,
        91,
        1,
        2,
        family,
        category);

    private static string BuiltInManifestJson(string family)
    {
        var manifest = RazerDeviceRegistry.BuiltIn.Manifests.Single(item =>
            item.ProtocolFamily == family);
        var resourceName = typeof(RazerDeviceRegistry).Assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith($"{manifest.SourceName}", StringComparison.Ordinal));
        using var stream = typeof(RazerDeviceRegistry).Assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
