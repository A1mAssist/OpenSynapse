using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using System.Text.Json.Nodes;

namespace OpenSynapse.Core.Tests;

public sealed class RazerDeviceTelemetryReaderTests
{
    private static readonly DeviceDescriptor Blade = new(
        "blade-path", "Blade", 0x1532, 0x02C6,
        DeviceAccessState.Available, DeviceCapabilityState.PendingValidation, 91, 1, 2,
        "blade-710");

    private static readonly DeviceDescriptor Viper = new(
        "viper-path", "Viper", 0x1532, 0x00B8,
        DeviceAccessState.Available, DeviceCapabilityState.PendingValidation, 91, 1, 2,
        "viper-184");

    [Fact]
    public async Task ReadsBladePlatformStateAndChargeLimit()
    {
        var transport = new FakeRazerFeatureTransport
        {
            PerformanceMode = BladePerformanceMode.Balanced,
            FanMode = BladeFanMode.Automatic,
            ChargeLimitRaw = 0xD0,
            CpuBoostMode = BladeCpuBoostMode.Medium,
            GpuBoostMode = BladeGpuBoostMode.Low,
        };
        var reader = new RazerDeviceTelemetryReader(transport);

        var telemetry = await reader.ReadAsync(new[] { Blade });

        Assert.Equal((byte)128, telemetry.BladeKeyboardBrightness);
        Assert.Equal(BladePerformanceMode.Balanced, telemetry.BladePerformanceMode);
        Assert.Equal(BladeFanMode.Automatic, telemetry.BladeFanMode);
        Assert.Null(telemetry.BladeFanTargetRpm);
        Assert.Equal(80, telemetry.BladeChargeLimitPercent);
        Assert.Equal(BladeCpuBoostMode.Medium, telemetry.BladeCpuBoostMode);
        Assert.Equal(BladeGpuBoostMode.Low, telemetry.BladeGpuBoostMode);
        Assert.Equal(BladeMaxFanMode.Disabled, telemetry.BladeMaxFanMode);
        Assert.Equal(2200, telemetry.BladeCurrentFanCpuRpm);
        Assert.Equal(2000, telemetry.BladeCurrentFanGpuRpm);
        Assert.Equal((byte)0, telemetry.BladeAdvancedFanCpuModeRaw);
        Assert.Equal((byte)0, telemetry.BladeAdvancedFanGpuModeRaw);
        Assert.Equal(50, telemetry.BladeWiredBatteryPercent);
        Assert.Equal((byte)0, telemetry.BladeChargingStatusRaw);
        Assert.Equal((byte)1, telemetry.BladeAutoSleepRaw);
        Assert.Equal(300, telemetry.BladeTimeToSleepSeconds);
        Assert.Equal(BladeLogoMode.Static, telemetry.BladeLogoMode);
        Assert.True(transport.BatteryQueryAllowedRemainingPacketsMismatch);
        Assert.Empty(telemetry.Errors);
    }

    [Fact]
    public async Task KeepsBladeProduct710TelemetryWhenOneReadFails()
    {
        var transport = new FakeRazerFeatureTransport { FailBladeProduct710Command = 0x84 };
        var reader = new RazerDeviceTelemetryReader(transport);

        var telemetry = await reader.ReadAsync(new[] { Blade });

        Assert.Equal(50, telemetry.BladeWiredBatteryPercent);
        Assert.Null(telemetry.BladeChargingStatusRaw);
        Assert.Equal((byte)1, telemetry.BladeAutoSleepRaw);
        Assert.Equal(300, telemetry.BladeTimeToSleepSeconds);
        Assert.Contains(telemetry.Errors, error => error.StartsWith("充电状态：", StringComparison.Ordinal));
    }

    [Fact]
    public async Task KeepsGpuFanTelemetryWhenCpuFanReadFails()
    {
        var transport = new FakeRazerFeatureTransport { FailCurrentFanId = BladeThermalProtocol.CpuFanId };
        var reader = new RazerDeviceTelemetryReader(transport);

        var telemetry = await reader.ReadAsync(new[] { Blade });

        Assert.Null(telemetry.BladeCurrentFanCpuRpm);
        Assert.Equal(2000, telemetry.BladeCurrentFanGpuRpm);
        Assert.Contains(telemetry.Errors, error => error.StartsWith("当前 CPU 风扇转速：", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReadsViperSourceBackedTelemetry()
    {
        var telemetry = await new RazerDeviceTelemetryReader(new FakeRazerFeatureTransport())
            .ReadAsync(new[] { Viper });

        Assert.Equal(84, telemetry.ViperBatteryPercent);
        Assert.Equal(500, telemetry.ViperPollingRateHertz);
        Assert.Equal(1600, telemetry.ViperDpiX);
        Assert.Equal(1600, telemetry.ViperDpiY);
        Assert.Equal(180, telemetry.ViperIdleSeconds);
        Assert.Equal((byte)3, telemetry.ViperDpiStages?.ActiveStage);
        Assert.Equal(new[] { 400, 800, 1600, 3200, 6400 },
            telemetry.ViperDpiStages?.Stages.Select(stage => stage.X));
        Assert.Equal((byte)0x4D, telemetry.ViperLowBatteryThresholdRaw);
        Assert.Empty(telemetry.Errors);
    }

    [Fact]
    public async Task UsesSameFamilyManifestForDifferentPidAndTransactionId()
    {
        var assembly = typeof(RazerDeviceRegistry).Assembly;
        var resourceName = Assert.Single(
            assembly.GetManifestResourceNames(),
            name => name.EndsWith(".viper-184.json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        var document = JsonNode.Parse(stream)!.AsObject();
        document["id"] = "viper-compatible";
        document["productIds"]![0] = "FFFE";
        foreach (var capability in document["capabilities"]!.AsObject())
        {
            capability.Value!["transactionId"] = "2A";
        }
        var registry = RazerDeviceRegistry.LoadJson(new[] { document.ToJsonString() });
        var compatible = Viper with { ProductId = 0xFFFE };
        var transport = new FakeRazerFeatureTransport();
        var reader = new RazerDeviceTelemetryReader(transport, registry);

        var telemetry = await reader.ReadAsync(new[] { compatible });
        var requested = new ViperDpiStagesTelemetry(
            1,
            [new(1, 450, 450), new(2, 800, 800)]);
        var actual = await reader.SetViperDpiStagesAsync(new[] { compatible }, requested);

        Assert.Equal(84, telemetry.ViperBatteryPercent);
        Assert.Equal(requested.ActiveStage, actual.ActiveStage);
        Assert.Equal(requested.Stages, actual.Stages);
        Assert.Equal((byte)0x2A, transport.LastViperTransactionId);
    }

    [Fact]
    public async Task KeepsViperDpiStagesWhenLowBatteryThresholdReadFails()
    {
        var transport = new FakeRazerFeatureTransport { FailViperCommand = 0x81 };

        var telemetry = await new RazerDeviceTelemetryReader(transport).ReadAsync(new[] { Viper });

        Assert.NotNull(telemetry.ViperDpiStages);
        Assert.Null(telemetry.ViperLowBatteryThresholdRaw);
        Assert.Contains(telemetry.Errors, error => error.StartsWith("鼠标低电量阈值：", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SetsViperDpiStagesAndReadsBackTheCompleteTable()
    {
        var transport = new FakeRazerFeatureTransport();
        var reader = new RazerDeviceTelemetryReader(transport);
        await reader.ReadAsync(new[] { Viper });

        var requested = new ViperDpiStagesTelemetry(
            3,
            [
                new(1, 450, 450),
                new(2, 800, 800),
                new(3, 1600, 1600),
                new(4, 3200, 3200),
                new(5, 6400, 6400),
            ]);

        var actual = await reader.SetViperDpiStagesAsync(new[] { Viper }, requested);

        Assert.Equal(requested.ActiveStage, actual.ActiveStage);
        Assert.Equal(requested.Stages, actual.Stages);
        Assert.Equal(requested.Stages, transport.DpiStages.Stages);
    }

    [Fact]
    public async Task RestoresViperDpiStagesWhenTargetReadbackFails()
    {
        var transport = new FakeRazerFeatureTransport { IgnoreNextDpiStagesWrite = true };
        var reader = new RazerDeviceTelemetryReader(transport);
        await reader.ReadAsync(new[] { Viper });

        var requested = new ViperDpiStagesTelemetry(
            3,
            [
                new(1, 450, 450),
                new(2, 800, 800),
                new(3, 1600, 1600),
                new(4, 3200, 3200),
                new(5, 6400, 6400),
            ]);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await reader.SetViperDpiStagesAsync(new[] { Viper }, requested));

        Assert.Contains("原值已恢复", error.Message, StringComparison.Ordinal);
        Assert.Equal(400, transport.DpiStages.Stages[0].X);
        Assert.Equal(2, transport.DpiStagesWriteCount);
    }

    [Fact]
    public async Task ReadsManualBladeFanTargetWhenBothZonesAgree()
    {
        var transport = new FakeRazerFeatureTransport
        {
            FanMode = BladeFanMode.Manual,
            FanTargetRpm = 3400,
        };
        var reader = new RazerDeviceTelemetryReader(transport);

        var telemetry = await reader.ReadAsync(new[] { Blade });

        Assert.Equal(BladeFanMode.Manual, telemetry.BladeFanMode);
        Assert.Equal(3400, telemetry.BladeFanTargetRpm);
    }

    [Fact]
    public async Task WritesBladeFanTargetsBeforeManualModeAndReadsBothZonesBack()
    {
        var transport = new FakeRazerFeatureTransport
        {
            PerformanceMode = BladePerformanceMode.Custom,
            FanMode = BladeFanMode.Automatic,
            FanTargetRpm = 3200,
        };
        var reader = new RazerDeviceTelemetryReader(transport);

        var actual = await reader.SetBladeFanAsync(new[] { Blade }, BladeFanMode.Manual, 3400);

        Assert.Equal(new BladeFanControlState(BladeFanMode.Manual, 3400), actual);
        Assert.Equal(3400, transport.FanTargetRpm);
        Assert.Equal((ushort)3400, transport.Zone2FanTargetRpm);
        Assert.Equal(BladeFanMode.Manual, transport.FanMode);
        Assert.Equal(BladeFanMode.Manual, transport.Zone2FanMode);
        Assert.Equal(
            new[] { "GET-M1", "GET-M2", "GET-T1", "GET-T2", "GET-RPM1", "GET-RPM2", "SET-T1", "SET-T2", "SET-M1", "SET-M2", "GET-M1", "GET-M2", "GET-T1", "GET-T2" },
            transport.FanCommands);
    }

    [Fact]
    public async Task WritesAutomaticModeWithoutChangingStoredTargets()
    {
        var transport = new FakeRazerFeatureTransport
        {
            FanMode = BladeFanMode.Manual,
            FanTargetRpm = 3400,
        };
        var reader = new RazerDeviceTelemetryReader(transport);

        var actual = await reader.SetBladeFanAsync(new[] { Blade }, BladeFanMode.Automatic, null);

        Assert.Equal(new BladeFanControlState(BladeFanMode.Automatic, 3400), actual);
        Assert.Equal((ushort)3400, transport.FanTargetRpm);
        Assert.DoesNotContain(transport.FanCommands, command => command.StartsWith("SET-T", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(1900)]
    [InlineData(5100)]
    [InlineData(3350)]
    public async Task RejectsInvalidBladeFanTargetBeforeAnyDeviceIo(int targetRpm)
    {
        var transport = new FakeRazerFeatureTransport();
        var reader = new RazerDeviceTelemetryReader(transport);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await reader.SetBladeFanAsync(new[] { Blade }, BladeFanMode.Manual, targetRpm));

        Assert.Empty(transport.FanCommands);
    }

    [Fact]
    public async Task BlocksBladeFanSetWhenCurrentPathGetFails()
    {
        var transport = new FakeRazerFeatureTransport { FailFanTargetGetZone = BladeFanProtocol.ZoneGpu };
        var reader = new RazerDeviceTelemetryReader(transport);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await reader.SetBladeFanAsync(new[] { Blade }, BladeFanMode.Manual, 3400));

        Assert.DoesNotContain(transport.FanCommands, command => command.StartsWith("SET-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FixedFanSetCanReplaceDifferentOriginalTargets()
    {
        var transport = new FakeRazerFeatureTransport
        {
            FanTargetRpm = 3200,
            Zone2FanTargetRpm = 3300,
        };
        var reader = new RazerDeviceTelemetryReader(transport);

        var actual = await reader.SetBladeFanAsync(
            new[] { Blade }, BladeFanMode.Manual, 3400);

        Assert.Equal(new BladeFanControlState(BladeFanMode.Manual, 3400), actual);
        Assert.Equal(3400, transport.FanTargetRpm);
        Assert.Equal((ushort)3400, transport.Zone2FanTargetRpm);
    }

    [Fact]
    public async Task WritesAndReadsIndependentCurveTargets()
    {
        var transport = new FakeRazerFeatureTransport
        {
            FanTargetRpm = 3200,
            Zone2FanTargetRpm = 3300,
        };
        var reader = new RazerDeviceTelemetryReader(transport);

        var actual = await reader.SetBladeFanTargetsAsync(
            new[] { Blade }, BladeFanMode.Manual, 2100, 1900);

        Assert.Equal(
            new BladeFanControlSnapshot(
                BladePerformanceMode.Balanced,
                BladeFanMode.Manual,
                2100,
                1900),
            actual);
        Assert.Equal(2100, transport.FanTargetRpm);
        Assert.Equal((ushort)1900, transport.Zone2FanTargetRpm);
    }

    [Fact]
    public async Task RestoresBladeFanWhenGpuTargetWriteIsIgnored()
    {
        var transport = new FakeRazerFeatureTransport
        {
            Zone2FanTargetRpm = 3200,
            IgnoreFanTargetWriteNumbers = [2],
        };
        var reader = new RazerDeviceTelemetryReader(transport);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await reader.SetBladeFanAsync(new[] { Blade }, BladeFanMode.Manual, 3400));

        Assert.Contains("原状态已恢复", error.Message, StringComparison.Ordinal);
        Assert.Equal(3200, transport.FanTargetRpm);
        Assert.Equal((ushort)3200, transport.Zone2FanTargetRpm);
        Assert.Equal(BladeFanMode.Automatic, transport.FanMode);
        Assert.Equal(BladeFanMode.Automatic, transport.Zone2FanMode);
    }

    [Fact]
    public async Task RestoresBladeFanNonCancelableWhenCanceledAfterCpuTargetWrite()
    {
        var transport = new FakeRazerFeatureTransport { CancelFanTargetWriteNumberAfterApplying = 1 };
        var reader = new RazerDeviceTelemetryReader(transport);

        var error = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await reader.SetBladeFanAsync(new[] { Blade }, BladeFanMode.Manual, 3400));

        Assert.Contains("原状态已恢复", error.Message, StringComparison.Ordinal);
        Assert.Equal(3200, transport.FanTargetRpm);
        Assert.Equal((ushort)3200, transport.Zone2FanTargetRpm);
        Assert.Equal(BladeFanMode.Automatic, transport.FanMode);
        Assert.All(transport.RestorationCancellationTokens, token => Assert.Equal(CancellationToken.None, token));
    }

    [Fact]
    public async Task AggregatesBladeFanOperationAndRestorationFailures()
    {
        var transport = new FakeRazerFeatureTransport
        {
            Zone2FanTargetRpm = 3200,
            CancelFanTargetWriteNumberAfterApplying = 1,
            IgnoreFanTargetWriteNumbers = [2],
        };
        var reader = new RazerDeviceTelemetryReader(transport);

        var error = await Assert.ThrowsAsync<AggregateException>(async () =>
            await reader.SetBladeFanAsync(new[] { Blade }, BladeFanMode.Manual, 3400));

        Assert.Contains(error.InnerExceptions, exception => exception is OperationCanceledException);
        Assert.Contains(error.InnerExceptions, exception =>
            exception.ToString().Contains("恢复读回", StringComparison.Ordinal));
        Assert.Equal(3400, transport.FanTargetRpm);
    }

    [Fact]
    public async Task RejectsSplitBladeFanTargetAndKeepsPerformanceWriteGated()
    {
        var transport = new FakeRazerFeatureTransport
        {
            FanMode = BladeFanMode.Manual,
            FanTargetRpm = 3400,
            Zone2FanTargetRpm = 3500,
        };
        var reader = new RazerDeviceTelemetryReader(transport);

        var telemetry = await reader.ReadAsync(new[] { Blade });

        Assert.Equal(BladePerformanceMode.Balanced, telemetry.BladePerformanceMode);
        Assert.Equal(BladeFanMode.Manual, telemetry.BladeFanMode);
        Assert.Null(telemetry.BladeFanTargetRpm);
        Assert.Contains(telemetry.Errors, error => error.Contains("两个风扇分区设定不一致", StringComparison.Ordinal));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await reader.SetBladePerformanceModeAsync(new[] { Blade }, BladePerformanceMode.Performance));

        Assert.Contains("请先成功读取 Blade 性能模式", error.Message, StringComparison.Ordinal);
        Assert.Empty(transport.PerformanceWriteZones);
    }

    [Fact]
    public async Task RejectsSplitBladeThermalZoneState()
    {
        var transport = new FakeRazerFeatureTransport
        {
            PerformanceMode = BladePerformanceMode.Balanced,
            Zone2PerformanceMode = BladePerformanceMode.Performance,
        };
        var reader = new RazerDeviceTelemetryReader(transport);

        var telemetry = await reader.ReadAsync(new[] { Blade });

        Assert.Null(telemetry.BladePerformanceMode);
        Assert.Contains(telemetry.Errors, error => error.Contains("两个风扇分区状态不一致", StringComparison.Ordinal));
        Assert.Null(telemetry.BladeCpuBoostMode);
        Assert.Null(telemetry.BladeGpuBoostMode);
        Assert.Equal(0, transport.BoostReadCount);
    }

    [Fact]
    public async Task RejectsOutOfRangeBladeFanTargetFromEitherZone()
    {
        var transport = new FakeRazerFeatureTransport { FanMode = BladeFanMode.Manual, FanTargetRpm = 5100 };
        var reader = new RazerDeviceTelemetryReader(transport);

        var telemetry = await reader.ReadAsync(new[] { Blade });

        Assert.Null(telemetry.BladeFanTargetRpm);
        Assert.Contains(telemetry.Errors, error => error.Contains("固定风扇转速", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RejectsOutOfRangeBladeFanTargetFromGpuZone()
    {
        var transport = new FakeRazerFeatureTransport
        {
            FanMode = BladeFanMode.Manual,
            FanTargetRpm = 3400,
            Zone2FanTargetRpm = 5100,
        };
        var reader = new RazerDeviceTelemetryReader(transport);

        var telemetry = await reader.ReadAsync(new[] { Blade });

        Assert.Null(telemetry.BladeFanTargetRpm);
        Assert.Contains(telemetry.Errors, error => error.Contains("固定风扇转速", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RejectsBladeBoostResponseFromWrongCluster()
    {
        var transport = new FakeRazerFeatureTransport { BoostResponseClusterOverride = 0x03 };
        var reader = new RazerDeviceTelemetryReader(transport);

        var telemetry = await reader.ReadAsync(new[] { Blade });

        Assert.Null(telemetry.BladeCpuBoostMode);
        Assert.Null(telemetry.BladeGpuBoostMode);
        Assert.Equal(BladePerformanceMode.Balanced, telemetry.BladePerformanceMode);
        Assert.Contains(telemetry.Errors, error => error.StartsWith("CPU/GPU Boost：", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WritesBladePerformanceModeAndPreservesFanMode()
    {
        var transport = new FakeRazerFeatureTransport
        {
            PerformanceMode = BladePerformanceMode.Balanced,
            FanMode = BladeFanMode.Manual,
        };
        var reader = new RazerDeviceTelemetryReader(transport);
        await reader.ReadAsync(new[] { Blade });

        var actual = await reader.SetBladePerformanceModeAsync(new[] { Blade }, BladePerformanceMode.Performance);

        Assert.Equal(BladePerformanceMode.Performance, actual);
        Assert.Equal(new byte[] { 0x01, 0x02 }, transport.PerformanceWriteZones);
        Assert.Equal(BladeFanMode.Manual, transport.FanMode);
    }

    [Fact]
    public async Task RestoresBladePerformanceModeWhenWriteIsCanceledAfterApplying()
    {
        var transport = new FakeRazerFeatureTransport
        {
            PerformanceMode = BladePerformanceMode.Balanced,
            CancelNextPerformanceWriteAfterApplying = true,
        };
        var reader = new RazerDeviceTelemetryReader(transport);
        await reader.ReadAsync(new[] { Blade });

        var error = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await reader.SetBladePerformanceModeAsync(new[] { Blade }, BladePerformanceMode.Performance));

        Assert.Contains("原状态已恢复", error.Message, StringComparison.Ordinal);
        Assert.Equal(BladePerformanceMode.Balanced, transport.PerformanceMode);
        Assert.Equal(new byte[] { 0x01, 0x01, 0x02 }, transport.PerformanceWriteZones);
    }

    [Fact]
    public async Task WritesBladeChargeLimitAndReadsItBack()
    {
        var transport = new FakeRazerFeatureTransport { ChargeLimitRaw = 0xD0 };
        var reader = new RazerDeviceTelemetryReader(transport);
        await reader.ReadAsync(new[] { Blade });

        var actual = await reader.SetBladeChargeLimitAsync(new[] { Blade }, 75);

        Assert.Equal(75, actual);
        Assert.Equal(1, transport.ChargeWriteCount);
    }

    [Fact]
    public async Task RestoresBladeChargeLimitWhenWriteIsCanceledAfterApplying()
    {
        var transport = new FakeRazerFeatureTransport
        {
            ChargeLimitRaw = 0xD0,
            CancelNextChargeWriteAfterApplying = true,
        };
        var reader = new RazerDeviceTelemetryReader(transport);
        await reader.ReadAsync(new[] { Blade });

        var error = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await reader.SetBladeChargeLimitAsync(new[] { Blade }, 75));

        Assert.Contains("原值已恢复", error.Message, StringComparison.Ordinal);
        Assert.Equal(0xD0, transport.ChargeLimitRaw);
        Assert.Equal(2, transport.ChargeWriteCount);
    }

    [Fact]
    public async Task WritesBladeCpuBoostAndPreservesGpuInCustomMode()
    {
        var transport = new FakeRazerFeatureTransport
        {
            PerformanceMode = BladePerformanceMode.Custom,
            CpuBoostMode = BladeCpuBoostMode.Medium,
            GpuBoostMode = BladeGpuBoostMode.High,
        };
        var reader = new RazerDeviceTelemetryReader(transport);
        await reader.ReadAsync(new[] { Blade });

        var actual = await reader.SetBladeCpuBoostModeAsync(new[] { Blade }, BladeCpuBoostMode.High);

        Assert.Equal(BladeCpuBoostMode.High, actual);
        Assert.Equal(BladeGpuBoostMode.High, transport.GpuBoostMode);
        Assert.Equal(new byte[] { BladeBoostProtocol.CpuCluster }, transport.BoostWriteClusters);
    }

    [Fact]
    public async Task RejectsBladeBoostWriteOutsideCustomMode()
    {
        var transport = new FakeRazerFeatureTransport { PerformanceMode = BladePerformanceMode.Performance };
        var reader = new RazerDeviceTelemetryReader(transport);
        await reader.ReadAsync(new[] { Blade });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await reader.SetBladeGpuBoostModeAsync(new[] { Blade }, BladeGpuBoostMode.Medium));

        Assert.Contains("Custom", error.Message, StringComparison.Ordinal);
        Assert.Empty(transport.BoostWriteClusters);
    }

    [Fact]
    public async Task SkipsBladeBoostWriteWhenValueAlreadyMatches()
    {
        var transport = new FakeRazerFeatureTransport
        {
            PerformanceMode = BladePerformanceMode.Custom,
            CpuBoostMode = BladeCpuBoostMode.Medium,
        };
        var reader = new RazerDeviceTelemetryReader(transport);
        await reader.ReadAsync(new[] { Blade });

        var actual = await reader.SetBladeCpuBoostModeAsync(new[] { Blade }, BladeCpuBoostMode.Medium);

        Assert.Equal(BladeCpuBoostMode.Medium, actual);
        Assert.Empty(transport.BoostWriteClusters);
    }

    [Fact]
    public async Task RestoresBothBladeBoostValuesWhenReadbackFails()
    {
        var transport = new FakeRazerFeatureTransport
        {
            PerformanceMode = BladePerformanceMode.Custom,
            CpuBoostMode = BladeCpuBoostMode.Medium,
            GpuBoostMode = BladeGpuBoostMode.Low,
            IgnoreNextBoostWrite = true,
        };
        var reader = new RazerDeviceTelemetryReader(transport);
        await reader.ReadAsync(new[] { Blade });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await reader.SetBladeGpuBoostModeAsync(new[] { Blade }, BladeGpuBoostMode.Medium));

        Assert.Contains("原值已恢复", error.Message, StringComparison.Ordinal);
        Assert.Equal(BladeCpuBoostMode.Medium, transport.CpuBoostMode);
        Assert.Equal(BladeGpuBoostMode.Low, transport.GpuBoostMode);
        Assert.Equal(
            new byte[]
            {
                BladeBoostProtocol.GpuCluster,
                BladeBoostProtocol.CpuCluster,
                BladeBoostProtocol.GpuCluster,
            },
            transport.BoostWriteClusters);
    }

    [Fact]
    public async Task RestoresBothBladeBoostValuesWhenWriteIsCanceledAfterApplying()
    {
        var transport = new FakeRazerFeatureTransport
        {
            PerformanceMode = BladePerformanceMode.Custom,
            CpuBoostMode = BladeCpuBoostMode.Medium,
            GpuBoostMode = BladeGpuBoostMode.Low,
            CancelNextBoostWriteAfterApplying = true,
        };
        var reader = new RazerDeviceTelemetryReader(transport);
        await reader.ReadAsync(new[] { Blade });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await reader.SetBladeCpuBoostModeAsync(new[] { Blade }, BladeCpuBoostMode.High));

        Assert.Equal(BladeCpuBoostMode.Medium, transport.CpuBoostMode);
        Assert.Equal(BladeGpuBoostMode.Low, transport.GpuBoostMode);
    }

    [Fact]
    public async Task WritesBladeMaxFanAndReadsItBackInCustomMode()
    {
        var transport = new FakeRazerFeatureTransport
        {
            PerformanceMode = BladePerformanceMode.Custom,
            MaxFanMode = BladeMaxFanMode.Disabled,
            PowerModeSiblingBits = 0x0D,
        };
        var reader = new RazerDeviceTelemetryReader(transport);
        await reader.ReadAsync(new[] { Blade });

        var actual = await reader.SetBladeMaxFanModeAsync(new[] { Blade }, BladeMaxFanMode.Enabled);

        Assert.Equal(BladeMaxFanMode.Enabled, actual);
        Assert.Equal(1, transport.MaxFanWriteCount);
        Assert.Equal(0x0D, transport.PowerModeSiblingBits);
    }

    [Fact]
    public async Task RestoresBladeMaxFanWhenReadbackFails()
    {
        var transport = new FakeRazerFeatureTransport
        {
            PerformanceMode = BladePerformanceMode.Custom,
            MaxFanMode = BladeMaxFanMode.Disabled,
            IgnoreNextMaxFanWrite = true,
        };
        var reader = new RazerDeviceTelemetryReader(transport);
        await reader.ReadAsync(new[] { Blade });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await reader.SetBladeMaxFanModeAsync(new[] { Blade }, BladeMaxFanMode.Enabled));

        Assert.Contains("原值已恢复", error.Message, StringComparison.Ordinal);
        Assert.Equal(BladeMaxFanMode.Disabled, transport.MaxFanMode);
        Assert.Equal(2, transport.MaxFanWriteCount);
    }

    [Fact]
    public async Task WritesBladeLogoOffAndStaticWithReadback()
    {
        var transport = new FakeRazerFeatureTransport { LogoPowered = true };
        var reader = new RazerDeviceTelemetryReader(transport);
        await reader.ReadAsync(new[] { Blade });

        Assert.Equal(BladeLogoMode.Off,
            await reader.SetBladeLogoModeAsync(new[] { Blade }, BladeLogoMode.Off));
        Assert.False(transport.LogoPowered);
        Assert.Equal(BladeLogoMode.Static,
            await reader.SetBladeLogoModeAsync(new[] { Blade }, BladeLogoMode.Static));
        Assert.True(transport.LogoPowered);
        Assert.Equal(BladeLogoMode.Static, transport.LogoPoweredMode);
    }

    [Fact]
    public async Task RestoresBladeLogoWhenReadbackFails()
    {
        var transport = new FakeRazerFeatureTransport
        {
            LogoPowered = false,
            LogoPoweredMode = BladeLogoMode.Static,
            IgnoreNextLogoPowerWrite = true,
        };
        var reader = new RazerDeviceTelemetryReader(transport);
        await reader.ReadAsync(new[] { Blade });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await reader.SetBladeLogoModeAsync(new[] { Blade }, BladeLogoMode.Static));

        Assert.Contains("原状态已恢复", error.Message, StringComparison.Ordinal);
        Assert.False(transport.LogoPowered);
        Assert.Equal(BladeLogoMode.Static, transport.LogoPoweredMode);
    }

    [Fact]
    public async Task RejectsUnverifiedBladeLogoBreathingTarget()
    {
        var reader = new RazerDeviceTelemetryReader(new FakeRazerFeatureTransport());
        await reader.ReadAsync(new[] { Blade });

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await reader.SetBladeLogoModeAsync(new[] { Blade }, BladeLogoMode.Breathing));
    }

    [Fact]
    public async Task RestoresBladeBrightnessWhenReadbackFails()
    {
        var transport = new FakeRazerFeatureTransport { IgnoreNextBrightnessWrite = true };
        var reader = new RazerDeviceTelemetryReader(transport);
        await reader.ReadAsync(new[] { Blade });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await reader.SetBladeKeyboardBrightnessAsync(new[] { Blade }, 129));

        Assert.Contains("原值已恢复", error.Message, StringComparison.Ordinal);
        Assert.Equal((byte)128, transport.BladeBrightness);
        Assert.Equal(2, transport.BrightnessWriteCount);
    }

    [Theory]
    [InlineData("polling")]
    [InlineData("dpi")]
    [InlineData("idle")]
    public async Task RestoresViperScalarSettingWhenReadbackFails(string setting)
    {
        var transport = new FakeRazerFeatureTransport();
        var reader = new RazerDeviceTelemetryReader(transport);
        await reader.ReadAsync(new[] { Viper });

        Exception error = setting switch
        {
            "polling" => await CaptureFailureAsync(
                async () => { await reader.SetViperPollingRateAsync(new[] { Viper }, 1000); },
                () => transport.IgnoreNextViperPollingWrite = true),
            "dpi" => await CaptureFailureAsync(
                async () => { await reader.SetViperDpiAsync(new[] { Viper }, 1650, 1650); },
                () => transport.IgnoreNextViperDpiWrite = true),
            _ => await CaptureFailureAsync(
                async () => { await reader.SetViperIdleSecondsAsync(new[] { Viper }, 240); },
                () => transport.IgnoreNextViperIdleWrite = true),
        };

        Assert.Contains("原值已恢复", error.Message, StringComparison.Ordinal);
        Assert.Equal(500, transport.ViperPollingRateHertz);
        Assert.Equal((1600, 1600), transport.ViperDpi);
        Assert.Equal(180, transport.ViperIdleSeconds);
    }

    private static async Task<Exception> CaptureFailureAsync(
        Func<Task> action,
        Action arrange)
    {
        arrange();
        return await Assert.ThrowsAsync<InvalidOperationException>(async () => await action());
    }
}

internal sealed class FakeRazerFeatureTransport : IRazerFeatureTransport
{
    private readonly List<byte> _performanceWriteZones = new();
    private readonly List<byte> _boostWriteClusters = new();
    private int _fanTargetWriteCount;

    public BladePerformanceMode PerformanceMode { get; set; } = BladePerformanceMode.Balanced;
    public BladePerformanceMode? Zone2PerformanceMode { get; set; }
    public BladeFanMode FanMode { get; set; } = BladeFanMode.Automatic;
    public BladeFanMode? Zone2FanMode { get; set; }
    public ushort FanTargetRpm { get; set; } = 3200;
    public ushort? Zone2FanTargetRpm { get; set; }
    public byte? FailFanTargetGetZone { get; set; }
    public HashSet<int> IgnoreFanTargetWriteNumbers { get; set; } = [];
    public int? CancelFanTargetWriteNumberAfterApplying { get; set; }
    public List<string> FanCommands { get; } = [];
    public List<CancellationToken> RestorationCancellationTokens { get; } = [];
    public ushort CurrentFanCpuRpm { get; set; } = 2200;
    public ushort CurrentFanGpuRpm { get; set; } = 2000;
    public byte? FailCurrentFanId { get; set; }
    public byte AdvancedFanCpuModeRaw { get; set; }
    public byte AdvancedFanGpuModeRaw { get; set; }
    public byte ChargeLimitRaw { get; set; } = 0xD0;
    public BladeCpuBoostMode CpuBoostMode { get; set; } = BladeCpuBoostMode.Medium;
    public BladeGpuBoostMode GpuBoostMode { get; set; } = BladeGpuBoostMode.Low;
    public byte? BoostResponseClusterOverride { get; set; }
    public int BoostReadCount { get; private set; }
    public bool IgnoreNextBoostWrite { get; set; }
    public bool CancelNextBoostWriteAfterApplying { get; set; }
    public bool IgnorePerformanceWrites { get; set; }
    public bool CancelNextPerformanceWriteAfterApplying { get; set; }
    public byte? FailPerformanceWriteZoneOnce { get; set; }
    public byte? FailPerformanceWriteZone { get; set; }
    public bool IgnoreChargeWrites { get; set; }
    public bool CancelNextChargeWriteAfterApplying { get; set; }
    public int ChargeWriteCount { get; private set; }
    public bool BatteryQueryAllowedRemainingPacketsMismatch { get; private set; }
    public BladeMaxFanMode MaxFanMode { get; set; } = BladeMaxFanMode.Disabled;
    public byte BladeWiredBatteryRaw { get; set; } = 128;
    public byte BladeChargingStatusRaw { get; set; }
    public byte BladeAutoSleepRaw { get; set; } = 1;
    public ushort BladeTimeToSleepSeconds { get; set; } = 300;
    public byte? FailBladeProduct710Command { get; set; }
    public byte PowerModeSiblingBits { get; set; }
    public bool IgnoreNextMaxFanWrite { get; set; }
    public int MaxFanWriteCount { get; private set; }
    public bool LogoPowered { get; set; } = true;
    public BladeLogoMode LogoPoweredMode { get; set; } = BladeLogoMode.Static;
    public bool IgnoreNextLogoPowerWrite { get; set; }
    public byte BladeBrightness { get; private set; } = 128;
    public bool IgnoreNextBrightnessWrite { get; set; }
    public int BrightnessWriteCount { get; private set; }
    public int ViperPollingRateHertz { get; private set; } = 500;
    public (int X, int Y) ViperDpi { get; private set; } = (1600, 1600);
    public int ViperIdleSeconds { get; private set; } = 180;
    public bool IgnoreNextViperPollingWrite { get; set; }
    public bool IgnoreNextViperDpiWrite { get; set; }
    public bool IgnoreNextViperIdleWrite { get; set; }
    public ViperDpiStagesTelemetry DpiStages { get; private set; } = new(
        3,
        [
            new(1, 400, 400),
            new(2, 800, 800),
            new(3, 1600, 1600),
            new(4, 3200, 3200),
            new(5, 6400, 6400),
        ]);
    public bool IgnoreNextDpiStagesWrite { get; set; }
    public int DpiStagesWriteCount { get; private set; }
    public byte? FailViperCommand { get; set; }
    public byte? LastViperTransactionId { get; private set; }
    public IReadOnlyList<byte> PerformanceWriteZones => _performanceWriteZones;
    public IReadOnlyList<byte> BoostWriteClusters => _boostWriteClusters;

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
        cancellationToken.ThrowIfCancellationRequested();
        var args = arguments.ToArray();

        if (devicePath == "viper-path")
        {
            LastViperTransactionId = transactionId;
            if (FailViperCommand == commandId)
            {
                throw new InvalidOperationException($"Simulated Viper command 0x{commandId:X2} failure.");
            }

            if ((commandClass, commandId) == (0x04, 0x06))
            {
                DpiStagesWriteCount++;
                if (IgnoreNextDpiStagesWrite)
                {
                    IgnoreNextDpiStagesWrite = false;
                }
                else
                {
                    DpiStages = ParseDpiStagesSet(args);
                }
                return Task.FromResult(CreateResponse(transactionId, dataSize, commandClass, commandId, args));
            }

            if ((commandClass, commandId) == (0x00, 0x05))
            {
                if (IgnoreNextViperPollingWrite)
                {
                    IgnoreNextViperPollingWrite = false;
                }
                else
                {
                    ViperPollingRateHertz = args[0] switch { 0x08 => 125, 0x02 => 500, _ => 1000 };
                }
                return Task.FromResult(CreateResponse(transactionId, dataSize, commandClass, commandId, args));
            }

            if ((commandClass, commandId) == (0x04, 0x05))
            {
                if (IgnoreNextViperDpiWrite)
                {
                    IgnoreNextViperDpiWrite = false;
                }
                else
                {
                    ViperDpi = ((args[1] << 8) | args[2], (args[3] << 8) | args[4]);
                }
                return Task.FromResult(CreateResponse(transactionId, dataSize, commandClass, commandId, args));
            }

            if ((commandClass, commandId) == (0x07, 0x03))
            {
                if (IgnoreNextViperIdleWrite)
                {
                    IgnoreNextViperIdleWrite = false;
                }
                else
                {
                    ViperIdleSeconds = (args[0] << 8) | args[1];
                }
                return Task.FromResult(CreateResponse(transactionId, dataSize, commandClass, commandId, args));
            }

            var values = (commandClass, commandId) switch
            {
                (0x07, 0x80) => new byte[] { 0x00, 0xD5 },
                (0x00, 0x85) => new byte[] { ViperPollingRateHertz switch { 125 => 0x08, 500 => 0x02, _ => 0x01 } },
                (0x04, 0x85) => new byte[] { 0x00, (byte)(ViperDpi.X >> 8), (byte)ViperDpi.X, (byte)(ViperDpi.Y >> 8), (byte)ViperDpi.Y },
                (0x07, 0x83) => new byte[] { (byte)(ViperIdleSeconds >> 8), (byte)ViperIdleSeconds },
                (0x07, 0x81) => new byte[] { 0x4D },
                (0x04, 0x86) => CreateDpiStagesGetArguments(),
                _ => throw new InvalidOperationException($"Unexpected Viper command {commandClass:X2}{commandId:X2}."),
            };
            return Task.FromResult(CreateResponse(transactionId, dataSize, commandClass, commandId, values));
        }

        if ((commandClass, commandId) == (0x0E, 0x84))
        {
            return Task.FromResult(CreateResponse(transactionId, 2, commandClass, commandId, new byte[] { 0x01, BladeBrightness }));
        }

        if ((commandClass, commandId) == (0x0E, 0x04))
        {
            BrightnessWriteCount++;
            if (IgnoreNextBrightnessWrite)
            {
                IgnoreNextBrightnessWrite = false;
            }
            else
            {
                BladeBrightness = args[1];
            }
            return Task.FromResult(CreateResponse(transactionId, dataSize, commandClass, commandId, args));
        }

        if (commandClass == 0x03 && commandId is 0x80 or 0x82)
        {
            var value = commandId == 0x80
                ? (LogoPowered ? (byte)0x01 : (byte)0x00)
                : (LogoPoweredMode == BladeLogoMode.Breathing ? (byte)0x02 : (byte)0x00);
            return Task.FromResult(CreateResponse(
                transactionId, 3, commandClass, commandId, new byte[] { 0x01, 0x04, value }));
        }

        if ((commandClass, commandId) == (0x03, 0x02))
        {
            LogoPoweredMode = args[2] == 0x02 ? BladeLogoMode.Breathing : BladeLogoMode.Static;
            return Task.FromResult(CreateResponse(transactionId, dataSize, commandClass, commandId, args));
        }

        if ((commandClass, commandId) == (0x03, 0x00))
        {
            if (IgnoreNextLogoPowerWrite)
            {
                IgnoreNextLogoPowerWrite = false;
            }
            else
            {
                LogoPowered = args[2] != 0;
            }
            return Task.FromResult(CreateResponse(transactionId, dataSize, commandClass, commandId, args));
        }

        if ((commandClass, commandId) == (0x0D, 0x82))
        {
            var zone = args[1];
            FanCommands.Add($"GET-M{zone}");
            var mode = zone == 0x02 && Zone2PerformanceMode is BladePerformanceMode zone2Mode
                ? zone2Mode
                : PerformanceMode;
            var fanMode = zone == 0x02 && Zone2FanMode is BladeFanMode zone2FanMode
                ? zone2FanMode
                : FanMode;
            return Task.FromResult(CreateResponse(
                transactionId, 4, commandClass, commandId,
                new byte[] { 0x00, zone, (byte)mode, (byte)fanMode }));
        }

        if ((commandClass, commandId) == (0x0D, 0x81))
        {
            var zone = args[1];
            FanCommands.Add($"GET-T{zone}");
            if (FailFanTargetGetZone == zone)
            {
                throw new InvalidOperationException($"Simulated fan target GET zone {zone} failure.");
            }
            var targetRpm = zone == BladeFanProtocol.ZoneGpu && Zone2FanTargetRpm is ushort zone2Target
                ? zone2Target
                : FanTargetRpm;
            return Task.FromResult(CreateResponse(
                transactionId, 3, commandClass, commandId,
                new byte[] { 0x00, zone, (byte)(targetRpm / 100) }));
        }

        if ((commandClass, commandId) == (0x0D, 0x88))
        {
            var fanId = args[1];
            FanCommands.Add($"GET-RPM{fanId}");
            if (FailCurrentFanId == fanId)
            {
                throw new InvalidOperationException($"Simulated current fan {fanId:X2} failure.");
            }
            var rpm = fanId == BladeThermalProtocol.CpuFanId ? CurrentFanCpuRpm : CurrentFanGpuRpm;
            return Task.FromResult(CreateResponse(
                transactionId, 3, commandClass, commandId,
                new byte[] { 0x01, fanId, (byte)(rpm / 100) }));
        }

        if ((commandClass, commandId) == (0x0D, 0x87) && args[0] == 0x01)
        {
            var fanId = args[1];
            var mode = fanId == BladeThermalProtocol.CpuFanId
                ? AdvancedFanCpuModeRaw
                : AdvancedFanGpuModeRaw;
            return Task.FromResult(CreateResponse(
                transactionId, 3, commandClass, commandId,
                new byte[] { 0x01, fanId, mode }));
        }

        if (commandClass == 0x07 && commandId is 0x80 or 0x84 or 0x88 or 0x83)
        {
            if (FailBladeProduct710Command == commandId)
            {
                throw new InvalidOperationException($"Simulated Product 710 command 0x{commandId:X2} failure.");
            }

            var values = commandId switch
            {
                0x80 => new[] { (byte)0x00, BladeWiredBatteryRaw },
                0x84 => new[] { (byte)0x00, BladeChargingStatusRaw },
                0x88 => new[] { (byte)0x00, BladeAutoSleepRaw },
                _ => new[] { (byte)(BladeTimeToSleepSeconds >> 8), (byte)BladeTimeToSleepSeconds },
            };
            return Task.FromResult(CreateResponse(transactionId, 2, commandClass, commandId, values));
        }

        if ((commandClass, commandId) == (0x0D, 0x87))
        {
            BoostReadCount++;
            var cluster = BoostResponseClusterOverride ?? args[1];
            var value = args[1] == BladeBoostProtocol.CpuCluster
                ? (byte)CpuBoostMode
                : (byte)GpuBoostMode;
            return Task.FromResult(CreateResponse(
                transactionId, 3, commandClass, commandId,
                new byte[] { 0x00, cluster, value }));
        }

        if ((commandClass, commandId) == (0x0D, 0x07))
        {
            _boostWriteClusters.Add(args[1]);
            if (IgnoreNextBoostWrite)
            {
                IgnoreNextBoostWrite = false;
            }
            else if (args[1] == BladeBoostProtocol.CpuCluster)
            {
                CpuBoostMode = (BladeCpuBoostMode)args[2];
            }
            else if (args[1] == BladeBoostProtocol.GpuCluster)
            {
                GpuBoostMode = (BladeGpuBoostMode)args[2];
            }
            else
            {
                throw new InvalidOperationException($"Unexpected boost cluster {args[1]:X2}.");
            }
            if (CancelNextBoostWriteAfterApplying)
            {
                CancelNextBoostWriteAfterApplying = false;
                throw new OperationCanceledException("Simulated cancellation after Boost write.");
            }
            return Task.FromResult(CreateResponse(transactionId, dataSize, commandClass, commandId, args));
        }

        if ((commandClass, commandId) == (0x0D, 0x02))
        {
            _performanceWriteZones.Add(args[1]);
            FanCommands.Add($"SET-M{args[1]}");
            if (!cancellationToken.CanBeCanceled)
            {
                RestorationCancellationTokens.Add(cancellationToken);
            }
            if (FailPerformanceWriteZone == args[1])
            {
                throw new InvalidOperationException($"Simulated zone {args[1]} write failure.");
            }
            if (FailPerformanceWriteZoneOnce == args[1])
            {
                FailPerformanceWriteZoneOnce = null;
                throw new InvalidOperationException($"Simulated one-time zone {args[1]} write failure.");
            }
            if (!IgnorePerformanceWrites)
            {
                if (args[1] == BladeFanProtocol.ZoneCpu)
                {
                    PerformanceMode = (BladePerformanceMode)args[2];
                    FanMode = (BladeFanMode)args[3];
                }
                else
                {
                    Zone2PerformanceMode = (BladePerformanceMode)args[2];
                    Zone2FanMode = (BladeFanMode)args[3];
                }
            }
            if (CancelNextPerformanceWriteAfterApplying)
            {
                CancelNextPerformanceWriteAfterApplying = false;
                throw new OperationCanceledException("Simulated cancellation after performance write.");
            }
            return Task.FromResult(CreateResponse(transactionId, dataSize, commandClass, commandId, args));
        }

        if ((commandClass, commandId) == (0x0D, 0x01))
        {
            var zone = args[1];
            _fanTargetWriteCount++;
            FanCommands.Add($"SET-T{zone}");
            if (!cancellationToken.CanBeCanceled)
            {
                RestorationCancellationTokens.Add(cancellationToken);
            }
            if (!IgnoreFanTargetWriteNumbers.Contains(_fanTargetWriteCount))
            {
                var rpm = checked((ushort)(args[2] * BladeFanProtocol.StepRpm));
                if (zone == BladeFanProtocol.ZoneCpu)
                {
                    FanTargetRpm = rpm;
                }
                else
                {
                    Zone2FanTargetRpm = rpm;
                }
            }
            if (CancelFanTargetWriteNumberAfterApplying == _fanTargetWriteCount)
            {
                throw new OperationCanceledException("Simulated cancellation after fan target write.");
            }
            return Task.FromResult(CreateResponse(transactionId, dataSize, commandClass, commandId, args));
        }

        if ((commandClass, commandId) == (0x07, 0x92))
        {
            BatteryQueryAllowedRemainingPacketsMismatch |= allowRemainingPacketsMismatch;
            var response = CreateResponse(transactionId, 1, commandClass, commandId, new[] { ChargeLimitRaw });
            response[3] = 0x01;
            response[89] = RazerFeatureReport.CalculateCrc(response);
            return Task.FromResult(response);
        }

        if ((commandClass, commandId) == (0x07, 0x12))
        {
            ChargeWriteCount++;
            if (!IgnoreChargeWrites)
            {
                ChargeLimitRaw = args[0];
            }
            if (CancelNextChargeWriteAfterApplying)
            {
                CancelNextChargeWriteAfterApplying = false;
                throw new OperationCanceledException("Simulated cancellation after charge-limit write.");
            }
            return Task.FromResult(CreateResponse(transactionId, dataSize, commandClass, commandId, args));
        }

        if ((commandClass, commandId) == (0x07, 0x8F))
        {
            var mask = (byte)(PowerModeSiblingBits | (byte)MaxFanMode);
            var response = CreateResponse(
                transactionId, 1, commandClass, commandId, new[] { mask });
            response[3] = 0x01;
            response[89] = RazerFeatureReport.CalculateCrc(response);
            return Task.FromResult(response);
        }

        if ((commandClass, commandId) == (0x07, 0x0F))
        {
            MaxFanWriteCount++;
            if (IgnoreNextMaxFanWrite)
            {
                IgnoreNextMaxFanWrite = false;
            }
            else
            {
                MaxFanMode = (args[0] & BladeMaxFanProtocol.MaxFanBit) != 0
                    ? BladeMaxFanMode.Enabled
                    : BladeMaxFanMode.Disabled;
                PowerModeSiblingBits = (byte)(args[0] & ~BladeMaxFanProtocol.MaxFanBit);
            }
            return Task.FromResult(CreateResponse(transactionId, dataSize, commandClass, commandId, args));
        }

        throw new InvalidOperationException($"Unexpected command {commandClass:X2}{commandId:X2}.");
    }

    private static byte[] CreateResponse(
        byte transactionId,
        byte dataSize,
        byte commandClass,
        byte commandId,
        byte[] arguments)
    {
        var response = RazerFeatureReport.CreateRequest(
            transactionId, dataSize, commandClass, commandId, arguments);
        response[1] = 0x02;
        return response;
    }

    private ViperDpiStagesTelemetry ParseDpiStagesSet(byte[] args)
    {
        var count = args[2];
        var stages = new ViperDpiStageTelemetry[count];
        for (var index = 0; index < count; index++)
        {
            var offset = 3 + (7 * index);
            stages[index] = new ViperDpiStageTelemetry(
                checked((byte)(index + 1)),
                (args[offset + 1] << 8) | args[offset + 2],
                (args[offset + 3] << 8) | args[offset + 4]);
        }
        return new ViperDpiStagesTelemetry(args[1], stages);
    }

    private byte[] CreateDpiStagesGetArguments()
    {
        var values = new byte[0x26];
        values[0] = 0x01;
        values[1] = DpiStages.ActiveStage;
        values[2] = checked((byte)DpiStages.Stages.Count);
        for (var index = 0; index < DpiStages.Stages.Count; index++)
        {
            var stage = DpiStages.Stages[index];
            var offset = 3 + (7 * index);
            values[offset] = stage.Number;
            values[offset + 1] = (byte)(stage.X >> 8);
            values[offset + 2] = (byte)stage.X;
            values[offset + 3] = (byte)(stage.Y >> 8);
            values[offset + 4] = (byte)stage.Y;
        }
        return values;
    }
}
