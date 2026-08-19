using System.Text.Json.Nodes;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Core.Tests;

public sealed class RazerDeviceRegistryTests : IDisposable
{
    private readonly string _externalDirectory = Path.Combine(
        Path.GetTempPath(),
        "OpenSynapse.RegistryTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void LoadsBuiltInBladeAndViperManifests()
    {
        var registry = RazerDeviceRegistry.BuiltIn;

        var blade = Assert.IsType<RazerDeviceManifest>(registry.Find(0x1532, 0x02C6));
        Assert.Equal("blade-710", blade.ProtocolFamily);
        Assert.Equal("Razer Blade 16 2025", blade.DisplayName);
        Assert.Equal((ushort)91, blade.Collection.FeatureReportLength);

        var viper = Assert.IsType<RazerDeviceManifest>(registry.Find(0x1532, 0x00B8));
        Assert.Equal("viper-184", viper.ProtocolFamily);
        Assert.Equal(TimeSpan.FromMilliseconds(60), viper.GetRequiredCapability("battery.get").Wait);
        Assert.Null(registry.Find(0x1532, 0xFFFF));
    }

    [Fact]
    public void LoadsExternalSameFamilyManifestAndPreservesSources()
    {
        Directory.CreateDirectory(_externalDirectory);
        var compatible = ParseBuiltIn("blade-710");
        compatible["id"] = "blade-compatible";
        compatible["displayName"] = "Compatible Blade";
        compatible["productIds"]![0] = "02C7";
        File.WriteAllText(
            Path.Combine(_externalDirectory, "compatible.json"),
            compatible.ToJsonString());

        var result = RazerDeviceRegistry.Load(_externalDirectory);

        Assert.Empty(result.Errors);
        Assert.EndsWith(".blade-710.json", result.Registry.Find(0x1532, 0x02C6)!.SourceName);
        var external = Assert.IsType<RazerDeviceManifest>(result.Registry.Find(0x1532, 0x02C7));
        Assert.Equal("Compatible Blade", external.DisplayName);
        Assert.Equal("compatible.json", external.SourceName);
    }

    [Fact]
    public void RejectsExternalConflictWithoutReplacingBuiltInOrValidSibling()
    {
        Directory.CreateDirectory(_externalDirectory);
        var duplicate = ParseBuiltIn("blade-710");
        duplicate["id"] = "duplicate-blade";
        File.WriteAllText(
            Path.Combine(_externalDirectory, "a-duplicate.json"),
            duplicate.ToJsonString());
        var duplicateId = ParseBuiltIn("blade-710");
        duplicateId["productIds"]![0] = "02C8";
        File.WriteAllText(
            Path.Combine(_externalDirectory, "b-duplicate-id.json"),
            duplicateId.ToJsonString());
        var compatible = ParseBuiltIn("blade-710");
        compatible["id"] = "blade-compatible";
        compatible["productIds"]![0] = "02C7";
        File.WriteAllText(
            Path.Combine(_externalDirectory, "c-compatible.json"),
            compatible.ToJsonString());

        var result = RazerDeviceRegistry.Load(_externalDirectory);

        Assert.Collection(
            result.Errors,
            error =>
            {
                Assert.StartsWith("a-duplicate.json：", error);
                Assert.Contains("1532:02C6", error);
            },
            error =>
            {
                Assert.StartsWith("b-duplicate-id.json：", error);
                Assert.Contains("'blade-710'", error);
            });
        Assert.All(result.Errors, error => Assert.DoesNotContain(_externalDirectory, error));
        Assert.Equal("blade-710", result.Registry.Find(0x1532, 0x02C6)!.Id);
        Assert.Equal("blade-compatible", result.Registry.Find(0x1532, 0x02C7)!.Id);
        Assert.Null(result.Registry.Find(0x1532, 0x02C8));
    }

    [Theory]
    [InlineData("transactionId", "20")]
    [InlineData("transactionId", "00")]
    [InlineData("waitMilliseconds", 1)]
    public void RejectsExternalReportHeaderOrRelaxedWait(string property, object value)
    {
        Directory.CreateDirectory(_externalDirectory);
        var document = ParseBuiltIn("viper-184");
        document["id"] = "viper-compatible";
        document["productIds"]![0] = "00B9";
        document["capabilities"]!["battery.get"]![property] = JsonValue.Create(value);
        File.WriteAllText(
            Path.Combine(_externalDirectory, "relaxed.json"),
            document.ToJsonString());

        var result = RazerDeviceRegistry.Load(_externalDirectory);

        Assert.StartsWith("relaxed.json：", Assert.Single(result.Errors));
        Assert.Null(result.Registry.Find(0x1532, 0x00B9));
    }

    [Fact]
    public void KeepsBuiltInsAndValidSiblingWhenExternalFilesAreInvalid()
    {
        Directory.CreateDirectory(_externalDirectory);
        File.WriteAllText(Path.Combine(_externalDirectory, "a-malformed.json"), "{");
        var unknownField = ParseBuiltIn("viper-184");
        unknownField["verified"] = true;
        File.WriteAllText(
            Path.Combine(_externalDirectory, "b-unknown-field.json"),
            unknownField.ToJsonString());
        var missingCapability = ParseBuiltIn("viper-184");
        missingCapability["capabilities"]!.AsObject().Remove("current-dpi.set");
        File.WriteAllText(
            Path.Combine(_externalDirectory, "c-missing-capability.json"),
            missingCapability.ToJsonString());
        var unknownFamily = ParseBuiltIn("viper-184");
        unknownFamily["protocolFamily"] = "unknown";
        File.WriteAllText(
            Path.Combine(_externalDirectory, "d-unknown-family.json"),
            unknownFamily.ToJsonString());
        var valid = ParseBuiltIn("viper-184");
        valid["id"] = "viper-compatible";
        valid["productIds"]![0] = "00B9";
        File.WriteAllText(
            Path.Combine(_externalDirectory, "e-valid.json"),
            valid.ToJsonString());

        var result = RazerDeviceRegistry.Load(_externalDirectory);

        Assert.Equal(4, result.Errors.Count);
        Assert.Equal(
            new[]
            {
                "a-malformed.json",
                "b-unknown-field.json",
                "c-missing-capability.json",
                "d-unknown-family.json",
            },
            result.Errors.Select(error => error[..error.IndexOf('：')]).ToArray());
        Assert.NotNull(result.Registry.Find(0x1532, 0x02C6));
        Assert.Equal("viper-compatible", result.Registry.Find(0x1532, 0x00B9)!.Id);
    }

    [Fact]
    public void RejectsOversizedExternalFileWithoutReadingItAsJson()
    {
        Directory.CreateDirectory(_externalDirectory);
        File.WriteAllText(
            Path.Combine(_externalDirectory, "oversized.json"),
            new string(' ', 65_537));

        var result = RazerDeviceRegistry.Load(_externalDirectory);

        Assert.Equal(
            "oversized.json：文件超过 65536 字节。",
            Assert.Single(result.Errors));
        Assert.NotNull(result.Registry.Find(0x1532, 0x02C6));
    }

    [Fact]
    public void RejectsAllExternalFilesWhenFileCountExceedsLimit()
    {
        Directory.CreateDirectory(_externalDirectory);
        for (var index = 0; index < 65; index++)
        {
            File.WriteAllText(Path.Combine(_externalDirectory, $"{index:D2}.json"), "{}");
        }

        var result = RazerDeviceRegistry.Load(_externalDirectory);

        Assert.Equal(
            "外部 manifest 超过 64 个，已拒绝全部外部配置。",
            Assert.Single(result.Errors));
        Assert.Equal(2, result.Registry.Manifests.Count);
    }

    [Fact]
    public void MissingExternalDirectoryLoadsBuiltInsWithoutErrors()
    {
        var result = RazerDeviceRegistry.Load(_externalDirectory);

        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Registry.Manifests.Count);
    }

    [Fact]
    public void CreatesByteIdenticalRepresentativeRequests()
    {
        var registry = RazerDeviceRegistry.BuiltIn;
        var blade = registry.Find(0x1532, 0x02C6)!;
        var viper = registry.Find(0x1532, 0x00B8)!;

        Assert.Equal(
            RazerFeatureReport.CreateRequest(0xFF, 0x02, 0x0E, 0x84, new byte[] { 0x01, 0x00 }),
            blade.GetRequiredCapability("keyboard-brightness.get").CreateRequest());
        Assert.Equal(
            RazerFeatureReport.CreateRequest(0x1F, 0x01, 0x07, 0x92, new byte[] { 0x00 }),
            blade.GetRequiredCapability("charge-limit.get").CreateRequest());
        Assert.True(blade.GetRequiredCapability("charge-limit.get").AllowRemainingPacketsMismatch);
        Assert.Equal(
            BladeSynapsePolicyProtocol.CreateGetGameModeRequest(),
            blade.GetRequiredCapability("gaming-mode.get").CreateRequest());
        Assert.Equal(
            BladeProduct710Protocol.CreateGetNativeDisplayModeRequest(),
            blade.GetRequiredCapability("native-display-mode.get").CreateRequest());
        Assert.True(blade.GetRequiredCapability("native-display-mode.get").AllowRemainingPacketsMismatch);
        Assert.Equal(
            BladeProduct710Protocol.CreateSetNativeDisplayModeRequest(BladeNativeDisplayMode.Fhd),
            blade.GetRequiredCapability("native-display-mode.set").CreateRequest(new byte[] { 0x01 }));
        Assert.Equal(
            ViperProduct184Protocol.CreateGetDpiRequest(),
            viper.GetRequiredCapability("current-dpi.get").CreateRequest());

        var variableRequest = viper.GetRequiredCapability("dpi-stages.set")
            .CreateRequest(new byte[10], dataSize: 10);
        Assert.Equal((byte)0x0A, variableRequest[6]);
        Assert.Equal((byte)0x04, variableRequest[7]);
        Assert.Equal((byte)0x06, variableRequest[8]);
    }

    [Fact]
    public void BuiltInCapabilitiesMatchEveryProductionHeader()
    {
        var registry = RazerDeviceRegistry.BuiltIn;
        var blade = registry.Find(0x1532, 0x02C6)!;
        var viper = registry.Find(0x1532, 0x00B8)!;
        AssertHeaders(blade, new Dictionary<string, (byte Tx, byte Size, byte Class, byte Id)>
        {
            ["keyboard-brightness.get"] = (0xFF, 0x02, 0x0E, 0x84),
            ["keyboard-brightness.set"] = (0xFF, 0x02, 0x0E, 0x04),
            ["thermal-state.get"] = (0x1F, 0x04, 0x0D, 0x82),
            ["thermal-state.set"] = (0x1F, 0x04, 0x0D, 0x02),
            ["fan-target.get"] = (0x1F, 0x03, 0x0D, 0x81),
            ["current-fan-rpm.get"] = (0x1F, 0x03, 0x0D, 0x88),
            ["advanced-fan-mode.get"] = (0x1F, 0x03, 0x0D, 0x87),
            ["boost.get"] = (0x1F, 0x03, 0x0D, 0x87),
            ["boost.set"] = (0x1F, 0x03, 0x0D, 0x07),
            ["charge-limit.get"] = (0x1F, 0x01, 0x07, 0x92),
            ["charge-limit.set"] = (0x1F, 0x01, 0x07, 0x12),
            ["max-fan.get"] = (0x1F, 0x01, 0x07, 0x8F),
            ["max-fan.set"] = (0x1F, 0x01, 0x07, 0x0F),
            ["gaming-mode.get"] = (0x00, 0x04, 0x00, 0x88),
            ["gaming-mode.set"] = (0x00, 0x04, 0x00, 0x08),
            ["gaming-mode-led.set"] = (0x00, 0x03, 0x03, 0x00),
            ["fn-key.set"] = (0x00, 0x02, 0x02, 0x06),
            ["startup-animation.get"] = (0x1F, 0x01, 0x0F, 0x98),
            ["startup-animation.set"] = (0x1F, 0x02, 0x0F, 0x18),
            ["native-display-mode.get"] = (0x1F, 0x01, 0x0D, 0x8E),
            ["native-display-mode.set"] = (0x1F, 0x01, 0x0D, 0x0E),
            ["sku-hardware-configuration.get"] = (0x1F, 0x01, 0x0D, 0x8F),
            ["logo-power.get"] = (0xFF, 0x03, 0x03, 0x80),
            ["logo-power.set"] = (0xFF, 0x03, 0x03, 0x00),
            ["logo-mode.get"] = (0xFF, 0x03, 0x03, 0x82),
            ["logo-mode.set"] = (0xFF, 0x03, 0x03, 0x02),
            ["logo-effect.set"] = (0x00, 0x03, 0x03, 0x02),
            ["logo-state.set"] = (0x00, 0x03, 0x03, 0x00),
        });
        AssertHeaders(viper, new Dictionary<string, (byte Tx, byte Size, byte Class, byte Id)>
        {
            ["battery.get"] = (0x1F, 0x02, 0x07, 0x80),
            ["polling-rate.get"] = (0x1F, 0x01, 0x00, 0x85),
            ["polling-rate.set"] = (0x1F, 0x01, 0x00, 0x05),
            ["current-dpi.get"] = (0x1F, 0x07, 0x04, 0x85),
            ["current-dpi.set"] = (0x1F, 0x07, 0x04, 0x05),
            ["idle-timeout.get"] = (0x1F, 0x02, 0x07, 0x83),
            ["idle-timeout.set"] = (0x1F, 0x02, 0x07, 0x03),
            ["dpi-stages.get"] = (0x1F, 0x26, 0x04, 0x86),
            ["dpi-stages.set"] = (0x1F, 0x26, 0x04, 0x06),
            ["low-battery-threshold.get"] = (0x1F, 0x01, 0x07, 0x81),
            ["obm-maximum-profiles.get"] = (0x1F, 0x01, 0x05, 0x8A),
            ["obm-profile-count.get"] = (0x1F, 0x01, 0x05, 0x80),
            ["obm-profile-ids.get"] = (0x1F, 0x50, 0x05, 0x81),
            ["obm-button-ids.get"] = (0x1F, 0x50, 0x02, 0x84),
            ["obm-assignment.get"] = (0x1F, 0x50, 0x02, 0x8C),
            ["obm-assignment.set"] = (0x1F, 0x50, 0x02, 0x0C),
        });
    }

    [Fact]
    public void RejectsUnknownProperty()
    {
        var document = ParseBuiltIn("viper-184");
        document["verified"] = true;

        Assert.Throws<InvalidOperationException>(() =>
            RazerDeviceRegistry.LoadJson(new[] { document.ToJsonString() }));
    }

    [Fact]
    public void RejectsMalformedOrLowercaseHex()
    {
        var malformed = ParseBuiltIn("viper-184");
        malformed["vendorId"] = "15G2";
        Assert.Throws<InvalidOperationException>(() =>
            RazerDeviceRegistry.LoadJson(new[] { malformed.ToJsonString() }));

        var lowercase = ParseBuiltIn("viper-184");
        lowercase["productIds"]![0] = "00b8";
        Assert.Throws<InvalidOperationException>(() =>
            RazerDeviceRegistry.LoadJson(new[] { lowercase.ToJsonString() }));
    }

    [Fact]
    public void RejectsDuplicateManifestIdAndPid()
    {
        var blade = ReadBuiltIn("blade-710");
        Assert.Throws<InvalidOperationException>(() =>
            RazerDeviceRegistry.LoadJson(new[] { blade, blade }));

        var second = ParseBuiltIn("blade-710");
        second["id"] = "other-blade";
        Assert.Throws<InvalidOperationException>(() =>
            RazerDeviceRegistry.LoadJson(new[] { blade, second.ToJsonString() }));
    }

    [Fact]
    public void RejectsDuplicateCapabilityProperty()
    {
        var json = ReadBuiltIn("viper-184");
        const string capability = "\"battery.get\": { \"transactionId\": \"1F\", \"dataSize\": \"02\", \"commandClass\": \"07\", \"commandId\": \"80\", \"arguments\": \"\" },";
        json = json.Replace("\"capabilities\": {", $"\"capabilities\": {{ {capability}", StringComparison.Ordinal);

        Assert.Throws<InvalidOperationException>(() =>
            RazerDeviceRegistry.LoadJson(new[] { json }));
    }

    [Fact]
    public void RejectsUnknownFamilyCapabilityAndMissingRequiredCapability()
    {
        var unknownFamily = ParseBuiltIn("viper-184");
        unknownFamily["protocolFamily"] = "unknown";
        Assert.Throws<InvalidOperationException>(() =>
            RazerDeviceRegistry.LoadJson(new[] { unknownFamily.ToJsonString() }));

        var inventedCapability = ParseBuiltIn("viper-184");
        inventedCapability["capabilities"]!["raw-report.set"] = inventedCapability["capabilities"]!["battery.get"]!.DeepClone();
        Assert.Throws<InvalidOperationException>(() =>
            RazerDeviceRegistry.LoadJson(new[] { inventedCapability.ToJsonString() }));

        var missing = ParseBuiltIn("viper-184");
        missing["capabilities"]!.AsObject().Remove("current-dpi.set");
        Assert.Throws<InvalidOperationException>(() =>
            RazerDeviceRegistry.LoadJson(new[] { missing.ToJsonString() }));
    }

    [Fact]
    public void RejectsArgumentsBeyondDataSizeAndUnsupportedReportLength()
    {
        var oversized = ParseBuiltIn("viper-184");
        oversized["capabilities"]!["battery.get"]!["arguments"] = "000000";
        Assert.Throws<InvalidOperationException>(() =>
            RazerDeviceRegistry.LoadJson(new[] { oversized.ToJsonString() }));

        var reportLength = ParseBuiltIn("viper-184");
        reportLength["collection"]!["featureReportLength"] = 90;
        Assert.Throws<InvalidOperationException>(() =>
            RazerDeviceRegistry.LoadJson(new[] { reportLength.ToJsonString() }));
    }

    [Fact]
    public void RejectsUnadmittedRemainingPacketsException()
    {
        var document = ParseBuiltIn("viper-184");
        document["capabilities"]!["battery.get"]!["allowRemainingPacketsMismatch"] = true;

        Assert.Throws<InvalidOperationException>(() =>
            RazerDeviceRegistry.LoadJson(new[] { document.ToJsonString() }));
    }

    [Fact]
    public void RejectsCapabilityRemappedToDifferentCommand()
    {
        var document = ParseBuiltIn("viper-184");
        var battery = document["capabilities"]!["battery.get"]!;
        battery["dataSize"] = "07";
        battery["commandClass"] = "04";
        battery["commandId"] = "05";

        Assert.Throws<InvalidOperationException>(() =>
            RazerDeviceRegistry.LoadJson(new[] { document.ToJsonString() }));
    }

    [Fact]
    public void RejectsCapabilityContractChanges()
    {
        var dataSize = ParseBuiltIn("viper-184");
        dataSize["capabilities"]!["battery.get"]!["dataSize"] = "03";
        Assert.Throws<InvalidOperationException>(() =>
            RazerDeviceRegistry.LoadJson(new[] { dataSize.ToJsonString() }));

        var arguments = ParseBuiltIn("viper-184");
        arguments["capabilities"]!["current-dpi.get"]!["arguments"] = "01";
        Assert.Throws<InvalidOperationException>(() =>
            RazerDeviceRegistry.LoadJson(new[] { arguments.ToJsonString() }));

        var commandClass = ParseBuiltIn("viper-184");
        commandClass["capabilities"]!["battery.get"]!["commandClass"] = "04";
        Assert.Throws<InvalidOperationException>(() =>
            RazerDeviceRegistry.LoadJson(new[] { commandClass.ToJsonString() }));

        var commandId = ParseBuiltIn("viper-184");
        commandId["capabilities"]!["battery.get"]!["commandId"] = "81";
        Assert.Throws<InvalidOperationException>(() =>
            RazerDeviceRegistry.LoadJson(new[] { commandId.ToJsonString() }));

        var requiredException = ParseBuiltIn("blade-710");
        requiredException["capabilities"]!["charge-limit.get"]!["allowRemainingPacketsMismatch"] = false;
        Assert.Throws<InvalidOperationException>(() =>
            RazerDeviceRegistry.LoadJson(new[] { requiredException.ToJsonString() }));
    }

    [Theory]
    [InlineData("productIds")]
    [InlineData("collection")]
    [InlineData("transport")]
    [InlineData("capabilities")]
    public void RejectsExplicitNullRequiredObjects(string property)
    {
        var document = ParseBuiltIn("viper-184");
        document[property] = null;

        Assert.Throws<InvalidOperationException>(() =>
            RazerDeviceRegistry.LoadJson(new[] { document.ToJsonString() }));
    }

    [Fact]
    public void RejectsExplicitNullCapability()
    {
        var document = ParseBuiltIn("viper-184");
        document["capabilities"]!["battery.get"] = null;

        Assert.Throws<InvalidOperationException>(() =>
            RazerDeviceRegistry.LoadJson(new[] { document.ToJsonString() }));
    }

    [Fact]
    public void RejectsDynamicRequestBeyondManifestMaximum()
    {
        var descriptor = RazerDeviceRegistry.BuiltIn.Find(0x1532, 0x00B8)!
            .GetRequiredCapability("dpi-stages.set");

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            descriptor.CreateRequest(new byte[39], dataSize: 39));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            descriptor.CreateRequest(new byte[11], dataSize: 10));
    }

    [Fact]
    public void MatchesOnlyManifestCollection()
    {
        var manifest = RazerDeviceRegistry.BuiltIn.Find(0x1532, 0x02C6)!;

        Assert.True(WindowsHidDiscovery.MatchesCollection(
            manifest.Collection, 0x0001, 0x0002, 91));
        Assert.False(WindowsHidDiscovery.MatchesCollection(
            manifest.Collection, 0x0001, 0x0001, 91));
        Assert.False(WindowsHidDiscovery.MatchesCollection(
            manifest.Collection, 0x0001, 0x0002, 64));
    }

    private static JsonObject ParseBuiltIn(string id) =>
        JsonNode.Parse(ReadBuiltIn(id))!.AsObject();

    private static void AssertHeaders(
        RazerDeviceManifest manifest,
        IReadOnlyDictionary<string, (byte Tx, byte Size, byte Class, byte Id)> expected)
    {
        Assert.Equal(expected.Keys.Order(), manifest.Capabilities.Keys.Order());
        foreach (var (capabilityId, header) in expected)
        {
            var descriptor = manifest.GetRequiredCapability(capabilityId);
            Assert.Equal(header.Tx, descriptor.TransactionId);
            Assert.Equal(header.Size, descriptor.MaximumDataSize);
            Assert.Equal(header.Class, descriptor.CommandClass);
            Assert.Equal(header.Id, descriptor.CommandId);
        }
    }

    private static string ReadBuiltIn(string id)
    {
        var assembly = typeof(RazerDeviceRegistry).Assembly;
        var resourceName = Assert.Single(
            assembly.GetManifestResourceNames(),
            name => name.EndsWith($".{id}.json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public void Dispose()
    {
        if (Directory.Exists(_externalDirectory))
        {
            Directory.Delete(_externalDirectory, recursive: true);
        }
    }
}
