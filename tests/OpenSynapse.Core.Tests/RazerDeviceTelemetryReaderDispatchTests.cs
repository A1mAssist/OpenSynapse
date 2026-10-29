using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerDeviceTelemetryReaderDispatchTests
{
    [Fact]
    public async Task ReadAsyncDispatchesBladeAndOpenRazerCommands()
    {
        var transport = new RecordingTransport();
        var reader = new RazerDeviceTelemetryReader(transport, RazerDeviceRegistry.BuiltIn);

        var telemetry = await reader.ReadAsync(
            [
                CreateDevice(0x02C6, "blade-710", DeviceCategory.Laptop),
                CreateDevice(0x00B8, "viper-184", DeviceCategory.Mouse),
            ]);

        Assert.Contains(((byte)0x0D, (byte)0x82), transport.Commands);
        Assert.Contains(((byte)0x07, (byte)0x80), transport.Commands);
        Assert.NotNull(telemetry.CapabilitySummaries);
        Assert.Equal(2, telemetry.CapabilitySummaries!.Count);
    }

    private static DeviceDescriptor CreateDevice(
        ushort productId,
        string family,
        DeviceCategory category) =>
        new(
            $"{family}-{productId:X4}",
            $@"\\?\hid#vid_1532&pid_{productId:x4}&mi_02#test#{{00000000-0000-0000-0000-000000000000}}",
            0x1532,
            productId,
            DeviceAccessState.Available,
            DeviceCapabilityState.PendingValidation,
            91,
            1,
            2,
            family,
            category);

    private sealed class RecordingTransport : IRazerFeatureTransport
    {
        public List<(byte CommandClass, byte CommandId)> Commands { get; } = [];

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
            Commands.Add((commandClass, commandId));
            var response = RazerFeatureReport.CreateRequest(
                transactionId, dataSize, commandClass, commandId, arguments.Span).ToArray();
            response[1] = 0x02;
            return Task.FromResult(response);
        }
    }
}
