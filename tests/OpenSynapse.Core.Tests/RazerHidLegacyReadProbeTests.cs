using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerHidLegacyReadProbeTests
{
    [Theory]
    [InlineData(0x0016, DeviceCategory.Mouse, 3500)]
    [InlineData(0x0014, DeviceCategory.Mouse, 4000)]
    [InlineData(0x0021, DeviceCategory.Mouse, 5600)]
    [InlineData(0x0033, DeviceCategory.Mouse, 8200)]
    [InlineData(0x003C, DeviceCategory.Mouse, 1800)]
    [InlineData(0x0007, DeviceCategory.Mouse, null)]
    [InlineData(0x0029, DeviceCategory.Mouse, null)]
    [InlineData(0x002A, DeviceCategory.Mouse, null)]
    [InlineData(0x0031, DeviceCategory.Mouse, null)]
    [InlineData(0x0035, DeviceCategory.Mouse, null)]
    [InlineData(0x0049, DeviceCategory.Mouse, null)]
    [InlineData(0x0056, DeviceCategory.Mouse, null)]
    [InlineData(0x0119, DeviceCategory.Keyboard, null)]
    [InlineData(0x011F, DeviceCategory.Keyboard, null)]
    [InlineData(0x002B, DeviceCategory.Keyboard, null)]
    [InlineData(0x0109, DeviceCategory.Keyboard, null)]
    [InlineData(0x010B, DeviceCategory.Keyboard, null)]
    [InlineData(0x0110, DeviceCategory.Keyboard, null)]
    [InlineData(0x0114, DeviceCategory.Keyboard, null)]
    [InlineData(0x011E, DeviceCategory.Keyboard, null)]
    [InlineData(0x020D, DeviceCategory.Keyboard, null)]
    [InlineData(0x020E, DeviceCategory.Keyboard, null)]
    public void LegacyProbeIsReadOnlyAndKeepsTheSiblingPageShape(
        ushort productId, DeviceCategory category, int? maximumDpi)
    {
        var probe = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, productId);

        Assert.NotNull(probe);
        Assert.Equal(category, probe!.Category);
        Assert.Equal(maximumDpi, probe.MaximumDpi);
        Assert.Null(probe.MatrixDimensions);
        Assert.Empty(probe.PollingRates);
        Assert.All(probe.Transactions.Keys, builder =>
            Assert.Contains("_get_", builder, StringComparison.Ordinal));
    }

    [Fact]
    public void LegacyMouseProbeExposesOnlyReadCapabilities()
    {
        var probe = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0016)!;
        var capabilities = RazerHidDeviceService.GetCapabilities(probe);

        Assert.Contains(RazerHidBackendCapability.FirmwareRead, capabilities);
        Assert.Contains(RazerHidBackendCapability.DpiRead, capabilities);
        Assert.Contains(RazerHidBackendCapability.PollingRateRead, capabilities);
        Assert.Contains(RazerHidBackendCapability.BrightnessRead, capabilities);
        Assert.DoesNotContain(RazerHidBackendCapability.DpiWrite, capabilities);
        Assert.DoesNotContain(RazerHidBackendCapability.PollingRateWrite, capabilities);
        Assert.DoesNotContain(RazerHidBackendCapability.BrightnessWrite, capabilities);
        Assert.DoesNotContain(RazerHidBackendCapability.LedStateWrite, capabilities);
        Assert.DoesNotContain(RazerHidBackendCapability.MatrixFrameWrite, capabilities);
    }

    [Fact]
    public void LegacyKeyboardProbeExposesInfoAndBrightnessOnly()
    {
        var probe = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0119)!;
        var capabilities = RazerHidDeviceService.GetCapabilities(probe);

        Assert.Contains(RazerHidBackendCapability.FirmwareRead, capabilities);
        Assert.Contains(RazerHidBackendCapability.BrightnessRead, capabilities);
        Assert.DoesNotContain(RazerHidBackendCapability.DpiRead, capabilities);
        Assert.DoesNotContain(RazerHidBackendCapability.PollingRateRead, capabilities);
    }

    [Fact]
    public void LegacyFramesUseTheP1ClassAndCommandBytes()
    {
        var mouse = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0016)!;

        var firmware = RazerHidStandardProtocol.GetFirmware(mouse).Report;
        Assert.Equal((byte)0x04, firmware[6]);
        Assert.Equal((byte)0x00, firmware[7]);
        Assert.Equal((byte)0x87, firmware[8]);

        var serial = RazerHidStandardProtocol.GetSerial(mouse).Report;
        Assert.Equal((byte)0x16, serial[6]);
        Assert.Equal((byte)0x00, serial[7]);
        Assert.Equal((byte)0x82, serial[8]);

        var dpi = RazerHidMouseProtocol.GetDpi(mouse).Report;
        Assert.Equal((byte)0x03, dpi[6]);
        Assert.Equal((byte)0x04, dpi[7]);
        Assert.Equal((byte)0x81, dpi[8]);

        var polling = RazerHidMouseProtocol.GetPollingRate(mouse).Report;
        Assert.Equal((byte)0x01, polling[6]);
        Assert.Equal((byte)0x00, polling[7]);
        Assert.Equal((byte)0x85, polling[8]);

        var brightness = RazerHidLightingProtocol
            .GetBrightness(mouse, mouse.DefaultStorage, mouse.DefaultLedZone).Report;
        Assert.Equal((byte)0x03, brightness[7]);
        Assert.Equal((byte)0x83, brightness[8]);
        Assert.Equal(new byte[] { 0x01, 0x00 }, brightness[9..11]);
    }

    [Fact]
    public void MouseDockDeclaredBrightnessBuilderIsUnbuildable()
    {
        var definition = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x00A4)!;

        Assert.Throws<NotSupportedException>(() =>
            RazerHidLightingProtocol.GetBrightness(definition, null, null));
    }

    [Fact]
    public void Blade02B7IsOwnedByTheManifestRegistry()
    {
        Assert.NotNull(RazerDeviceRegistry.BuiltIn.Find(0x1532, 0x02B7));
        Assert.Null(RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x02B7));
    }

    [Theory]
    [InlineData(0x025C)]
    [InlineData(0x0088)]
    [InlineData(0x00A4)]
    public void SafeProbesSurviveADeclaredButUnbuildableLightingBuilder(ushort productId)
    {
        var definition = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, productId);
        Assert.NotNull(definition);
        var method = typeof(RazerHidDeviceService).GetMethod(
            "CreateSafeProbes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var probes = (IReadOnlyList<RazerHidRequest>)method!.Invoke(null, [definition!])!;

        Assert.NotEmpty(probes);
        Assert.Contains(probes, probe => probe.BuilderName == "razer_chroma_standard_get_firmware_version");
    }

    [Fact]
    public void P1DpiDecodesWithTheLegacyScale()
    {
        var response = new byte[RazerFeatureReport.Length];
        response[1] = 0x02;
        response[6] = 0x02;
        response[7] = 0x04;
        response[8] = 0x81;
        response[9] = 60;
        response[10] = 60;
        response[89] = RazerFeatureReport.CalculateCrc(response);

        var dpi = RazerHidStandardProtocol.ParseP1Dpi(response);

        Assert.Equal((1600, 1600), dpi);
    }

    public static IEnumerable<object[]> LegacyProbeCases()
    {
        yield return [(ushort)0x0016, true];
        yield return [(ushort)0x0014, true];
        yield return [(ushort)0x0021, true];
        yield return [(ushort)0x0033, true];
        yield return [(ushort)0x003C, true];
        yield return [(ushort)0x0007, true];
        yield return [(ushort)0x0029, true];
        yield return [(ushort)0x002A, true];
        yield return [(ushort)0x0031, true];
        yield return [(ushort)0x0035, true];
        yield return [(ushort)0x0049, true];
        yield return [(ushort)0x0056, true];
        yield return [(ushort)0x0119, false];
        yield return [(ushort)0x011F, false];
        yield return [(ushort)0x002B, false];
        yield return [(ushort)0x0109, false];
        yield return [(ushort)0x010B, false];
        yield return [(ushort)0x0110, false];
        yield return [(ushort)0x0114, false];
        yield return [(ushort)0x011E, false];
        yield return [(ushort)0x020D, false];
        yield return [(ushort)0x020E, false];
    }

    public static IEnumerable<object[]> LegacyMouseIds()
    {
        yield return [(ushort)0x0016];
        yield return [(ushort)0x0014];
        yield return [(ushort)0x0021];
        yield return [(ushort)0x0033];
        yield return [(ushort)0x003C];
        yield return [(ushort)0x0007];
        yield return [(ushort)0x0029];
        yield return [(ushort)0x002A];
        yield return [(ushort)0x0031];
        yield return [(ushort)0x0035];
        yield return [(ushort)0x0049];
        yield return [(ushort)0x0056];
    }

    public static IEnumerable<object[]> LegacyKeyboardIds()
    {
        yield return [(ushort)0x0119];
        yield return [(ushort)0x011F];
        yield return [(ushort)0x002B];
        yield return [(ushort)0x0109];
        yield return [(ushort)0x010B];
        yield return [(ushort)0x0110];
        yield return [(ushort)0x0114];
        yield return [(ushort)0x011E];
        yield return [(ushort)0x020D];
        yield return [(ushort)0x020E];
    }

    [Theory]
    [MemberData(nameof(LegacyProbeCases))]
    public void LegacyProbesDeclareOnlyTheByteExactReadBuilders(ushort productId, bool isMouse)
    {
        var probe = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, productId);
        Assert.NotNull(probe);

        string[] expected = isMouse
            ? [
                "razer_chroma_misc_get_active_profile",
                "razer_chroma_misc_get_dpi_xy_byte",
                "razer_chroma_misc_get_polling_rate",
                "razer_chroma_standard_get_firmware_version",
                "razer_chroma_standard_get_led_brightness",
                "razer_chroma_standard_get_serial",
            ]
            : [
                "razer_chroma_standard_get_firmware_version",
                "razer_chroma_standard_get_led_brightness",
                "razer_chroma_standard_get_serial",
            ];

        Assert.Equal(expected, probe!.Transactions.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray());
        Assert.All(probe.Transactions.Values, transaction => Assert.Equal((byte)0xFF, transaction));
        Assert.Empty(probe.CommandSequences);
        Assert.Equal(
            new[] { "razer_chroma_standard_get_led_brightness" },
            probe.BuilderArgumentPrefixes.Keys.ToArray());
        Assert.Equal(isMouse, probe.UsesP1DpiScale);
        Assert.Equal(RazerHidStorage.Persistent, probe.DefaultStorage);
        Assert.DoesNotContain("razer_chroma_misc_get_dpi_xy", probe.Transactions.Keys);
        Assert.DoesNotContain("razer_chroma_misc_get_dpi_stages", probe.Transactions.Keys);
        Assert.DoesNotContain("razer_chroma_extended_matrix_get_brightness", probe.Transactions.Keys);
        Assert.DoesNotContain("razer_chroma_misc_get_blade_brightness", probe.Transactions.Keys);
    }

    [Theory]
    [MemberData(nameof(LegacyProbeCases))]
    public void LegacySafeProbesKeepTheReadOnlyProbeOrder(ushort productId, bool isMouse)
    {
        var definition = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, productId);
        Assert.NotNull(definition);

        var probes = CreateSafeProbes(definition!);
        string[] expected = isMouse
            ? [
                "razer_chroma_standard_get_firmware_version",
                "razer_chroma_standard_get_firmware_version_fallback",
                "razer_chroma_standard_get_serial",
                "razer_chroma_misc_get_dpi_xy_byte",
                "razer_chroma_misc_get_polling_rate",
                "razer_chroma_standard_get_led_brightness",
            ]
            : [
                "razer_chroma_standard_get_firmware_version",
                "razer_chroma_standard_get_firmware_version_fallback",
                "razer_chroma_standard_get_serial",
                "razer_chroma_standard_get_led_brightness",
            ];

        Assert.Equal(expected, probes.Select(probe => probe.BuilderName).ToArray());
    }

    [Theory]
    [MemberData(nameof(LegacyMouseIds))]
    public void LegacyMouseFramesMatchTheP1ReadTable(ushort productId)
    {
        var mouse = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, productId);
        Assert.NotNull(mouse);
        var device = mouse!;

        var dockTypeZero = productId is 0x0021 or 0x0031 or 0x0033;
        Assert.Equal("razer_chroma_misc_get_dpi_xy_byte", RazerHidMouseProtocol.GetDpi(device).BuilderName);
        AssertFrame(RazerHidStandardProtocol.GetFirmware(device), 0x04, 0x00,
            dockTypeZero ? (byte)0x93 : (byte)0x87);
        AssertFrame(RazerHidStandardProtocol.GetFirmwareFallback(device), 0x02, 0x00,
            dockTypeZero ? (byte)0x91 : (byte)0x81);
        AssertFrame(RazerHidStandardProtocol.GetSerial(device), 0x16, 0x00,
            dockTypeZero ? (byte)0x92 : (byte)0x82);
        AssertFrame(RazerHidMouseProtocol.GetDpi(device), 0x03, 0x04, 0x81);
        AssertFrame(RazerHidMouseProtocol.GetPollingRate(device), 0x01, 0x00, 0x85);
        AssertFrame(
            RazerHidLightingProtocol.GetBrightness(device, device.DefaultStorage, device.DefaultLedZone),
            0x03, 0x03, 0x83, 0x01, 0x00);
        var activeProfile = RazerHidMouseProtocol.GetActiveProfile(device);
        Assert.NotNull(activeProfile);
        AssertFrame(activeProfile!, 0x01, 0x05, 0x84);
    }

    [Theory]
    [InlineData(0x0021)]
    [InlineData(0x0031)]
    [InlineData(0x0033)]
    public void LegacyDockTypeZeroMiceUseTheDockZeroInfoCommands(ushort productId)
    {
        var mouse = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, productId);
        Assert.NotNull(mouse);

        AssertFrame(RazerHidStandardProtocol.GetFirmware(mouse!), 0x04, 0x00, 0x93);
        AssertFrame(RazerHidStandardProtocol.GetFirmwareFallback(mouse!), 0x02, 0x00, 0x91);
        AssertFrame(RazerHidStandardProtocol.GetSerial(mouse!), 0x16, 0x00, 0x92);
    }

    [Theory]
    [MemberData(nameof(LegacyKeyboardIds))]
    public void LegacyKeyboardFramesMatchTheP1ReadTable(ushort productId)
    {
        var keyboard = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, productId);
        Assert.NotNull(keyboard);
        var device = keyboard!;

        AssertFrame(RazerHidStandardProtocol.GetFirmware(device), 0x04, 0x00, 0x87);
        AssertFrame(RazerHidStandardProtocol.GetFirmwareFallback(device), 0x02, 0x00, 0x81);
        AssertFrame(RazerHidStandardProtocol.GetSerial(device), 0x16, 0x00, 0x82);
        AssertFrame(
            RazerHidLightingProtocol.GetBrightness(device, device.DefaultStorage, device.DefaultLedZone),
            0x03, 0x03, 0x83, 0x01, 0x05);
    }

    [Theory]
    [MemberData(nameof(LegacyProbeCases))]
    public void LegacyProbesPublishNoWriteCapabilities(ushort productId, bool isMouse)
    {
        var probe = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, productId);
        Assert.NotNull(probe);

        var capabilities = RazerHidDeviceService.GetCapabilities(probe!);
        Assert.Contains(RazerHidBackendCapability.FirmwareRead, capabilities);
        Assert.Contains(RazerHidBackendCapability.BrightnessRead, capabilities);
        Assert.DoesNotContain(capabilities, capability =>
            capability.ToString().EndsWith("Write", StringComparison.Ordinal));
        if (isMouse)
        {
            Assert.Contains(RazerHidBackendCapability.DpiRead, capabilities);
            Assert.Contains(RazerHidBackendCapability.PollingRateRead, capabilities);
        }
        else
        {
            Assert.DoesNotContain(RazerHidBackendCapability.DpiRead, capabilities);
            Assert.DoesNotContain(RazerHidBackendCapability.PollingRateRead, capabilities);
        }
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(60, 1600)]
    [InlineData(255, 6475)]
    public void P1DpiDecodesEachAxisWithTheLegacyScale(byte encoded, int expected)
    {
        var response = new byte[RazerFeatureReport.Length];
        response[1] = 0x02;
        response[6] = 0x03;
        response[7] = 0x04;
        response[8] = 0x81;
        response[9] = encoded;
        response[10] = encoded;
        response[89] = RazerFeatureReport.CalculateCrc(response);

        Assert.Equal((expected, expected), RazerHidStandardProtocol.ParseP1Dpi(response));
    }

    [Fact]
    public void LegacyFirmwareReadAcceptsTheFourByteInfoReply()
    {
        var mouse = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0016)!;
        var request = RazerHidStandardProtocol.GetFirmware(mouse).Report;
        Assert.Equal((byte)0x04, request[6]);

        var response = (byte[])request.Clone();
        response[1] = 0x02;
        response[9] = 1;
        response[10] = 2;
        response[11] = 3;
        response[12] = 4;
        response[89] = RazerFeatureReport.CalculateCrc(response);

        Assert.Equal(new Version(1, 2), RazerHidStandardProtocol.ParseFirmware(response));
    }

    [Theory]
    [InlineData(0x0116, DeviceCategory.Keyboard)]
    [InlineData(0x011D, DeviceCategory.Laptop)]
    public void BladeInfoProbesExposeOnlyFirmware(ushort productId, DeviceCategory category)
    {
        var probe = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, productId);

        Assert.NotNull(probe);
        Assert.Equal(category, probe!.Category);
        Assert.Equal(
            new[] { "razer_chroma_standard_get_firmware_version" },
            probe.Transactions.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray());
        Assert.All(probe.Transactions.Values, transaction => Assert.Equal((byte)0xFF, transaction));
        Assert.Empty(probe.CommandSequences);
        Assert.Empty(probe.BuilderArgumentPrefixes);
        AssertFrame(RazerHidStandardProtocol.GetFirmware(probe), 0x04, 0x00, 0x87);
        AssertFrame(RazerHidStandardProtocol.GetFirmwareFallback(probe), 0x02, 0x00, 0x81);

        var capabilities = RazerHidDeviceService.GetCapabilities(probe);
        Assert.Contains(RazerHidBackendCapability.FirmwareRead, capabilities);
        Assert.DoesNotContain(RazerHidBackendCapability.BrightnessRead, capabilities);
        Assert.DoesNotContain(RazerHidBackendCapability.DpiRead, capabilities);
        Assert.DoesNotContain(capabilities, capability =>
            capability.ToString().EndsWith("Write", StringComparison.Ordinal));

        Assert.Equal(
            new[]
            {
                "razer_chroma_standard_get_firmware_version",
                "razer_chroma_standard_get_firmware_version_fallback",
            },
            CreateSafeProbes(probe).Select(request => request.BuilderName).ToArray());
    }

    [Theory]
    [InlineData(0x002C)]
    [InlineData(0x0501)]
    [InlineData(0x0502)]
    [InlineData(0x0503)]
    [InlineData(0x0504)]
    [InlineData(0x0506)]
    [InlineData(0x0507)]
    [InlineData(0x0508)]
    [InlineData(0x0206)]
    [InlineData(0x0900)]
    public void AudioTurretAndServalStayOutOfTheRazerHidCatalog(ushort productId)
    {
        // These use rzaudiodll/COM, the Turret 91-byte branch or the Onza transport.
        // No evidence maps them to the P1/P2 tables, so the catalog must not claim them.
        Assert.Null(RazerHidDeviceCatalog.BuiltIn.Find(0x1532, productId));
    }

    private static void AssertFrame(
        RazerHidRequest request,
        byte dataSize,
        byte commandClass,
        byte commandId,
        params byte[] arguments)
    {
        var report = request.Report;
        Assert.Equal(RazerFeatureReport.Length, report.Length);
        Assert.Equal((byte)0x00, report[0]);
        Assert.Equal((byte)0xFF, report[2]);
        Assert.Equal(dataSize, report[6]);
        Assert.Equal(commandClass, report[7]);
        Assert.Equal(commandId, report[8]);
        Assert.Equal(arguments, report[9..(9 + arguments.Length)]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(report), report[89]);
    }

    private static IReadOnlyList<RazerHidRequest> CreateSafeProbes(RazerHidDeviceDefinition definition)
    {
        var method = typeof(RazerHidDeviceService).GetMethod(
            "CreateSafeProbes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
        return (IReadOnlyList<RazerHidRequest>)method!.Invoke(null, [definition])!;
    }
}