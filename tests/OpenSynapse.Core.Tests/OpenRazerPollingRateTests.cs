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
