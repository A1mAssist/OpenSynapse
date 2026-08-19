using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Sensors;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Core.Tests;

public sealed class DeviceIdParserTests
{
    [Theory]
    [InlineData("\\\\?\\hid#vid_1532&pid_02c6&mi_00#7&123#", 0x1532, 0x02C6)]
    [InlineData("HID\\VID_1532&PID_00B8", 0x1532, 0x00B8)]
    public void ParsesVidAndPid(string deviceId, ushort expectedVendorId, ushort expectedProductId)
    {
        Assert.True(DeviceIdParser.TryParse(deviceId, out var vendorId, out var productId));
        Assert.Equal(expectedVendorId, vendorId);
        Assert.Equal(expectedProductId, productId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("HID\\VID_1532")]
    [InlineData("HID\\VID_ZZZZ&PID_02C6")]
    public void RejectsMalformedIds(string? deviceId)
    {
        Assert.False(DeviceIdParser.TryParse(deviceId, out _, out _));
    }
}

public sealed class RazerFeatureReportTests
{
    [Fact]
    public void BuildsNinetyOneByteFeatureRequestWithCrc()
    {
        var report = RazerFeatureReport.CreateRequest(0x1F, 0x07, 0x04, 0x85, new byte[] { 0x00 });

        Assert.Equal(91, report.Length);
        Assert.Equal(0x1F, report[2]);
        Assert.Equal(0x07, report[6]);
        Assert.Equal(0x04, report[7]);
        Assert.Equal(0x85, report[8]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(report), report[89]);
    }

    [Fact]
    public void RejectsFeatureResponseWithInvalidCrc()
    {
        var request = RazerFeatureReport.CreateRequest(0xFF, 0x02, 0x0E, 0x84, new byte[] { 0x01, 0x00 });
        var response = (byte[])request.Clone();
        response[1] = 0x02;

        Assert.True(RazerFeatureReport.Matches(request, response));

        response[RazerFeatureReport.ArgumentsOffset + 1] = 0xFF;
        Assert.False(RazerFeatureReport.Matches(request, response));
    }

    [Fact]
    public void RejectsFeatureResponseFromAnotherTransaction()
    {
        var request = RazerFeatureReport.CreateRequest(0x1F, 0x01, 0x07, 0x80, Array.Empty<byte>());
        var response = (byte[])request.Clone();
        response[1] = 0x02;
        response[2] = 0x20;

        Assert.False(RazerFeatureReport.Matches(request, response));
    }

    [Fact]
    public void RejectsFeatureResponseFromAnotherReportId()
    {
        var response = new byte[RazerFeatureReport.Length];
        response[0] = 0x02;

        Assert.True(RazerFeatureReport.MatchesReportId(response, 0x02));
        Assert.False(RazerFeatureReport.MatchesReportId(response, 0x00));
    }
}

public sealed class BladeBoostProtocolTests
{
    [Theory]
    [InlineData(0x00, BladeCpuBoostMode.Low)]
    [InlineData(0x01, BladeCpuBoostMode.Medium)]
    [InlineData(0x02, BladeCpuBoostMode.High)]
    [InlineData(0x03, BladeCpuBoostMode.Boost)]
    [InlineData(0x04, BladeCpuBoostMode.Undervolt)]
    public void ParsesCpuBoostValues(byte raw, BladeCpuBoostMode expected)
    {
        var response = CreateBoostResponse(BladeBoostProtocol.CpuCluster, raw);

        Assert.Equal(expected, BladeBoostProtocol.ParseCpu(response));
    }

    [Theory]
    [InlineData(0x00, BladeGpuBoostMode.Low)]
    [InlineData(0x01, BladeGpuBoostMode.Medium)]
    [InlineData(0x02, BladeGpuBoostMode.High)]
    public void ParsesGpuBoostValues(byte raw, BladeGpuBoostMode expected)
    {
        var response = CreateBoostResponse(BladeBoostProtocol.GpuCluster, raw);

        Assert.Equal(expected, BladeBoostProtocol.ParseGpu(response));
    }

    [Fact]
    public void RejectsUnknownAndCrossClusterBoostValues()
    {
        Assert.Throws<InvalidOperationException>(() =>
            BladeBoostProtocol.ParseCpu(CreateBoostResponse(BladeBoostProtocol.CpuCluster, 0x05)));
        Assert.Throws<InvalidOperationException>(() =>
            BladeBoostProtocol.ParseGpu(CreateBoostResponse(BladeBoostProtocol.CpuCluster, 0x00)));
    }

    private static byte[] CreateBoostResponse(byte cluster, byte value)
    {
        var response = RazerFeatureReport.CreateRequest(
            0x1F, 0x03, 0x0D, 0x87, new byte[] { 0x00, cluster, value });
        response[1] = 0x02;
        return response;
    }
}

public sealed class RazerWriteSafetyTests
{
    [Fact]
    public async Task RejectsWritesUntilTheMatchingReadHasSucceeded()
    {
        var blade = new DeviceDescriptor(
            "blade-path", "Blade", 0x1532, 0x02C6,
            DeviceAccessState.Available, DeviceCapabilityState.PendingValidation, 91, 1, 2,
            "blade-710");
        var viper = new DeviceDescriptor(
            "viper-path", "Viper", 0x1532, 0x00B8,
            DeviceAccessState.Available, DeviceCapabilityState.PendingValidation, 91, 1, 2,
            "viper-184");
        var controller = new RazerDeviceTelemetryReader();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await controller.SetBladeKeyboardBrightnessAsync(new[] { blade }, 128));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await controller.SetBladeCpuBoostModeAsync(new[] { blade }, BladeCpuBoostMode.Medium));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await controller.SetBladeGpuBoostModeAsync(new[] { blade }, BladeGpuBoostMode.Medium));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await controller.SetViperPollingRateAsync(new[] { viper }, 500));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await controller.SetViperDpiAsync(new[] { viper }, 1600, 1600));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await controller.SetViperIdleSecondsAsync(new[] { viper }, 180));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(99)]
    [InlineData(101)]
    [InlineData(125)]
    public async Task RejectsDpiOutsideTheVerifiedRangeOrStep(int dpi)
    {
        var viper = new DeviceDescriptor(
            "viper-path", "Viper", 0x1532, 0x00B8,
            DeviceAccessState.Available, DeviceCapabilityState.PendingValidation, 91, 1, 2,
            "viper-184");
        var controller = new RazerDeviceTelemetryReader();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await controller.SetViperDpiAsync(new[] { viper }, dpi, dpi));
    }

    [Theory]
    [InlineData(61)]
    [InlineData(90)]
    [InlineData(901)]
    public async Task RejectsIdleTimeoutOutsideWholeMinuteRange(int seconds)
    {
        var viper = new DeviceDescriptor(
            "viper-path", "Viper", 0x1532, 0x00B8,
            DeviceAccessState.Available, DeviceCapabilityState.PendingValidation, 91, 1, 2,
            "viper-184");
        var controller = new RazerDeviceTelemetryReader();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await controller.SetViperIdleSecondsAsync(new[] { viper }, seconds));
    }
}

public sealed class RazerHardwareSmokeTests
{
    [HardwareFact]
    [Trait("Category", "Hardware")]
    public async Task ReadsBladePlatformStateAndChargeLimit()
    {
        var snapshot = await new WindowsHidDiscovery().DiscoverAsync();
        var blade = snapshot.Devices.Where(device => device.ProductId == 0x02C6).ToArray();
        Assert.Single(blade);

        var telemetry = await new RazerDeviceTelemetryReader().ReadAsync(blade);

        Assert.NotNull(telemetry.BladePerformanceMode);
        Assert.NotNull(telemetry.BladeFanMode);
        Assert.NotNull(telemetry.BladeChargeLimitPercent);
    }

    [HardwareFact]
    [Trait("Category", "Hardware")]
    public async Task ChangesBladeBrightnessByOneRawStepAndRestoresIt()
    {
        var snapshot = await new WindowsHidDiscovery().DiscoverAsync();
        var blade = snapshot.Devices.Where(device => device.ProductId == 0x02C6).ToArray();
        Assert.Single(blade);

        var controller = new RazerDeviceTelemetryReader();
        var telemetry = await controller.ReadAsync(blade);
        Assert.NotNull(telemetry.BladeKeyboardBrightness);

        var original = telemetry.BladeKeyboardBrightness.Value;
        var changed = original == byte.MaxValue ? (byte)(original - 1) : (byte)(original + 1);
        byte? actual = null;
        try
        {
            actual = await controller.SetBladeKeyboardBrightnessAsync(blade, changed);
        }
        finally
        {
            var restored = await controller.SetBladeKeyboardBrightnessAsync(blade, original);
            Assert.Equal(original, restored);
        }

        Assert.Equal(changed, actual);
    }

    [HardwareFact]
    [Trait("Category", "Hardware")]
    public async Task ChangesBladePerformanceModeAndRestoresIt()
    {
        var snapshot = await new WindowsHidDiscovery().DiscoverAsync();
        var blade = snapshot.Devices.Where(device => device.ProductId == 0x02C6).ToArray();
        Assert.Single(blade);

        var controller = new RazerDeviceTelemetryReader();
        var telemetry = await controller.ReadAsync(blade);
        Assert.NotNull(telemetry.BladePerformanceMode);
        Assert.NotNull(telemetry.BladeFanMode);

        var originalMode = telemetry.BladePerformanceMode.Value;
        var originalFanMode = telemetry.BladeFanMode.Value;
        var changedMode = originalMode == BladePerformanceMode.Balanced
            ? BladePerformanceMode.Performance
            : BladePerformanceMode.Balanced;
        try
        {
            Assert.Equal(changedMode, await controller.SetBladePerformanceModeAsync(blade, changedMode));
            var changed = await controller.ReadAsync(blade);
            Assert.Equal(changedMode, changed.BladePerformanceMode);
            Assert.Equal(originalFanMode, changed.BladeFanMode);
        }
        finally
        {
            Assert.Equal(originalMode, await controller.SetBladePerformanceModeAsync(blade, originalMode));
        }
    }

    [HardwareFact]
    [Trait("Category", "Hardware")]
    public async Task ChangesBladeBoostByOneStepAndRestoresItInCustomMode()
    {
        var snapshot = await new WindowsHidDiscovery().DiscoverAsync();
        var blade = snapshot.Devices.Where(device => device.ProductId == 0x02C6).ToArray();
        Assert.Single(blade);

        var controller = new RazerDeviceTelemetryReader();
        var initial = await controller.ReadAsync(blade);
        Assert.NotNull(initial.BladePerformanceMode);
        Assert.NotNull(initial.BladeCpuBoostMode);
        Assert.NotNull(initial.BladeGpuBoostMode);

        var initialPerformance = initial.BladePerformanceMode.Value;
        var initialCpu = initial.BladeCpuBoostMode.Value;
        var initialGpu = initial.BladeGpuBoostMode.Value;
        var transport = new RazerFeatureTransport();
        try
        {
            if (initialPerformance != BladePerformanceMode.Custom)
            {
                Assert.Equal(
                    BladePerformanceMode.Custom,
                    await controller.SetBladePerformanceModeAsync(blade, BladePerformanceMode.Custom));
            }

            var thermal = await ReadBladeThermalModeAsync(transport, blade[0].Id);
            Assert.Equal(BladePerformanceMode.Custom, thermal);

            await WriteBladeBoostAsync(transport, blade[0].Id, BladeBoostProtocol.CpuCluster, (byte)initialCpu);
            await WriteBladeBoostAsync(transport, blade[0].Id, BladeBoostProtocol.GpuCluster, (byte)initialGpu);
            Assert.Equal(initialCpu, await ReadBladeCpuBoostAsync(transport, blade[0].Id));
            Assert.Equal(initialGpu, await ReadBladeGpuBoostAsync(transport, blade[0].Id));

            var changedCpu = initialCpu == BladeCpuBoostMode.Undervolt
                ? initialCpu - 1
                : initialCpu + 1;
            var changedGpu = initialGpu == BladeGpuBoostMode.High
                ? initialGpu - 1
                : initialGpu + 1;

            Assert.Equal(changedCpu, await controller.SetBladeCpuBoostModeAsync(blade, changedCpu));
            Assert.Equal(changedCpu, await ReadBladeCpuBoostAsync(transport, blade[0].Id));
            Assert.Equal(changedGpu, await controller.SetBladeGpuBoostModeAsync(blade, changedGpu));
            Assert.Equal(changedGpu, await ReadBladeGpuBoostAsync(transport, blade[0].Id));
        }
        finally
        {
            try
            {
                if (await ReadBladeThermalModeAsync(transport, blade[0].Id) == BladePerformanceMode.Custom)
                {
                    await WriteBladeBoostAsync(transport, blade[0].Id, BladeBoostProtocol.CpuCluster, (byte)initialCpu);
                    await WriteBladeBoostAsync(transport, blade[0].Id, BladeBoostProtocol.GpuCluster, (byte)initialGpu);
                    Assert.Equal(initialCpu, await ReadBladeCpuBoostAsync(transport, blade[0].Id));
                    Assert.Equal(initialGpu, await ReadBladeGpuBoostAsync(transport, blade[0].Id));
                }
            }
            finally
            {
                if (initialPerformance != BladePerformanceMode.Custom)
                {
                    Assert.Equal(
                        initialPerformance,
                        await controller.SetBladePerformanceModeAsync(blade, initialPerformance));
                }
            }
        }
    }

    private static async Task<BladePerformanceMode> ReadBladeThermalModeAsync(
        IRazerFeatureTransport transport,
        string devicePath)
    {
        BladePerformanceMode? mode = null;
        foreach (var zone in new byte[] { 0x01, 0x02 })
        {
            var response = await transport.QueryAsync(
                devicePath, 0x1F, 0x04, 0x0D, 0x82,
                new byte[] { 0x00, zone, 0x00, 0x00 },
                TimeSpan.FromMilliseconds(2), CancellationToken.None);
            Assert.Equal(zone, response[RazerFeatureReport.ArgumentsOffset + 1]);
            var current = (BladePerformanceMode)response[RazerFeatureReport.ArgumentsOffset + 2];
            Assert.True(Enum.IsDefined(current));
            mode ??= current;
            Assert.Equal(mode, current);
        }

        return mode ?? throw new InvalidOperationException("Blade 未返回性能模式。");
    }

    private static async Task<BladeCpuBoostMode> ReadBladeCpuBoostAsync(
        IRazerFeatureTransport transport,
        string devicePath)
    {
        var response = await transport.QueryAsync(
            devicePath, 0x1F, 0x03, 0x0D, 0x87,
            new byte[] { 0x00, BladeBoostProtocol.CpuCluster, 0x00 },
            TimeSpan.FromMilliseconds(2), CancellationToken.None);
        return BladeBoostProtocol.ParseCpu(response);
    }

    private static async Task<BladeGpuBoostMode> ReadBladeGpuBoostAsync(
        IRazerFeatureTransport transport,
        string devicePath)
    {
        var response = await transport.QueryAsync(
            devicePath, 0x1F, 0x03, 0x0D, 0x87,
            new byte[] { 0x00, BladeBoostProtocol.GpuCluster, 0x00 },
            TimeSpan.FromMilliseconds(2), CancellationToken.None);
        return BladeBoostProtocol.ParseGpu(response);
    }

    private static Task WriteBladeBoostAsync(
        IRazerFeatureTransport transport,
        string devicePath,
        byte cluster,
        byte value) =>
        transport.QueryAsync(
            devicePath, 0x1F, 0x03, 0x0D, 0x07,
            new byte[] { 0x00, cluster, value },
            TimeSpan.FromMilliseconds(2), CancellationToken.None);

    [HardwareFact]
    [Trait("Category", "Hardware")]
    public async Task ChangesBladeChargeLimitAndRestoresIt()
    {
        var snapshot = await new WindowsHidDiscovery().DiscoverAsync();
        var blade = snapshot.Devices.Where(device => device.ProductId == 0x02C6).ToArray();
        Assert.Single(blade);

        var controller = new RazerDeviceTelemetryReader();
        var telemetry = await controller.ReadAsync(blade);
        Assert.NotNull(telemetry.BladeChargeLimitPercent);

        var original = telemetry.BladeChargeLimitPercent.Value;
        var allowed = new[] { 50, 55, 60, 65, 70, 75, 80, 100 };
        var changed = allowed.First(value => value != original);
        try
        {
            Assert.Equal(changed, await controller.SetBladeChargeLimitAsync(blade, changed));
        }
        finally
        {
            Assert.Equal(original, await controller.SetBladeChargeLimitAsync(blade, original));
        }
    }

    [HardwareFact]
    [Trait("Category", "Hardware")]
    public async Task ChangesBladeMaxFanModeAndRestoresItInCustomMode()
    {
        var snapshot = await new WindowsHidDiscovery().DiscoverAsync();
        var blade = snapshot.Devices.Where(device => device.ProductId == 0x02C6).ToArray();
        Assert.Single(blade);

        var controller = new RazerDeviceTelemetryReader();
        var initial = await controller.ReadAsync(blade);
        Assert.NotNull(initial.BladePerformanceMode);
        Assert.NotNull(initial.BladeMaxFanMode);

        var originalPerformance = initial.BladePerformanceMode.Value;
        var originalMaxFan = initial.BladeMaxFanMode.Value;
        var changed = originalMaxFan == BladeMaxFanMode.Enabled
            ? BladeMaxFanMode.Disabled
            : BladeMaxFanMode.Enabled;
        try
        {
            if (originalPerformance != BladePerformanceMode.Custom)
            {
                Assert.Equal(
                    BladePerformanceMode.Custom,
                    await controller.SetBladePerformanceModeAsync(blade, BladePerformanceMode.Custom));
            }

            Assert.Equal(changed, await controller.SetBladeMaxFanModeAsync(blade, changed));
            Assert.Equal(changed, (await controller.ReadAsync(blade)).BladeMaxFanMode);
        }
        finally
        {
            await controller.ReadAsync(blade);
            Assert.Equal(originalMaxFan, await controller.SetBladeMaxFanModeAsync(blade, originalMaxFan));
            if (originalPerformance != BladePerformanceMode.Custom)
            {
                Assert.Equal(
                    originalPerformance,
                    await controller.SetBladePerformanceModeAsync(blade, originalPerformance));
            }
        }
    }

    [HardwareFact]
    [Trait("Category", "Hardware")]
    public async Task ChangesBladeLogoModeAndRestoresPowerAndMode()
    {
        var snapshot = await new WindowsHidDiscovery().DiscoverAsync();
        var blade = Assert.Single(snapshot.Devices, device => device.ProductId == 0x02C6);
        var transport = new RazerFeatureTransport();
        var original = await ReadBladeLogoStateAsync(transport, blade.Id);

        try
        {
            var states = new[]
            {
                original,
                new BladeLogoState(false, original.Mode),
                new BladeLogoState(true, BladeLogoMode.Static),
                new BladeLogoState(true, BladeLogoMode.Breathing),
            }.Distinct();

            foreach (var state in states)
            {
                await WriteBladeLogoStateAsync(transport, blade.Id, state);
                Assert.Equal(state, await ReadBladeLogoStateAsync(transport, blade.Id));
            }
        }
        finally
        {
            await WriteBladeLogoStateAsync(transport, blade.Id, original);
            Assert.Equal(original, await ReadBladeLogoStateAsync(transport, blade.Id));
        }
    }

    private static async Task<BladeLogoState> ReadBladeLogoStateAsync(
        IRazerFeatureTransport transport,
        string devicePath)
    {
        var powerResponse = await SendBladeLogoAsync(
            transport, devicePath, BladeLogoProtocol.CreateGetPowerRequest());
        var modeResponse = await SendBladeLogoAsync(
            transport, devicePath, BladeLogoProtocol.CreateGetModeRequest());

        return new BladeLogoState(
            BladeLogoProtocol.ParsePower(powerResponse),
            BladeLogoProtocol.ParseMode(modeResponse));
    }

    private static async Task WriteBladeLogoStateAsync(
        IRazerFeatureTransport transport,
        string devicePath,
        BladeLogoState state)
    {
        await SendBladeLogoAsync(
            transport, devicePath, BladeLogoProtocol.CreateSetModeRequest(state.Mode));
        await SendBladeLogoAsync(
            transport, devicePath, BladeLogoProtocol.CreateSetPowerRequest(state.Powered));
    }

    private static Task<byte[]> SendBladeLogoAsync(
        IRazerFeatureTransport transport,
        string devicePath,
        byte[] request) =>
        transport.QueryAsync(
            devicePath, request[2], request[6], request[7], request[8],
            request.AsMemory(RazerFeatureReport.ArgumentsOffset, request[6]),
            TimeSpan.FromMilliseconds(2), CancellationToken.None);

    private sealed record BladeLogoState(bool Powered, BladeLogoMode Mode);

    [HardwareFact]
    [Trait("Category", "Hardware")]
    public async Task ChangesViperSettingsAndRestoresThem()
    {
        var snapshot = await new WindowsHidDiscovery().DiscoverAsync();
        var viper = snapshot.Devices.Where(device => device.ProductId == 0x00B8).ToArray();
        Assert.Single(viper);

        var controller = new RazerDeviceTelemetryReader();
        var telemetry = await controller.ReadAsync(viper);
        Assert.NotNull(telemetry.ViperPollingRateHertz);
        Assert.NotNull(telemetry.ViperDpiX);
        Assert.NotNull(telemetry.ViperDpiY);
        Assert.NotNull(telemetry.ViperIdleSeconds);

        var originalPolling = telemetry.ViperPollingRateHertz.Value;
        var changedPolling = originalPolling == 1000 ? 500 : 1000;
        try
        {
            Assert.Equal(changedPolling, await controller.SetViperPollingRateAsync(viper, changedPolling));
        }
        finally
        {
            Assert.Equal(originalPolling, await controller.SetViperPollingRateAsync(viper, originalPolling));
        }

        var originalX = telemetry.ViperDpiX.Value;
        var originalY = telemetry.ViperDpiY.Value;
        var changedX = originalX == 30000 ? originalX - 50 : originalX + 50;
        var changedY = originalY == 30000 ? originalY - 50 : originalY + 50;
        try
        {
            Assert.Equal((changedX, changedY), await controller.SetViperDpiAsync(viper, changedX, changedY));
        }
        finally
        {
            Assert.Equal((originalX, originalY), await controller.SetViperDpiAsync(viper, originalX, originalY));
        }

        var originalIdle = telemetry.ViperIdleSeconds.Value;
        Assert.InRange(originalIdle, 60, 900);
        var changedIdle = originalIdle == 900 ? originalIdle - 60 : originalIdle + 60;
        try
        {
            Assert.Equal(changedIdle, await controller.SetViperIdleSecondsAsync(viper, changedIdle));
        }
        finally
        {
            Assert.Equal(originalIdle, await controller.SetViperIdleSecondsAsync(viper, originalIdle));
        }
    }
}

public sealed class HardwareFactAttribute : FactAttribute
{
    public HardwareFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("OPENSYNAPSE_HARDWARE_TEST") != "1")
        {
            Skip = "Set OPENSYNAPSE_HARDWARE_TEST=1 to run tests that write to connected hardware.";
        }
    }
}

public sealed class NvidiaSmiOutputParserTests
{
    [Fact]
    public void ParsesRealNvidiaSmiShape()
    {
        var sample = NvidiaSmiOutputParser.Parse("NVIDIA GeForce RTX 5070 Laptop GPU, 62, 17, 11.64, 1800, 1024, 8151\r\n");

        Assert.NotNull(sample);
        Assert.Equal("NVIDIA GeForce RTX 5070 Laptop GPU", sample.Name);
        Assert.Equal(62, sample.TemperatureCelsius);
        Assert.Equal(17, sample.UsagePercent);
        Assert.Equal(11.64, sample.PowerWatts);
        Assert.Equal(1800, sample.ClockMegahertz);
        Assert.Equal(1024, sample.MemoryUsedMebibytes);
        Assert.Equal(8151, sample.MemoryTotalMebibytes);
    }
}

public sealed class WindowsGpuActivityReaderTests
{
    [Fact]
    public void UsesIntegratedGpuWhileNvidiaHasNoExternalWork()
    {
        WindowsGpuSample[] samples =
        [
            new("NVIDIA", 0x10DE, 1.1, 0, 512, 8192),
            new("AMD Radeon", 0x1002, 6.5, 6.5, 400, 16384, IsIntegrated: true),
        ];

        Assert.False(WindowsGpuActivityReader.IsNvidiaActive(samples));
        Assert.Equal("AMD Radeon", WindowsGpuActivityReader.SelectIntegrated(samples)!.Name);
        Assert.Equal("NVIDIA", WindowsGpuActivityReader.SelectNvidia(samples)!.Name);
    }

    [Fact]
    public void TreatsExternalNvidiaWorkAsActive()
    {
        WindowsGpuSample[] samples =
        [
            new("NVIDIA", 0x10DE, 12, 11, 2048, 8192),
            new("AMD Radeon", 0x1002, 4, 4, 400, 16384, IsIntegrated: true),
        ];

        Assert.True(WindowsGpuActivityReader.IsNvidiaActive(samples));
    }

    [Fact]
    public void ParsesSupportedAmdPmLogSensors()
    {
        var sensors = new AmdAdlTelemetryReader.AmdSensorData[256];
        sensors[1] = new(1, 2227);
        sensors[8] = new(1, 50);
        sensors[23] = new(1, 27);

        var sample = AmdAdlTelemetryReader.Parse(sensors);

        Assert.Equal(50, sample.TemperatureCelsius);
        Assert.Equal(27, sample.PowerWatts);
        Assert.Equal(2227, sample.ClockMegahertz);
    }
}
