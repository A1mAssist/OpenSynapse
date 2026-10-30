using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class OpenRazerPollingRateTests
{
    /// <summary>
    /// OpenRazer sends get_polling_rate2 with data_size 1 and documents the rate as
    /// argument 1, so the firmware answers with a one-byte payload whose second byte
    /// carries the rate. Upstream reads that byte without checking the declared length.
    /// </summary>
    [Theory]
    [InlineData(0x01, 8000)]
    [InlineData(0x02, 4000)]
    [InlineData(0x04, 2000)]
    [InlineData(0x08, 1000)]
    [InlineData(0x10, 500)]
    [InlineData(0x40, 125)]
    public void HighPollingRateReadsArgumentOneOfAOneByteResponse(byte code, int expected)
    {
        var response = RazerFeatureReport.CreateRequest(0x1F, 0x01, 0x00, 0xC0, [0x00]);
        response[1] = 0x02;
        response[RazerFeatureReport.ArgumentsOffset + 1] = code;
        response[89] = RazerFeatureReport.CalculateCrc(response);

        Assert.Equal(expected, OpenRazerMouseProtocol.ParsePollingRate(response, highRate: true));
    }

    [Fact]
    public void HighPollingRateStillRejectsAnUnknownCode()
    {
        var response = RazerFeatureReport.CreateRequest(0x1F, 0x01, 0x00, 0xC0, [0x00]);
        response[1] = 0x02;
        response[RazerFeatureReport.ArgumentsOffset + 1] = 0x7F;
        response[89] = RazerFeatureReport.CalculateCrc(response);

        Assert.Throws<InvalidDataException>(() =>
            OpenRazerMouseProtocol.ParsePollingRate(response, highRate: true));
    }

    [Fact]
    public void LegacyPollingRateStillReadsArgumentZero()
    {
        var response = RazerFeatureReport.CreateRequest(0x1F, 0x01, 0x00, 0x85, [0x02]);
        response[1] = 0x02;
        response[89] = RazerFeatureReport.CalculateCrc(response);

        Assert.Equal(500, OpenRazerMouseProtocol.ParsePollingRate(response, highRate: false));
    }

    /// <summary>
    /// A DeathAdder V3 that answers the polling-rate query with a one-byte payload must
    /// still report a rate instead of dropping the capability as malformed.
    /// </summary>
    [Fact]
    public async Task DeathAdderV3ReportsItsPollingRateFromAShortResponse()
    {
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x00B2)!;
        var transport = new ShortPollingRateTransport();
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);
        var connection = new OpenRazerDeviceConnection(device, "test-path", "test-device",
            OpenRazerEndpointState.Resolved, OpenRazerDeviceService.GetCapabilities(device),
            OpenRazerDeviceService.GetLightingZoneCapabilities(device), null);

        var state = await service.ReadBasicStateAsync(connection);

        Assert.Equal(2000, state.PollingRate);
        Assert.DoesNotContain(OpenRazerBackendCapability.PollingRateRead, state.UnsupportedCapabilities);
        Assert.Empty(state.Errors);
    }

    /// <summary>
    /// The polling-rate write must reach the device as the two reports OpenRazer sends
    /// for a DeathAdder V3: the rate in argument 1, once with argument 0 = 0x00 and once
    /// with argument 0 = 0x01.
    /// </summary>
    [Fact]
    public async Task DeathAdderV3WritesBothPollingRateReports()
    {
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x00B2)!;
        var transport = new ShortPollingRateTransport();
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);
        var connection = new OpenRazerDeviceConnection(device, "test-path", "test-device",
            OpenRazerEndpointState.Resolved, OpenRazerDeviceService.GetCapabilities(device),
            OpenRazerDeviceService.GetLightingZoneCapabilities(device), null);

        await service.SetPollingRateAsync(connection, 1000);

        var writes = transport.Requests
            .Where(request => request[8] == 0x40)
            .ToArray();
        Assert.Equal(2, writes.Length);
        Assert.Equal(0x00, writes[0][RazerFeatureReport.ArgumentsOffset]);
        Assert.Equal(0x01, writes[1][RazerFeatureReport.ArgumentsOffset]);
        foreach (var write in writes)
        {
            Assert.Equal(0x1F, write[2]);
            Assert.Equal(0x02, write[6]);
            Assert.Equal(0x00, write[7]);
            Assert.Equal(0x08, write[RazerFeatureReport.ArgumentsOffset + 1]);
        }
    }

    /// <summary>
    /// The firmware answers a successful write without echoing the payload, so the rate
    /// read back afterwards is the only evidence that the write landed. A device that
    /// ignores the write must be reported with the rate it actually holds.
    /// </summary>
    [Fact]
    public async Task PollingRateWriteReportsTheRateTheDeviceHoldsAfterwards()
    {
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x00B2)!;
        var transport = new PollingRateWriteTransport(storedCode: 0x01);
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);
        var connection = new OpenRazerDeviceConnection(device, "test-path", "test-device",
            OpenRazerEndpointState.Resolved,
            new HashSet<OpenRazerBackendCapability>
            {
                OpenRazerBackendCapability.PollingRateRead,
                OpenRazerBackendCapability.PollingRateWrite,
            },
            new Dictionary<OpenRazerLedZone, OpenRazerLightingZoneCapabilities>(), null);

        var reported = await service.SetPollingRateAsync(connection, 8000);

        Assert.Equal(8000, reported);
    }

    [Fact]
    public async Task PollingRateWriteReportsTheOldRateWhenTheDeviceIgnoresTheWrite()
    {
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x00B2)!;
        var transport = new PollingRateWriteTransport(storedCode: 0x04);
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);
        var connection = new OpenRazerDeviceConnection(device, "test-path", "test-device",
            OpenRazerEndpointState.Resolved,
            new HashSet<OpenRazerBackendCapability>
            {
                OpenRazerBackendCapability.PollingRateRead,
                OpenRazerBackendCapability.PollingRateWrite,
            },
            new Dictionary<OpenRazerLedZone, OpenRazerLightingZoneCapabilities>(), null);

        var reported = await service.SetPollingRateAsync(connection, 8000);

        Assert.Equal(2000, reported);
    }

    /// <summary>
    /// The middleware DeviceInfo links each wireless or Bluetooth identity to its wired
    /// product with dongleId/bleId and declares one rate ladder for the family. Pin the
    /// identities that inherit that ladder so a future catalog edit cannot drop it.
    /// </summary>
    [Fact]
    public void WirelessIdentitiesInheritTheMiddlewareRateLadder()
    {
        ushort[] productIds =
        [
            0x005A, // 0059 dongleId 90
            0x006F, // 0070 dongleId 111
            0x0072, // 0073 dongleId 114
            0x007B, // 007A dongleId 123
            0x007D, // 007C dongleId 125
            0x0088, // 0086 dongleId 136
            0x0090, // 008F dongleId 144
            0x0095, // 0094 bleId 149
            0x00A6, // 00A5 dongleId 166
            0x00A8, // 00A7 dongleId 168
            0x00B0, // 00AF dongleId 176
            0x00CD, // 00CC dongleId 205
            0x00D4, // 00D3 dongleId 212
            0x00D7, // 00D6 dongleId 215
        ];

        foreach (var productId in productIds)
        {
            var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, productId);
            Assert.NotNull(device);
            Assert.Equal([125, 500, 1000], device!.PollingRates);
        }
    }

    /// <summary>
    /// OpenRazer's device base assigns 125/500/1000 to every class that exposes
    /// set_poll_rate without overriding POLL_RATES. These legacy mice also take the
    /// driver's generic polling-rate branch, so pin the ladder the catalog publishes.
    /// </summary>
    [Fact]
    public void LegacyDevicesUseTheOpenRazerDefaultPollRateLadder()
    {
        // 0013 is excluded: it uses the Orochi 2011 poll-DPI command, which the
        // catalog has no transaction for. 0046 and 004C do not expose poll_rate.
        ushort[] productIds =
        [
            0x0015, 0x001F, 0x0020, 0x0024, 0x0025, 0x002E, 0x002F, 0x0032,
            0x0034, 0x0036, 0x0037, 0x0038, 0x0039, 0x003E, 0x003F, 0x0040,
            0x0041, 0x0042, 0x0043, 0x0044, 0x0045, 0x0048, 0x004F, 0x0054,
            0x005B, 0x005E, 0x006A, 0x006B, 0x0085,
        ];

        foreach (var productId in productIds)
        {
            var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, productId);
            Assert.NotNull(device);
            Assert.Equal([125, 500, 1000], device!.PollingRates);
        }
    }

    private sealed class ShortPollingRateTransport : IRazerFeatureTransport
    {
        internal List<byte[]> Requests { get; } = [];

        public Task<byte[]> QueryAsync(
            string devicePath, byte transactionId, byte dataSize, byte commandClass, byte commandId,
            ReadOnlyMemory<byte> arguments, TimeSpan deviceWait, CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false) => throw new NotSupportedException();

        public Task<byte[]> QueryPreparedAsync(
            string devicePath, ReadOnlyMemory<byte> request, TimeSpan deviceWait,
            CancellationToken cancellationToken, bool allowRemainingPacketsMismatch = false)
        {
            var response = request.ToArray();
            Requests.Add(response.ToArray());
            response[1] = 0x02;
            if (request.Span[8] == 0xC0)
            {
                // The firmware declares a one-byte payload and puts the rate in argument 1.
                response[6] = 0x01;
                response[RazerFeatureReport.ArgumentsOffset + 1] = 0x04;
            }
            response[89] = RazerFeatureReport.CalculateCrc(response);
            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// A device that accepts the write but stays silent on the read-back must not turn a
    /// completed write into a failure.
    /// </summary>
    [Fact]
    public async Task PollingRateWriteSucceedsWhenTheReadBackGoesUnanswered()
    {
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x00B2)!;
        var transport = new SilentReadBackTransport();
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);
        var connection = new OpenRazerDeviceConnection(device, "test-path", "test-device",
            OpenRazerEndpointState.Resolved,
            new HashSet<OpenRazerBackendCapability>
            {
                OpenRazerBackendCapability.PollingRateRead,
                OpenRazerBackendCapability.PollingRateWrite,
            },
            new Dictionary<OpenRazerLedZone, OpenRazerLightingZoneCapabilities>(), null);

        var reported = await service.SetPollingRateAsync(connection, 8000);

        Assert.Null(reported);
    }

    /// <summary>
    /// The Basilisk V4 middleware products use 00:C0/40 high-rate reports. Their
    /// catalog sibling only declares the legacy 00:85/05 pair, so without these
    /// reports an 8000 Hz selection cannot be encoded.
    /// </summary>
    [Theory]
    [InlineData(0x00E2)]
    [InlineData(0x00E3)]
    [InlineData(0x00EA)]
    [InlineData(0x00EB)]
    public void BasiliskV4UsesTheMiddlewareHighRateReports(ushort productId)
    {
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, productId);

        Assert.NotNull(device);
        var get = OpenRazerMouseProtocol.GetPollingRate(device!).Report;
        Assert.Equal(0x00, get[2]);
        Assert.Equal(0x02, get[6]);
        Assert.Equal(0x00, get[7]);
        Assert.Equal(0xC0, get[8]);
        Assert.Equal(0x01, get[RazerFeatureReport.ArgumentsOffset]);

        var set = Assert.Single(OpenRazerMouseProtocol.SetPollingRate(device, 8000)).Report;
        Assert.Equal(0x00, set[2]);
        Assert.Equal(0x02, set[6]);
        Assert.Equal(0x00, set[7]);
        Assert.Equal(0x40, set[8]);
        Assert.Equal(0x01, set[RazerFeatureReport.ArgumentsOffset]);
        Assert.Equal(0x01, set[RazerFeatureReport.ArgumentsOffset + 1]);
    }

    [Fact]
    public void EveryDeclaredRateAboveOneThousandHasTheHighRateReports()
    {
        var offenders = OpenRazerDeviceCatalog.BuiltIn.Devices
            .Where(device => device.PollingRates.Any(rate => rate > 1000))
            .Where(device =>
                !device.Transactions.ContainsKey("razer_chroma_misc_get_polling_rate2") ||
                (!device.Transactions.ContainsKey("razer_chroma_misc_set_polling_rate2") &&
                 !device.CommandSequences.ContainsKey("razer_chroma_misc_set_polling_rate2")))
            .Select(device =>
                $"1532:{device.ProductId:X4} {device.DisplayName} [{string.Join(',', device.PollingRates)}]")
            .ToArray();

        Assert.True(offenders.Length == 0, string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public async Task HighSpeedPollingUsesTheActiveProfile()
    {
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x02CF)!;
        var transport = new ActiveProfilePollingTransport();
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);
        var connection = new OpenRazerDeviceConnection(device, "test-path", "test-device",
            OpenRazerEndpointState.Resolved,
            new HashSet<OpenRazerBackendCapability>
            {
                OpenRazerBackendCapability.PollingRateRead,
                OpenRazerBackendCapability.PollingRateWrite,
            },
            new Dictionary<OpenRazerLedZone, OpenRazerLightingZoneCapabilities>(), null);

        var reported = await service.SetPollingRateAsync(connection, 8_000);

        Assert.Equal(8_000, reported);
        var write = Assert.Single(transport.Requests, request => request[8] == 0x40);
        Assert.Equal(0x03, write[RazerFeatureReport.ArgumentsOffset]);
        Assert.Equal(0x01, write[RazerFeatureReport.ArgumentsOffset + 1]);

        var read = Assert.Single(transport.Requests, request => request[8] == 0xC0);
        Assert.Equal(0x03, read[RazerFeatureReport.ArgumentsOffset]);
    }

    [Fact]
    public async Task HighSpeedPollingFallsBackWhenTheActiveProfileReadIsRejected()
    {
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x02CF)!;
        var transport = new RejectedProfilePollingTransport();
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);
        var connection = new OpenRazerDeviceConnection(device, "test-path", "test-device",
            OpenRazerEndpointState.Resolved,
            new HashSet<OpenRazerBackendCapability>
            {
                OpenRazerBackendCapability.PollingRateRead,
                OpenRazerBackendCapability.PollingRateWrite,
            },
            new Dictionary<OpenRazerLedZone, OpenRazerLightingZoneCapabilities>(), null);

        var reported = await service.SetPollingRateAsync(connection, 8_000);

        Assert.Equal(8_000, reported);
        var write = Assert.Single(transport.Requests, request => request[8] == 0x40);
        Assert.Equal(0x00, write[RazerFeatureReport.ArgumentsOffset]);
    }

    private sealed class RejectedProfilePollingTransport : IRazerFeatureTransport
    {
        internal List<byte[]> Requests { get; } = [];

        public Task<byte[]> QueryAsync(
            string devicePath, byte transactionId, byte dataSize, byte commandClass, byte commandId,
            ReadOnlyMemory<byte> arguments, TimeSpan deviceWait, CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false) => throw new NotSupportedException();

        public Task<byte[]> QueryPreparedAsync(
            string devicePath, ReadOnlyMemory<byte> request, TimeSpan deviceWait,
            CancellationToken cancellationToken, bool allowRemainingPacketsMismatch = false)
        {
            var response = request.ToArray();
            Requests.Add(response.ToArray());
            if (request.Span[8] == 0x84)
            {
                throw new RazerCommandRejectedException("The device rejected the active-profile read.");
            }

            response[1] = 0x02;
            if (request.Span[8] == 0xC0)
            {
                response[6] = 0x01;
                response[RazerFeatureReport.ArgumentsOffset + 1] = 0x01;
            }
            response[89] = RazerFeatureReport.CalculateCrc(response);
            return Task.FromResult(response);
        }
    }

    private sealed class ActiveProfilePollingTransport : IRazerFeatureTransport
    {
        internal List<byte[]> Requests { get; } = [];

        public Task<byte[]> QueryAsync(
            string devicePath, byte transactionId, byte dataSize, byte commandClass, byte commandId,
            ReadOnlyMemory<byte> arguments, TimeSpan deviceWait, CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false) => throw new NotSupportedException();

        public Task<byte[]> QueryPreparedAsync(
            string devicePath, ReadOnlyMemory<byte> request, TimeSpan deviceWait,
            CancellationToken cancellationToken, bool allowRemainingPacketsMismatch = false)
        {
            var response = request.ToArray();
            Requests.Add(response.ToArray());
            response[1] = 0x02;
            if (request.Span[8] == 0x84)
            {
                response[RazerFeatureReport.ArgumentsOffset] = 0x03;
            }
            else if (request.Span[8] == 0xC0)
            {
                response[6] = 0x01;
                response[RazerFeatureReport.ArgumentsOffset + 1] = 0x01;
            }
            response[89] = RazerFeatureReport.CalculateCrc(response);
            return Task.FromResult(response);
        }
    }

    private sealed class SilentReadBackTransport : IRazerFeatureTransport
    {
        public Task<byte[]> QueryAsync(
            string devicePath, byte transactionId, byte dataSize, byte commandClass, byte commandId,
            ReadOnlyMemory<byte> arguments, TimeSpan deviceWait, CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false) => throw new NotSupportedException();

        public Task<byte[]> QueryPreparedAsync(
            string devicePath, ReadOnlyMemory<byte> request, TimeSpan deviceWait,
            CancellationToken cancellationToken, bool allowRemainingPacketsMismatch = false)
        {
            if (request.Span[8] == 0xC0)
            {
                throw new RazerDeviceTimeoutException("The device did not answer this command.");
            }

            var response = request.ToArray();
            response[1] = 0x02;
            response[89] = RazerFeatureReport.CalculateCrc(response);
            return Task.FromResult(response);
        }
    }

    private sealed class PollingRateWriteTransport(byte storedCode) : IRazerFeatureTransport
    {
        public Task<byte[]> QueryAsync(
            string devicePath, byte transactionId, byte dataSize, byte commandClass, byte commandId,
            ReadOnlyMemory<byte> arguments, TimeSpan deviceWait, CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false) => throw new NotSupportedException();

        public Task<byte[]> QueryPreparedAsync(
            string devicePath, ReadOnlyMemory<byte> request, TimeSpan deviceWait,
            CancellationToken cancellationToken, bool allowRemainingPacketsMismatch = false)
        {
            var response = request.ToArray();
            response[1] = 0x02;
            if (request.Span[8] == 0xC0)
            {
                // The firmware declares a one-byte payload and puts the rate in argument 1.
                response[6] = 0x01;
                response[RazerFeatureReport.ArgumentsOffset + 1] = storedCode;
            }
            response[89] = RazerFeatureReport.CalculateCrc(response);
            return Task.FromResult(response);
        }
    }
}
