using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class Blade597Tests
{
    private const string DevicePath =
        @"\\?\hid#vid_1532&pid_0255&mi_00#test#{00000000-0000-0000-0000-000000000000}";

    [Fact]
    public void ManifestAdmitsSourceBackedReadAndWriteReports()
    {
        var manifest = RazerDeviceRegistry.BuiltIn.Find(0x1532, 0x0255);

        Assert.NotNull(manifest);
        Assert.Equal(
            [
                "adapter-power.get",
                "charge-limit.get",
                "charge-limit.set",
                "current-fan-rpm.get",
                "keyboard-brightness.get",
                "keyboard-brightness.set",
                "native-display-mode.get",
                "native-display-mode.set",
                "power-mode.get",
                "power-mode.set",
                "sku-hardware-configuration.get",
                "thermal-state.get",
                "thermal-state.set",
            ],
            manifest.Capabilities.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(0x8C, manifest.Capabilities["adapter-power.get"].CommandId);
        Assert.Equal(0x92, manifest.Capabilities["charge-limit.get"].CommandId);
        Assert.Equal(0x12, manifest.Capabilities["charge-limit.set"].CommandId);
        Assert.Equal(0x8F, manifest.Capabilities["power-mode.get"].CommandId);
        Assert.Equal(0x0F, manifest.Capabilities["power-mode.set"].CommandId);
        Assert.DoesNotContain("display-mux.get", manifest.Capabilities.Keys);

        var brightness = manifest.Capabilities["keyboard-brightness.get"];
        Assert.Equal(0x01, brightness.TransactionId);
        Assert.Equal(0x03, brightness.MaximumDataSize);
        Assert.Equal(0x0F, brightness.CommandClass);
        Assert.Equal(0x84, brightness.CommandId);
        Assert.Equal([0x01, 0x05], brightness.Arguments.ToArray());
    }

    [Fact]
    public async Task ReadAsyncParsesChromaBatteryPowerPanelAndFanState()
    {
        var descriptor = new DeviceDescriptor(
            "blade-597-test",
            DevicePath,
            0x1532,
            0x0255,
            DeviceAccessState.Available,
            DeviceCapabilityState.PendingValidation,
            91,
            1,
            2,
            "blade-597",
            DeviceCategory.Laptop);
        var reader = new RazerDeviceTelemetryReader(
            new Blade597Transport(),
            RazerDeviceRegistry.BuiltIn);

        var telemetry = await reader.ReadAsync([descriptor]);

        Assert.Empty(telemetry.Errors);
        Assert.Equal((byte)51, telemetry.BladeKeyboardBrightness);
        Assert.Equal(BladePerformanceMode.Performance, telemetry.BladePerformanceMode);
        Assert.Equal(BladeFanMode.Automatic, telemetry.BladeFanMode);
        Assert.Equal(3000, telemetry.BladeCurrentFanCpuRpm);
        Assert.Equal(3000, telemetry.BladeCurrentFanGpuRpm);
        Assert.Equal(80, telemetry.BladeChargeLimitPercent);
        Assert.Equal(BladeNativeDisplayMode.Uhd, telemetry.BladeNativeDisplayMode);
        Assert.NotNull(telemetry.BladeSkuHardwareConfiguration);
        Assert.False(telemetry.BladeOneTimeFullChargeEnabled);
        Assert.Equal(65, telemetry.BladeAdapterPower?.ActualWatts);
        Assert.Equal(80, telemetry.BladeAdapterPower?.RecommendedWatts);
        Assert.Equal(
            new DeviceCapabilitySummary(10, 10),
            DeviceCapabilitySummaryCalculator.Calculate(descriptor, telemetry));
    }

    [Fact]
    public async Task SourceBackedWritesRoundTripThroughReadback()
    {
        var descriptor = new DeviceDescriptor(
            "blade-597-test",
            DevicePath,
            0x1532,
            0x0255,
            DeviceAccessState.Available,
            DeviceCapabilityState.PendingValidation,
            91,
            1,
            2,
            "blade-597",
            DeviceCategory.Laptop);
        var reader = new RazerDeviceTelemetryReader(
            new Blade597Transport(),
            RazerDeviceRegistry.BuiltIn);

        var initial = await reader.ReadAsync([descriptor]);
        Assert.Equal(BladePerformanceMode.Performance, initial.BladePerformanceMode);

        Assert.True(BladeModelCapabilities.AllowsControlWrites(0x0255));
        Assert.True(BladeModelCapabilities.AllowsControlWrites(0x02C6));

        await reader.SetBladePerformanceModeAsync(
            [descriptor], BladePerformanceMode.Balanced);
        await reader.SetBladeKeyboardBrightnessAsync([descriptor], 50);
        await reader.SetBladeChargeLimitAsync([descriptor], 70);

        var updated = await reader.ReadAsync([descriptor]);
        Assert.Equal(BladePerformanceMode.Balanced, updated.BladePerformanceMode);
        Assert.Equal(BladeFanMode.Automatic, updated.BladeFanMode);
        Assert.Equal((byte)50, updated.BladeKeyboardBrightness);
        Assert.Equal(70, updated.BladeChargeLimitPercent);
    }

    private sealed class Blade597Transport : IRazerFeatureTransport
    {
        private BladePerformanceMode _performanceMode = BladePerformanceMode.Performance;
        private BladeFanMode _fanMode = BladeFanMode.Automatic;
        private byte _brightnessRaw = 128;
        private byte _chargeLimit = 0xD0;
        private byte _powerModeMask;
        private BladeNativeDisplayMode _nativeDisplayMode = BladeNativeDisplayMode.Uhd;

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
            response[1] = 0x01;
            switch (commandClass, commandId)
            {
                case (0x0D, 0x02):
                    _performanceMode = (BladePerformanceMode)arguments.Span[2];
                    _fanMode = (BladeFanMode)arguments.Span[3];
                    break;
                case (0x0D, 0x82):
                    response[RazerFeatureReport.ArgumentsOffset + 2] = (byte)_performanceMode;
                    response[RazerFeatureReport.ArgumentsOffset + 3] = (byte)_fanMode;
                    break;
                case (0x0D, 0x88):
                    response[RazerFeatureReport.ArgumentsOffset + 2] = 30;
                    break;
                case (0x0F, 0x04):
                    _brightnessRaw = arguments.Span[2];
                    break;
                case (0x0F, 0x84):
                    response[RazerFeatureReport.ArgumentsOffset + 2] = _brightnessRaw;
                    break;
                case (0x07, 0x8C):
                    response[RazerFeatureReport.ArgumentsOffset] = 0x04;
                    response[RazerFeatureReport.ArgumentsOffset + 1] = 0x05;
                    break;
                case (0x07, 0x92):
                    response[RazerFeatureReport.ArgumentsOffset] = _chargeLimit;
                    break;
                case (0x07, 0x12):
                    _chargeLimit = arguments.Span[0];
                    break;
                case (0x07, 0x8F):
                    response[RazerFeatureReport.ArgumentsOffset] = _powerModeMask;
                    break;
                case (0x07, 0x0F):
                    _powerModeMask = arguments.Span[0];
                    break;
                case (0x0D, 0x8E):
                    response[RazerFeatureReport.ArgumentsOffset] = (byte)_nativeDisplayMode;
                    break;
                case (0x0D, 0x0E):
                    _nativeDisplayMode = (BladeNativeDisplayMode)arguments.Span[0];
                    break;
                case (0x0D, 0x8F):
                    response[RazerFeatureReport.ArgumentsOffset] = 0;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unexpected Blade 597 command {commandClass:X2}:{commandId:X2}.");
            }

            response[89] = RazerFeatureReport.CalculateCrc(response);
            return Task.FromResult(response);
        }
    }
}