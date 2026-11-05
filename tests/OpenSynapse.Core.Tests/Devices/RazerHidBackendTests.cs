using OpenSynapse.Windows.Devices;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerHidBackendTests
{
    /// <summary>
    /// 1532:005F and 1532:004D are reported as DeathAdder 2000 variants that RazerHid does not
    /// catalogue. Their entries exist only so a probe can read the firmware; without this the
    /// owner gets no device page at all. Every write builder must stay absent until a 005F owner
    /// returns real evidence, so the probe cannot change a setting even if the UI offers one.
    /// </summary>
    [Theory]
    [InlineData(0x005F)]
    [InlineData(0x004D)]
    public void TheLocallyAdmittedDeathAdderProbesExposeReadsAndNoWrites(ushort productId)
    {
        var catalog = RazerHidDeviceCatalog.BuiltIn;
        var probe = catalog.Find(0x1532, productId);
        var sibling = catalog.Find(0x1532, 0x004F)!;

        Assert.NotNull(probe);
        Assert.Contains("read-only probe", probe!.DisplayName, StringComparison.Ordinal);
        Assert.Equal(sibling.Category, probe.Category);
        Assert.Equal(2000, probe.MaximumDpi);

        // The read half must reach the firmware, otherwise the probe collects nothing.
        Assert.Contains("razer_chroma_misc_get_dpi_xy", probe.Transactions.Keys);
        Assert.Contains("razer_chroma_misc_get_polling_rate", probe.Transactions.Keys);
        Assert.Contains("razer_chroma_standard_get_led_brightness", probe.Transactions.Keys);
        Assert.Equal(0xFF, probe.GetRequiredTransaction("razer_chroma_standard_get_led_brightness"));
        Assert.Equal(0xFF, probe.GetRequiredTransaction("razer_chroma_misc_get_dpi_xy"));
        Assert.Equal(0x00, probe.GetRequiredFixedArgument("razer_chroma_misc_get_dpi_xy", 0));

        // Every retained builder must be a read. This is the whole safety argument for
        // shipping the probe, so it is asserted against the names rather than by subtracting
        // a set of write prefixes, which would silently pass if a new write builder appeared.
        Assert.NotEmpty(probe.Transactions.Keys);
        Assert.All(probe.Transactions.Keys, builder => Assert.Contains("_get_", builder, StringComparison.Ordinal));
        Assert.Empty(probe.CommandSequences.Keys);

        // The sibling's writes must be dropped rather than inherited.
        Assert.Contains("razer_chroma_misc_set_dpi_xy", sibling.Transactions.Keys);
        Assert.Contains("razer_chroma_standard_set_led_state", sibling.Transactions.Keys);
        Assert.DoesNotContain("razer_chroma_misc_set_dpi_xy", probe.Transactions.Keys);
        Assert.DoesNotContain("razer_chroma_standard_set_led_state", probe.Transactions.Keys);
    }

    [Fact]
    public void CatalogCarriesTheUpstreamSnapshotPlusLocallyAdmittedProbes()
    {
        var catalog = RazerHidDeviceCatalog.BuiltIn;

        // 263 upstream entries plus two DeathAdder probes, two Viper V3 Pro SE aliases,
        // the locally admitted 054A Leviathan V2 X family, 22 middleware mouse product
        // ids (wired and dongle halves included), 22 Synapse 2 P1 read probes, and the
        // two Blade firmware-only probes.
        Assert.Equal(314, catalog.Devices.Count);
        Assert.Equal(314, catalog.Devices.Select(device => device.ProductId).Distinct().Count());
        Assert.DoesNotContain(catalog.Devices, device => device.ProductId is 0x00B8 or 0x02C6);
        Assert.Null(catalog.Find(0x1234, catalog.Devices[0].ProductId));
        Assert.All(catalog.Devices, device =>
        {
            Assert.NotEmpty(device.DisplayName);
            Assert.Equal(device.SourceCapabilities.Count, device.SourceCapabilities.Distinct(StringComparer.Ordinal).Count());
            Assert.Empty(device.Transactions.Keys.Intersect(device.CommandSequences.Keys, StringComparer.Ordinal));
        });
    }

    [Fact]
    public void LeviathanV2XIsAdmittedForIdentityAndBrightness()
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x054A);

        Assert.NotNull(device);
        Assert.Equal("Razer Leviathan V2 X", device!.DisplayName);
        Assert.Equal(DeviceCategory.Accessory, device.Category);
        Assert.Equal(TimeSpan.FromMilliseconds(5), device.DeviceWait);
        Assert.Equal(RazerHidStorage.Temporary, device.DefaultStorage);
        Assert.Contains("razer_chroma_extended_matrix_get_region_ids", device.Transactions.Keys);
        Assert.Contains("get_firmware", device.SourceCapabilities);
        Assert.Contains("get_serial", device.SourceCapabilities);
        Assert.Contains("get_brightness", device.SourceCapabilities);
        Assert.Contains("set_brightness", device.SourceCapabilities);
        Assert.Contains("set_none_effect", device.SourceCapabilities);
        Assert.Contains("set_static_effect", device.SourceCapabilities);
        Assert.Contains("set_spectrum_effect", device.SourceCapabilities);
        Assert.Contains("set_wave_effect", device.SourceCapabilities);
        Assert.Contains("set_breath_dual_effect", device.SourceCapabilities);

        var capabilities = RazerHidDeviceService.GetCapabilities(device);
        Assert.Contains(RazerHidBackendCapability.FirmwareRead, capabilities);
        Assert.Contains(RazerHidBackendCapability.BrightnessRead, capabilities);
        Assert.Contains(RazerHidBackendCapability.BrightnessWrite, capabilities);
        Assert.Contains(RazerHidBackendCapability.LightingEffectWrite, capabilities);
        Assert.DoesNotContain(RazerHidBackendCapability.MatrixFrameWrite, capabilities);

        var zone = RazerHidDeviceService.GetLightingZoneCapabilities(device)[RazerHidLedZone.All];
        Assert.Equal(
            [
                RazerHidLightingEffect.Off,
                RazerHidLightingEffect.Static,
                RazerHidLightingEffect.Spectrum,
                RazerHidLightingEffect.Wave,
                RazerHidLightingEffect.BreathingDual,
            ],
            zone.LightingEffects.Order());

        var renamed = device.WithDisplayName("Razer | Kuromi Limited Edition Soundbar");
        Assert.Equal("Razer | Kuromi Limited Edition Soundbar", renamed.DisplayName);
        Assert.Equal(device.DefaultStorage, renamed.DefaultStorage);
        Assert.Same(device.Transactions, renamed.Transactions);
        Assert.True(capabilities.SetEquals(RazerHidDeviceService.GetCapabilities(renamed)));
    }

    [Theory]
    [InlineData(@"\\?\hid#vid_1532&pid_054a&mi_01#test", true)]
    [InlineData(@"\\?\hid#vid_1532&pid_054b&mi_01#test", false)]
    public void OnlyLeviathan054AUsesReportIdSeven(string devicePath, bool expected)
    {
        Assert.Equal(expected, RazerFeatureTransport.UsesLeviathanReportId(devicePath));
    }

    [Fact]
    public void FeatureTransactionIdsCycleFromZeroThroughThirtyOne()
    {
        var sequence = -1;
        var actual = Enumerable.Range(0, 34)
            .Select(_ => RazerFeatureTransport.NextTransactionId(ref sequence))
            .ToArray();

        Assert.Equal(Enumerable.Range(0, 32).Concat([0, 1]).Select(value => (byte)value), actual);
    }

    [Theory]
    [InlineData(0x00DE, 0x00C0, "Razer Viper V3 Pro SE (Wired)")]
    [InlineData(0x00DF, 0x00C1, "Razer Viper V3 Pro SE (Wireless)")]
    public void ViperV3ProSeAliasesUseTheMatchingPollingProtocol(
        ushort productId,
        ushort siblingProductId,
        string displayName)
    {
        var catalog = RazerHidDeviceCatalog.BuiltIn;
        var alias = catalog.Find(0x1532, productId);
        var sibling = catalog.Find(0x1532, siblingProductId);

        Assert.NotNull(alias);
        Assert.NotNull(sibling);
        Assert.Equal(displayName, alias!.DisplayName);
        Assert.Equal(sibling!.Category, alias.Category);
        Assert.Equal(sibling.MaximumDpi, alias.MaximumDpi);
        if (productId == 0x00DF)
        {
            Assert.Equal(new[] { 125, 500, 1000 }, alias.PollingRates);
            Assert.Equal(0x1F, alias.GetRequiredTransaction("razer_chroma_misc_get_polling_rate"));
            Assert.Equal(0x1F, alias.GetRequiredTransaction("razer_chroma_misc_set_polling_rate"));
            Assert.DoesNotContain("razer_chroma_misc_get_polling_rate2", alias.Transactions.Keys);
            Assert.DoesNotContain("razer_chroma_misc_set_polling_rate2", alias.Transactions.Keys);
            Assert.DoesNotContain("razer_chroma_misc_set_polling_rate2", alias.CommandSequences.Keys);

            var get = RazerHidMouseProtocol.GetPollingRate(alias).Report;
            Assert.Equal(0x1F, get[2]);
            Assert.Equal(0x01, get[6]);
            Assert.Equal(0x00, get[7]);
            Assert.Equal(0x85, get[8]);

            var set = Assert.Single(RazerHidMouseProtocol.SetPollingRate(alias, 1000));
            Assert.Equal(0x1F, set.Report[2]);
            Assert.Equal(0x01, set.Report[6]);
            Assert.Equal(0x00, set.Report[7]);
            Assert.Equal(0x05, set.Report[8]);
            Assert.Equal(0x01, set.Report[RazerFeatureReport.ArgumentsOffset]);
        }
        else
        {
            Assert.Equal(sibling.PollingRates, alias.PollingRates);
            Assert.Equal(
                sibling.Transactions.OrderBy(entry => entry.Key, StringComparer.Ordinal).ToArray(),
                alias.Transactions.OrderBy(entry => entry.Key, StringComparer.Ordinal).ToArray());
            Assert.Equal(
                sibling.BuilderArgumentPrefixes.Keys.OrderBy(key => key, StringComparer.Ordinal),
                alias.BuilderArgumentPrefixes.Keys.OrderBy(key => key, StringComparer.Ordinal));
            Assert.Equal(
                sibling.CommandSequences.Keys.OrderBy(key => key, StringComparer.Ordinal),
                alias.CommandSequences.Keys.OrderBy(key => key, StringComparer.Ordinal));
        }
        Assert.Equal(0x3F, alias.GetRequiredTransaction("razer_chroma_misc_get_battery_level"));
    }

    [Theory]
    [InlineData(0x0F1A)]
    [InlineData(0x0F21)]
    public void ConditionalDockWaveUsesTheResolved1FTransaction(ushort productId)
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, productId)!;

        Assert.Equal(0x1F, device.GetRequiredTransaction("razer_chroma_extended_matrix_effect_wave"));
        var request = RazerHidLightingProtocol.CreateEffect(device,
            new RazerHidLightingSettings(RazerHidLightingEffect.Wave));
        Assert.Equal(0x1F, request.Report[2]);
        Assert.Equal(0x0F, request.Report[7]);
        Assert.Equal(0x02, request.Report[8]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(request.Report), request.Report[89]);
    }

    [Theory]
    [InlineData(0x0287)]
    [InlineData(0x028D)]
    [InlineData(0x028F)]
    [InlineData(0x0293)]
    [InlineData(0x02A5)]
    public void BlackWidowV4FamilyStaticUsesTheExtendedMatrixBuilder(ushort productId)
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, productId)!;
        var zone = RazerHidDeviceService.GetLightingZoneCapabilities(device)[RazerHidLedZone.Backlight];

        Assert.Contains(RazerHidLightingEffect.Static, zone.LightingEffects);

        var request = RazerHidLightingProtocol.CreateEffect(device,
            new RazerHidLightingSettings(
                RazerHidLightingEffect.Static,
                Primary: new RazerHidColor(0xFF, 0xFF, 0xFF))).Report;
        Assert.Equal((byte)9, request[6]);
        Assert.Equal(0x0F, request[7]);
        Assert.Equal(0x02, request[8]);
        Assert.Equal([0x01, 0x05, 0x01, 0x00, 0x00, 0x01, 0xFF, 0xFF, 0xFF], request[9..18]);
    }

    [Fact]
    public void BlackWidowV3MiniHyperSpeedStaticReusesTheMatrixNoneBuilder()
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0271)!;
        var zone = RazerHidDeviceService.GetLightingZoneCapabilities(device)[RazerHidLedZone.Backlight];

        Assert.Contains(RazerHidLightingEffect.Static, zone.LightingEffects);

        var request = RazerHidLightingProtocol.CreateEffect(device,
            new RazerHidLightingSettings(
                RazerHidLightingEffect.Static,
                Primary: new RazerHidColor(0xFF, 0xFF, 0xFF))).Report;
        Assert.Equal(0x9F, request[2]);
        Assert.Equal((byte)9, request[6]);
        Assert.Equal(0x0F, request[7]);
        Assert.Equal(0x02, request[8]);
        Assert.Equal([0x01, 0x05, 0x01, 0x00, 0x00, 0x01, 0xFF, 0xFF, 0xFF], request[9..18]);
    }

    [Fact]
    public void EveryMatrixFamilyDeviceThatDeclaresStaticExposesIt()
    {
        var missing = RazerHidDeviceCatalog.BuiltIn.Devices
            .Where(device => device.HasSourceCapability("set_static_effect"))
            .Where(device => device.Transactions.Keys.Any(key =>
                key.EndsWith("matrix_effect_none", StringComparison.Ordinal)))
            .Where(device => RazerHidDeviceService.GetLightingZoneCapabilities(device)
                .Values.SelectMany(zone => zone.LightingEffects)
                .All(effect => effect != RazerHidLightingEffect.Static))
            .Select(device => device.ProductId)
            .ToArray();

        Assert.Empty(missing);
    }

    [Theory]
    [InlineData(0x0091, 0xFF)]
    [InlineData(0x00B3, 0xFF)]
    [InlineData(0x009E, 0x1F)]
    [InlineData(0x009F, 0x1F)]
    [InlineData(0x00B2, 0x1F)]
    [InlineData(0x00BE, 0x1F)]
    [InlineData(0x00BF, 0x1F)]
    public async Task HighPollingRateSetterPreservesBothWrites(ushort productId, byte secondTransaction)
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, productId)!;
        var transport = new RecordingTransport();
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);
        var connection = new RazerHidDeviceConnection(device, "test-path", "test-device", RazerHidEndpointState.Resolved,
            new HashSet<RazerHidBackendCapability> { RazerHidBackendCapability.PollingRateWrite },
            new Dictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities>(), null);

        await service.SetPollingRateAsync(connection, 1000);

        Assert.Equal(2, transport.Requests.Count);
        Assert.Equal((0x1F, 0x00, 0x08),
            (transport.Requests[0][2], transport.Requests[0][RazerFeatureReport.ArgumentsOffset], transport.Requests[0][RazerFeatureReport.ArgumentsOffset + 1]));
        Assert.Equal((secondTransaction, 0x01, 0x08),
            (transport.Requests[1][2], transport.Requests[1][RazerFeatureReport.ArgumentsOffset], transport.Requests[1][RazerFeatureReport.ArgumentsOffset + 1]));
        Assert.DoesNotContain("razer_chroma_misc_set_polling_rate2", device.Transactions.Keys);
    }

    [Fact]
    public void ViperV3ProWirelessPacketsMatchMiddleware()
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x00C1)!;

        var dpiStages = RazerHidMouseProtocol.GetDpiStages(device).Report;
        Assert.Equal(0x50, dpiStages[6]);
        Assert.Equal(0x04, dpiStages[7]);
        Assert.Equal(0x86, dpiStages[8]);
        Assert.Equal(0x01, dpiStages[RazerFeatureReport.ArgumentsOffset]);

        var dpiStagesSet = RazerHidMouseProtocol.SetDpiStages(device, new RazerHidDpiStages(
            1,
            [
                new(1, 800, 800),
                new(2, 1600, 1600),
                new(3, 2400, 2400),
                new(4, 3200, 3200),
                new(5, 4000, 4000),
            ])).Report;
        Assert.Equal(0x26, dpiStagesSet[6]);
        Assert.Equal(0x06, dpiStagesSet[8]);
        Assert.Equal(0x01, dpiStagesSet[RazerFeatureReport.ArgumentsOffset]);

        var polling = RazerHidMouseProtocol.GetPollingRate(device).Report;
        Assert.Equal(0x02, polling[6]);
        Assert.Equal(0xC0, polling[8]);
        Assert.Equal(0x01, polling[RazerFeatureReport.ArgumentsOffset]);

        var pollingSet = Assert.Single(RazerHidMouseProtocol.SetPollingRate(device, 1000));
        Assert.Equal(0x02, pollingSet.Report[6]);
        Assert.Equal(0x40, pollingSet.Report[8]);
        Assert.Equal(0x01, pollingSet.Report[RazerFeatureReport.ArgumentsOffset]);
        Assert.Equal(0x08, pollingSet.Report[RazerFeatureReport.ArgumentsOffset + 1]);
    }

    [Theory]
    [InlineData(0x00BE)]
    [InlineData(0x00BF)]
    public void DeathAdderV4IndicatorConfigurationUsesTheThreeLedProtocol(ushort productId)
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, productId)!;
        var capabilities = RazerHidDeviceService.GetCapabilities(device);

        Assert.Contains(RazerHidBackendCapability.IndicatorLedConfigurationV2Read, capabilities);
        Assert.Contains(RazerHidBackendCapability.IndicatorLedConfigurationV2Write, capabilities);
        Assert.DoesNotContain(RazerHidBackendCapability.IndicatorLedConfigurationRead, capabilities);
        Assert.DoesNotContain(RazerHidBackendCapability.IndicatorLedConfigurationWrite, capabilities);

        var get = RazerHidDongleIndicatorProtocol.Get(device).Report;
        Assert.Equal(0x1F, get[2]);
        Assert.Equal(0x03, get[6]);
        Assert.Equal(0x07, get[7]);
        Assert.Equal(0x95, get[8]);
        Assert.Equal(new byte[3], get[RazerFeatureReport.ArgumentsOffset..(RazerFeatureReport.ArgumentsOffset + 3)]);

        var set = RazerHidDongleIndicatorProtocol.Set(device,
            new RazerHidIndicatorLedConfigurationV2(0, 2, 1)).Report;
        Assert.Equal(0x1F, set[2]);
        Assert.Equal(0x03, set[6]);
        Assert.Equal(0x07, set[7]);
        Assert.Equal(0x15, set[8]);
        Assert.Equal([0, 2, 1], set[RazerFeatureReport.ArgumentsOffset..(RazerFeatureReport.ArgumentsOffset + 3)]);

        var response = RazerFeatureReport.CreateRequest(0x1F, 0x03, 0x07, 0x95, [0, 2, 1]);
        response[1] = 0x02;
        response[89] = RazerFeatureReport.CalculateCrc(response);
        Assert.Equal(new RazerHidIndicatorLedConfigurationV2(0, 2, 1),
            RazerHidDongleIndicatorProtocol.Parse(response));
    }

    [Fact]
    public async Task UnresolvedEndpointRejectsWritesBeforeTransport()
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0203)!;
        var transport = new RecordingTransport();
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);
        var connection = new RazerHidDeviceConnection(device, null, "test-device",
            RazerHidEndpointState.RecognizedButUnresolved,
            new HashSet<RazerHidBackendCapability>(),
            new Dictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities>(), "unresolved");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SetBrightnessAsync(connection, 100));
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public void MissingTransactionFailsClosed()
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0013)!;

        Assert.Throws<NotSupportedException>(() =>
            device.GetRequiredTransaction("razer_chroma_misc_get_polling_rate"));
    }

    [Fact]
    public void DpiStagesFollowTheMiddlewareLayoutWithAZAxis()
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x00B9)!;
        var arguments = new byte[]
        {
            0x01, 0x01, 0x02,
            0x00, 0x03, 0x20, 0x03, 0x20, 0x7A, 0x55,
            0x01, 0x05, 0xDC, 0x05, 0xDC, 0x12, 0x34,
        };
        var response = RazerFeatureReport.CreateRequest(0x1F, checked((byte)arguments.Length),
            0x04, 0x86, arguments);
        response[1] = 0x02;
        response[89] = RazerFeatureReport.CalculateCrc(response);

        var stages = RazerHidMouseProtocol.ParseDpiStages(device, response);

        Assert.Equal(1, stages.ActiveStage);
        Assert.Equal(
            [
                new RazerHidDpiStage(0, 800, 800, 31317),
                new RazerHidDpiStage(1, 1500, 1500, 4660),
            ],
            stages.Stages);
    }

    [Fact]
    public async Task OptionalChargingRejectionDoesNotInvalidateBasiliskState()
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x00B9)!;
        var transport = new OptionalChargingRejectionTransport();
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);
        DeviceBatteryReading? observed = null;
        service.BatteryObserved += reading => observed = reading;
        var connection = new RazerHidDeviceConnection(device, "test-path", "test-device",
            RazerHidEndpointState.Resolved,
            new HashSet<RazerHidBackendCapability>
            {
                RazerHidBackendCapability.BatteryRead,
                RazerHidBackendCapability.ChargingRead,
            },
            new Dictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities>(), null);

        var state = await service.ReadBasicStateAsync(connection);

        Assert.Equal(96, state.BatteryPercent);
        Assert.Equal(96, observed?.Percent);
        Assert.Equal(device.DisplayName, observed?.DeviceName);
        Assert.Null(observed?.IsCharging);
        Assert.Null(state.IsCharging);
        Assert.Empty(state.Errors);
        Assert.Contains(RazerHidBackendCapability.ChargingRead, state.UnsupportedCapabilities);
        Assert.DoesNotContain(RazerHidBackendCapability.BatteryRead, state.UnsupportedCapabilities);

        await service.ReadBasicStateAsync(connection, excludedCapabilities: state.UnsupportedCapabilities);
        Assert.Equal(1, transport.ChargingQueries);
    }

    [Fact]
    public async Task MalformedOptionalFieldDoesNotInvalidateBasicState()
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x00B9)!;
        var transport = new OptionalChargingRejectionTransport(
            new InvalidDataException("Malformed charging report."));
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);
        var connection = new RazerHidDeviceConnection(device, "test-path", "test-device",
            RazerHidEndpointState.Resolved,
            new HashSet<RazerHidBackendCapability>
            {
                RazerHidBackendCapability.BatteryRead,
                RazerHidBackendCapability.ChargingRead,
            },
            new Dictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities>(), null);

        var state = await service.ReadBasicStateAsync(connection);

        Assert.Equal(96, state.BatteryPercent);
        Assert.Null(state.IsCharging);
        Assert.Empty(state.Errors);
        Assert.Contains(RazerHidBackendCapability.ChargingRead, state.UnsupportedCapabilities);
    }

    [Fact]
    public async Task SilentChargingCommandDoesNotInvalidateTheRestOfTheDevice()
    {
        // The DeathAdder V4 Pro answers its battery read but can leave the optional
        // charging read silent. A transient timeout must stay retryable without turning
        // the rest of the device into an error state.
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x00BF)!;
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn,
            new OptionalChargingRejectionTransport(
                new RazerDeviceTimeoutException("The device did not answer this command (0x04).")));
        var connection = new RazerHidDeviceConnection(device, "test-path", "test-device",
            RazerHidEndpointState.Resolved,
            new HashSet<RazerHidBackendCapability>
            {
                RazerHidBackendCapability.BatteryRead,
                RazerHidBackendCapability.ChargingRead,
            },
            new Dictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities>(), null);

        var state = await service.ReadBasicStateAsync(connection);

        Assert.Equal(96, state.BatteryPercent);
        Assert.Null(state.IsCharging);
        Assert.Empty(state.Errors);
        Assert.DoesNotContain(RazerHidBackendCapability.ChargingRead, state.UnsupportedCapabilities);
    }

    [Fact]
    public async Task TransportFailureRemainsARescanError()
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x00B9)!;
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn,
            new FailingReadTransport(new IOException("Device disconnected.")));
        var batteryObservations = 0;
        service.BatteryObserved += _ => batteryObservations++;
        var connection = new RazerHidDeviceConnection(device, "test-path", "test-device",
            RazerHidEndpointState.Resolved,
            new HashSet<RazerHidBackendCapability> { RazerHidBackendCapability.ChargingRead },
            new Dictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities>(), null);

        var state = await service.ReadBasicStateAsync(connection);

        Assert.Equal("Device disconnected.", state.Errors[nameof(RazerHidBackendCapability.ChargingRead)]);
        Assert.Empty(state.UnsupportedCapabilities);
        Assert.Equal(0, batteryObservations);
    }

    [Fact]
    public async Task FailedChargingReadDoesNotEmitAnUnreliableDischargingObservation()
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x00B9)!;
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn,
            new OptionalChargingRejectionTransport(new IOException("Receiver unavailable")));
        var observations = 0;
        service.BatteryObserved += _ => observations++;
        var connection = new RazerHidDeviceConnection(device, "test-path", "test-device",
            RazerHidEndpointState.Resolved,
            new HashSet<RazerHidBackendCapability>
            {
                RazerHidBackendCapability.BatteryRead,
                RazerHidBackendCapability.ChargingRead,
            }, new Dictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities>(), null);
        var state = await service.ReadBasicStateAsync(connection);
        Assert.Equal(96, state.BatteryPercent);
        Assert.Contains(nameof(RazerHidBackendCapability.ChargingRead), state.Errors.Keys);
        Assert.Equal(0, observations);
    }

    [Fact]
    public async Task FailedReadsAreKeyedByCapabilitySoTheUiCanLineThemUp()
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x00B9)!;
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn,
            new FailingReadTransport(new IOException("Device disconnected.")));
        var connection = new RazerHidDeviceConnection(device, "test-path", "test-device",
            RazerHidEndpointState.Resolved,
            new HashSet<RazerHidBackendCapability> { RazerHidBackendCapability.ChargingRead },
            new Dictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities>(), null);

        var state = await service.ReadBasicStateAsync(connection);

        // The diagnostic page lists protocol rows by capability name, so a failure has to
        // carry the same key or the failing row still reads as "read".
        Assert.Equal(["ChargingRead"], state.Errors.Keys);
    }

    [Fact]
    public async Task SerialReadIsShownOnlyAfterSuccessfulOptionalQuery()
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0203)!;
        Assert.Contains("razer_chroma_standard_get_serial", device.Transactions.Keys);
        var connection = new RazerHidDeviceConnection(device, "test-path", "test-device",
            RazerHidEndpointState.Resolved,
            new HashSet<RazerHidBackendCapability>(),
            new Dictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities>(), null);

        var read = await new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn,
            new SerialTransport()).ReadBasicStateAsync(connection);
        Assert.Equal("RAZER-123", read.Serial);
        Assert.Empty(read.Errors);

        var rejected = await new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn,
            new FailingReadTransport(new IOException("Serial unavailable"))).ReadBasicStateAsync(connection);
        Assert.Null(rejected.Serial);
        Assert.Empty(rejected.Errors);
    }

    [Theory]
    [InlineData("RAZER-123", true)]
    [InlineData("000000000000", false)]
    [InlineData("FFFFFFFF", false)]
    [InlineData("", false)]
    public void SerialPlaceholderIsNotShown(string serial, bool expected)
    {
        Assert.Equal(expected, WindowsHidDiscovery.IsUsableSerial(serial));
    }

    [Fact]
    public void WideStandardMatrixPreservesBytesBeyondDeclaredSize()
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0225)!;
        var colors = Enumerable.Range(0, 25)
            .Select(index => new RazerHidColor((byte)index, (byte)(index + 1), (byte)(index + 2)))
            .ToArray();

        var request = RazerHidLightingProtocol.SetCustomRow(device, 0, 0, colors).Report;

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
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, productId)!;

        Assert.Equal(firmware, device.GetRequiredTransaction("razer_chroma_standard_get_firmware_version"));
        Assert.Equal(serial, device.GetRequiredTransaction("razer_chroma_standard_get_serial"));
        Assert.Equal(mode, device.GetRequiredTransaction("razer_chroma_standard_get_device_mode"));
    }

    [Theory]
    [InlineData(0x0258, 0x1F)]
    [InlineData(0x008F, 0x3F)]
    public void BatteryTransactionStaysDriverFamilySpecific(ushort productId, byte transaction)
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, productId)!;

        Assert.Equal(transaction, device.GetRequiredTransaction("razer_chroma_misc_get_battery_level"));
    }

    [Fact]
    public void DpiUsesPerDeviceStorageAndLegacyScaling()
    {
        var imperator = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x002F)!;
        var viper8K = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0091)!;
        var viperV3Pro = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x00C1)!;
        var deathAdder = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0098)!;
        var legacy = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0015)!;

        Assert.Equal(0x01, RazerHidMouseProtocol.GetDpi(imperator).Report[RazerFeatureReport.ArgumentsOffset]);
        Assert.Equal(0x00, RazerHidMouseProtocol.GetDpi(viper8K).Report[RazerFeatureReport.ArgumentsOffset]);
        Assert.Equal(0x01, RazerHidMouseProtocol.SetDpi(imperator, 1600, 1600).Report[RazerFeatureReport.ArgumentsOffset]);
        Assert.Equal(0x00, RazerHidMouseProtocol.SetDpi(viper8K, 1600, 1600).Report[RazerFeatureReport.ArgumentsOffset]);
        Assert.Equal(0x00, RazerHidMouseProtocol.GetDpi(deathAdder).Report[RazerFeatureReport.ArgumentsOffset]);
        Assert.Equal(0x00, RazerHidMouseProtocol.SetDpi(deathAdder, 1600, 1600).Report[RazerFeatureReport.ArgumentsOffset]);
        Assert.Equal(0x01, RazerHidMouseProtocol.GetDpi(viperV3Pro).Report[RazerFeatureReport.ArgumentsOffset]);
        Assert.Equal(0x01, RazerHidMouseProtocol.SetDpi(viperV3Pro, 1600, 1600).Report[RazerFeatureReport.ArgumentsOffset]);

        var legacyRequest = RazerHidMouseProtocol.SetDpi(legacy, 1600, 1600).Report;
        Assert.Equal(60, legacyRequest[RazerFeatureReport.ArgumentsOffset]);
        legacyRequest[1] = 0x02;
        legacyRequest[RazerFeatureReport.ArgumentsOffset] = 60;
        legacyRequest[RazerFeatureReport.ArgumentsOffset + 1] = 60;
        Assert.Equal((1588, 1588), RazerHidMouseProtocol.ParseDpi(legacyRequest, byteEncoding: true));
    }

    [Fact]
    public async Task DpiRejectsAZeroFilledBusyResponse()
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0098)!;
        var connection = new RazerHidDeviceConnection(device, "test-path", "test-device",
            RazerHidEndpointState.Resolved,
            new HashSet<RazerHidBackendCapability> { RazerHidBackendCapability.DpiRead },
            new Dictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities>(), null);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, new ZeroDpiTransport())
                .GetDpiAsync(connection));
    }

    [Fact]
    public void DeathAdderEssential2021PublishesLegacyPollingWithTheFFTransaction()
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0098)!;
        var capabilities = RazerHidDeviceService.GetCapabilities(device);

        Assert.Equal([500, 1000], device.PollingRates);
        // 0098 is wired: no battery, charging, idle, sleep, charge-limit or discharge-curve capability.
        Assert.Equal(
            [
                RazerHidBackendCapability.BrightnessRead,
                RazerHidBackendCapability.BrightnessWrite,
                RazerHidBackendCapability.DpiRead,
                RazerHidBackendCapability.DpiWrite,
                RazerHidBackendCapability.LightingEffectWrite,
                RazerHidBackendCapability.PollingRateRead,
                RazerHidBackendCapability.PollingRateWrite,
            ],
            capabilities.OrderBy(capability => capability.ToString(), StringComparer.Ordinal).ToArray());
        Assert.Equal(0xFF, device.GetRequiredTransaction("razer_chroma_misc_get_polling_rate"));

        var request = RazerHidMouseProtocol.GetPollingRate(device).Report;
        Assert.Equal(0xFF, request[2]);
        Assert.Equal(0x01, request[6]);
        Assert.Equal(0x00, request[7]);
        Assert.Equal(0x85, request[8]);
    }

    [Fact]
    public async Task BusyStateReadStaysRetryable()
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0098)!;
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn,
            new FailingReadTransport(new RazerDeviceTimeoutException("The device is busy (0x01).", retryableBusy: true)));
        var connection = new RazerHidDeviceConnection(device, "test-path", "test-device",
            RazerHidEndpointState.Resolved,
            new HashSet<RazerHidBackendCapability> { RazerHidBackendCapability.PollingRateRead },
            new Dictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities>(), null);

        var state = await service.ReadBasicStateAsync(connection);

        Assert.Contains(nameof(RazerHidBackendCapability.PollingRateRead), state.Errors.Keys);
        Assert.DoesNotContain(RazerHidBackendCapability.PollingRateRead, state.UnsupportedCapabilities);
    }

    [Fact]
    public async Task SilentStateReadStaysRetryable()
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0098)!;
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn,
            new FailingReadTransport(new RazerDeviceTimeoutException("The device did not answer this command.")));
        var connection = new RazerHidDeviceConnection(device, "test-path", "test-device",
            RazerHidEndpointState.Resolved,
            new HashSet<RazerHidBackendCapability> { RazerHidBackendCapability.PollingRateRead },
            new Dictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities>(), null);

        var state = await service.ReadBasicStateAsync(connection);

        Assert.Null(state.PollingRate);
        Assert.Empty(state.Errors);
        Assert.DoesNotContain(RazerHidBackendCapability.PollingRateRead, state.UnsupportedCapabilities);
    }

    [Fact]
    public void LegacyFFReportsAcceptTheReplyTransactionAsAWildcard()
    {
        var request = RazerFeatureReport.CreateRequest(0xFF, 0x01, 0x00, 0x85, [0x00]);
        var response = request.ToArray();
        response[1] = 0x02;
        response[2] = 0x1F;
        response[89] = RazerFeatureReport.CalculateCrc(response);

        Assert.True(RazerFeatureReport.Matches(request, response));

        var rotatedRequest = RazerFeatureReport.CreateRequest(0x1F, 0x01, 0x00, 0x85, [0x00]);
        var rotatedResponse = rotatedRequest.ToArray();
        rotatedResponse[1] = 0x02;
        rotatedResponse[2] = 0x20;
        rotatedResponse[89] = RazerFeatureReport.CalculateCrc(rotatedResponse);

        Assert.False(RazerFeatureReport.Matches(rotatedRequest, rotatedResponse));
    }

    [Theory]
    [InlineData(5, 0x0D)]
    [InlineData(15, 0x27)]
    [InlineData(25, 0x40)]
    public void LowBatteryThresholdMatchesMiddlewareScaling(int percent, byte raw)
    {
        // The middleware writes `Math.ceil(percent / 100 * 255)`, so 15% is 39 and
        // not 38. RazerHid truncates; the official app is the ground truth here.
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x008F)!;
        var report = RazerHidMouseProtocol.SetLowBatteryThreshold(device, percent).Report;

        Assert.Equal(raw, report[RazerFeatureReport.ArgumentsOffset]);
        report[1] = 0x02;
        report[RazerFeatureReport.ArgumentsOffset] = raw;
        Assert.Equal(percent, RazerHidMouseProtocol.ParseLowBatteryThreshold(report));
    }

    [Fact]
    public void MultiFamilyLightingRoutesByZoneAndCustomUsesFixedPrefix()
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x008F)!;

        Assert.StartsWith("razer_chroma_mouse_extended_",
            RazerHidLightingProtocol.CreateEffect(device,
                new RazerHidLightingSettings(RazerHidLightingEffect.Spectrum, Zone: RazerHidLedZone.Backlight)).BuilderName);
        Assert.StartsWith("razer_chroma_extended_",
            RazerHidLightingProtocol.CreateEffect(device,
                new RazerHidLightingSettings(RazerHidLightingEffect.Spectrum, Zone: RazerHidLedZone.All)).BuilderName);

        var custom = RazerHidLightingProtocol.CreateEffect(device,
            new RazerHidLightingSettings(RazerHidLightingEffect.Custom, Zone: RazerHidLedZone.All)).Report;
        Assert.Equal(new byte[] { 0x00, 0x00, 0x08 },
            custom[RazerFeatureReport.ArgumentsOffset..(RazerFeatureReport.ArgumentsOffset + 3)]);
    }

    [Fact]
    public void LightingEffectsCarryTheMiddlewareEffectIdsAndFlags()
    {
        var keyboard = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0203)!;
        var reactive = RazerHidLightingProtocol.CreateEffect(keyboard,
            new RazerHidLightingSettings(RazerHidLightingEffect.Reactive,
                Speed: 3, Primary: new RazerHidColor(1, 2, 3))).Report;
        Assert.Equal(new byte[] { 0x02, 0x00, 0x01, 0x02, 0x03 },
            reactive[RazerFeatureReport.ArgumentsOffset..(RazerFeatureReport.ArgumentsOffset + 5)]);
    }

    [Fact]
    public void BrightnessAndBreathingFlagsMatchTheMiddleware()
    {
        // The middleware writes Math.floor(percent / 100 * 255).
        Assert.Equal((byte)127, RazerChromaStandardProtocol.ToBrightnessLevel(50));
        Assert.Equal((byte)255, RazerChromaStandardProtocol.ToBrightnessLevel(100));

        var dual = RazerLegacyChromaProtocol.SetEffect(0x1F, RazerChromaEffect.Breathing, [1, 2, 3, 4, 5, 6]);
        Assert.Equal(2, dual[RazerFeatureReport.ArgumentsOffset + 3]);
    }

    [Fact]
    public void StandardCustomEffectUsesItsSourceFixedStorage()
    {
        var keyboard = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0203)!;

        var request = RazerHidLightingProtocol.CreateEffect(keyboard,
            new RazerHidLightingSettings(RazerHidLightingEffect.Custom)).Report;

        Assert.Equal(new byte[] { 0x05, 0x00 },
            request[RazerFeatureReport.ArgumentsOffset..(RazerFeatureReport.ArgumentsOffset + 2)]);
        Assert.Throws<NotSupportedException>(() => RazerHidLightingProtocol.CreateEffect(keyboard,
            new RazerHidLightingSettings(RazerHidLightingEffect.Custom,
                Storage: RazerHidStorage.Persistent)));
    }

    [Theory]
    [InlineData(0x008F)]
    [InlineData(0x0090)]
    [InlineData(0x00A7)]
    [InlineData(0x00A8)]
    public void NagaDefaultEffectsUseTheUpstreamGenericBuilderFamily(ushort productId)
    {
        var naga = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, productId)!;

        var off = RazerHidLightingProtocol.CreateEffect(naga,
            new RazerHidLightingSettings(RazerHidLightingEffect.Off));
        Assert.Equal("razer_chroma_standard_matrix_effect_none", off.BuilderName);
        Assert.Equal(0xFF, off.Report[2]);

        var spectrum = RazerHidLightingProtocol.CreateEffect(naga,
            new RazerHidLightingSettings(RazerHidLightingEffect.Spectrum));
        Assert.Equal("razer_chroma_mouse_extended_matrix_effect_spectrum", spectrum.BuilderName);
        Assert.Equal(0x1F, spectrum.Report[2]);
        Assert.Equal((byte)RazerHidLedZone.Backlight,
            spectrum.Report[RazerFeatureReport.ArgumentsOffset + 1]);
    }

    [Fact]
    public void BrightnessDefaultsAreResolvedSeparatelyForReadAndWrite()
    {
        var nommo = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0517)!;

        var get = RazerHidLightingProtocol.GetBrightness(nommo, null, null).Report;
        var set = RazerHidLightingProtocol.SetBrightness(nommo, null, null, 128).Report;

        Assert.Equal((byte)RazerHidLedZone.Backlight,
            get[RazerFeatureReport.ArgumentsOffset + 1]);
        Assert.Equal((byte)RazerHidLedZone.All,
            set[RazerFeatureReport.ArgumentsOffset + 1]);
    }

    [Fact]
    public async Task UnsupportedZoneEffectFailsBeforeTransport()
    {
        var tartarus = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x022B)!;
        var transport = new RecordingTransport();
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);
        var zones = RazerHidDeviceService.GetLightingZoneCapabilities(tartarus);
        var connection = new RazerHidDeviceConnection(tartarus, "test-path", "test-device",
            RazerHidEndpointState.Resolved, RazerHidDeviceService.GetCapabilities(tartarus), zones, null);

        await Assert.ThrowsAsync<NotSupportedException>(() => service.SetLightingAsync(connection,
            new RazerHidLightingSettings(RazerHidLightingEffect.Spectrum, Zone: RazerHidLedZone.All)));
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public async Task UnsupportedLedStateStorageFailsBeforeTransport()
    {
        var mamba = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0048)!;
        var transport = new RecordingTransport();
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);
        var zones = RazerHidDeviceService.GetLightingZoneCapabilities(mamba);
        var connection = new RazerHidDeviceConnection(mamba, "test-path", "test-device",
            RazerHidEndpointState.Resolved, RazerHidDeviceService.GetCapabilities(mamba), zones, null);

        Assert.True(zones[RazerHidLedZone.ScrollWheel].CanWriteState);
        await Assert.ThrowsAsync<NotSupportedException>(() => service.SetLedStateAsync(connection,
            RazerHidStorage.Temporary, RazerHidLedZone.ScrollWheel, false));
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public void DefaultZonesAndSupportedZonesComeFromDeviceFacts()
    {
        var keyboard = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0203)!;
        var mouse = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x008F)!;
        var dock = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0F1A)!;

        Assert.Equal(RazerHidLedZone.Backlight, keyboard.DefaultLedZone);
        Assert.Equal(RazerHidLedZone.All, mouse.DefaultLedZone);
        Assert.Equal(RazerHidLedZone.All, dock.DefaultLedZone);
        var zones = RazerHidDeviceService.GetSupportedLedZones(mouse);
        Assert.Contains(RazerHidLedZone.All, zones);
        Assert.Contains(RazerHidLedZone.Backlight, zones);
        Assert.Contains(RazerHidLedZone.ScrollWheel, zones);
    }

    [Fact]
    public void LightingCapabilitiesArePublishedPerZoneAndDefaultEffectUsesItsOwnZone()
    {
        var tartarus = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x022B)!;
        var zones = RazerHidDeviceService.GetLightingZoneCapabilities(tartarus);

        Assert.False(zones[RazerHidLedZone.All].CanWriteBrightness);
        Assert.DoesNotContain(RazerHidLightingEffect.Spectrum,
            zones[RazerHidLedZone.All].LightingEffects);
        Assert.Contains(RazerHidLightingEffect.Spectrum,
            zones[RazerHidLedZone.Backlight].LightingEffects);

        var request = RazerHidLightingProtocol.CreateEffect(tartarus,
            new RazerHidLightingSettings(RazerHidLightingEffect.Spectrum));
        Assert.Equal((byte)RazerHidLedZone.Backlight,
            request.Report[RazerFeatureReport.ArgumentsOffset + 1]);
    }

    [Fact]
    public async Task KeyswitchAndHyperPollingPreserveEverySourceTransaction()
    {
        var keyboard = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x026B)!;
        Assert.Equal(0x1F, keyboard.GetRequiredTransaction("razer_chroma_misc_set_keyswitch_optimization_command1"));
        Assert.Equal(0x1F, keyboard.GetRequiredTransaction("razer_chroma_misc_set_keyswitch_optimization_command2"));
        Assert.Equal(0x1F, keyboard.GetRequiredTransaction("razer_chroma_misc_get_keyswitch_optimization"));

        var dongle = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x00B3)!;
        var transport = new RecordingTransport();
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);
        var connection = new RazerHidDeviceConnection(dongle, "test-path", "test-device",
            RazerHidEndpointState.Resolved, new HashSet<RazerHidBackendCapability>(),
            new Dictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities>(), null);

        await service.SetHyperPollingIndicatorAsync(connection, 1);
        await service.PairHyperPollingAsync(connection, 0x0091);
        await service.UnpairHyperPollingAsync(connection, 0x0091);

        Assert.Equal(new byte[] { 0x1F, 0x1F, 0x1F, 0xFF },
            transport.Requests.Select(request => request[2]).ToArray());
    }

    [Fact]
    public async Task EnsureDriverMode_WritesDriverModeOnlyWhenTheDeviceIsInNormalMode()
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x00BE)!;
        var transport = new DeviceModeTransport(0x00);
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);
        var connection = new RazerHidDeviceConnection(device, "test-path", "test-device",
            RazerHidEndpointState.Resolved,
            new HashSet<RazerHidBackendCapability>
            {
                RazerHidBackendCapability.DeviceModeRead,
                RazerHidBackendCapability.DeviceModeWrite,
            },
            new Dictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities>(), null);

        Assert.True(await service.EnsureDriverModeAsync(connection));

        // read mode, write 0x04 with mode 0x03, read again to confirm
        Assert.Equal(3, transport.Requests.Count);
        var write = transport.Requests[1];
        Assert.Equal(0x1F, write[2]);
        Assert.Equal(0x00, write[7]);
        Assert.Equal(0x04, write[8]);
        Assert.Equal(0x03, write[9]);
        Assert.Equal(0x00, write[10]);
    }

    [Fact]
    public async Task EnsureDriverMode_DoesNotWriteWhenTheDeviceIsAlreadyInDriverMode()
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x00BE)!;
        var transport = new DeviceModeTransport(0x03);
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);
        var connection = new RazerHidDeviceConnection(device, "test-path", "test-device",
            RazerHidEndpointState.Resolved,
            new HashSet<RazerHidBackendCapability>
            {
                RazerHidBackendCapability.DeviceModeRead,
                RazerHidBackendCapability.DeviceModeWrite,
            },
            new Dictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities>(), null);

        Assert.True(await service.EnsureDriverModeAsync(connection));

        Assert.Single(transport.Requests);
        Assert.Equal(0x84, transport.Requests[0][8]);
    }

    [Theory]
    // 0x02 is upstream's "unknown mode" (razerchromacommon.c) and must never be
    // overwritten; only an exact 0x00 read is repaired.
    [InlineData(0x02)]
    [InlineData(0x01)]
    [InlineData(0xFF)]
    public async Task EnsureDriverMode_NeverOverwritesAnUnknownMode(byte mode)
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x00BE)!;
        var transport = new DeviceModeTransport(mode);
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);
        var connection = new RazerHidDeviceConnection(device, "test-path", "test-device",
            RazerHidEndpointState.Resolved,
            new HashSet<RazerHidBackendCapability> { RazerHidBackendCapability.DeviceModeWrite },
            new Dictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities>(), null);

        Assert.False(await service.EnsureDriverModeAsync(connection));

        Assert.Single(transport.Requests);
    }

    [Fact]
    public async Task EnsureDriverMode_SkipsDevicesWithoutTheWriteCapability()
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x00BE)!;
        var transport = new DeviceModeTransport(0x00);
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);
        var connection = new RazerHidDeviceConnection(device, "test-path", "test-device",
            RazerHidEndpointState.Resolved, new HashSet<RazerHidBackendCapability>(),
            new Dictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities>(), null);

        Assert.False(await service.EnsureDriverModeAsync(connection));

        Assert.Empty(transport.Requests);
    }

    private sealed class DeviceModeTransport : IRazerFeatureTransport
    {
        private byte _mode;

        internal DeviceModeTransport(byte initialMode) => _mode = initialMode;

        internal List<byte[]> Requests { get; } = [];

        public Task<byte[]> QueryAsync(
            string devicePath, byte transactionId, byte dataSize, byte commandClass,
            byte commandId, ReadOnlyMemory<byte> arguments, TimeSpan deviceWait,
            CancellationToken cancellationToken, bool allowRemainingPacketsMismatch = false) =>
            throw new NotSupportedException();

        public Task<byte[]> QueryPreparedAsync(
            string devicePath, ReadOnlyMemory<byte> request, TimeSpan deviceWait,
            CancellationToken cancellationToken, bool allowRemainingPacketsMismatch = false)
        {
            var response = request.ToArray();
            Requests.Add(response);
            response[1] = 0x02;
            if (response[8] == 0x04)
            {
                _mode = response[9];
            }
            else if (response[8] == 0x84)
            {
                response[6] = 0x02;
                response[9] = _mode;
                response[10] = 0x00;
            }

            // Keep the accepted-status response valid: Arguments() rejects a stale CRC.
            response[89] = RazerFeatureReport.CalculateCrc(response);
            return Task.FromResult(response);
        }
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
        Assert.Equal(0xFF, RazerHidDeviceCatalog.BuiltIn.Find(0x1532, productId)!
            .GetRequiredTransaction("razer_chroma_misc_fn_key_toggle"));
        Assert.Equal(6, RazerHidDeviceCatalog.BuiltIn.Devices.Count(device =>
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

    [Theory]
    [InlineData(@"\\?\HID#{00001812-0000-1000-8000-00805F9B34FB}_DEV_VID&02A3_PID&008F#a&1")]
    [InlineData(@"\\?\BTHLE\DEV_AABBCCDDEEFF\7&1")]
    [InlineData(@"\\?\BTHENUM\{00001812-0000-1000-8000-00805F9B34FB}_VID&00010000_PID&008F\7&1")]
    public void BluetoothHidPathsAreRecognized(string path)
    {
        Assert.True(WindowsHidDiscovery.IsBluetoothHidPath(path));
        Assert.False(WindowsHidDiscovery.IsBluetoothHidPath(
            @"\\?\hid#vid_1532&pid_008F&mi_02&col02#7&1"));
    }

    [Fact]
    public void BluetoothHidIdentityFallsBackToHidAttributes()
    {
        const string path =
            @"\\?\HID#{00001812-0000-1000-8000-00805F9B34FB}_DEV_VID&02A3_PID&008F_REV&0101_AABBCCDDEEFF#a&1&0&0001";

        Assert.True(WindowsHidDiscovery.TryResolveDeviceIdentity(
            path, 0x1532, 0x008F, out var vendorId, out var productId));
        Assert.Equal(0x1532, vendorId);
        Assert.Equal(0x008F, productId);
    }

    [Fact]
    public void UsbHidPathTakesPrecedenceOverHidAttributes()
    {
        const string path = @"\\?\hid#vid_1532&pid_0090&mi_02&col02#7&1a2b&0&0000#{guid}";

        Assert.True(WindowsHidDiscovery.TryResolveDeviceIdentity(
            path, 0x1532, 0x008F, out var vendorId, out var productId));
        Assert.Equal(0x1532, vendorId);
        Assert.Equal(0x0090, productId);
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
    public void SystemContainerFallsBackToTheUsbParentAcrossInterfaces()
    {
        const string mi00 = @"\\?\hid#vid_1532&pid_02c6&mi_00#8&32f21f36&0&0000#{guid}";
        const string mi03 = @"\\?\hid#vid_1532&pid_02c6&mi_03#8&30e7a9e9&0&0000#{guid}";
        var systemContainer = Guid.Parse("00000000-0000-0000-FFFF-FFFFFFFFFFFF");
        const string parent = @"USB\VID_1532&PID_02C6\6&31630CAC&0&4";

        Assert.Equal($"usb-parent:{parent}",
            WindowsHidDiscovery.GetPhysicalDeviceKey(mi00, systemContainer, parent));
        Assert.Equal(WindowsHidDiscovery.GetPhysicalDeviceKey(mi00, systemContainer, parent),
            WindowsHidDiscovery.GetPhysicalDeviceKey(mi03, systemContainer, parent));
    }

    [Fact]
    public void SystemContainerWithoutAUsbParentFallsBackToTheInterfacePath()
    {
        const string path = @"\\?\hid#vid_1532&pid_02c6&mi_01&col02#8&a87716d&0&0001#{guid}";
        var systemContainer = Guid.Parse("00000000-0000-0000-FFFF-FFFFFFFFFFFF");

        Assert.Equal(WindowsHidDiscovery.GetPhysicalDeviceKey(path),
            WindowsHidDiscovery.GetPhysicalDeviceKey(path, systemContainer, null));
    }

    [Fact]
    public void BluetoothHidPathKeepsItsOwnIdentityWhenAUsbParentExists()
    {
        const string path =
            @"\\?\HID#{00001812-0000-1000-8000-00805F9B34FB}_DEV_VID&02A3_PID&008F#a&1";
        var systemContainer = Guid.Parse("00000000-0000-0000-FFFF-FFFFFFFFFFFF");

        Assert.Equal(path,
            WindowsHidDiscovery.GetPhysicalDeviceKey(path, systemContainer, @"USB\VID_8087&PID_0026\5&1"));
    }

    [Fact]
    public void DiagnosticDevicePathOmitsInstanceSpecificSuffix()
    {
        const string path = @"\\?\hid#vid_1532&pid_027a&mi_00&col01#8&2d946264&0&0000#{guid}";

        Assert.Equal("vid_1532&pid_027a&mi_00&col01", RazerFeatureTransport.DescribeDevicePath(path));
    }

    /// <summary>
    /// A mouse that answers the polling-rate query with a one-byte payload puts the rate
    /// in the second argument byte, so the log must show it even though the declared
    /// length is one.
    /// </summary>
    [Fact]
    public void DiagnosticResponseArgumentsShowTheByteTheFirmwareActuallyUsed()
    {
        var response = RazerFeatureReport.CreateRequest(0x1F, 0x01, 0x00, 0xC0, [0x00]);
        response[RazerFeatureReport.ArgumentsOffset + 1] = 0x04;

        Assert.Equal("len=1, args=0004000000000000", RazerFeatureTransport.DescribeResponseArguments(response));
    }

    [Theory]
    [InlineData(0x01, 0x83, true)]
    [InlineData(0x01, 0x03, false)]
    [InlineData(0x02, 0x83, false)]
    public void BusyReadResponsesAreRetriedRegardlessOfTheEchoedLength(
        int status, int commandId, bool retry)
    {
        Assert.Equal(
            retry,
            RazerFeatureTransport.IsRetryableBusyResponse((byte)status, (byte)commandId));
    }

    [Fact]
    public async Task AProtocolTimeoutKeepsTheResolvedEndpointCached()
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0098)!;
        var connection = new RazerHidDeviceConnection(device, "test-path", "test-device",
            RazerHidEndpointState.Resolved, RazerHidDeviceService.GetCapabilities(device),
            RazerHidDeviceService.GetLightingZoneCapabilities(device), null);
        var cache = new System.Collections.Concurrent.ConcurrentDictionary<string, string>
        {
            [connection.InstanceId] = connection.DevicePath!,
        };
        var executor = new RazerHidRequestExecutor(
            new FailingReadTransport(new RazerDeviceTimeoutException("The device is busy (0x01).")),
            cache,
            (_, _) => { });
        var request = new RazerHidRequest(
            "test",
            RazerFeatureReport.CreateRequest(0x1F, 0x01, 0x00, 0xC0, [0x00]));

        await Assert.ThrowsAsync<RazerDeviceTimeoutException>(() =>
            executor.ExecuteAsync(connection, request, CancellationToken.None));

        Assert.Equal("test-path", cache[connection.InstanceId]);
    }

    [Fact]
    public void RejectedCollectionDescriptionNamesThePathAndDescriptor()
    {
        // Covers the message shown when no 91-byte control collection is usable: a wireless
        // mouse can leave a wired collection enumerated whose descriptor does not match, and
        // "busy or unavailable" alone cannot explain the device to its owner.
        const string path = @"\\?\hid#vid_1532&pid_00bf&mi_02&col02#7&1a2b&0&0000#{guid}";
        var message = RazerHidDeviceService.DescribeRejected([
            new(path, "container:key", 0x1532, 0x00BF, 8, 0, 64, 0x0001, 0x0002,
                DeviceAccessState.BusyOrUnavailable, "Razer Viper"),
        ]);

        Assert.Equal(
            "vid_1532&pid_00bf&mi_02&col02 feature=64 usage=0x0001:0x0002 access=BusyOrUnavailable",
            message);
    }

    [Fact]
    public void UnlistedGenericTransactionsDoNotBecomeProductCapabilities()
    {
        var published = RazerHidDeviceCatalog.BuiltIn.Devices
            .SelectMany(RazerHidDeviceService.GetCapabilities)
            .ToHashSet();

        Assert.Contains(RazerHidBackendCapability.BatteryRead, published);
        Assert.Contains(RazerHidBackendCapability.LightingEffectWrite, published);
        Assert.DoesNotContain(RazerHidBackendCapability.SerialRead, published);
        Assert.DoesNotContain(RazerHidBackendCapability.FnPrimaryWrite, published);
    }

    [Fact]
    public void MiddlewareSupplementalCapabilitiesArePublishedByTheCatalog()
    {
        var published = RazerHidDeviceCatalog.BuiltIn.Devices
            .SelectMany(RazerHidDeviceService.GetCapabilities)
            .ToHashSet();

        RazerHidBackendCapability[] expected =
        [
            RazerHidBackendCapability.TimeToDimRead,
            RazerHidBackendCapability.TimeToDimWrite,
            RazerHidBackendCapability.IndicatorLedConfigurationRead,
            RazerHidBackendCapability.IndicatorLedConfigurationWrite,
            RazerHidBackendCapability.IndicatorLedConfigurationV2Read,
            RazerHidBackendCapability.IndicatorLedConfigurationV2Write,
            RazerHidBackendCapability.GpuCardDetectionRead,
            RazerHidBackendCapability.GpuCardDetectionWrite,
            RazerHidBackendCapability.MacroStorageInfoRead,
        ];

        foreach (var capability in expected)
        {
            Assert.Contains(capability, published);
        }
    }

    [Fact]
    public void CapabilitySnapshotDistinguishesUnknownAndMatrixOnlyLayouts()
    {
        var unknown = RazerHidDeviceCatalog.BuiltIn.Devices
            .First(device => device.MatrixDimensions is null);
        Assert.Equal(RazerHidLayoutSupport.None, unknown.LayoutSupport);

        var matrix = RazerHidDeviceCatalog.BuiltIn.Devices.FirstOrDefault(device =>
            device.MatrixDimensions is not null && device.HasSourceCapability("set_key_row"));
        Assert.NotNull(matrix);
        Assert.Equal(RazerHidLayoutSupport.MatrixOnly, matrix.LayoutSupport);

        var unknownConnection = new RazerHidDeviceConnection(
            unknown, "unknown", "unknown", RazerHidEndpointState.Resolved,
            new HashSet<RazerHidBackendCapability>(),
            new Dictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities>(), null);
        Assert.False(unknownConnection.CapabilitySnapshot.SupportsMatrixFrames);
        Assert.False(unknownConnection.CapabilitySnapshot.SupportsNamedKeyEffects);

        var matrixConnection = new RazerHidDeviceConnection(
            matrix, "matrix", "matrix", RazerHidEndpointState.Resolved,
            new HashSet<RazerHidBackendCapability> { RazerHidBackendCapability.MatrixFrameWrite },
            new Dictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities>(), null);
        Assert.True(matrixConnection.CapabilitySnapshot.SupportsMatrixFrames);
        Assert.False(matrixConnection.CapabilitySnapshot.SupportsNamedKeyEffects);
    }

    [Fact]
    public void EveryPublishedProductControlIsDeclaredByItsRazerHidCapabilityList()
    {
        foreach (var device in RazerHidDeviceCatalog.BuiltIn.Devices)
        {
            var published = RazerHidDeviceService.GetCapabilities(device);
            foreach (var (capability, source) in new (RazerHidBackendCapability Capability, string[] Source)[]
            {
                (RazerHidBackendCapability.FirmwareRead, ["get_firmware"]),
                (RazerHidBackendCapability.BatteryRead, ["get_battery"]),
                (RazerHidBackendCapability.ChargingRead, ["is_charging"]),
                (RazerHidBackendCapability.PollingRateRead, ["get_poll_rate"]),
                (RazerHidBackendCapability.PollingRateWrite, ["set_poll_rate"]),
                (RazerHidBackendCapability.DpiRead, ["get_dpi_xy", "get_dpi_xy_byte"]),
                (RazerHidBackendCapability.DpiWrite, ["set_dpi_xy", "set_dpi_xy_byte"]),
                (RazerHidBackendCapability.DpiStagesRead, ["get_dpi_stages"]),
                (RazerHidBackendCapability.DpiStagesWrite, ["set_dpi_stages"]),
                (RazerHidBackendCapability.IdleTimeoutRead, ["get_idle_time"]),
                (RazerHidBackendCapability.IdleTimeoutWrite, ["set_idle_time"]),
                (RazerHidBackendCapability.LowBatteryThresholdRead, ["get_low_battery_threshold"]),
                (RazerHidBackendCapability.LowBatteryThresholdWrite, ["set_low_battery_threshold"]),
                (RazerHidBackendCapability.MatrixFrameWrite, ["set_key_row"]),
                (RazerHidBackendCapability.ReactiveTriggerWrite, ["trigger_reactive_effect"]),
                (RazerHidBackendCapability.ScrollModeRead, ["get_scroll_mode"]),
                (RazerHidBackendCapability.ScrollModeWrite, ["set_scroll_mode"]),
                (RazerHidBackendCapability.ScrollAccelerationRead, ["get_scroll_acceleration"]),
                (RazerHidBackendCapability.ScrollAccelerationWrite, ["set_scroll_acceleration"]),
                (RazerHidBackendCapability.SmartReelRead, ["get_scroll_smart_reel"]),
                (RazerHidBackendCapability.SmartReelWrite, ["set_scroll_smart_reel"]),
                (RazerHidBackendCapability.SmartReelThresholdRead, ["get_scroll_smart_reel_threshold"]),
                (RazerHidBackendCapability.SmartReelThresholdWrite, ["set_scroll_smart_reel_threshold"]),
                (RazerHidBackendCapability.KeyswitchOptimizationRead, ["get_keyswitch_optimization"]),
                (RazerHidBackendCapability.KeyswitchOptimizationWrite, ["set_keyswitch_optimization"]),
                (RazerHidBackendCapability.HyperPollingIndicatorWrite, ["set_hyperpolling_wireless_dongle_indicator_led_mode"]),
                (RazerHidBackendCapability.HyperPollingPairWrite, ["set_hyperpolling_wireless_dongle_pair"]),
                (RazerHidBackendCapability.HyperPollingUnpairWrite, ["set_hyperpolling_wireless_dongle_unpair"]),
                (RazerHidBackendCapability.TimeToDimRead, ["get_time_to_dim"]),
                (RazerHidBackendCapability.TimeToDimWrite, ["set_time_to_dim"]),
                (RazerHidBackendCapability.IndicatorLedConfigurationRead, ["get_indicator_led_configuration"]),
                (RazerHidBackendCapability.IndicatorLedConfigurationWrite, ["set_indicator_led_configuration"]),
                (RazerHidBackendCapability.IndicatorLedConfigurationV2Read, ["get_indicator_leds_configuration"]),
                (RazerHidBackendCapability.IndicatorLedConfigurationV2Write, ["set_indicator_leds_configuration"]),
                (RazerHidBackendCapability.GpuCardDetectionRead, ["get_gpu_card_detection"]),
                (RazerHidBackendCapability.GpuCardDetectionWrite, ["set_gpu_card_detection"]),
                (RazerHidBackendCapability.ForceChromaCtrlRead, ["get_force_chroma_ctrl"]),
                (RazerHidBackendCapability.ForceChromaCtrlWrite, ["set_force_chroma_ctrl"]),
                (RazerHidBackendCapability.ProfileDebounceTimeRead, ["get_profile_debounce_time"]),
                (RazerHidBackendCapability.ProfileDebounceTimeWrite, ["set_profile_debounce_time"]),
                (RazerHidBackendCapability.ChromaTapRead, ["get_chroma_tap"]),
                (RazerHidBackendCapability.ChromaTapWrite, ["set_chroma_tap"]),
                (RazerHidBackendCapability.BatteryDischargeCurveRead, ["get_battery_discharge_curve"]),
                (RazerHidBackendCapability.BatteryDischargeCurveWrite, ["set_battery_discharge_curve"]),
                (RazerHidBackendCapability.MacroStorageInfoRead, ["get_macro_storage_info"]),
                (RazerHidBackendCapability.LkScrollWheelModeRead, ["get_lk_scroll_wheel_mode"]),
                (RazerHidBackendCapability.LkScrollWheelModeWrite, ["set_lk_scroll_wheel_mode"]),
            })
            {
                if (!published.Contains(capability)) continue;
                Assert.True(source.Any(device.HasSourceCapability),
                    $"1532:{device.ProductId:X4} published {capability} without an RazerHid declaration.");
            }

            Assert.DoesNotContain(published, capability => capability is
                RazerHidBackendCapability.SerialRead or RazerHidBackendCapability.DeviceModeRead or
                RazerHidBackendCapability.FnPrimaryWrite);
            Assert.False(published.Contains(RazerHidBackendCapability.MatrixFrameWrite) &&
                device.MatrixDimensions is null,
                $"1532:{device.ProductId:X4} published a matrix editor without dimensions.");

            var zones = RazerHidDeviceService.GetLightingZoneCapabilities(device);
            foreach (var (zone, facts) in zones)
            {
                var sourceZone = zone switch
                {
                    RazerHidLedZone.Logo => "logo",
                    RazerHidLedZone.ScrollWheel => "scroll",
                    RazerHidLedZone.Backlight => "backlight",
                    RazerHidLedZone.LeftSide => "left",
                    RazerHidLedZone.RightSide => "right",
                    RazerHidLedZone.Charging => "charging",
                    RazerHidLedZone.FastCharging => "fast_charging",
                    RazerHidLedZone.FullyCharged => "fully_charged",
                    _ => null,
                };
                var prefix = zone == device.DefaultLedZone;
                Assert.False(facts.CanReadBrightness && !prefix &&
                    (sourceZone is null || !device.HasSourceCapability($"get_{sourceZone}_brightness")),
                    $"1532:{device.ProductId:X4} published unlisted {zone} brightness read.");
                Assert.False(facts.CanWriteBrightness && !prefix &&
                    (sourceZone is null || !device.HasSourceCapability($"set_{sourceZone}_brightness")),
                    $"1532:{device.ProductId:X4} published unlisted {zone} brightness write.");
                Assert.False(facts.CanReadState && (zone != RazerHidLedZone.Logo ||
                    !device.HasSourceCapability("get_logo_active")),
                    $"1532:{device.ProductId:X4} published unlisted {zone} state read.");
                Assert.False(facts.CanWriteState && (sourceZone is null ||
                    !device.HasSourceCapability($"set_{sourceZone}_active") &&
                    !device.HasSourceCapability($"set_{sourceZone}_on")),
                    $"1532:{device.ProductId:X4} published unlisted {zone} state write.");
                Assert.False(facts.CanReadEffect && (zone != RazerHidLedZone.Macro ||
                    !device.HasSourceCapability("get_macro_effect")),
                    $"1532:{device.ProductId:X4} published unlisted {zone} effect read.");
                Assert.False(facts.CanReadColor,
                    $"1532:{device.ProductId:X4} published an RGB read without a source capability.");
                Assert.False(facts.CanWriteColor && !facts.LightingEffects.Any(effect => effect is
                    RazerHidLightingEffect.Static or RazerHidLightingEffect.BreathingSingle or
                    RazerHidLightingEffect.Blinking),
                    $"1532:{device.ProductId:X4} published {zone} color write without a color effect.");
                Assert.False(facts.CanWriteBlinking && !facts.LightingEffects.Contains(RazerHidLightingEffect.Blinking),
                    $"1532:{device.ProductId:X4} published {zone} blinking without its effect.");
                foreach (var effect in facts.LightingEffects)
                {
                    var declaredGeneric = device.HasSourceCapability(
                        RazerHidLightingProtocol.GetGenericEffectCapability(effect));
                    var declaredZone = sourceZone is not null && effect switch
                    {
                        RazerHidLightingEffect.Off => device.HasSourceCapability($"set_{sourceZone}_none"),
                        RazerHidLightingEffect.Static => device.HasSourceCapability($"set_{sourceZone}_static"),
                        RazerHidLightingEffect.Spectrum => device.HasSourceCapability($"set_{sourceZone}_spectrum"),
                        RazerHidLightingEffect.Wave => device.HasSourceCapability($"set_{sourceZone}_wave"),
                        RazerHidLightingEffect.Reactive => device.HasSourceCapability($"set_{sourceZone}_reactive"),
                        RazerHidLightingEffect.BreathingRandom => device.HasSourceCapability($"set_{sourceZone}_breath_random"),
                        RazerHidLightingEffect.BreathingSingle =>
                            device.HasSourceCapability($"set_{sourceZone}_breath_single") ||
                            device.HasSourceCapability($"set_{sourceZone}_breath_mono"),
                        RazerHidLightingEffect.BreathingDual => device.HasSourceCapability($"set_{sourceZone}_breath_dual"),
                        _ => false,
                    };
                    Assert.True(declaredGeneric || declaredZone,
                        $"1532:{device.ProductId:X4} published unlisted {zone} {effect}.");
                }
            }
        }
    }

    [Fact]
    public void BasiliskV3XHyperSpeedDoesNotInheritUnlistedGenericControls()
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x00B9)!;
        var capabilities = RazerHidDeviceService.GetCapabilities(device);
        var zones = RazerHidDeviceService.GetLightingZoneCapabilities(device);

        Assert.Equal(RazerHidLedZone.ScrollWheel, device.DefaultLedZone);
        Assert.DoesNotContain(RazerHidBackendCapability.ChargingRead, capabilities);
        Assert.Contains(RazerHidBackendCapability.BrightnessRead, capabilities);
        // The middleware bundle declares the newer lighting commands for this mouse, so
        // they are declared rather than inherited from a generic mouse. The power settings
        // that only exist in the BLE module are not published over USB.
        Assert.Equal(18, capabilities.Count);
        Assert.DoesNotContain(RazerHidBackendCapability.LkScrollWheelModeWrite, capabilities);
        Assert.Contains(RazerHidBackendCapability.AutoSleepRead, capabilities);
        Assert.Contains(RazerHidBackendCapability.BatteryChargeLimiterRead, capabilities);
        Assert.Equal([125, 500, 1000], device.PollingRates);
        Assert.Contains(RazerHidBackendCapability.PollingRateRead, capabilities);
        Assert.Contains(RazerHidBackendCapability.PollingRateWrite, capabilities);
        Assert.DoesNotContain(RazerHidBackendCapability.FirmwareRead, capabilities);
        Assert.DoesNotContain(RazerHidBackendCapability.SerialRead, capabilities);
        Assert.DoesNotContain(RazerHidBackendCapability.DeviceModeRead, capabilities);
        Assert.Single(zones);
        Assert.Contains(RazerHidLedZone.ScrollWheel, zones.Keys);
        Assert.DoesNotContain(RazerHidLightingEffect.Custom,
            zones[RazerHidLedZone.ScrollWheel].LightingEffects);
    }

    [Theory]
    [InlineData(0x0062)]
    [InlineData(0x0083)]
    [InlineData(0x0094)]
    [InlineData(0x0095)]
    [InlineData(0x009A)]
    [InlineData(0x009C)]
    [InlineData(0x00B4)]
    public void ReplaceableBatteryMiceDoNotAdvertiseCharging(ushort productId)
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, productId)!;
        var capabilities = RazerHidDeviceService.GetCapabilities(device);

        Assert.Contains(RazerHidBackendCapability.BatteryRead, capabilities);
        Assert.DoesNotContain(RazerHidBackendCapability.ChargingRead, capabilities);
    }

    [Theory]
    [InlineData(0x0077)]
    [InlineData(0x00A6)]
    public void OtherMiceSharingTheOriginalCapabilitySetsRetainCharging(ushort productId)
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, productId)!;

        Assert.Contains(RazerHidBackendCapability.ChargingRead,
            RazerHidDeviceService.GetCapabilities(device));
    }

    [Fact]
    public async Task BasiliskBrightnessReadTargetsTheScrollWheel()
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x00B9)!;
        var transport = new RecordingTransport();
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);
        var connection = new RazerHidDeviceConnection(device, "test-path", "test-device",
            RazerHidEndpointState.Resolved, RazerHidDeviceService.GetCapabilities(device),
            RazerHidDeviceService.GetLightingZoneCapabilities(device), null);

        await service.GetBrightnessAsync(connection);

        var request = Assert.Single(transport.Requests);
        Assert.Equal(0x0F, request[7]);
        Assert.Equal(0x84, request[8]);
        Assert.Equal(0x01, request[RazerFeatureReport.ArgumentsOffset]);
        Assert.Equal((byte)RazerHidLedZone.ScrollWheel, request[RazerFeatureReport.ArgumentsOffset + 1]);
    }

    [Theory]
    [InlineData(0x00AA)]
    [InlineData(0x00AB)]
    public async Task BasiliskV3ProBrightnessReadFallsBackWhenAllIsRejected(ushort productId)
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, productId)!;
        var transport = new BrightnessZoneFallbackTransport();
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);
        var connection = new RazerHidDeviceConnection(device, "test-path", "test-device",
            RazerHidEndpointState.Resolved, RazerHidDeviceService.GetCapabilities(device),
            RazerHidDeviceService.GetLightingZoneCapabilities(device), null);

        var brightness = await service.GetBrightnessAsync(connection);

        Assert.Equal(0x2A, brightness);
        Assert.Equal(2, transport.Requests.Count);
        Assert.Equal((byte)RazerHidLedZone.All,
            transport.Requests[0][RazerFeatureReport.ArgumentsOffset + 1]);
        Assert.Equal((byte)RazerHidLedZone.ScrollWheel,
            transport.Requests[1][RazerFeatureReport.ArgumentsOffset + 1]);
    }

    [Theory]
    [InlineData(0x00AA)]
    [InlineData(0x00AB)]
    public async Task BasiliskV3ProBrightnessReadFallsBackWhenAllTimesOut(ushort productId)
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, productId)!;
        var transport = new BrightnessZoneFallbackTransport(timeoutForAll: true);
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);
        var connection = new RazerHidDeviceConnection(device, "test-path", "test-device",
            RazerHidEndpointState.Resolved, RazerHidDeviceService.GetCapabilities(device),
            RazerHidDeviceService.GetLightingZoneCapabilities(device), null);

        var brightness = await service.GetBrightnessAsync(connection);

        Assert.Equal(0x2A, brightness);
        Assert.Equal(2, transport.Requests.Count);
        Assert.Equal((byte)RazerHidLedZone.All,
            transport.Requests[0][RazerFeatureReport.ArgumentsOffset + 1]);
        Assert.Equal((byte)RazerHidLedZone.ScrollWheel,
            transport.Requests[1][RazerFeatureReport.ArgumentsOffset + 1]);
    }

    [Theory]
    [InlineData(0x00AA)]
    [InlineData(0x00AB)]
    public void BasiliskV3ProUsesTheWorkingLowBatteryTransaction(ushort productId)
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, productId)!;

        Assert.Equal(0x1F, device.Transactions["razer_chroma_misc_get_low_battery_threshold"]);
        Assert.Equal(0x1F, device.Transactions["razer_chroma_misc_set_low_battery_threshold"]);
        Assert.Equal(0x1F, RazerHidMouseProtocol.GetLowBatteryThreshold(device).Report[2]);
        Assert.Equal(0x1F, RazerHidMouseProtocol.SetLowBatteryThreshold(device, 15).Report[2]);
    }

    [Fact]
    public async Task PublishedBrightnessCapabilitiesHaveUsableDefaultZones()
    {
        foreach (var device in RazerHidDeviceCatalog.BuiltIn.Devices)
        {
            var capabilities = RazerHidDeviceService.GetCapabilities(device);
            var transport = new RecordingTransport();
            var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);
            var connection = new RazerHidDeviceConnection(device, "test-path", "test-device",
                RazerHidEndpointState.Resolved, capabilities,
                RazerHidDeviceService.GetLightingZoneCapabilities(device), null);

            if (capabilities.Contains(RazerHidBackendCapability.BrightnessRead))
            {
                var error = await Record.ExceptionAsync(async () => await service.GetBrightnessAsync(connection));
                Assert.True(error is null, $"1532:{device.ProductId:X4} brightness read: {error}");
            }
            if (capabilities.Contains(RazerHidBackendCapability.BrightnessWrite))
            {
                var error = await Record.ExceptionAsync(async () => await service.SetBrightnessAsync(connection, 128));
                Assert.True(error is null, $"1532:{device.ProductId:X4} brightness write: {error}");
            }
        }
    }

    [Fact]
    public void ExtendedMatrixKeyboardsPublishTheInheritedBrightnessControl()
    {
        // RazerDeviceBrightnessSuspend adds get_brightness/set_brightness at runtime, so the
        // extracted capability list omits them on these keyboards.
        foreach (var productId in new ushort[] { 0x0258, 0x0271, 0x028D, 0x02A5 })
        {
            var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, productId)!;
            var capabilities = RazerHidDeviceService.GetCapabilities(device);
            var backlight = RazerHidDeviceService.GetLightingZoneCapabilities(device)[RazerHidLedZone.Backlight];
            Assert.Contains(RazerHidBackendCapability.BrightnessRead, capabilities);
            Assert.Contains(RazerHidBackendCapability.BrightnessWrite, capabilities);
            Assert.True(backlight.CanReadBrightness);
            Assert.True(backlight.CanWriteBrightness);
        }
    }

    [Fact]
    public void TimeToDimStaysOnTheKeyboardUsbProtocol()
    {
        var keyboard = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x02A5)!;
        Assert.Contains(
            RazerHidBackendCapability.TimeToDimWrite,
            RazerHidDeviceService.GetCapabilities(keyboard));

        foreach (var device in RazerHidDeviceCatalog.BuiltIn.Devices.Where(
            device => device.Category is DeviceCategory.Mouse or DeviceCategory.Accessory))
        {
            Assert.DoesNotContain(
                RazerHidBackendCapability.TimeToDimWrite,
                RazerHidDeviceService.GetCapabilities(device));
        }
    }

    [Theory]
    [InlineData(125, 0x08)]
    [InlineData(500, 0x02)]
    [InlineData(1000, 0x01)]
    public void BasiliskV3XHyperSpeedPollingRatesUseDeclaredTransaction(int hertz, byte code)
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x00B9)!;
        var request = Assert.Single(RazerHidMouseProtocol.SetPollingRate(device, hertz));

        Assert.Equal(0x1F, request.Report[2]);
        Assert.Equal(0x01, request.Report[6]);
        Assert.Equal(0x00, request.Report[7]);
        Assert.Equal(0x05, request.Report[8]);
        Assert.Equal(code, request.Report[RazerFeatureReport.ArgumentsOffset]);
    }

    [Theory]
    // A mouse that declares get/set_polling_rate must also publish the rate list, otherwise
    // the polling card stays hidden and takes the battery readout down with it.
    [InlineData(0x009C)]
    [InlineData(0x00B9)]
    [InlineData(0x0080)]
    [InlineData(0x00B6)]
    [InlineData(0x00B7)]
    [InlineData(0x00C2)]
    [InlineData(0x00C3)]
    [InlineData(0x00C4)]
    [InlineData(0x00C5)]
    public void DeclaredPollingControlAlwaysPublishesRates(ushort productId)
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, productId)!;
        var capabilities = RazerHidDeviceService.GetCapabilities(device);

        if (capabilities.Contains(RazerHidBackendCapability.PollingRateRead) ||
            capabilities.Contains(RazerHidBackendCapability.PollingRateWrite))
        {
            Assert.NotEmpty(device.PollingRates);
        }
    }

    [Fact]
    public void DeathAdderV2XHyperSpeedPublishesBatteryAndItsPollingRates()
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x009C)!;
        var capabilities = RazerHidDeviceService.GetCapabilities(device);

        Assert.Equal([125, 500, 1000], device.PollingRates);
        Assert.Contains(RazerHidBackendCapability.BatteryRead, capabilities);
        Assert.Contains(RazerHidBackendCapability.PollingRateRead, capabilities);
        Assert.Contains(RazerHidBackendCapability.PollingRateWrite, capabilities);
        Assert.Contains(RazerHidBackendCapability.DpiRead, capabilities);
        Assert.Contains(RazerHidBackendCapability.DpiWrite, capabilities);
    }

    /// <summary>
    /// The DeathAdder V3 Pro family reaches the mouse through two identities. The
    /// 00B6/00B7 pair is the plain Pro body and dongle, and the 00C2/00C3 pair is
    /// the HyperPolling body and dongle. The official bundles list 125/500/1000 for
    /// every body, so the plain pair uses the legacy 00:85/00:05 commands. Only the
    /// 00C3 dongle carries the wireless high-speed commands, which is the same
    /// shape as the Viper V3 Pro dongle (00C1).
    /// </summary>
    [Fact]
    public void DeathAdderV3ProPublishesThePollingRatesItsBundlesDeclare()
    {
        var plain = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x00B6)!;
        var plainDongle = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x00B7)!;
        var hyperPolling = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x00C2)!;
        var hyperPollingDongle = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x00C3)!;

        Assert.Equal([125, 500, 1000], plain.PollingRates);
        Assert.Equal([125, 500, 1000], plainDongle.PollingRates);
        Assert.Equal([125, 500, 1000], hyperPolling.PollingRates);
        Assert.Equal([125, 500, 1000, 2000, 4000, 8000], hyperPollingDongle.PollingRates);

        // The plain pair answers the legacy polling commands only.
        Assert.Contains("razer_chroma_misc_get_polling_rate", plain.Transactions.Keys);
        Assert.Contains("razer_chroma_misc_set_polling_rate", plain.Transactions.Keys);
        Assert.DoesNotContain("razer_chroma_misc_get_polling_rate2", plain.Transactions.Keys);

        // The HyperPolling dongle answers the high-speed pair instead, so the 8 kHz
        // codes reach the device.
        Assert.Contains("razer_chroma_misc_get_polling_rate2", hyperPollingDongle.Transactions.Keys);
        Assert.Contains("razer_chroma_misc_set_polling_rate2", hyperPollingDongle.Transactions.Keys);
        Assert.DoesNotContain("razer_chroma_misc_get_polling_rate", hyperPollingDongle.Transactions.Keys);
    }


    [Theory]
    [InlineData(0x0050, "500,1000")]
    [InlineData(0x0053, "500,1000")]
    [InlineData(0x0059, "125,500,1000")]
    [InlineData(0x005C, "125,500,1000")]
    [InlineData(0x0064, "125,500,1000")]
    [InlineData(0x0065, "500,1000")]
    [InlineData(0x0067, "125,500,1000")]
    [InlineData(0x006C, "125,500,1000")]
    [InlineData(0x006E, "500,1000")]
    [InlineData(0x0070, "125,500,1000")]
    [InlineData(0x0071, "500,1000")]
    [InlineData(0x0073, "125,500,1000")]
    [InlineData(0x0078, "125,500,1000")]
    [InlineData(0x007A, "125,500,1000")]
    [InlineData(0x007C, "125,500,1000")]
    [InlineData(0x0083, "125,500,1000")]
    [InlineData(0x0084, "125,500,1000")]
    [InlineData(0x0086, "125,500,1000")]
    [InlineData(0x008A, "125,500,1000")]
    [InlineData(0x008F, "125,500,1000")]
    [InlineData(0x0094, "125,500,1000")]
    [InlineData(0x0098, "500,1000")]
    [InlineData(0x00A5, "125,500,1000")]
    [InlineData(0x00A7, "125,500,1000")]
    [InlineData(0x00AF, "125,500,1000")]
    [InlineData(0x00B4, "125,500,1000")]
    [InlineData(0x00CC, "125,500,1000")]
    [InlineData(0x00D3, "125,500,1000")]
    [InlineData(0x00D6, "125,500,1000")]
    public void CatalogPollingRatesMatchTheBundleDeviceInfo(ushort productId, string expected)
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, productId)!;
        Assert.Equal(expected, string.Join(",", device.PollingRates));
    }

    /// <summary>
    /// These mice declare polling control in their RazerHid capability sets, but their
    /// catalog rows previously carried no rates. The local middleware bundles list
    /// 125/500/1000 for each of them, so the selector can appear without guessing.
    /// </summary>
    [Theory]
    [InlineData(0x0060)]
    [InlineData(0x0062)]
    [InlineData(0x008C)]
    [InlineData(0x008D)]
    [InlineData(0x0096)]
    [InlineData(0x0099)]
    [InlineData(0x00A1)]
    [InlineData(0x00CB)]
    public void BundleBackedPollingRatesAreNonEmptyAndEncodable(ushort productId)
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, productId)!;

        Assert.NotEmpty(device.PollingRates);
        Assert.Equal([125, 500, 1000], device.PollingRates);
        foreach (var rate in device.PollingRates)
        {
            Assert.Single(RazerHidMouseProtocol.SetPollingRate(device, rate));
        }
    }

    [Fact]
    public void DeathAdderV3HyperSpeedPublishesItsPollingRatesForBothIdentities()
    {
        foreach (var productId in new ushort[] { 0x00C4, 0x00C5 })
        {
            var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, productId)!;
            var capabilities = RazerHidDeviceService.GetCapabilities(device);

            Assert.Equal([125, 500, 1000], device.PollingRates);
            Assert.Contains(RazerHidBackendCapability.PollingRateRead, capabilities);
            Assert.Contains(RazerHidBackendCapability.PollingRateWrite, capabilities);
            Assert.Contains("razer_chroma_misc_set_polling_rate", device.Transactions.Keys);
        }
    }

    [Fact]
    public void HuntsmanV3Pro8kPublishesTheBundleHighSpeedPollingRates()
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x02CF)!;
        var capabilities = RazerHidDeviceService.GetCapabilities(device);

        Assert.Equal([125, 250, 500, 1_000, 2_000, 4_000, 8_000], device.PollingRates);
        Assert.Contains(RazerHidBackendCapability.PollingRateRead, capabilities);
        Assert.Contains(RazerHidBackendCapability.PollingRateWrite, capabilities);
        Assert.Contains("razer_chroma_misc_get_polling_rate2", device.Transactions.Keys);
        Assert.Contains("razer_chroma_misc_set_polling_rate2", device.Transactions.Keys);

        var get = RazerHidMouseProtocol.GetPollingRate(device).Report;
        Assert.Equal((0x02, 0x00, 0xC0), (get[6], get[7], get[8]));
        Assert.Equal(0x00, get[RazerFeatureReport.ArgumentsOffset]);
        Assert.Equal(0x03, RazerHidMouseProtocol.GetPollingRate(device, 0x03).Report[RazerFeatureReport.ArgumentsOffset]);

        var set = Assert.Single(RazerHidMouseProtocol.SetPollingRate(device, 8_000)).Report;
        Assert.Equal((0x02, 0x00, 0x40), (set[6], set[7], set[8]));
        Assert.Equal(0x01, set[10]);

        var profile = RazerHidMouseProtocol.GetActiveProfile(device);
        Assert.NotNull(profile);
        Assert.Equal((0x01, 0x05, 0x84), (profile.Report[6], profile.Report[7], profile.Report[8]));

        var activeProfileSet = Assert.Single(RazerHidMouseProtocol.SetPollingRate(device, 8_000, 0x03)).Report;
        Assert.Equal(0x03, activeProfileSet[9]);
        Assert.Equal(0x01, activeProfileSet[10]);
    }

    /// <summary>
    /// The DeathAdder V2 X HyperSpeed answers the 0x1F command family. RazerHid sends
    /// 0xFF for the low-battery threshold, but the device answers that request with status
    /// 0x04 on every attempt; read and write both work with 0x1F.
    /// </summary>
    [Fact]
    public void DeathAdderV2XHyperSpeedUsesTheTransactionIdsThatWorkOnDevice()
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x009C)!;

        Assert.Equal(0x1F, device.Transactions["razer_chroma_standard_get_firmware_version"]);
        Assert.Equal(0x1F, device.Transactions["razer_chroma_standard_get_serial"]);
        Assert.Equal(0x1F, device.Transactions["razer_chroma_misc_get_battery_level"]);
        Assert.Equal(0xFF, device.Transactions["razer_chroma_standard_get_device_mode"]);
        Assert.Equal(0x1F, device.Transactions["razer_chroma_misc_get_low_battery_threshold"]);
        Assert.Equal(0x1F, device.Transactions["razer_chroma_misc_set_low_battery_threshold"]);
        Assert.Equal(0x1F, RazerHidMouseProtocol.GetLowBatteryThreshold(device).Report[2]);
        Assert.Equal(0x1F, RazerHidMouseProtocol.SetLowBatteryThreshold(device, 15).Report[2]);
        Assert.Equal(0x1F, device.Transactions["razer_chroma_misc_get_dpi_xy"]);
        Assert.Equal(0x1F, device.Transactions["razer_chroma_misc_set_dpi_xy"]);
        Assert.Equal(0x1F, device.Transactions["razer_chroma_misc_get_polling_rate"]);
        Assert.Equal(0x1F, device.Transactions["razer_chroma_misc_set_polling_rate"]);
    }

    [Fact]
    public void DeathAdderV2XHyperSpeedDeclaresNoChargingTransaction()
    {
        // RazerHid answers the charging query for this mouse with a hard-coded zero because
        // the firmware does not implement it.
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x009C)!;

        Assert.False(device.Transactions.ContainsKey("razer_chroma_misc_get_charging_status"));
    }

    [Fact]
    public void EveryPublishedDeviceCapabilityCanConstructItsDefaultRequest()
    {
        foreach (var device in RazerHidDeviceCatalog.BuiltIn.Devices)
        {
            var zones = RazerHidDeviceService.GetLightingZoneCapabilities(device);
            foreach (var capability in RazerHidDeviceService.GetCapabilities(device))
            {
                var exception = Record.Exception(() => Construct(capability, device, zones));
                Assert.True(exception is null,
                    $"1532:{device.ProductId:X4} published {capability} but request construction failed: {exception}");
            }
        }

        static void Construct(
            RazerHidBackendCapability capability,
            RazerHidDeviceDefinition device,
            IReadOnlyDictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities> zones)
        {
            object constructed = capability switch
            {
                RazerHidBackendCapability.FirmwareRead => RazerHidStandardProtocol.GetFirmware(device),
                RazerHidBackendCapability.SerialRead => RazerHidStandardProtocol.GetSerial(device),
                RazerHidBackendCapability.DeviceModeRead => RazerHidStandardProtocol.GetDeviceMode(device),
                RazerHidBackendCapability.DeviceModeWrite => RazerHidStandardProtocol.SetDriverDeviceMode(device),
                RazerHidBackendCapability.BatteryRead => RazerHidMouseProtocol.GetBattery(device),
                RazerHidBackendCapability.ChargingRead => RazerHidMouseProtocol.GetCharging(device),
                RazerHidBackendCapability.PollingRateRead => RazerHidMouseProtocol.GetPollingRate(device),
                RazerHidBackendCapability.PollingRateWrite => RazerHidMouseProtocol.SetPollingRate(device,
                    device.PollingRates.FirstOrDefault(1000)),
                RazerHidBackendCapability.DpiRead => RazerHidMouseProtocol.GetDpi(device),
                RazerHidBackendCapability.DpiWrite => RazerHidMouseProtocol.SetDpi(device,
                    Math.Min(device.MaximumDpi ?? 800, 800), Math.Min(device.MaximumDpi ?? 800, 800)),
                RazerHidBackendCapability.DpiStagesRead => RazerHidMouseProtocol.GetDpiStages(device),
                RazerHidBackendCapability.DpiStagesWrite => RazerHidMouseProtocol.SetDpiStages(device,
                    new RazerHidDpiStages(1, [new RazerHidDpiStage(1, 800, 800)])),
                RazerHidBackendCapability.IdleTimeoutRead => RazerHidMouseProtocol.GetIdleTime(device),
                RazerHidBackendCapability.IdleTimeoutWrite => RazerHidMouseProtocol.SetIdleTime(device, 60),
                RazerHidBackendCapability.LowBatteryThresholdRead => RazerHidMouseProtocol.GetLowBatteryThreshold(device),
                RazerHidBackendCapability.LowBatteryThresholdWrite => RazerHidMouseProtocol.SetLowBatteryThreshold(device, 15),
                RazerHidBackendCapability.AutoSleepRead => RazerHidMouseProtocol.GetAutoSleep(device),
                RazerHidBackendCapability.AutoSleepWrite => RazerHidMouseProtocol.SetAutoSleep(device, true),
                RazerHidBackendCapability.BatteryChargeLimiterRead => RazerHidMouseProtocol.GetBatteryChargeLimiter(device),
                RazerHidBackendCapability.BatteryChargeLimiterWrite => RazerHidMouseProtocol.SetBatteryChargeLimiter(device, 80, true),
                RazerHidBackendCapability.TimeToDimRead => RazerHidMouseProtocol.GetTimeToDim(device),
                RazerHidBackendCapability.TimeToDimWrite => RazerHidMouseProtocol.SetTimeToDim(device, 0, true, 60, 20),
                RazerHidBackendCapability.IndicatorLedConfigurationRead => RazerHidMouseProtocol.GetIndicatorLedConfiguration(device),
                RazerHidBackendCapability.IndicatorLedConfigurationWrite => RazerHidMouseProtocol.SetIndicatorLedConfiguration(device, 0),
                RazerHidBackendCapability.IndicatorLedConfigurationV2Read => RazerHidDongleIndicatorProtocol.Get(device),
                RazerHidBackendCapability.IndicatorLedConfigurationV2Write => RazerHidDongleIndicatorProtocol.Set(device,
                    new RazerHidIndicatorLedConfigurationV2(0, 1, 2)),
                RazerHidBackendCapability.GpuCardDetectionRead => RazerHidMouseProtocol.GetGpuCardDetection(device, 0),
                RazerHidBackendCapability.GpuCardDetectionWrite => RazerHidMouseProtocol.SetGpuCardDetection(device, 0, 1),
                RazerHidBackendCapability.ForceChromaCtrlRead => RazerHidMouseProtocol.GetForceChromaCtrl(device),
                RazerHidBackendCapability.ForceChromaCtrlWrite => RazerHidMouseProtocol.SetForceChromaCtrl(device, 1),
                RazerHidBackendCapability.ProfileDebounceTimeRead => RazerHidMouseProtocol.GetProfileDebounceTime(device, 0),
                RazerHidBackendCapability.ProfileDebounceTimeWrite => RazerHidMouseProtocol.SetProfileDebounceTime(device, 0, 1, 2, 3, 4),
                RazerHidBackendCapability.ChromaTapRead => RazerHidMouseProtocol.GetChromaTap(device, 0),
                RazerHidBackendCapability.ChromaTapWrite => RazerHidMouseProtocol.SetChromaTap(device, 0, 1, 0, 1),
                RazerHidBackendCapability.BatteryDischargeCurveRead => RazerHidMouseProtocol.GetBatteryDischargeCurve(device),
                RazerHidBackendCapability.BatteryDischargeCurveWrite => RazerHidMouseProtocol.SetBatteryDischargeCurve(device, 0),
                RazerHidBackendCapability.MacroStorageInfoRead => RazerHidMouseProtocol.GetMacroStorageInfo(device),
                RazerHidBackendCapability.LkScrollWheelModeRead => RazerHidMouseProtocol.GetLkScrollWheelMode(device),
                RazerHidBackendCapability.LkScrollWheelModeWrite => RazerHidMouseProtocol.SetLkScrollWheelMode(device, 7),
                RazerHidBackendCapability.BrightnessRead => RazerHidLightingProtocol.GetBrightness(device, null,
                    zones.Values.First(zone => zone.CanReadBrightness).Zone),
                RazerHidBackendCapability.BrightnessWrite => RazerHidLightingProtocol.SetBrightness(device, null,
                    zones.Values.First(zone => zone.CanWriteBrightness).Zone, 128),
                RazerHidBackendCapability.LedStateRead => RazerHidStandardProtocol.GetLedState(device,
                    (byte)device.DefaultStorage, (byte)zones.Values.First(zone => zone.CanReadState).Zone),
                RazerHidBackendCapability.LedStateWrite => RazerHidStandardProtocol.SetLedState(device,
                    (byte)device.DefaultStorage, (byte)zones.Values.First(zone => zone.CanWriteState).Zone, true),
                RazerHidBackendCapability.LedEffectRead => RazerHidStandardProtocol.GetLedEffect(device,
                    (byte)device.DefaultStorage, (byte)zones.Values.First(zone => zone.CanReadEffect).Zone),
                RazerHidBackendCapability.LedEffectWrite => RazerHidStandardProtocol.SetLedEffect(device,
                    (byte)device.DefaultStorage, (byte)zones.Values.First(zone => zone.CanWriteEffect).Zone,
                    RazerHidClassicLedEffect.Static),
                RazerHidBackendCapability.LedColorWrite => RazerHidStandardProtocol.SetLedColor(device,
                    (byte)device.DefaultStorage, (byte)zones.Values.First(zone => zone.CanWriteColor).Zone,
                    new RazerHidColor(1, 2, 3)),
                RazerHidBackendCapability.LedBlinkingWrite => RazerHidStandardProtocol.SetLedBlinking(device,
                    (byte)device.DefaultStorage, (byte)zones.Values.First(zone => zone.CanWriteBlinking).Zone),
                RazerHidBackendCapability.LightingEffectWrite => RazerHidLightingProtocol.CreateEffectRequests(device,
                    CreateLightingSettings(zones)),
                RazerHidBackendCapability.MatrixFrameWrite => RazerHidLightingProtocol.SetCustomRow(device, 0, 0,
                    [new RazerHidColor(1, 2, 3)]),
                RazerHidBackendCapability.ReactiveTriggerWrite => RazerHidStandardProtocol.Create(device,
                    "razer_chroma_misc_matrix_reactive_trigger", 0x05, 0x03, 0x0A, [0x02, 0, 0, 0, 0]),
                RazerHidBackendCapability.ScrollModeRead => RazerHidMouseProtocol.GetScrollMode(device),
                RazerHidBackendCapability.ScrollModeWrite => RazerHidMouseProtocol.SetScrollMode(device, 0),
                RazerHidBackendCapability.ScrollAccelerationRead => RazerHidMouseProtocol.GetScrollAcceleration(device),
                RazerHidBackendCapability.ScrollAccelerationWrite => RazerHidMouseProtocol.SetScrollAcceleration(device, false),
                RazerHidBackendCapability.SmartReelRead => RazerHidMouseProtocol.GetSmartReel(device),
                RazerHidBackendCapability.SmartReelWrite => RazerHidMouseProtocol.SetSmartReel(device, false),
                RazerHidBackendCapability.SmartReelThresholdRead => RazerHidMouseProtocol.GetSmartReelThreshold(device),
                RazerHidBackendCapability.SmartReelThresholdWrite => RazerHidMouseProtocol.SetSmartReelThreshold(device, 50),
                RazerHidBackendCapability.FnPrimaryWrite => RazerHidStandardProtocol.Create(device,
                    "razer_chroma_misc_fn_key_toggle", 0x02, 0x02, 0x06, [0, 0]),
                RazerHidBackendCapability.KeyswitchOptimizationRead => RazerHidStandardProtocol.Create(device,
                    "razer_chroma_misc_get_keyswitch_optimization", 0x04, 0x02, 0x82, []),
                RazerHidBackendCapability.KeyswitchOptimizationWrite => CreateKeyswitchRequests(device),
                RazerHidBackendCapability.HyperPollingIndicatorWrite => RazerHidStandardProtocol.Create(device,
                    "razer_chroma_misc_set_hyperpolling_wireless_dongle_indicator_led_mode", 1, 7, 0x10, [1]),
                RazerHidBackendCapability.HyperPollingPairWrite => CreatePairRequests(device),
                RazerHidBackendCapability.HyperPollingUnpairWrite => RazerHidStandardProtocol.Create(device,
                    "razer_chroma_misc_set_hyperpolling_wireless_dongle_unpair", 2, 0, 0x42, [0, 1]),
                RazerHidBackendCapability.MappingRead => RazerHidMappingProtocol.GetButtonList(),
                RazerHidBackendCapability.MappingWrite => RazerHidMappingProtocol.SetAssignment(
                    0x01, RazerHidMappingProtocol.NormalMode, RazerHidMappingFunctions.ButtonCode, [0x01]),
                _ => throw new InvalidOperationException($"Capability {capability} has no construction assertion."),
            };
            _ = constructed;
        }

        static RazerHidLightingSettings CreateLightingSettings(
            IReadOnlyDictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities> zones)
        {
            var zone = zones.Values.First(value => value.LightingEffects.Count > 0);
            return new RazerHidLightingSettings(zone.LightingEffects.First(), Zone: zone.Zone);
        }

        static RazerHidRequest[] CreateKeyswitchRequests(RazerHidDeviceDefinition device) =>
        [
            RazerHidStandardProtocol.Create(device,
                "razer_chroma_misc_set_keyswitch_optimization_command1", 4, 2, 2, [0, 0, 0, 0]),
            RazerHidStandardProtocol.Create(device,
                "razer_chroma_misc_set_keyswitch_optimization_command2", 5, 2, 0x15, [1, 0, 0, 0, 0]),
        ];

        static RazerHidRequest[] CreatePairRequests(RazerHidDeviceDefinition device) =>
        [
            RazerHidStandardProtocol.Create(device,
                "razer_chroma_misc_set_hyperpolling_wireless_dongle_pair_step1", 1, 0, 0x46, [1]),
            RazerHidStandardProtocol.Create(device,
                "razer_chroma_misc_set_hyperpolling_wireless_dongle_pair_step2", 3, 0, 0x41, [1, 0, 1]),
        ];
    }

    [Fact]
    public void EveryPublishedLightingZoneControlCanConstructItsRequest()
    {
        var failures = new List<string>();
        foreach (var device in RazerHidDeviceCatalog.BuiltIn.Devices)
        foreach (var (zone, capabilities) in RazerHidDeviceService.GetLightingZoneCapabilities(device))
        {
            var operations = new (bool Enabled, string Name, Func<object> Build)[]
            {
                (capabilities.CanReadBrightness, "brightness read", () =>
                    RazerHidLightingProtocol.GetBrightness(device, device.DefaultStorage, zone)),
                (capabilities.CanWriteBrightness, "brightness write", () =>
                    RazerHidLightingProtocol.SetBrightness(device, device.DefaultStorage, zone, 128)),
                (capabilities.CanReadState, "LED state read", () =>
                    RazerHidStandardProtocol.GetLedState(device, (byte)device.DefaultStorage, (byte)zone)),
                (capabilities.CanWriteState, "LED state write", () =>
                    RazerHidStandardProtocol.SetLedState(device, (byte)device.DefaultStorage, (byte)zone, true)),
                (capabilities.CanReadEffect, "LED effect read", () =>
                    RazerHidStandardProtocol.GetLedEffect(device, (byte)device.DefaultStorage, (byte)zone)),
                (capabilities.CanWriteEffect, "LED effect write", () =>
                    RazerHidStandardProtocol.SetLedEffect(device, (byte)device.DefaultStorage, (byte)zone,
                        RazerHidClassicLedEffect.Static)),
                (capabilities.CanWriteColor, "LED color write", () =>
                    RazerHidStandardProtocol.SetLedColor(device, (byte)device.DefaultStorage, (byte)zone,
                        new RazerHidColor(1, 2, 3))),
                (capabilities.CanWriteBlinking, "LED blinking write", () =>
                    RazerHidStandardProtocol.SetLedBlinking(device, (byte)device.DefaultStorage, (byte)zone)),
            };
            foreach (var operation in operations.Where(operation => operation.Enabled))
            {
                var error = Record.Exception(() => operation.Build());
                if (error is not null)
                    failures.Add($"1532:{device.ProductId:X4} {zone} {operation.Name}: {error.Message}");
            }
            foreach (var effect in capabilities.LightingEffects)
            {
                var error = Record.Exception(() => RazerHidLightingProtocol.CreateEffectRequests(device,
                    new RazerHidLightingSettings(effect, Zone: zone)));
                if (error is not null)
                    failures.Add($"1532:{device.ProductId:X4} {zone} {effect}: {error.Message}");
            }
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Theory]
    [InlineData(0x0044)]
    [InlineData(0x0203)]
    public void ExplicitDefaultCustomZoneUsesTheSameSelector(ushort productId)
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, productId)!;
        var implicitRequest = Assert.Single(RazerHidLightingProtocol.CreateEffectRequests(device,
            new RazerHidLightingSettings(RazerHidLightingEffect.Custom)));
        var explicitRequest = Assert.Single(RazerHidLightingProtocol.CreateEffectRequests(device,
            new RazerHidLightingSettings(RazerHidLightingEffect.Custom, Zone: device.DefaultLedZone)));

        Assert.Equal(implicitRequest.Report, explicitRequest.Report);
        Assert.Throws<NotSupportedException>(() => RazerHidLightingProtocol.CreateEffectRequests(device,
            new RazerHidLightingSettings(RazerHidLightingEffect.Custom, Zone: RazerHidLedZone.Logo)));
    }

    [Fact]
    public void EverySupportedGenericSourceEffectHasOneDefaultBinding()
    {
        var mappings = new (string Capability, string BuilderSuffix, RazerHidLightingEffect Effect)[]
        {
            ("set_none_effect", "matrix_effect_none", RazerHidLightingEffect.Off),
            ("set_static_effect", "matrix_effect_static", RazerHidLightingEffect.Static),
            ("set_spectrum_effect", "matrix_effect_spectrum", RazerHidLightingEffect.Spectrum),
            ("set_wave_effect", "matrix_effect_wave", RazerHidLightingEffect.Wave),
            ("set_reactive_effect", "matrix_effect_reactive", RazerHidLightingEffect.Reactive),
            ("set_breath_random_effect", "matrix_effect_breathing_random", RazerHidLightingEffect.BreathingRandom),
            ("set_breath_single_effect", "matrix_effect_breathing_single", RazerHidLightingEffect.BreathingSingle),
            ("set_breath_dual_effect", "matrix_effect_breathing_dual", RazerHidLightingEffect.BreathingDual),
            ("set_starlight_random_effect", "matrix_effect_starlight_random", RazerHidLightingEffect.StarlightRandom),
            ("set_starlight_single_effect", "matrix_effect_starlight_single", RazerHidLightingEffect.StarlightSingle),
            ("set_starlight_dual_effect", "matrix_effect_starlight_dual", RazerHidLightingEffect.StarlightDual),
            ("set_wheel_effect", "matrix_effect_wheel", RazerHidLightingEffect.Wheel),
            ("set_custom_effect", "matrix_effect_custom_frame", RazerHidLightingEffect.Custom),
        };

        foreach (var device in RazerHidDeviceCatalog.BuiltIn.Devices)
        {
            foreach (var mapping in mappings.Where(mapping =>
                device.HasSourceCapability(mapping.Capability) &&
                device.Transactions.Keys.Any(builder => builder.EndsWith(mapping.BuilderSuffix, StringComparison.Ordinal))))
            {
                var exception = Record.Exception(() => RazerHidLightingProtocol.CreateEffect(device,
                    new RazerHidLightingSettings(mapping.Effect)));
                Assert.True(exception is null,
                    $"1532:{device.ProductId:X4} source capability {mapping.Capability} has no default binding: {exception}");
            }
        }
    }

    [Fact]
    public void EverySupportedPerZoneSourceEffectHasOneExplicitBinding()
    {
        var zones = new (string Name, RazerHidLedZone Zone)[]
        {
            ("logo", RazerHidLedZone.Logo),
            ("scroll", RazerHidLedZone.ScrollWheel),
            ("backlight", RazerHidLedZone.Backlight),
            ("left", RazerHidLedZone.LeftSide),
            ("right", RazerHidLedZone.RightSide),
            ("charging", RazerHidLedZone.Charging),
            ("fast_charging", RazerHidLedZone.FastCharging),
            ("fully_charged", RazerHidLedZone.FullyCharged),
        };
        var effects = new (string Suffix, string BuilderSuffix, RazerHidLightingEffect Effect)[]
        {
            ("none", "matrix_effect_none", RazerHidLightingEffect.Off),
            ("static", "matrix_effect_static", RazerHidLightingEffect.Static),
            ("spectrum", "matrix_effect_spectrum", RazerHidLightingEffect.Spectrum),
            ("wave", "matrix_effect_wave", RazerHidLightingEffect.Wave),
            ("reactive", "matrix_effect_reactive", RazerHidLightingEffect.Reactive),
            ("breath_random", "matrix_effect_breathing_random", RazerHidLightingEffect.BreathingRandom),
            ("breath_mono", "matrix_effect_breathing_single", RazerHidLightingEffect.BreathingSingle),
            ("breath_single", "matrix_effect_breathing_single", RazerHidLightingEffect.BreathingSingle),
            ("breath_dual", "matrix_effect_breathing_dual", RazerHidLightingEffect.BreathingDual),
        };

        foreach (var device in RazerHidDeviceCatalog.BuiltIn.Devices)
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

                var exception = Record.Exception(() => RazerHidLightingProtocol.CreateEffect(device,
                    new RazerHidLightingSettings(effect.Effect, Zone: zone.Zone)));
                Assert.True(exception is null,
                    $"1532:{device.ProductId:X4} source capability {capability} has no explicit binding: {exception}");
            }
        }
    }

    private sealed class BrightnessZoneFallbackTransport(bool timeoutForAll = false) : IRazerFeatureTransport
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
            Requests.Add(request.ToArray());
            if (request.Span[7] == 0x0F && request.Span[8] == 0x84 &&
                request.Span[RazerFeatureReport.ArgumentsOffset + 1] == (byte)RazerHidLedZone.All)
            {
                if (timeoutForAll)
                {
                    throw new RazerDeviceTimeoutException("The device stayed busy (0x01).", retryableBusy: true);
                }
                throw new RazerCommandRejectedException("The device rejected the query (0x03).");
            }

            var response = request.ToArray();
            response[1] = 0x02;
            response[6] = 0x03;
            response[RazerFeatureReport.ArgumentsOffset + 2] = 0x2A;
            response[89] = RazerFeatureReport.CalculateCrc(response);
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task MappingWriteRepairsNormalDeviceModeFirst()
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x00BE)!;
        var transport = new NormalModeTransport();
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);
        var connection = new RazerHidDeviceConnection(device, "test-path", "test-device",
            RazerHidEndpointState.Resolved,
            new HashSet<RazerHidBackendCapability>
            {
                RazerHidBackendCapability.MappingWrite,
                RazerHidBackendCapability.DeviceModeWrite,
            },
            new Dictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities>(), null);

        // The transport answers normal mode and never stores the assignment, so the
        // final readback cannot match. This pins the order Synapse uses before a write.
        await Record.ExceptionAsync(() => service.SetButtonAssignmentAsync(
            connection, buttonId: 0x04, mode: 0x00, functionId: 0x01, data: [0x01]));

        var modeWrite = transport.Requests.FindIndex(request =>
            request[7] == 0x00 && request[8] == 0x04 && request[9] == 0x03);
        var assignmentWrite = transport.Requests.FindIndex(request =>
            request[7] == 0x02 && request[8] == 0x0C);
        Assert.True(modeWrite >= 0, "the driver-mode write must be sent");
        Assert.True(assignmentWrite > modeWrite, "the assignment must follow the mode repair");
    }

    /// <summary>Answers get-device-mode with normal mode and echoes every other frame.</summary>
    private sealed class NormalModeTransport : IRazerFeatureTransport
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
            Requests.Add(request.ToArray());
            var response = request.ToArray();
            response[1] = 0x02;
            if (request.Span[7] == 0x00 && request.Span[8] == 0x84)
            {
                response[6] = 0x02;
                response[9] = 0x00;
            }
            response[89] = RazerFeatureReport.CalculateCrc(response);
            return Task.FromResult(response);
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

    private sealed class ZeroDpiTransport : IRazerFeatureTransport
    {
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
            response[1] = 0x02;
            return Task.FromResult(response);
        }
    }

    private sealed class SerialTransport : IRazerFeatureTransport
    {
        public Task<byte[]> QueryAsync(string devicePath, byte transactionId, byte dataSize,
            byte commandClass, byte commandId, ReadOnlyMemory<byte> arguments, TimeSpan deviceWait,
            CancellationToken cancellationToken, bool allowRemainingPacketsMismatch = false) =>
            throw new NotSupportedException();

        public Task<byte[]> QueryPreparedAsync(string devicePath, ReadOnlyMemory<byte> request,
            TimeSpan deviceWait, CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false)
        {
            var response = request.ToArray();
            response[1] = 0x02;
            "RAZER-123"u8.CopyTo(response.AsSpan(RazerFeatureReport.ArgumentsOffset));
            response[89] = RazerFeatureReport.CalculateCrc(response);
            return Task.FromResult(response);
        }
    }

    private sealed class OptionalChargingRejectionTransport(Exception? chargingFailure = null) : IRazerFeatureTransport
    {
        internal int ChargingQueries { get; private set; }

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
            if (request.Span[8] == 0x84)
            {
                ChargingQueries++;
                throw chargingFailure ?? new NotSupportedException("The device rejected the query (0x03).");
            }

            var response = request.ToArray();
            response[1] = 0x02;
            response[RazerFeatureReport.ArgumentsOffset + 1] = 244;
            response[89] = RazerFeatureReport.CalculateCrc(response);
            return Task.FromResult(response);
        }
    }

    private sealed class FailingReadTransport(Exception failure) : IRazerFeatureTransport
    {
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
            throw failure;

        public Task<byte[]> QueryPreparedAsync(
            string devicePath,
            ReadOnlyMemory<byte> request,
            TimeSpan deviceWait,
            CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false) =>
            throw failure;
    }
}
