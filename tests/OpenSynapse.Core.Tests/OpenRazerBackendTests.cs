using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class OpenRazerBackendTests
{
    [Fact]
    public void CatalogContainsOnlyThe254NewStandardDevices()
    {
        var catalog = OpenRazerDeviceCatalog.BuiltIn;

        Assert.Equal(254, catalog.Devices.Count);
        Assert.Equal(254, catalog.Devices.Select(device => device.ProductId).Distinct().Count());
        Assert.DoesNotContain(catalog.Devices, device => device.ProductId is 0x00B8 or 0x02C6);
        Assert.Null(catalog.Find(0x1234, catalog.Devices[0].ProductId));
        Assert.All(catalog.Devices, device =>
        {
            Assert.NotEmpty(device.DisplayName);
            Assert.Equal(device.SourceCapabilities.Count, device.SourceCapabilities.Distinct(StringComparer.Ordinal).Count());
            Assert.Empty(device.Transactions.Keys.Intersect(device.CommandSequences.Keys, StringComparer.Ordinal));
        });
    }

    [Theory]
    [InlineData(0x0F1A)]
    [InlineData(0x0F21)]
    public void ConditionalDockWaveUsesTheResolved1FTransaction(ushort productId)
    {
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, productId)!;

        Assert.Equal(0x1F, device.GetRequiredTransaction("razer_chroma_extended_matrix_effect_wave"));
        var request = OpenRazerLightingProtocol.CreateEffect(device,
            new OpenRazerLightingSettings(OpenRazerLightingEffect.Wave));
        Assert.Equal(0x1F, request.Report[2]);
        Assert.Equal(0x0F, request.Report[7]);
        Assert.Equal(0x02, request.Report[8]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(request.Report), request.Report[89]);
    }

    [Theory]
    [InlineData(0x0091, 0xFF)]
    [InlineData(0x00B3, 0xFF)]
    [InlineData(0x009E, 0x1F)]
    [InlineData(0x009F, 0x1F)]
    [InlineData(0x00B2, 0x1F)]
    [InlineData(0x00BE, 0x1F)]
    [InlineData(0x00BF, 0x1F)]
    [InlineData(0x00C1, 0x1F)]
    public async Task HighPollingRateSetterPreservesBothWrites(ushort productId, byte secondTransaction)
    {
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, productId)!;
        var transport = new RecordingTransport();
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);
        var connection = new OpenRazerDeviceConnection(device, "test-path", "test-device", OpenRazerEndpointState.Resolved,
            new HashSet<OpenRazerBackendCapability> { OpenRazerBackendCapability.PollingRateWrite },
            new Dictionary<OpenRazerLedZone, OpenRazerLightingZoneCapabilities>(), null);

        await service.SetPollingRateAsync(connection, 1000);

        Assert.Equal(2, transport.Requests.Count);
        Assert.Equal((0x1F, 0x00, 0x08),
            (transport.Requests[0][2], transport.Requests[0][RazerFeatureReport.ArgumentsOffset], transport.Requests[0][RazerFeatureReport.ArgumentsOffset + 1]));
        Assert.Equal((secondTransaction, 0x01, 0x08),
            (transport.Requests[1][2], transport.Requests[1][RazerFeatureReport.ArgumentsOffset], transport.Requests[1][RazerFeatureReport.ArgumentsOffset + 1]));
        Assert.DoesNotContain("razer_chroma_misc_set_polling_rate2", device.Transactions.Keys);
    }

    [Fact]
    public async Task UnresolvedEndpointRejectsWritesBeforeTransport()
    {
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x0203)!;
        var transport = new RecordingTransport();
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);
        var connection = new OpenRazerDeviceConnection(device, null, "test-device",
            OpenRazerEndpointState.RecognizedButUnresolved,
            new HashSet<OpenRazerBackendCapability>(),
            new Dictionary<OpenRazerLedZone, OpenRazerLightingZoneCapabilities>(), "unresolved");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SetBrightnessAsync(connection, 100));
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public void MissingTransactionFailsClosed()
    {
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x0013)!;

        Assert.Throws<NotSupportedException>(() =>
            device.GetRequiredTransaction("razer_chroma_misc_get_polling_rate"));
    }

    [Fact]
    public void WideStandardMatrixPreservesBytesBeyondDeclaredSize()
    {
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x0225)!;
        var colors = Enumerable.Range(0, 25)
            .Select(index => new OpenRazerColor((byte)index, (byte)(index + 1), (byte)(index + 2)))
            .ToArray();

        var request = OpenRazerLightingProtocol.SetCustomRow(device, 0, 0, colors).Report;

        Assert.Equal(0x46, request[6]);
        Assert.Equal(0x18, request[RazerFeatureReport.ArgumentsOffset + 3]);
        Assert.Equal(colors[^1].Blue, request[RazerFeatureReport.ArgumentsOffset + 78]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(request), request[89]);
    }

    [Theory]
    [InlineData(0x0F1A, 0x1F, 0x00, 0x1F)]
    [InlineData(0x0203, 0xFF, 0xFF, 0xFF)]
    [InlineData(0x0091, 0x00, 0x00, 0x00)]
    public void GenericIdentityTransactionsStayDriverFamilySpecific(
        ushort productId, byte firmware, byte serial, byte mode)
    {
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, productId)!;

        Assert.Equal(firmware, device.GetRequiredTransaction("razer_chroma_standard_get_firmware_version"));
        Assert.Equal(serial, device.GetRequiredTransaction("razer_chroma_standard_get_serial"));
        Assert.Equal(mode, device.GetRequiredTransaction("razer_chroma_standard_get_device_mode"));
    }

    [Theory]
    [InlineData(0x0258, 0x1F)]
    [InlineData(0x008F, 0x3F)]
    public void BatteryTransactionStaysDriverFamilySpecific(ushort productId, byte transaction)
    {
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, productId)!;

        Assert.Equal(transaction, device.GetRequiredTransaction("razer_chroma_misc_get_battery_level"));
    }

    [Fact]
    public void DpiUsesPerDeviceStorageAndLegacyScaling()
    {
        var imperator = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x002F)!;
        var viper8K = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x0091)!;
        var legacy = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x0015)!;

        Assert.Equal(0x01, OpenRazerMouseProtocol.GetDpi(imperator).Report[RazerFeatureReport.ArgumentsOffset]);
        Assert.Equal(0x00, OpenRazerMouseProtocol.GetDpi(viper8K).Report[RazerFeatureReport.ArgumentsOffset]);
        Assert.Equal(0x01, OpenRazerMouseProtocol.SetDpi(imperator, 1600, 1600).Report[RazerFeatureReport.ArgumentsOffset]);
        Assert.Equal(0x01, OpenRazerMouseProtocol.SetDpi(viper8K, 1600, 1600).Report[RazerFeatureReport.ArgumentsOffset]);

        var legacyRequest = OpenRazerMouseProtocol.SetDpi(legacy, 1600, 1600).Report;
        Assert.Equal(60, legacyRequest[RazerFeatureReport.ArgumentsOffset]);
        legacyRequest[1] = 0x02;
        legacyRequest[RazerFeatureReport.ArgumentsOffset] = 60;
        legacyRequest[RazerFeatureReport.ArgumentsOffset + 1] = 60;
        Assert.Equal((1588, 1588), OpenRazerMouseProtocol.ParseDpi(legacyRequest, byteEncoding: true));
    }

    [Theory]
    [InlineData(5, 0x0C)]
    [InlineData(15, 0x26)]
    [InlineData(25, 0x3F)]
    public void LowBatteryThresholdMatchesOpenRazerScaling(int percent, byte raw)
    {
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x008F)!;
        var report = OpenRazerMouseProtocol.SetLowBatteryThreshold(device, percent).Report;

        Assert.Equal(raw, report[RazerFeatureReport.ArgumentsOffset]);
        report[1] = 0x02;
        report[RazerFeatureReport.ArgumentsOffset] = raw;
        Assert.Equal(percent, OpenRazerMouseProtocol.ParseLowBatteryThreshold(report));
    }

    [Fact]
    public void MultiFamilyLightingRoutesByZoneAndCustomUsesFixedPrefix()
    {
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x008F)!;

        Assert.StartsWith("razer_chroma_mouse_extended_",
            OpenRazerLightingProtocol.CreateEffect(device,
                new OpenRazerLightingSettings(OpenRazerLightingEffect.Spectrum, Zone: OpenRazerLedZone.Backlight)).BuilderName);
        Assert.StartsWith("razer_chroma_extended_",
            OpenRazerLightingProtocol.CreateEffect(device,
                new OpenRazerLightingSettings(OpenRazerLightingEffect.Spectrum, Zone: OpenRazerLedZone.All)).BuilderName);

        var custom = OpenRazerLightingProtocol.CreateEffect(device,
            new OpenRazerLightingSettings(OpenRazerLightingEffect.Custom, Zone: OpenRazerLedZone.All)).Report;
        Assert.Equal(new byte[] { 0x00, 0x00, 0x08 },
            custom[RazerFeatureReport.ArgumentsOffset..(RazerFeatureReport.ArgumentsOffset + 3)]);
    }

    [Fact]
    public void StandardCustomEffectUsesItsSourceFixedStorage()
    {
        var keyboard = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x0203)!;

        var request = OpenRazerLightingProtocol.CreateEffect(keyboard,
            new OpenRazerLightingSettings(OpenRazerLightingEffect.Custom)).Report;

        Assert.Equal(new byte[] { 0x05, 0x00 },
            request[RazerFeatureReport.ArgumentsOffset..(RazerFeatureReport.ArgumentsOffset + 2)]);
        Assert.Throws<NotSupportedException>(() => OpenRazerLightingProtocol.CreateEffect(keyboard,
            new OpenRazerLightingSettings(OpenRazerLightingEffect.Custom,
                Storage: OpenRazerStorage.Persistent)));
    }

    [Theory]
    [InlineData(0x008F)]
    [InlineData(0x0090)]
    [InlineData(0x00A7)]
    [InlineData(0x00A8)]
    public void NagaDefaultEffectsUseTheUpstreamGenericBuilderFamily(ushort productId)
    {
        var naga = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, productId)!;

        var off = OpenRazerLightingProtocol.CreateEffect(naga,
            new OpenRazerLightingSettings(OpenRazerLightingEffect.Off));
        Assert.Equal("razer_chroma_standard_matrix_effect_none", off.BuilderName);
        Assert.Equal(0xFF, off.Report[2]);

        var spectrum = OpenRazerLightingProtocol.CreateEffect(naga,
            new OpenRazerLightingSettings(OpenRazerLightingEffect.Spectrum));
        Assert.Equal("razer_chroma_mouse_extended_matrix_effect_spectrum", spectrum.BuilderName);
        Assert.Equal(0x1F, spectrum.Report[2]);
        Assert.Equal((byte)OpenRazerLedZone.Backlight,
            spectrum.Report[RazerFeatureReport.ArgumentsOffset + 1]);
    }

    [Fact]
    public void BrightnessDefaultsAreResolvedSeparatelyForReadAndWrite()
    {
        var nommo = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x0517)!;

        var get = OpenRazerLightingProtocol.GetBrightness(nommo, null, null).Report;
        var set = OpenRazerLightingProtocol.SetBrightness(nommo, null, null, 128).Report;

        Assert.Equal((byte)OpenRazerLedZone.Backlight,
            get[RazerFeatureReport.ArgumentsOffset + 1]);
        Assert.Equal((byte)OpenRazerLedZone.All,
            set[RazerFeatureReport.ArgumentsOffset + 1]);
    }

    [Fact]
    public async Task UnsupportedZoneEffectFailsBeforeTransport()
    {
        var tartarus = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x022B)!;
        var transport = new RecordingTransport();
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);
        var zones = OpenRazerDeviceService.GetLightingZoneCapabilities(tartarus);
        var connection = new OpenRazerDeviceConnection(tartarus, "test-path", "test-device",
            OpenRazerEndpointState.Resolved, OpenRazerDeviceService.GetCapabilities(tartarus), zones, null);

        await Assert.ThrowsAsync<NotSupportedException>(() => service.SetLightingAsync(connection,
            new OpenRazerLightingSettings(OpenRazerLightingEffect.Spectrum, Zone: OpenRazerLedZone.All)));
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public async Task UnsupportedLedStateStorageFailsBeforeTransport()
    {
        var mamba = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x0048)!;
        var transport = new RecordingTransport();
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);
        var zones = OpenRazerDeviceService.GetLightingZoneCapabilities(mamba);
        var connection = new OpenRazerDeviceConnection(mamba, "test-path", "test-device",
            OpenRazerEndpointState.Resolved, OpenRazerDeviceService.GetCapabilities(mamba), zones, null);

        Assert.True(zones[OpenRazerLedZone.ScrollWheel].CanWriteState);
        await Assert.ThrowsAsync<NotSupportedException>(() => service.SetLedStateAsync(connection,
            OpenRazerStorage.Temporary, OpenRazerLedZone.ScrollWheel, false));
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public void DefaultZonesAndSupportedZonesComeFromDeviceFacts()
    {
        var keyboard = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x0203)!;
        var mouse = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x008F)!;
        var dock = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x0F1A)!;

        Assert.Equal(OpenRazerLedZone.Backlight, keyboard.DefaultLedZone);
        Assert.Equal(OpenRazerLedZone.All, mouse.DefaultLedZone);
        Assert.Equal(OpenRazerLedZone.All, dock.DefaultLedZone);
        var zones = OpenRazerDeviceService.GetSupportedLedZones(mouse);
        Assert.Contains(OpenRazerLedZone.All, zones);
        Assert.Contains(OpenRazerLedZone.Backlight, zones);
        Assert.Contains(OpenRazerLedZone.ScrollWheel, zones);
    }

    [Fact]
    public void LightingCapabilitiesArePublishedPerZoneAndDefaultEffectUsesItsOwnZone()
    {
        var tartarus = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x022B)!;
        var zones = OpenRazerDeviceService.GetLightingZoneCapabilities(tartarus);

        Assert.True(zones[OpenRazerLedZone.All].CanWriteBrightness);
        Assert.DoesNotContain(OpenRazerLightingEffect.Spectrum,
            zones[OpenRazerLedZone.All].LightingEffects);
        Assert.Contains(OpenRazerLightingEffect.Spectrum,
            zones[OpenRazerLedZone.Backlight].LightingEffects);

        var request = OpenRazerLightingProtocol.CreateEffect(tartarus,
            new OpenRazerLightingSettings(OpenRazerLightingEffect.Spectrum));
        Assert.Equal((byte)OpenRazerLedZone.Backlight,
            request.Report[RazerFeatureReport.ArgumentsOffset + 1]);
    }

    [Fact]
    public async Task KeyswitchAndHyperPollingPreserveEverySourceTransaction()
    {
        var keyboard = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x026B)!;
        Assert.Equal(0x1F, keyboard.GetRequiredTransaction("razer_chroma_misc_set_keyswitch_optimization_command1"));
        Assert.Equal(0x1F, keyboard.GetRequiredTransaction("razer_chroma_misc_set_keyswitch_optimization_command2"));
        Assert.Equal(0x1F, keyboard.GetRequiredTransaction("razer_chroma_misc_get_keyswitch_optimization"));

        var dongle = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x00B3)!;
        var transport = new RecordingTransport();
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);
        var connection = new OpenRazerDeviceConnection(dongle, "test-path", "test-device",
            OpenRazerEndpointState.Resolved, new HashSet<OpenRazerBackendCapability>(),
            new Dictionary<OpenRazerLedZone, OpenRazerLightingZoneCapabilities>(), null);

        await service.SetHyperPollingIndicatorAsync(connection, 1);
        await service.PairHyperPollingAsync(connection, 0x0091);
        await service.UnpairHyperPollingAsync(connection, 0x0091);

        Assert.Equal(new byte[] { 0x1F, 0x1F, 0x1F, 0xFF },
            transport.Requests.Select(request => request[2]).ToArray());
    }

    [Theory]
    [InlineData(0x0205)]
    [InlineData(0x020F)]
    [InlineData(0x0220)]
    [InlineData(0x0224)]
    [InlineData(0x022D)]
    [InlineData(0x0232)]
    public void FnToggleExistsOnlyForTheSourceListedBladeModels(ushort productId)
    {
        Assert.Equal(0xFF, OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, productId)!
            .GetRequiredTransaction("razer_chroma_misc_fn_key_toggle"));
        Assert.Equal(6, OpenRazerDeviceCatalog.BuiltIn.Devices.Count(device =>
            device.Transactions.ContainsKey("razer_chroma_misc_fn_key_toggle")));
    }

    [Fact]
    public void PhysicalDeviceKeyRemovesOnlyTheCollectionSegment()
    {
        const string col01 = @"\\?\hid#vid_1532&pid_0091&mi_00&col01#7&123456&0&0000#{guid}";
        const string col02 = @"\\?\hid#vid_1532&pid_0091&mi_00&col02#7&123456&0&0000#{guid}";
        const string other = @"\\?\hid#vid_1532&pid_0091&mi_00&col01#8&abcdef&0&0000#{guid}";

        Assert.Equal(WindowsHidDiscovery.GetPhysicalDeviceKey(col01),
            WindowsHidDiscovery.GetPhysicalDeviceKey(col02));
        Assert.NotEqual(WindowsHidDiscovery.GetPhysicalDeviceKey(col01),
            WindowsHidDiscovery.GetPhysicalDeviceKey(other));
        Assert.Contains("#7&123456&0&0000#", WindowsHidDiscovery.GetPhysicalDeviceKey(col01));
    }

    [Fact]
    public void PhysicalDeviceKeyPrefersTheWindowsContainerIdAcrossInterfaces()
    {
        const string mi00 = @"\\?\hid#vid_1532&pid_0091&mi_00&col01#7&123456&0&0000#{guid}";
        const string mi02 = @"\\?\hid#vid_1532&pid_0091&mi_02&col04#7&abcdef&0&0000#{guid}";
        var container = Guid.Parse("B71F26B1-CEB2-42A4-98A0-32844232EBAA");

        Assert.Equal(WindowsHidDiscovery.GetPhysicalDeviceKey(mi00, container),
            WindowsHidDiscovery.GetPhysicalDeviceKey(mi02, container));
        Assert.Equal("container:b71f26b1-ceb2-42a4-98a0-32844232ebaa",
            WindowsHidDiscovery.GetPhysicalDeviceKey(mi00, container));
    }

    [Fact]
    public void EveryPublishedBackendCapabilityHasAtLeastOneResolvedDeviceDefinition()
    {
        var published = OpenRazerDeviceCatalog.BuiltIn.Devices
            .SelectMany(OpenRazerDeviceService.GetCapabilities)
            .ToHashSet();

        Assert.Equal(Enum.GetValues<OpenRazerBackendCapability>().Order(), published.Order());
    }

    [Fact]
    public void EveryPublishedDeviceCapabilityCanConstructItsDefaultRequest()
    {
        foreach (var device in OpenRazerDeviceCatalog.BuiltIn.Devices)
        {
            var zones = OpenRazerDeviceService.GetLightingZoneCapabilities(device);
            foreach (var capability in OpenRazerDeviceService.GetCapabilities(device))
            {
                var exception = Record.Exception(() => Construct(capability, device, zones));
                Assert.True(exception is null,
                    $"1532:{device.ProductId:X4} published {capability} but request construction failed: {exception}");
            }
        }

        static void Construct(
            OpenRazerBackendCapability capability,
            OpenRazerDeviceDefinition device,
            IReadOnlyDictionary<OpenRazerLedZone, OpenRazerLightingZoneCapabilities> zones)
        {
            object constructed = capability switch
            {
                OpenRazerBackendCapability.FirmwareRead => OpenRazerStandardProtocol.GetFirmware(device),
                OpenRazerBackendCapability.SerialRead => OpenRazerStandardProtocol.GetSerial(device),
                OpenRazerBackendCapability.DeviceModeRead => OpenRazerStandardProtocol.GetDeviceMode(device),
                OpenRazerBackendCapability.BatteryRead => OpenRazerMouseProtocol.GetBattery(device),
                OpenRazerBackendCapability.ChargingRead => OpenRazerMouseProtocol.GetCharging(device),
                OpenRazerBackendCapability.PollingRateRead => OpenRazerMouseProtocol.GetPollingRate(device),
                OpenRazerBackendCapability.PollingRateWrite => OpenRazerMouseProtocol.SetPollingRate(device,
                    device.PollingRates.FirstOrDefault(1000)),
                OpenRazerBackendCapability.DpiRead => OpenRazerMouseProtocol.GetDpi(device),
                OpenRazerBackendCapability.DpiWrite => OpenRazerMouseProtocol.SetDpi(device,
                    Math.Min(device.MaximumDpi ?? 800, 800), Math.Min(device.MaximumDpi ?? 800, 800)),
                OpenRazerBackendCapability.DpiStagesRead => OpenRazerMouseProtocol.GetDpiStages(device),
                OpenRazerBackendCapability.DpiStagesWrite => OpenRazerMouseProtocol.SetDpiStages(device,
                    new OpenRazerDpiStages(1, [new OpenRazerDpiStage(1, 800, 800)])),
                OpenRazerBackendCapability.IdleTimeoutRead => OpenRazerMouseProtocol.GetIdleTime(device),
                OpenRazerBackendCapability.IdleTimeoutWrite => OpenRazerMouseProtocol.SetIdleTime(device, 60),
                OpenRazerBackendCapability.LowBatteryThresholdRead => OpenRazerMouseProtocol.GetLowBatteryThreshold(device),
                OpenRazerBackendCapability.LowBatteryThresholdWrite => OpenRazerMouseProtocol.SetLowBatteryThreshold(device, 15),
                OpenRazerBackendCapability.BrightnessRead => OpenRazerLightingProtocol.GetBrightness(device, null,
                    zones.Values.First(zone => zone.CanReadBrightness).Zone),
                OpenRazerBackendCapability.BrightnessWrite => OpenRazerLightingProtocol.SetBrightness(device, null,
                    zones.Values.First(zone => zone.CanWriteBrightness).Zone, 128),
                OpenRazerBackendCapability.LedStateRead => OpenRazerStandardProtocol.GetLedState(device,
                    (byte)device.DefaultStorage, (byte)zones.Values.First(zone => zone.CanReadState).Zone),
                OpenRazerBackendCapability.LedStateWrite => OpenRazerStandardProtocol.SetLedState(device,
                    (byte)device.DefaultStorage, (byte)zones.Values.First(zone => zone.CanWriteState).Zone, true),
                OpenRazerBackendCapability.LedEffectRead => OpenRazerStandardProtocol.GetLedEffect(device,
                    (byte)device.DefaultStorage, (byte)zones.Values.First(zone => zone.CanReadEffect).Zone),
                OpenRazerBackendCapability.LedEffectWrite => OpenRazerStandardProtocol.SetLedEffect(device,
                    (byte)device.DefaultStorage, (byte)zones.Values.First(zone => zone.CanWriteEffect).Zone,
                    OpenRazerClassicLedEffect.Static),
                OpenRazerBackendCapability.LedColorWrite => OpenRazerStandardProtocol.SetLedColor(device,
                    (byte)device.DefaultStorage, (byte)zones.Values.First(zone => zone.CanWriteColor).Zone,
                    new OpenRazerColor(1, 2, 3)),
                OpenRazerBackendCapability.LedBlinkingWrite => OpenRazerStandardProtocol.SetLedBlinking(device,
                    (byte)device.DefaultStorage, (byte)zones.Values.First(zone => zone.CanWriteBlinking).Zone),
                OpenRazerBackendCapability.LightingEffectWrite => OpenRazerLightingProtocol.CreateEffectRequests(device,
                    CreateLightingSettings(zones)),
                OpenRazerBackendCapability.MatrixFrameWrite => OpenRazerLightingProtocol.SetCustomRow(device, 0, 0,
                    [new OpenRazerColor(1, 2, 3)]),
                OpenRazerBackendCapability.ReactiveTriggerWrite => OpenRazerStandardProtocol.Create(device,
                    "razer_chroma_misc_matrix_reactive_trigger", 0x05, 0x03, 0x0A, [0x02, 0, 0, 0, 0]),
                OpenRazerBackendCapability.ScrollModeRead => OpenRazerMouseProtocol.GetScrollMode(device),
                OpenRazerBackendCapability.ScrollModeWrite => OpenRazerMouseProtocol.SetScrollMode(device, 0),
                OpenRazerBackendCapability.ScrollAccelerationRead => OpenRazerMouseProtocol.GetScrollAcceleration(device),
                OpenRazerBackendCapability.ScrollAccelerationWrite => OpenRazerMouseProtocol.SetScrollAcceleration(device, false),
                OpenRazerBackendCapability.SmartReelRead => OpenRazerMouseProtocol.GetSmartReel(device),
                OpenRazerBackendCapability.SmartReelWrite => OpenRazerMouseProtocol.SetSmartReel(device, false),
                OpenRazerBackendCapability.FnPrimaryWrite => OpenRazerStandardProtocol.Create(device,
                    "razer_chroma_misc_fn_key_toggle", 0x02, 0x02, 0x06, [0, 0]),
                OpenRazerBackendCapability.KeyswitchOptimizationRead => OpenRazerStandardProtocol.Create(device,
                    "razer_chroma_misc_get_keyswitch_optimization", 0x04, 0x02, 0x82, []),
                OpenRazerBackendCapability.KeyswitchOptimizationWrite => CreateKeyswitchRequests(device),
                OpenRazerBackendCapability.HyperPollingIndicatorWrite => OpenRazerStandardProtocol.Create(device,
                    "razer_chroma_misc_set_hyperpolling_wireless_dongle_indicator_led_mode", 1, 7, 0x10, [1]),
                OpenRazerBackendCapability.HyperPollingPairWrite => CreatePairRequests(device),
                OpenRazerBackendCapability.HyperPollingUnpairWrite => OpenRazerStandardProtocol.Create(device,
                    "razer_chroma_misc_set_hyperpolling_wireless_dongle_unpair", 2, 0, 0x42, [0, 1]),
                _ => throw new InvalidOperationException($"Capability {capability} has no construction assertion."),
            };
            _ = constructed;
        }

        static OpenRazerLightingSettings CreateLightingSettings(
            IReadOnlyDictionary<OpenRazerLedZone, OpenRazerLightingZoneCapabilities> zones)
        {
            var zone = zones.Values.First(value => value.LightingEffects.Count > 0);
            return new OpenRazerLightingSettings(zone.LightingEffects.First(), Zone: zone.Zone);
        }

        static OpenRazerRequest[] CreateKeyswitchRequests(OpenRazerDeviceDefinition device) =>
        [
            OpenRazerStandardProtocol.Create(device,
                "razer_chroma_misc_set_keyswitch_optimization_command1", 4, 2, 2, [0, 0, 0, 0]),
            OpenRazerStandardProtocol.Create(device,
                "razer_chroma_misc_set_keyswitch_optimization_command2", 5, 2, 0x15, [1, 0, 0, 0, 0]),
        ];

        static OpenRazerRequest[] CreatePairRequests(OpenRazerDeviceDefinition device) =>
        [
            OpenRazerStandardProtocol.Create(device,
                "razer_chroma_misc_set_hyperpolling_wireless_dongle_pair_step1", 1, 0, 0x46, [1]),
            OpenRazerStandardProtocol.Create(device,
                "razer_chroma_misc_set_hyperpolling_wireless_dongle_pair_step2", 3, 0, 0x41, [1, 0, 1]),
        ];
    }

    [Fact]
    public void EverySupportedGenericSourceEffectHasOneDefaultBinding()
    {
        var mappings = new (string Capability, string BuilderSuffix, OpenRazerLightingEffect Effect)[]
        {
            ("set_none_effect", "matrix_effect_none", OpenRazerLightingEffect.Off),
            ("set_static_effect", "matrix_effect_static", OpenRazerLightingEffect.Static),
            ("set_spectrum_effect", "matrix_effect_spectrum", OpenRazerLightingEffect.Spectrum),
            ("set_wave_effect", "matrix_effect_wave", OpenRazerLightingEffect.Wave),
            ("set_reactive_effect", "matrix_effect_reactive", OpenRazerLightingEffect.Reactive),
            ("set_breath_random_effect", "matrix_effect_breathing_random", OpenRazerLightingEffect.BreathingRandom),
            ("set_breath_single_effect", "matrix_effect_breathing_single", OpenRazerLightingEffect.BreathingSingle),
            ("set_breath_dual_effect", "matrix_effect_breathing_dual", OpenRazerLightingEffect.BreathingDual),
            ("set_starlight_random_effect", "matrix_effect_starlight_random", OpenRazerLightingEffect.StarlightRandom),
            ("set_starlight_single_effect", "matrix_effect_starlight_single", OpenRazerLightingEffect.StarlightSingle),
            ("set_starlight_dual_effect", "matrix_effect_starlight_dual", OpenRazerLightingEffect.StarlightDual),
            ("set_wheel_effect", "matrix_effect_wheel", OpenRazerLightingEffect.Wheel),
            ("set_custom_effect", "matrix_effect_custom_frame", OpenRazerLightingEffect.Custom),
        };

        foreach (var device in OpenRazerDeviceCatalog.BuiltIn.Devices)
        {
            foreach (var mapping in mappings.Where(mapping =>
                device.HasSourceCapability(mapping.Capability) &&
                device.Transactions.Keys.Any(builder => builder.EndsWith(mapping.BuilderSuffix, StringComparison.Ordinal))))
            {
                var exception = Record.Exception(() => OpenRazerLightingProtocol.CreateEffect(device,
                    new OpenRazerLightingSettings(mapping.Effect)));
                Assert.True(exception is null,
                    $"1532:{device.ProductId:X4} source capability {mapping.Capability} has no default binding: {exception}");
            }
        }
    }

    [Fact]
    public void EverySupportedPerZoneSourceEffectHasOneExplicitBinding()
    {
        var zones = new (string Name, OpenRazerLedZone Zone)[]
        {
            ("logo", OpenRazerLedZone.Logo),
            ("scroll", OpenRazerLedZone.ScrollWheel),
            ("backlight", OpenRazerLedZone.Backlight),
            ("left", OpenRazerLedZone.LeftSide),
            ("right", OpenRazerLedZone.RightSide),
            ("charging", OpenRazerLedZone.Charging),
            ("fast_charging", OpenRazerLedZone.FastCharging),
            ("fully_charged", OpenRazerLedZone.FullyCharged),
        };
        var effects = new (string Suffix, string BuilderSuffix, OpenRazerLightingEffect Effect)[]
        {
            ("none", "matrix_effect_none", OpenRazerLightingEffect.Off),
            ("static", "matrix_effect_static", OpenRazerLightingEffect.Static),
            ("spectrum", "matrix_effect_spectrum", OpenRazerLightingEffect.Spectrum),
            ("wave", "matrix_effect_wave", OpenRazerLightingEffect.Wave),
            ("reactive", "matrix_effect_reactive", OpenRazerLightingEffect.Reactive),
            ("breath_random", "matrix_effect_breathing_random", OpenRazerLightingEffect.BreathingRandom),
            ("breath_mono", "matrix_effect_breathing_single", OpenRazerLightingEffect.BreathingSingle),
            ("breath_single", "matrix_effect_breathing_single", OpenRazerLightingEffect.BreathingSingle),
            ("breath_dual", "matrix_effect_breathing_dual", OpenRazerLightingEffect.BreathingDual),
        };

        foreach (var device in OpenRazerDeviceCatalog.BuiltIn.Devices)
        {
            foreach (var zone in zones)
            foreach (var effect in effects)
            {
                var capability = $"set_{zone.Name}_{effect.Suffix}";
                if (!device.HasSourceCapability(capability) ||
                    !device.Transactions.Keys.Any(builder =>
                        (builder.StartsWith("razer_chroma_extended_matrix_effect_", StringComparison.Ordinal) ||
                         builder.StartsWith("razer_chroma_mouse_extended_matrix_effect_", StringComparison.Ordinal)) &&
                        builder.EndsWith(effect.BuilderSuffix, StringComparison.Ordinal)))
                {
                    continue;
                }

                var exception = Record.Exception(() => OpenRazerLightingProtocol.CreateEffect(device,
                    new OpenRazerLightingSettings(effect.Effect, Zone: zone.Zone)));
                Assert.True(exception is null,
                    $"1532:{device.ProductId:X4} source capability {capability} has no explicit binding: {exception}");
            }
        }
    }

    private sealed class RecordingTransport : IRazerFeatureTransport
    {
        internal List<byte[]> Requests { get; } = [];

        public Task<byte[]> QueryAsync(
            string devicePath,
            byte transactionId,
            byte dataSize,
            byte commandClass,
            byte commandId,
            ReadOnlyMemory<byte> arguments,
            TimeSpan deviceWait,
            CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false) =>
            throw new NotSupportedException();

        public Task<byte[]> QueryPreparedAsync(
            string devicePath,
            ReadOnlyMemory<byte> request,
            TimeSpan deviceWait,
            CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false)
        {
            var response = request.ToArray();
            Requests.Add(response.ToArray());
            response[1] = 0x02;
            return Task.FromResult(response);
        }
    }
}
