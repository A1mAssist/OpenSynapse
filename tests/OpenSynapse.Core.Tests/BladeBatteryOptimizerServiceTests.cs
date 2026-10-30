using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

/// <summary>
/// The optimizer service is a thin layer over the existing charge-limiter and
/// power-mode reports. These tests exercise the wiring against a fake 02E0 EC:
/// read the bundle state, enable it, and confirm the one-time override bit.
/// </summary>
public sealed class BladeBatteryOptimizerServiceTests
{
    [Fact]
    public async Task ReadsAndWritesTheOptimizerThroughTheChargeLimiterReports()
    {
        var transport = new BatteryOptimizerTransport();
        var service = new BladeBatteryOptimizerService(
            new RazerDeviceTelemetryReader(transport));
        var devices = new[] { Descriptor(0x02E0) };

        var initial = await service.ReadAsync(devices);
        Assert.NotNull(initial);
        Assert.True(initial!.Value.IsEnabled);
        Assert.Equal(80, initial.Value.ThresholdPercent);
        Assert.False(initial.Value.OneTimeOverrideEnabled);

        var applied = await service.ApplyAsync(
            devices, enabled: true, thresholdPercent: 60, oneTimeOverride: true);

        Assert.True(applied.IsEnabled);
        Assert.Equal(60, applied.ThresholdPercent);
        Assert.True(applied.OneTimeOverrideEnabled);
        Assert.Equal(0xBC, transport.ChargeLimitRaw);
        Assert.Equal(0x04, transport.PowerModeMask & 0x04);

        var disabled = await service.ApplyAsync(
            devices, enabled: false, thresholdPercent: 60, oneTimeOverride: false);

        Assert.False(disabled.IsEnabled);
        Assert.Equal(80, disabled.ThresholdPercent);
        Assert.False(disabled.OneTimeOverrideEnabled);
        Assert.Equal(0x50, transport.ChargeLimitRaw);
        Assert.Equal(0x00, transport.PowerModeMask & 0x04);
    }

    [Fact]
    public async Task AModelWithoutTheBundleFlagIsNotExposed()
    {
        var transport = new BatteryOptimizerTransport();
        var service = new BladeBatteryOptimizerService(
            new RazerDeviceTelemetryReader(transport));
        var devices = new[] { Descriptor(0x0245) };

        Assert.False(service.IsSupported(devices));
        Assert.Null(await service.ReadAsync(devices));
        await Assert.ThrowsAsync<NotSupportedException>(
            async () => await service.ApplyAsync(devices, true, 80, false));
    }

    [Fact]
    public async Task AModelWithoutTheOverrideFlagRejectsTheOverride()
    {
        var service = new BladeBatteryOptimizerService(
            new RazerDeviceTelemetryReader(new BatteryOptimizerTransport()));
        var devices = new[] { Descriptor(0x028A) };

        Assert.True(service.IsSupported(devices));
        await Assert.ThrowsAsync<NotSupportedException>(
            async () => await service.ApplyAsync(devices, true, 80, true));
    }

    private static DeviceDescriptor Descriptor(ushort productId) =>
        new(
            $"blade-{productId:x4}-test",
            $@"\\?\hid#vid_1532&pid_{productId:x4}&mi_02#test#{{00000000-0000-0000-0000-000000000000}}",
            0x1532,
            productId,
            DeviceAccessState.Available,
            DeviceCapabilityState.PendingValidation,
            91,
            1,
            2,
            "blade-671",
            DeviceCategory.Laptop);

    private sealed class BatteryOptimizerTransport : IRazerFeatureTransport
    {
        private byte _chargeLimitRaw = 0xD0;
        private byte _powerModeMask;

        public byte ChargeLimitRaw => _chargeLimitRaw;

        public byte PowerModeMask => _powerModeMask;

        public Task<byte[]> QueryAsync(
            string devicePath,
            byte transactionId,
            byte dataSize,
            byte commandClass,
            byte commandId,
            ReadOnlyMemory<byte> arguments,
            TimeSpan deviceWait,
            CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false)
        {
            var request = RazerFeatureReport.CreateRequest(
                transactionId, dataSize, commandClass, commandId, arguments.Span);
            var response = request.ToArray();
            response[1] = 0x02;
            switch (commandClass, commandId)
            {
                case (0x07, 0x92):
                    response[RazerFeatureReport.ArgumentsOffset] = _chargeLimitRaw;
                    break;
                case (0x07, 0x12):
                    _chargeLimitRaw = arguments.Span[0];
                    break;
                case (0x07, 0x8F):
                    response[RazerFeatureReport.ArgumentsOffset] = _powerModeMask;
                    break;
                case (0x07, 0x0F):
                    _powerModeMask = arguments.Span[0];
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unexpected command {commandClass:X2}:{commandId:X2}.");
            }

            response[89] = RazerFeatureReport.CalculateCrc(response);
            return Task.FromResult(response);
        }
    }
}
