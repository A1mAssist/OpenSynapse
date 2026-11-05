using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerDeviceTelemetryReaderDispatchTests
{
    [Fact]
    public async Task ReadAsyncDispatchesBladeAndRazerHidCommands()
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

    [Fact]
    public async Task ReadAsyncKeepsBlade02B7PartialReadsOutOfProtocolFailure()
    {
        // Fail the logo report only. The remaining 02B7 reports must still produce a
        // summary, and the absent startup-animation manifest entry must stay uncounted.
        var transport = new RecordingTransport((0x03, 0x80));
        var reader = new RazerDeviceTelemetryReader(transport, RazerDeviceRegistry.BuiltIn);
        var device = CreateDevice(0x02B7, DeviceProtocolFamilies.BladeChroma, DeviceCategory.Laptop);

        var telemetry = await reader.ReadAsync([device]);

        Assert.True(telemetry.CapabilitySummaries!.TryGetValue(device.Id, out var summary));
        Assert.Equal(14, summary.Supported);
        Assert.InRange(summary.Available, 1, summary.Supported - 1);
        Assert.DoesNotContain(telemetry.Errors,
            error => error.StartsWith("Startup animation", StringComparison.Ordinal));
        Assert.DoesNotContain(((byte)0x0F, (byte)0x98), transport.Commands);
        Assert.True(DeviceVisibilityPolicy.IsVisible(device, telemetry));
    }

    [Fact]
    public async Task ReadAsyncMarksGamingModeWritableOnlyWhenTheManifestOwnsTheSetter()
    {
        var reader = new RazerDeviceTelemetryReader(
            new RecordingTransport(),
            RazerDeviceRegistry.BuiltIn);

        var blade14 = await reader.ReadAsync(
            [CreateDevice(0x0270, "blade-671", DeviceCategory.Laptop)]);
        var blade16 = await reader.ReadAsync(
            [CreateDevice(0x02C6, "blade-710", DeviceCategory.Laptop)]);

        Assert.False(blade14.BladeGamingModeWritable);
        Assert.True(blade16.BladeGamingModeWritable);
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

    private sealed class RecordingTransport(
        (byte CommandClass, byte CommandId)? fail = null) : IRazerFeatureTransport
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
            if (fail is { } failed &&
                failed.CommandClass == commandClass &&
                failed.CommandId == commandId)
            {
                throw new IOException("Injected optional report failure.");
            }
            var response = RazerFeatureReport.CreateRequest(
                transactionId, dataSize, commandClass, commandId, arguments.Span).ToArray();
            response[1] = 0x02;
            return Task.FromResult(response);
        }
    }
}
