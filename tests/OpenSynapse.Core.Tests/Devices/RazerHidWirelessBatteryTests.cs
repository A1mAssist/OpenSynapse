using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerHidWirelessBatteryTests
{
    [Fact]
    public async Task BatteryAndChargingTimeoutsStayRetryable()
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0271)!;
        var transport = new TimeoutTransport();
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);
        var connection = new RazerHidDeviceConnection(
            device,
            "test-path",
            "test-device",
            RazerHidEndpointState.Resolved,
            new HashSet<RazerHidBackendCapability>
            {
                RazerHidBackendCapability.BatteryRead,
                RazerHidBackendCapability.ChargingRead,
            },
            new Dictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities>(),
            null);

        var state = await service.ReadBasicStateAsync(connection);

        // A sleeping wireless device must not lose its battery card until a rescan.
        Assert.DoesNotContain(RazerHidBackendCapability.BatteryRead, state.UnsupportedCapabilities);
        Assert.DoesNotContain(RazerHidBackendCapability.ChargingRead, state.UnsupportedCapabilities);
        Assert.Empty(state.Errors);
    }

    [Fact]
    public async Task BatteryTimeoutStaysRetryableWhenChargingAnswers()
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0088)!;
        var transport = new BatteryTimeoutTransport();
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);
        var connection = new RazerHidDeviceConnection(
            device,
            "test-path",
            "test-device",
            RazerHidEndpointState.Resolved,
            new HashSet<RazerHidBackendCapability>
            {
                RazerHidBackendCapability.BatteryRead,
                RazerHidBackendCapability.ChargingRead,
            },
            new Dictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities>(),
            null);

        var state = await service.ReadBasicStateAsync(connection);

        // A single sleeping battery read must not stay unsupported for the rest of the session.
        Assert.DoesNotContain(RazerHidBackendCapability.BatteryRead, state.UnsupportedCapabilities);
        Assert.Empty(state.Errors);
        Assert.Null(state.BatteryPercent);
        Assert.False(state.IsCharging);
    }

    [Fact]
    public void BasiliskUltimateDongleIsTheWirelessMouse()
    {
        var wireless = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0088)!;

        // 0088 is the mouse's wireless endpoint, not a dumb receiver: it carries the
        // full mouse command set plus the receiver's charge-light controls.
        Assert.Equal("Razer Basilisk Ultimate (Wireless)", wireless.DisplayName);
        Assert.Contains("get_battery", wireless.SourceCapabilities);
        Assert.Contains("set_logo_static", wireless.SourceCapabilities);
        Assert.Contains("set_charge_effect", wireless.SourceCapabilities);
        Assert.Contains(RazerHidBackendCapability.BatteryRead,
            RazerHidDeviceService.GetCapabilities(wireless));
    }

    private sealed class BatteryTimeoutTransport : IRazerFeatureTransport
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
            if (request.Span[8] == 0x80)
            {
                throw new RazerDeviceTimeoutException("The battery read did not answer.");
            }

            var response = request.ToArray();
            response[1] = 0x02;
            response[6] = 0x02;
            response[9] = 0x00;
            response[10] = 0x00;
            response[89] = RazerFeatureReport.CalculateCrc(response);
            return Task.FromResult(response);
        }
    }

    private sealed class TimeoutTransport : IRazerFeatureTransport
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
            throw new RazerDeviceTimeoutException("The device did not answer this command.");

        public Task<byte[]> QueryPreparedAsync(
            string devicePath,
            ReadOnlyMemory<byte> request,
            TimeSpan deviceWait,
            CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false) =>
            throw new RazerDeviceTimeoutException("The device did not answer this command.");
    }
}
