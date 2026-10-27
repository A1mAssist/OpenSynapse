using OpenSynapse.Windows.Devices;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class OpenRazerBackendTests
{
    /// <summary>
    /// 1532:005F and 1532:004D are reported as DeathAdder 2000 variants that OpenRazer does not
    /// catalogue. Their entries exist only so a probe can read the firmware; without this the
    /// owner gets no device page at all. Every write builder must stay absent until a 005F owner
    /// returns real evidence, so the probe cannot change a setting even if the UI offers one.
    /// </summary>
    [Theory]
    [InlineData(0x005F)]
    [InlineData(0x004D)]
    public void TheLocallyAdmittedDeathAdderProbesExposeReadsAndNoWrites(ushort productId)
    {
        var catalog = OpenRazerDeviceCatalog.BuiltIn;
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
        var catalog = OpenRazerDeviceCatalog.BuiltIn;

        // 259 upstream entries plus two DeathAdder probes, two Viper V3 Pro SE aliases,
        // the locally admitted 054A Leviathan V2 X family, and 18 middleware mouse
        // product ids (wired and dongle halves included).
        Assert.Equal(282, catalog.Devices.Count);
        Assert.Equal(282, catalog.Devices.Select(device => device.ProductId).Distinct().Count());
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
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x054A);

        Assert.NotNull(device);
        Assert.Equal("Razer Leviathan V2 X", device!.DisplayName);
        Assert.Equal(DeviceCategory.Accessory, device.Category);
        Assert.Equal(TimeSpan.FromMilliseconds(5), device.DeviceWait);
        Assert.Equal(OpenRazerStorage.Temporary, device.DefaultStorage);
        Assert.Contains("razer_chroma_extended_matrix_get_region_ids", device.Transactions.Keys);
        Assert.Contains("get_firmware", device.SourceCapabilities);
        Assert.Contains("get_serial", device.SourceCapabilities);
        Assert.Contains("get_brightness", device.SourceCapabilities);
        Assert.Contains("set_brightness", device.SourceCapabilities);
        Assert.DoesNotContain("set_static_effect", device.SourceCapabilities);

        var capabilities = OpenRazerDeviceService.GetCapabilities(device);
        Assert.Contains(OpenRazerBackendCapability.FirmwareRead, capabilities);
        Assert.Contains(OpenRazerBackendCapability.BrightnessRead, capabilities);
        Assert.Contains(OpenRazerBackendCapability.BrightnessWrite, capabilities);
        Assert.DoesNotContain(OpenRazerBackendCapability.LightingEffectWrite, capabilities);
        Assert.DoesNotContain(OpenRazerBackendCapability.MatrixFrameWrite, capabilities);

        var renamed = device.WithDisplayName("Razer | Kuromi Limited Edition Soundbar");
        Assert.Equal("Razer | Kuromi Limited Edition Soundbar", renamed.DisplayName);
        Assert.Equal(device.DefaultStorage, renamed.DefaultStorage);
        Assert.Same(device.Transactions, renamed.Transactions);
        Assert.True(capabilities.SetEquals(OpenRazerDeviceService.GetCapabilities(renamed)));
    }

    [Theory]
    [InlineData(@"\\?\hid#vid_1532&pid_054a&mi_01#test", true)]
    [InlineData(@"\\?\hid#vid_1532&pid_054b&mi_01#test", false)]
    public void OnlyLeviathan054AUsesReportIdSeven(string devicePath, bool expected)
    {
        Assert.Equal(expected, RazerFeatureTransport.UsesLeviathanReportId(devicePath));
    }

    [Theory]
    [InlineData(0x00DE, 0x00C0, "Razer Viper V3 Pro SE (Wired)")]
    [InlineData(0x00DF, 0x00C1, "Razer Viper V3 Pro SE (Wireless)")]
    public void ViperV3ProSeAliasesUseTheMatchingPollingProtocol(
        ushort productId,
        ushort siblingProductId,
        string displayName)
    {
        var catalog = OpenRazerDeviceCatalog.BuiltIn;
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

            var get = OpenRazerMouseProtocol.GetPollingRate(alias).Report;
            Assert.Equal(0x1F, get[2]);
            Assert.Equal(0x01, get[6]);
            Assert.Equal(0x00, get[7]);
            Assert.Equal(0x85, get[8]);

            var set = Assert.Single(OpenRazerMouseProtocol.SetPollingRate(alias, 1000));
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
    public void ViperV3ProWirelessPacketsMatchMiddleware()
    {
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x00C1)!;

        var dpiStages = OpenRazerMouseProtocol.GetDpiStages(device).Report;
        Assert.Equal(0x50, dpiStages[6]);
        Assert.Equal(0x04, dpiStages[7]);
        Assert.Equal(0x86, dpiStages[8]);
        Assert.Equal(0x01, dpiStages[RazerFeatureReport.ArgumentsOffset]);

        var dpiStagesSet = OpenRazerMouseProtocol.SetDpiStages(device, new OpenRazerDpiStages(
            1,
            [
                new(1, 800, 800),
                new(2, 1600, 1600),
                new(3, 2400, 2400),
                new(4, 3200, 3200),
                new(5, 4000, 4000),
            ])).Report;
        Assert.Equal(0x50, dpiStagesSet[6]);
        Assert.Equal(0x06, dpiStagesSet[8]);
        Assert.Equal(0x01, dpiStagesSet[RazerFeatureReport.ArgumentsOffset]);

        var polling = OpenRazerMouseProtocol.GetPollingRate(device).Report;
        Assert.Equal(0x02, polling[6]);
        Assert.Equal(0xC0, polling[8]);
        Assert.Equal(0x01, polling[RazerFeatureReport.ArgumentsOffset]);

        var pollingSet = Assert.Single(OpenRazerMouseProtocol.SetPollingRate(device, 1000));
        Assert.Equal(0x02, pollingSet.Report[6]);
        Assert.Equal(0x40, pollingSet.Report[8]);
        Assert.Equal(0x01, pollingSet.Report[RazerFeatureReport.ArgumentsOffset]);
        Assert.Equal(0x08, pollingSet.Report[RazerFeatureReport.ArgumentsOffset + 1]);
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
    public void DpiStagesFollowTheMiddlewareLayoutWithAZAxis()
    {
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x00B9)!;
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

        var stages = OpenRazerMouseProtocol.ParseDpiStages(device, response);

        Assert.Equal(1, stages.ActiveStage);
        Assert.Equal(
            [
                new OpenRazerDpiStage(0, 800, 800, 31317),
                new OpenRazerDpiStage(1, 1500, 1500, 4660),
            ],
            stages.Stages);
    }

    [Fact]
    public async Task OptionalChargingRejectionDoesNotInvalidateBasiliskState()
    {
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x00B9)!;
        var transport = new OptionalChargingRejectionTransport();
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);
        DeviceBatteryReading? observed = null;
        service.BatteryObserved += reading => observed = reading;
        var connection = new OpenRazerDeviceConnection(device, "test-path", "test-device",
            OpenRazerEndpointState.Resolved,
            new HashSet<OpenRazerBackendCapability>
            {
                OpenRazerBackendCapability.BatteryRead,
                OpenRazerBackendCapability.ChargingRead,
            },
            new Dictionary<OpenRazerLedZone, OpenRazerLightingZoneCapabilities>(), null);

        var state = await service.ReadBasicStateAsync(connection);

        Assert.Equal(96, state.BatteryPercent);
        Assert.Equal(96, observed?.Percent);
        Assert.Equal(device.DisplayName, observed?.DeviceName);
        Assert.Null(observed?.IsCharging);
        Assert.Null(state.IsCharging);
        Assert.Empty(state.Errors);
        Assert.Contains(OpenRazerBackendCapability.ChargingRead, state.UnsupportedCapabilities);
        Assert.DoesNotContain(OpenRazerBackendCapability.BatteryRead, state.UnsupportedCapabilities);

        await service.ReadBasicStateAsync(connection, excludedCapabilities: state.UnsupportedCapabilities);
        Assert.Equal(1, transport.ChargingQueries);
    }

    [Fact]
    public async Task MalformedOptionalFieldDoesNotInvalidateBasicState()
    {
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x00B9)!;
        var transport = new OptionalChargingRejectionTransport(
            new InvalidDataException("Malformed charging report."));
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);
        var connection = new OpenRazerDeviceConnection(device, "test-path", "test-device",
            OpenRazerEndpointState.Resolved,
            new HashSet<OpenRazerBackendCapability>
            {
                OpenRazerBackendCapability.BatteryRead,
                OpenRazerBackendCapability.ChargingRead,
            },
            new Dictionary<OpenRazerLedZone, OpenRazerLightingZoneCapabilities>(), null);

        var state = await service.ReadBasicStateAsync(connection);

        Assert.Equal(96, state.BatteryPercent);
        Assert.Null(state.IsCharging);
        Assert.Empty(state.Errors);
        Assert.Contains(OpenRazerBackendCapability.ChargingRead, state.UnsupportedCapabilities);
    }

    [Fact]
    public async Task SilentBatteryCommandDoesNotInvalidateTheRestOfTheDevice()
    {
        // The DeathAdder V4 Pro answers every command on its 2.4 GHz endpoint except
        // get_battery_level, which returns status 0x04. That single silence used to land in
        // Errors, which pauses every write on the device and leaves the card unusable.
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x00BF)!;
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn,
            new OptionalChargingRejectionTransport(
                new RazerDeviceTimeoutException("The device did not answer this command (0x04).")));
        var connection = new OpenRazerDeviceConnection(device, "test-path", "test-device",
            OpenRazerEndpointState.Resolved,
            new HashSet<OpenRazerBackendCapability>
            {
                OpenRazerBackendCapability.BatteryRead,
                OpenRazerBackendCapability.ChargingRead,
            },
            new Dictionary<OpenRazerLedZone, OpenRazerLightingZoneCapabilities>(), null);

        var state = await service.ReadBasicStateAsync(connection);

        Assert.Equal(96, state.BatteryPercent);
        Assert.Null(state.IsCharging);
        Assert.Empty(state.Errors);
        Assert.Contains(OpenRazerBackendCapability.ChargingRead, state.UnsupportedCapabilities);
    }

    [Fact]
    public async Task TransportFailureRemainsARescanError()
    {
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x00B9)!;
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn,
            new FailingReadTransport(new IOException("Device disconnected.")));
        var batteryObservations = 0;
        service.BatteryObserved += _ => batteryObservations++;
        var connection = new OpenRazerDeviceConnection(device, "test-path", "test-device",
            OpenRazerEndpointState.Resolved,
            new HashSet<OpenRazerBackendCapability> { OpenRazerBackendCapability.ChargingRead },
            new Dictionary<OpenRazerLedZone, OpenRazerLightingZoneCapabilities>(), null);

        var state = await service.ReadBasicStateAsync(connection);

        Assert.Equal("Device disconnected.", state.Errors[nameof(OpenRazerBackendCapability.ChargingRead)]);
        Assert.Empty(state.UnsupportedCapabilities);
        Assert.Equal(0, batteryObservations);
    }

    [Fact]
    public async Task FailedChargingReadDoesNotEmitAnUnreliableDischargingObservation()
    {
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x00B9)!;
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn,
            new OptionalChargingRejectionTransport(new IOException("Receiver unavailable")));
        var observations = 0;
        service.BatteryObserved += _ => observations++;
        var connection = new OpenRazerDeviceConnection(device, "test-path", "test-device",
            OpenRazerEndpointState.Resolved,
            new HashSet<OpenRazerBackendCapability>
            {
                OpenRazerBackendCapability.BatteryRead,
                OpenRazerBackendCapability.ChargingRead,
            }, new Dictionary<OpenRazerLedZone, OpenRazerLightingZoneCapabilities>(), null);
        var state = await service.ReadBasicStateAsync(connection);
        Assert.Equal(96, state.BatteryPercent);
        Assert.Contains(nameof(OpenRazerBackendCapability.ChargingRead), state.Errors.Keys);
        Assert.Equal(0, observations);
    }

    [Fact]
    public async Task FailedReadsAreKeyedByCapabilitySoTheUiCanLineThemUp()
    {
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x00B9)!;
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn,
            new FailingReadTransport(new IOException("Device disconnected.")));
        var connection = new OpenRazerDeviceConnection(device, "test-path", "test-device",
            OpenRazerEndpointState.Resolved,
            new HashSet<OpenRazerBackendCapability> { OpenRazerBackendCapability.ChargingRead },
            new Dictionary<OpenRazerLedZone, OpenRazerLightingZoneCapabilities>(), null);

        var state = await service.ReadBasicStateAsync(connection);

        // The diagnostic page lists protocol rows by capability name, so a failure has to
        // carry the same key or the failing row still reads as "read".
        Assert.Equal(["ChargingRead"], state.Errors.Keys);
    }

    [Fact]
    public async Task SerialReadIsShownOnlyAfterSuccessfulOptionalQuery()
    {
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x0203)!;
        Assert.Contains("razer_chroma_standard_get_serial", device.Transactions.Keys);
        var connection = new OpenRazerDeviceConnection(device, "test-path", "test-device",
            OpenRazerEndpointState.Resolved,
            new HashSet<OpenRazerBackendCapability>(),
            new Dictionary<OpenRazerLedZone, OpenRazerLightingZoneCapabilities>(), null);

        var read = await new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn,
            new SerialTransport()).ReadBasicStateAsync(connection);
        Assert.Equal("RAZER-123", read.Serial);
        Assert.Empty(read.Errors);

        var rejected = await new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn,
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
        var viperV3Pro = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x00C1)!;
        var legacy = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x0015)!;

        Assert.Equal(0x01, OpenRazerMouseProtocol.GetDpi(imperator).Report[RazerFeatureReport.ArgumentsOffset]);
        Assert.Equal(0x00, OpenRazerMouseProtocol.GetDpi(viper8K).Report[RazerFeatureReport.ArgumentsOffset]);
        Assert.Equal(0x01, OpenRazerMouseProtocol.SetDpi(imperator, 1600, 1600).Report[RazerFeatureReport.ArgumentsOffset]);
        Assert.Equal(0x01, OpenRazerMouseProtocol.SetDpi(viper8K, 1600, 1600).Report[RazerFeatureReport.ArgumentsOffset]);
        Assert.Equal(0x01, OpenRazerMouseProtocol.GetDpi(viperV3Pro).Report[RazerFeatureReport.ArgumentsOffset]);
        Assert.Equal(0x01, OpenRazerMouseProtocol.SetDpi(viperV3Pro, 1600, 1600).Report[RazerFeatureReport.ArgumentsOffset]);

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

        Assert.False(zones[OpenRazerLedZone.All].CanWriteBrightness);
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

    [Fact]
    public async Task EnsureDriverMode_WritesDriverModeOnlyWhenTheDeviceIsInNormalMode()
    {
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x00BE)!;
        var transport = new DeviceModeTransport(0x00);
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);
        var connection = new OpenRazerDeviceConnection(device, "test-path", "test-device",
            OpenRazerEndpointState.Resolved,
            new HashSet<OpenRazerBackendCapability>
            {
                OpenRazerBackendCapability.DeviceModeRead,
                OpenRazerBackendCapability.DeviceModeWrite,
            },
            new Dictionary<OpenRazerLedZone, OpenRazerLightingZoneCapabilities>(), null);

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
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x00BE)!;
        var transport = new DeviceModeTransport(0x03);
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);
        var connection = new OpenRazerDeviceConnection(device, "test-path", "test-device",
            OpenRazerEndpointState.Resolved,
            new HashSet<OpenRazerBackendCapability>
            {
                OpenRazerBackendCapability.DeviceModeRead,
                OpenRazerBackendCapability.DeviceModeWrite,
            },
            new Dictionary<OpenRazerLedZone, OpenRazerLightingZoneCapabilities>(), null);

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
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x00BE)!;
        var transport = new DeviceModeTransport(mode);
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);
        var connection = new OpenRazerDeviceConnection(device, "test-path", "test-device",
            OpenRazerEndpointState.Resolved,
            new HashSet<OpenRazerBackendCapability> { OpenRazerBackendCapability.DeviceModeWrite },
            new Dictionary<OpenRazerLedZone, OpenRazerLightingZoneCapabilities>(), null);

        Assert.False(await service.EnsureDriverModeAsync(connection));

        Assert.Single(transport.Requests);
    }

    [Fact]
    public async Task EnsureDriverMode_SkipsDevicesWithoutTheWriteCapability()
    {
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x00BE)!;
        var transport = new DeviceModeTransport(0x00);
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);
        var connection = new OpenRazerDeviceConnection(device, "test-path", "test-device",
            OpenRazerEndpointState.Resolved, new HashSet<OpenRazerBackendCapability>(),
            new Dictionary<OpenRazerLedZone, OpenRazerLightingZoneCapabilities>(), null);

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

    [Fact]
    public void RejectedCollectionDescriptionNamesThePathAndDescriptor()
    {
        // Covers the message shown when no 91-byte control collection is usable: a wireless
        // mouse can leave a wired collection enumerated whose descriptor does not match, and
        // "busy or unavailable" alone cannot explain the device to its owner.
        const string path = @"\\?\hid#vid_1532&pid_00bf&mi_02&col02#7&1a2b&0&0000#{guid}";
        var message = OpenRazerDeviceService.DescribeRejected([
            new(path, "container:key", 0x1532, 0x00BF, 8, 0, 64, 0x0001, 0x0002,
                DeviceAccessState.BusyOrUnavailable, "Razer Viper"),
        ]);

        Assert.Equal(
            "vid_1532&pid_00bf&mi_02&col02 feature=64 usage=0x0001:0x0002",
            message);
    }

    [Fact]
    public void UnlistedGenericTransactionsDoNotBecomeProductCapabilities()
    {
        var published = OpenRazerDeviceCatalog.BuiltIn.Devices
            .SelectMany(OpenRazerDeviceService.GetCapabilities)
            .ToHashSet();

        Assert.Contains(OpenRazerBackendCapability.BatteryRead, published);
        Assert.Contains(OpenRazerBackendCapability.LightingEffectWrite, published);
        Assert.DoesNotContain(OpenRazerBackendCapability.SerialRead, published);
        Assert.DoesNotContain(OpenRazerBackendCapability.FnPrimaryWrite, published);
    }

    [Fact]
    public void CapabilitySnapshotDistinguishesUnknownAndMatrixOnlyLayouts()
    {
        var unknown = OpenRazerDeviceCatalog.BuiltIn.Devices
            .First(device => device.MatrixDimensions is null);
        Assert.Equal(OpenRazerLayoutSupport.None, unknown.LayoutSupport);

        var matrix = OpenRazerDeviceCatalog.BuiltIn.Devices.FirstOrDefault(device =>
            device.MatrixDimensions is not null && device.HasSourceCapability("set_key_row"));
        Assert.NotNull(matrix);
        Assert.Equal(OpenRazerLayoutSupport.MatrixOnly, matrix.LayoutSupport);

        var unknownConnection = new OpenRazerDeviceConnection(
            unknown, "unknown", "unknown", OpenRazerEndpointState.Resolved,
            new HashSet<OpenRazerBackendCapability>(),
            new Dictionary<OpenRazerLedZone, OpenRazerLightingZoneCapabilities>(), null);
        Assert.False(unknownConnection.CapabilitySnapshot.SupportsMatrixFrames);
        Assert.False(unknownConnection.CapabilitySnapshot.SupportsNamedKeyEffects);

        var matrixConnection = new OpenRazerDeviceConnection(
            matrix, "matrix", "matrix", OpenRazerEndpointState.Resolved,
            new HashSet<OpenRazerBackendCapability> { OpenRazerBackendCapability.MatrixFrameWrite },
            new Dictionary<OpenRazerLedZone, OpenRazerLightingZoneCapabilities>(), null);
        Assert.True(matrixConnection.CapabilitySnapshot.SupportsMatrixFrames);
        Assert.False(matrixConnection.CapabilitySnapshot.SupportsNamedKeyEffects);
    }

    [Fact]
    public void EveryPublishedProductControlIsDeclaredByItsOpenRazerCapabilityList()
    {
        foreach (var device in OpenRazerDeviceCatalog.BuiltIn.Devices)
        {
            var published = OpenRazerDeviceService.GetCapabilities(device);
            foreach (var (capability, source) in new (OpenRazerBackendCapability Capability, string[] Source)[]
            {
                (OpenRazerBackendCapability.FirmwareRead, ["get_firmware"]),
                (OpenRazerBackendCapability.BatteryRead, ["get_battery"]),
                (OpenRazerBackendCapability.ChargingRead, ["is_charging"]),
                (OpenRazerBackendCapability.PollingRateRead, ["get_poll_rate"]),
                (OpenRazerBackendCapability.PollingRateWrite, ["set_poll_rate"]),
                (OpenRazerBackendCapability.DpiRead, ["get_dpi_xy", "get_dpi_xy_byte"]),
                (OpenRazerBackendCapability.DpiWrite, ["set_dpi_xy", "set_dpi_xy_byte"]),
                (OpenRazerBackendCapability.DpiStagesRead, ["get_dpi_stages"]),
                (OpenRazerBackendCapability.DpiStagesWrite, ["set_dpi_stages"]),
                (OpenRazerBackendCapability.IdleTimeoutRead, ["get_idle_time"]),
                (OpenRazerBackendCapability.IdleTimeoutWrite, ["set_idle_time"]),
                (OpenRazerBackendCapability.LowBatteryThresholdRead, ["get_low_battery_threshold"]),
                (OpenRazerBackendCapability.LowBatteryThresholdWrite, ["set_low_battery_threshold"]),
                (OpenRazerBackendCapability.MatrixFrameWrite, ["set_key_row"]),
                (OpenRazerBackendCapability.ReactiveTriggerWrite, ["trigger_reactive_effect"]),
                (OpenRazerBackendCapability.ScrollModeRead, ["get_scroll_mode"]),
                (OpenRazerBackendCapability.ScrollModeWrite, ["set_scroll_mode"]),
                (OpenRazerBackendCapability.ScrollAccelerationRead, ["get_scroll_acceleration"]),
                (OpenRazerBackendCapability.ScrollAccelerationWrite, ["set_scroll_acceleration"]),
                (OpenRazerBackendCapability.SmartReelRead, ["get_scroll_smart_reel"]),
                (OpenRazerBackendCapability.SmartReelWrite, ["set_scroll_smart_reel"]),
                (OpenRazerBackendCapability.KeyswitchOptimizationRead, ["get_keyswitch_optimization"]),
                (OpenRazerBackendCapability.KeyswitchOptimizationWrite, ["set_keyswitch_optimization"]),
                (OpenRazerBackendCapability.HyperPollingIndicatorWrite, ["set_hyperpolling_wireless_dongle_indicator_led_mode"]),
                (OpenRazerBackendCapability.HyperPollingPairWrite, ["set_hyperpolling_wireless_dongle_pair"]),
                (OpenRazerBackendCapability.HyperPollingUnpairWrite, ["set_hyperpolling_wireless_dongle_unpair"]),
            })
            {
                if (!published.Contains(capability)) continue;
                Assert.True(source.Any(device.HasSourceCapability),
                    $"1532:{device.ProductId:X4} published {capability} without an OpenRazer declaration.");
            }

            Assert.DoesNotContain(published, capability => capability is
                OpenRazerBackendCapability.SerialRead or OpenRazerBackendCapability.DeviceModeRead or
                OpenRazerBackendCapability.FnPrimaryWrite);
            Assert.False(published.Contains(OpenRazerBackendCapability.MatrixFrameWrite) &&
                device.MatrixDimensions is null,
                $"1532:{device.ProductId:X4} published a matrix editor without dimensions.");

            var zones = OpenRazerDeviceService.GetLightingZoneCapabilities(device);
            foreach (var (zone, facts) in zones)
            {
                var sourceZone = zone switch
                {
                    OpenRazerLedZone.Logo => "logo",
                    OpenRazerLedZone.ScrollWheel => "scroll",
                    OpenRazerLedZone.Backlight => "backlight",
                    OpenRazerLedZone.LeftSide => "left",
                    OpenRazerLedZone.RightSide => "right",
                    OpenRazerLedZone.Charging => "charging",
                    OpenRazerLedZone.FastCharging => "fast_charging",
                    OpenRazerLedZone.FullyCharged => "fully_charged",
                    _ => null,
                };
                var prefix = zone == device.DefaultLedZone;
                Assert.False(facts.CanReadBrightness && !(prefix && device.HasSourceCapability("get_brightness")) &&
                    (sourceZone is null || !device.HasSourceCapability($"get_{sourceZone}_brightness")),
                    $"1532:{device.ProductId:X4} published unlisted {zone} brightness read.");
                Assert.False(facts.CanWriteBrightness && !(prefix && device.HasSourceCapability("set_brightness")) &&
                    (sourceZone is null || !device.HasSourceCapability($"set_{sourceZone}_brightness")),
                    $"1532:{device.ProductId:X4} published unlisted {zone} brightness write.");
                Assert.False(facts.CanReadState && (zone != OpenRazerLedZone.Logo ||
                    !device.HasSourceCapability("get_logo_active")),
                    $"1532:{device.ProductId:X4} published unlisted {zone} state read.");
                Assert.False(facts.CanWriteState && (sourceZone is null ||
                    !device.HasSourceCapability($"set_{sourceZone}_active") &&
                    !device.HasSourceCapability($"set_{sourceZone}_on")),
                    $"1532:{device.ProductId:X4} published unlisted {zone} state write.");
                Assert.False(facts.CanReadEffect && (zone != OpenRazerLedZone.Macro ||
                    !device.HasSourceCapability("get_macro_effect")),
                    $"1532:{device.ProductId:X4} published unlisted {zone} effect read.");
                Assert.False(facts.CanReadColor,
                    $"1532:{device.ProductId:X4} published an RGB read without a source capability.");
                Assert.False(facts.CanWriteColor && !facts.LightingEffects.Any(effect => effect is
                    OpenRazerLightingEffect.Static or OpenRazerLightingEffect.BreathingSingle or
                    OpenRazerLightingEffect.Blinking),
                    $"1532:{device.ProductId:X4} published {zone} color write without a color effect.");
                Assert.False(facts.CanWriteBlinking && !facts.LightingEffects.Contains(OpenRazerLightingEffect.Blinking),
                    $"1532:{device.ProductId:X4} published {zone} blinking without its effect.");
                foreach (var effect in facts.LightingEffects)
                {
                    var declaredGeneric = device.HasSourceCapability(
                        OpenRazerLightingProtocol.GetGenericEffectCapability(effect));
                    var declaredZone = sourceZone is not null && effect switch
                    {
                        OpenRazerLightingEffect.Off => device.HasSourceCapability($"set_{sourceZone}_none"),
                        OpenRazerLightingEffect.Static => device.HasSourceCapability($"set_{sourceZone}_static"),
                        OpenRazerLightingEffect.Spectrum => device.HasSourceCapability($"set_{sourceZone}_spectrum"),
                        OpenRazerLightingEffect.Wave => device.HasSourceCapability($"set_{sourceZone}_wave"),
                        OpenRazerLightingEffect.Reactive => device.HasSourceCapability($"set_{sourceZone}_reactive"),
                        OpenRazerLightingEffect.BreathingRandom => device.HasSourceCapability($"set_{sourceZone}_breath_random"),
                        OpenRazerLightingEffect.BreathingSingle =>
                            device.HasSourceCapability($"set_{sourceZone}_breath_single") ||
                            device.HasSourceCapability($"set_{sourceZone}_breath_mono"),
                        OpenRazerLightingEffect.BreathingDual => device.HasSourceCapability($"set_{sourceZone}_breath_dual"),
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
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x00B9)!;
        var capabilities = OpenRazerDeviceService.GetCapabilities(device);
        var zones = OpenRazerDeviceService.GetLightingZoneCapabilities(device);

        Assert.Equal(OpenRazerLedZone.ScrollWheel, device.DefaultLedZone);
        Assert.DoesNotContain(OpenRazerBackendCapability.ChargingRead, capabilities);
        Assert.Contains(OpenRazerBackendCapability.BrightnessRead, capabilities);
        // The middleware bundle declares the auto sleep and battery charge
        // limiter commands for this mouse, so the four extra capabilities are
        // declared rather than inherited from a generic mouse.
        Assert.Equal(18, capabilities.Count);
        Assert.Contains(OpenRazerBackendCapability.AutoSleepRead, capabilities);
        Assert.Contains(OpenRazerBackendCapability.BatteryChargeLimiterRead, capabilities);
        Assert.Equal([125, 500, 1000], device.PollingRates);
        Assert.Contains(OpenRazerBackendCapability.PollingRateRead, capabilities);
        Assert.Contains(OpenRazerBackendCapability.PollingRateWrite, capabilities);
        Assert.DoesNotContain(OpenRazerBackendCapability.FirmwareRead, capabilities);
        Assert.DoesNotContain(OpenRazerBackendCapability.SerialRead, capabilities);
        Assert.DoesNotContain(OpenRazerBackendCapability.DeviceModeRead, capabilities);
        Assert.Single(zones);
        Assert.Contains(OpenRazerLedZone.ScrollWheel, zones.Keys);
        Assert.DoesNotContain(OpenRazerLightingEffect.Custom,
            zones[OpenRazerLedZone.ScrollWheel].LightingEffects);
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
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, productId)!;
        var capabilities = OpenRazerDeviceService.GetCapabilities(device);

        Assert.Contains(OpenRazerBackendCapability.BatteryRead, capabilities);
        Assert.DoesNotContain(OpenRazerBackendCapability.ChargingRead, capabilities);
    }

    [Theory]
    [InlineData(0x0077)]
    [InlineData(0x00A6)]
    public void OtherMiceSharingTheOriginalCapabilitySetsRetainCharging(ushort productId)
    {
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, productId)!;

        Assert.Contains(OpenRazerBackendCapability.ChargingRead,
            OpenRazerDeviceService.GetCapabilities(device));
    }

    [Fact]
    public async Task BasiliskBrightnessReadTargetsTheScrollWheel()
    {
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x00B9)!;
        var transport = new RecordingTransport();
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);
        var connection = new OpenRazerDeviceConnection(device, "test-path", "test-device",
            OpenRazerEndpointState.Resolved, OpenRazerDeviceService.GetCapabilities(device),
            OpenRazerDeviceService.GetLightingZoneCapabilities(device), null);

        await service.GetBrightnessAsync(connection);

        var request = Assert.Single(transport.Requests);
        Assert.Equal(0x0F, request[7]);
        Assert.Equal(0x84, request[8]);
        Assert.Equal(0x01, request[RazerFeatureReport.ArgumentsOffset]);
        Assert.Equal((byte)OpenRazerLedZone.ScrollWheel, request[RazerFeatureReport.ArgumentsOffset + 1]);
    }

    [Fact]
    public async Task BasiliskV3ProBrightnessReadFallsBackWhenAllIsRejected()
    {
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x00AB)!;
        var transport = new BrightnessZoneFallbackTransport();
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);
        var connection = new OpenRazerDeviceConnection(device, "test-path", "test-device",
            OpenRazerEndpointState.Resolved, OpenRazerDeviceService.GetCapabilities(device),
            OpenRazerDeviceService.GetLightingZoneCapabilities(device), null);

        var brightness = await service.GetBrightnessAsync(connection);

        Assert.Equal(0x2A, brightness);
        Assert.Equal(2, transport.Requests.Count);
        Assert.Equal((byte)OpenRazerLedZone.All,
            transport.Requests[0][RazerFeatureReport.ArgumentsOffset + 1]);
        Assert.Equal((byte)OpenRazerLedZone.ScrollWheel,
            transport.Requests[1][RazerFeatureReport.ArgumentsOffset + 1]);
    }

    [Theory]
    [InlineData(0x00AA)]
    [InlineData(0x00AB)]
    public void BasiliskV3ProUsesTheWorkingLowBatteryTransaction(ushort productId)
    {
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, productId)!;

        Assert.Equal(0x1F, device.Transactions["razer_chroma_misc_get_low_battery_threshold"]);
        Assert.Equal(0x1F, device.Transactions["razer_chroma_misc_set_low_battery_threshold"]);
        Assert.Equal(0x1F, OpenRazerMouseProtocol.GetLowBatteryThreshold(device).Report[2]);
        Assert.Equal(0x1F, OpenRazerMouseProtocol.SetLowBatteryThreshold(device, 15).Report[2]);
    }

    [Fact]
    public async Task PublishedBrightnessCapabilitiesHaveUsableDefaultZones()
    {
        foreach (var device in OpenRazerDeviceCatalog.BuiltIn.Devices)
        {
            var capabilities = OpenRazerDeviceService.GetCapabilities(device);
            var transport = new RecordingTransport();
            var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);
            var connection = new OpenRazerDeviceConnection(device, "test-path", "test-device",
                OpenRazerEndpointState.Resolved, capabilities,
                OpenRazerDeviceService.GetLightingZoneCapabilities(device), null);

            if (capabilities.Contains(OpenRazerBackendCapability.BrightnessRead))
            {
                var error = await Record.ExceptionAsync(async () => await service.GetBrightnessAsync(connection));
                Assert.True(error is null, $"1532:{device.ProductId:X4} brightness read: {error}");
            }
            if (capabilities.Contains(OpenRazerBackendCapability.BrightnessWrite))
            {
                var error = await Record.ExceptionAsync(async () => await service.SetBrightnessAsync(connection, 128));
                Assert.True(error is null, $"1532:{device.ProductId:X4} brightness write: {error}");
            }
        }
    }

    [Theory]
    [InlineData(125, 0x08)]
    [InlineData(500, 0x02)]
    [InlineData(1000, 0x01)]
    public void BasiliskV3XHyperSpeedPollingRatesUseDeclaredTransaction(int hertz, byte code)
    {
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x00B9)!;
        var request = Assert.Single(OpenRazerMouseProtocol.SetPollingRate(device, hertz));

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
    public void DeclaredPollingControlAlwaysPublishesRates(ushort productId)
    {
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, productId)!;
        var capabilities = OpenRazerDeviceService.GetCapabilities(device);

        if (capabilities.Contains(OpenRazerBackendCapability.PollingRateRead) ||
            capabilities.Contains(OpenRazerBackendCapability.PollingRateWrite))
        {
            Assert.NotEmpty(device.PollingRates);
        }
    }

    [Fact]
    public void DeathAdderV2XHyperSpeedPublishesBatteryAndItsPollingRates()
    {
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x009C)!;
        var capabilities = OpenRazerDeviceService.GetCapabilities(device);

        Assert.Equal([125, 500, 1000], device.PollingRates);
        Assert.Contains(OpenRazerBackendCapability.BatteryRead, capabilities);
        Assert.Contains(OpenRazerBackendCapability.PollingRateRead, capabilities);
        Assert.Contains(OpenRazerBackendCapability.PollingRateWrite, capabilities);
        Assert.Contains(OpenRazerBackendCapability.DpiRead, capabilities);
        Assert.Contains(OpenRazerBackendCapability.DpiWrite, capabilities);
    }

    /// <summary>
    /// The DeathAdder V2 X HyperSpeed answers the 0x1F command family. OpenRazer sends
    /// 0xFF for the low-battery threshold, but the device answers that request with status
    /// 0x04 on every attempt; read and write both work with 0x1F.
    /// </summary>
    [Fact]
    public void DeathAdderV2XHyperSpeedUsesTheTransactionIdsThatWorkOnDevice()
    {
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x009C)!;

        Assert.Equal(0x1F, device.Transactions["razer_chroma_standard_get_firmware_version"]);
        Assert.Equal(0x1F, device.Transactions["razer_chroma_standard_get_serial"]);
        Assert.Equal(0x1F, device.Transactions["razer_chroma_misc_get_battery_level"]);
        Assert.Equal(0xFF, device.Transactions["razer_chroma_standard_get_device_mode"]);
        Assert.Equal(0x1F, device.Transactions["razer_chroma_misc_get_low_battery_threshold"]);
        Assert.Equal(0x1F, device.Transactions["razer_chroma_misc_set_low_battery_threshold"]);
        Assert.Equal(0x1F, OpenRazerMouseProtocol.GetLowBatteryThreshold(device).Report[2]);
        Assert.Equal(0x1F, OpenRazerMouseProtocol.SetLowBatteryThreshold(device, 15).Report[2]);
        Assert.Equal(0x1F, device.Transactions["razer_chroma_misc_get_dpi_xy"]);
        Assert.Equal(0x1F, device.Transactions["razer_chroma_misc_set_dpi_xy"]);
        Assert.Equal(0x1F, device.Transactions["razer_chroma_misc_get_polling_rate"]);
        Assert.Equal(0x1F, device.Transactions["razer_chroma_misc_set_polling_rate"]);
    }

    [Fact]
    public void DeathAdderV2XHyperSpeedDeclaresNoChargingTransaction()
    {
        // OpenRazer answers the charging query for this mouse with a hard-coded zero because
        // the firmware does not implement it.
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x009C)!;

        Assert.False(device.Transactions.ContainsKey("razer_chroma_misc_get_charging_status"));
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
                OpenRazerBackendCapability.DeviceModeWrite => OpenRazerStandardProtocol.SetDriverDeviceMode(device),
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
                OpenRazerBackendCapability.AutoSleepRead => OpenRazerMouseProtocol.GetAutoSleep(device),
                OpenRazerBackendCapability.AutoSleepWrite => OpenRazerMouseProtocol.SetAutoSleep(device, true),
                OpenRazerBackendCapability.BatteryChargeLimiterRead => OpenRazerMouseProtocol.GetBatteryChargeLimiter(device),
                OpenRazerBackendCapability.BatteryChargeLimiterWrite => OpenRazerMouseProtocol.SetBatteryChargeLimiter(device, 80, true),
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
                OpenRazerBackendCapability.MappingRead => OpenRazerMappingProtocol.GetButtonList(),
                OpenRazerBackendCapability.MappingWrite => OpenRazerMappingProtocol.SetAssignment(
                    0x01, OpenRazerMappingProtocol.NormalMode, OpenRazerMappingFunctions.ButtonCode, [0x01]),
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
    public void EveryPublishedLightingZoneControlCanConstructItsRequest()
    {
        var failures = new List<string>();
        foreach (var device in OpenRazerDeviceCatalog.BuiltIn.Devices)
        foreach (var (zone, capabilities) in OpenRazerDeviceService.GetLightingZoneCapabilities(device))
        {
            var operations = new (bool Enabled, string Name, Func<object> Build)[]
            {
                (capabilities.CanReadBrightness, "brightness read", () =>
                    OpenRazerLightingProtocol.GetBrightness(device, device.DefaultStorage, zone)),
                (capabilities.CanWriteBrightness, "brightness write", () =>
                    OpenRazerLightingProtocol.SetBrightness(device, device.DefaultStorage, zone, 128)),
                (capabilities.CanReadState, "LED state read", () =>
                    OpenRazerStandardProtocol.GetLedState(device, (byte)device.DefaultStorage, (byte)zone)),
                (capabilities.CanWriteState, "LED state write", () =>
                    OpenRazerStandardProtocol.SetLedState(device, (byte)device.DefaultStorage, (byte)zone, true)),
                (capabilities.CanReadEffect, "LED effect read", () =>
                    OpenRazerStandardProtocol.GetLedEffect(device, (byte)device.DefaultStorage, (byte)zone)),
                (capabilities.CanWriteEffect, "LED effect write", () =>
                    OpenRazerStandardProtocol.SetLedEffect(device, (byte)device.DefaultStorage, (byte)zone,
                        OpenRazerClassicLedEffect.Static)),
                (capabilities.CanWriteColor, "LED color write", () =>
                    OpenRazerStandardProtocol.SetLedColor(device, (byte)device.DefaultStorage, (byte)zone,
                        new OpenRazerColor(1, 2, 3))),
                (capabilities.CanWriteBlinking, "LED blinking write", () =>
                    OpenRazerStandardProtocol.SetLedBlinking(device, (byte)device.DefaultStorage, (byte)zone)),
            };
            foreach (var operation in operations.Where(operation => operation.Enabled))
            {
                var error = Record.Exception(() => operation.Build());
                if (error is not null)
                    failures.Add($"1532:{device.ProductId:X4} {zone} {operation.Name}: {error.Message}");
            }
            foreach (var effect in capabilities.LightingEffects)
            {
                var error = Record.Exception(() => OpenRazerLightingProtocol.CreateEffectRequests(device,
                    new OpenRazerLightingSettings(effect, Zone: zone)));
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
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, productId)!;
        var implicitRequest = Assert.Single(OpenRazerLightingProtocol.CreateEffectRequests(device,
            new OpenRazerLightingSettings(OpenRazerLightingEffect.Custom)));
        var explicitRequest = Assert.Single(OpenRazerLightingProtocol.CreateEffectRequests(device,
            new OpenRazerLightingSettings(OpenRazerLightingEffect.Custom, Zone: device.DefaultLedZone)));

        Assert.Equal(implicitRequest.Report, explicitRequest.Report);
        Assert.Throws<NotSupportedException>(() => OpenRazerLightingProtocol.CreateEffectRequests(device,
            new OpenRazerLightingSettings(OpenRazerLightingEffect.Custom, Zone: OpenRazerLedZone.Logo)));
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

    private sealed class BrightnessZoneFallbackTransport : IRazerFeatureTransport
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
                request.Span[RazerFeatureReport.ArgumentsOffset + 1] == (byte)OpenRazerLedZone.All)
            {
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
