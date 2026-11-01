using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class OpenRazerWirelessBatteryTests
{
    [Fact]
    public async Task BatteryAndChargingTimeoutsStayRetryable()
    {
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x0271)!;
        var transport = new TimeoutTransport();
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);
        var connection = new OpenRazerDeviceConnection(
            device,
            "test-path",
            "test-device",
            OpenRazerEndpointState.Resolved,
            new HashSet<OpenRazerBackendCapability>
            {
                OpenRazerBackendCapability.BatteryRead,
                OpenRazerBackendCapability.ChargingRead,
            },
            new Dictionary<OpenRazerLedZone, OpenRazerLightingZoneCapabilities>(),
            null);

        var state = await service.ReadBasicStateAsync(connection);

        // A sleeping wireless keyboard must not lose its battery card until a rescan.
        Assert.DoesNotContain(OpenRazerBackendCapability.BatteryRead, state.UnsupportedCapabilities);
        Assert.DoesNotContain(OpenRazerBackendCapability.ChargingRead, state.UnsupportedCapabilities);
        Assert.Contains(nameof(OpenRazerBackendCapability.BatteryRead), state.Errors.Keys);
        Assert.Contains(nameof(OpenRazerBackendCapability.ChargingRead), state.Errors.Keys);
    }

    [Fact]
    public void BasiliskUltimateDongleIsTheWirelessMouse()
    {
        var wireless = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x0088)!;

        // 0088 is the mouse's wireless endpoint, not a dumb receiver: it carries the
        // full mouse command set plus the receiver's charge-light controls.
        Assert.Equal("Razer Basilisk Ultimate (Wireless)", wireless.DisplayName);
        Assert.Contains("get_battery", wireless.SourceCapabilities);
        Assert.Contains("set_logo_static", wireless.SourceCapabilities);
        Assert.Contains("set_charge_effect", wireless.SourceCapabilities);
        Assert.Contains(OpenRazerBackendCapability.BatteryRead,
            OpenRazerDeviceService.GetCapabilities(wireless));
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
