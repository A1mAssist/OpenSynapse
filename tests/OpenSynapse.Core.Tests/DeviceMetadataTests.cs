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
            BladeLocalDimmingEnabled: true,
            BladeAdapterPower: new(0x0C, 0x0C, 200, 200));

        // PID 02C6 has no audited Custom maximum-fan switch, so that row is neither
        // counted nor advertised even though the shared Power Mode bit reads back.
        var summary = DeviceCapabilitySummaryCalculator.Calculate(descriptor, telemetry);

        Assert.Equal(new DeviceCapabilitySummary(17, 17), summary);
    }

    [Fact]
    public void NativeDisplayModeRequiresMiniLedPanelResolution()
    {
        var unsupported = new BladeSkuHardwareConfiguration(
            Dds: false,
            MiniLedResolution: false,
            IllegalBatterySupport: false,
            Raw: 0x00);
        var supported = new BladeSkuHardwareConfiguration(
            Dds: true,
            MiniLedResolution: true,
            IllegalBatterySupport: false,
            Raw: 0x03);

        Assert.False(unsupported.SupportsNativeDisplayMode);
        Assert.True(supported.SupportsNativeDisplayMode);
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

    private static JsonObject LegacyBladeManifest()
    {
        var root = JsonNode.Parse(BuiltInManifestJson("blade-710"))!.AsObject();
        root["schemaVersion"] = 1;
        var capabilities = root["capabilities"]!.AsObject();
        capabilities["max-fan.get"] = capabilities["power-mode.get"]!.DeepClone();
        capabilities["max-fan.set"] = capabilities["power-mode.set"]!.DeepClone();
        capabilities.Remove("power-mode.get");
        capabilities.Remove("power-mode.set");
        return root;
    }

    [Fact]
    public void LegacyMaxFanCapabilityIdsLoadUnderTheirRenamedIds()
    {
        var registry = RazerDeviceRegistry.LoadJson([LegacyBladeManifest().ToJsonString()]);

        var manifest = registry.Manifests.Single();
        Assert.Contains("power-mode.get", manifest.Capabilities.Keys);
        Assert.Contains("power-mode.set", manifest.Capabilities.Keys);
        Assert.DoesNotContain("max-fan.get", manifest.Capabilities.Keys);
        Assert.DoesNotContain("max-fan.set", manifest.Capabilities.Keys);
    }

    [Fact]
    public void ExternalLegacyManifestIsRewrittenOnDiskAndReloads()
    {
        var root = LegacyBladeManifest();
        root["id"] = "blade-legacy-test";
        root["productIds"] = new JsonArray("02FF");
        var directory = Path.Combine(Path.GetTempPath(), $"OpenSynapse-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "device.json");
            File.WriteAllText(path, root.ToJsonString());

            var first = RazerDeviceRegistry.Load(directory);
            Assert.Empty(first.Errors);
            Assert.Contains(first.Registry.Manifests, item => item.Id == "blade-legacy-test");

            var migrated = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            Assert.Equal(2, migrated["schemaVersion"]!.GetValue<int>());
            Assert.Null(migrated["capabilities"]!.AsObject()["max-fan.get"]);
            Assert.NotNull(migrated["capabilities"]!.AsObject()["power-mode.get"]);

            // The rewrite is stable: a second load must not migrate again.
            var second = RazerDeviceRegistry.Load(directory);
            Assert.Empty(second.Errors);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// The regression that motivated the model gate: Blade 16 2025 (02C6) is not a
    /// Custom maximum-fan model, so the shared Power Mode bit must not expose the control.
    /// </summary>
    [Theory]
    [InlineData(0x02C6, false)] // Blade 16 (2025): fixed 5400 RPM fan target instead
    [InlineData(0x02E0, false)] // Blade 16 (2026)
    [InlineData(0x029D, true)] // Blade 14 (2023): middleware enables maximum fan
    [InlineData(0x02C5, false)] // Blade 14 (2025)
    [InlineData(0x02B6, true)]  // Blade 14 (2024): middleware enables maximum fan
    [InlineData(0x02B8, true)]  // Blade 18 (2024): middleware enables maximum fan
    [InlineData(0x029F, true)]  // Blade 16 (2023): owner-tested
    [InlineData(0x02A0, true)]  // Blade 18 (2023): owner-reported
    public void MaxFanIsAdmittedOnlyForModelsWithEvidence(ushort productId, bool expected)
    {
        Assert.Equal(expected, BladeModelCapabilities.SupportsMaxFan(productId));
    }

    /// <summary>
    /// The regression behind #13: the Custom CPU column used to collapse whenever the
    /// readback did not map to an admitted index, so firmware reporting Boost or
    /// Undervolt on a Low/Medium/High model hid all three levels.
    /// </summary>
    [Theory]
    [InlineData(0x02C6, true)]
    [InlineData(0x029F, true)]
    [InlineData(0x028C, true)]
    [InlineData(0x029D, true)]
    [InlineData(0x02B6, true)]
    [InlineData(0x0245, false)] // Blade 15 (2019): no audited Custom CPU levels
    [InlineData(0xFFFF, false)]
    public void CpuBoostVisibilityFollowsTheModelContract(ushort productId, bool expected)
    {
        Assert.Equal(expected, BladeModelCapabilities.SupportsCpuBoost(productId));
    }

    [Fact]

    public void UnknownProductIdAdmitsNothing()
    {
        Assert.False(BladeModelCapabilities.SupportsMaxFan(0xFFFF));
        Assert.Empty(BladeModelCapabilities.CpuBoostModesFor(0xFFFF));
    }

    /// <summary>
    /// Only 029F captures a CPU Boost level; the captured 02C6 Custom payloads stop at
    /// High. Undervolt must not be offered on any audited model.
    /// </summary>
    [Fact]
    public void CpuBoostLevelsFollowTheCapturedModelContract()
    {
        Assert.False(BladeModelCapabilities.AllowsCpuBoost(0x02C6, BladeCpuBoostMode.Boost));
        Assert.False(BladeModelCapabilities.AllowsCpuBoost(0x02C6, BladeCpuBoostMode.Undervolt));
        Assert.True(BladeModelCapabilities.AllowsCpuBoost(0x029F, BladeCpuBoostMode.Boost));
        Assert.False(BladeModelCapabilities.AllowsCpuBoost(0x029F, BladeCpuBoostMode.Undervolt));

        Assert.Equal(
            [BladeCpuBoostMode.Low, BladeCpuBoostMode.Medium, BladeCpuBoostMode.High],
            BladeModelCapabilities.CpuBoostModesFor(0x02C6));
    }

    /// <summary>
/// Blade 16 (2023) is admitted as its own family: it shares the 710 thermal and
/// power-mode commands but has no keyboard-brightness or panel report, so it must not
/// inherit the 02C6 capability contract.
/// </summary>
[Fact]
public void Blade671IsItsOwnFamilyWithoutTheProduct710OnlyReports()
{
    var manifest = RazerDeviceRegistry.BuiltIn.Find(0x1532, 0x029F);

    Assert.NotNull(manifest);
    Assert.Equal("blade-671", manifest!.ProtocolFamily);
    Assert.Equal(DeviceCategory.Laptop, manifest.Category);
    Assert.Contains("power-mode.get", manifest.Capabilities.Keys);
    Assert.Contains("fan-target.get", manifest.Capabilities.Keys);
    // Its own lighting reports: brightness in 0x03:0x83 and the uniform effect store,
    // not the 02C6 matrix protocol.
    Assert.Contains("keyboard-brightness.get", manifest.Capabilities.Keys);
    Assert.Contains("keyboard-effect.get", manifest.Capabilities.Keys);
    Assert.DoesNotContain("logo-power.get", manifest.Capabilities.Keys);
    Assert.DoesNotContain("native-display-mode.get", manifest.Capabilities.Keys);
    Assert.DoesNotContain("sku-hardware-configuration.get", manifest.Capabilities.Keys);

    // The 02C6 keeps its own family so the panel and lighting reports stay exclusive.
    Assert.Equal("blade-710", RazerDeviceRegistry.BuiltIn.Find(0x1532, 0x02C6)?.ProtocolFamily);
}

[Fact]
public void Blade671CapabilitySummaryCountsOnlyItsAdmittedReports()
{
    var descriptor = new DeviceDescriptor(
        "blade-671",
        "blade-671",
        0x1532,
        0x029F,
        DeviceAccessState.Available,
        DeviceCapabilityState.PendingValidation,
        91,
        1,
        2,
        "blade-671",
        DeviceCategory.Laptop);
    var telemetry = new RazerDeviceTelemetry(
        BladeKeyboardBrightness: 128,
        BladePerformanceMode: BladePerformanceMode.Balanced,
        BladeFanMode: BladeFanMode.Automatic,
        BladeFanTargetRpm: null,
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
        BladeLogoMode: null,
        BladeOneTimeFullChargeEnabled: false,
        BladeKeyboardEffect: BladeLighting2023Effect.Wave);

    // 11 admitted rows, and Max Fan is one of them because 029F owns that switch.
    var summary = DeviceCapabilitySummaryCalculator.Calculate(descriptor, telemetry);

    Assert.Equal(new DeviceCapabilitySummary(11, 11), summary);
}

private static string BuiltInManifestJson(string family)
    {
        var manifest = RazerDeviceRegistry.BuiltIn.Manifests.FirstOrDefault(item =>
            item.Id == family) ??
            RazerDeviceRegistry.BuiltIn.Manifests.Single(item =>
                item.ProtocolFamily == family);
        var resourceName = typeof(RazerDeviceRegistry).Assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith($"{manifest.SourceName}", StringComparison.Ordinal));
        using var stream = typeof(RazerDeviceRegistry).Assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
