using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;

namespace OpenSynapse.Core.Tests;

public sealed class VerifiedProfileApplierTests
{
    private static DeviceDescriptor Blade => new(
        "blade", "Blade", 0x1532, 0x02C6,
        DeviceAccessState.Available, DeviceCapabilityState.PendingValidation, 91, 1, 2,
        "blade-710");

    private static DeviceDescriptor Viper => new(
        "viper", "Viper", 0x1532, 0x00B8,
        DeviceAccessState.Available, DeviceCapabilityState.PendingValidation, 91, 1, 2,
        "viper-184");

    [Fact]
    public async Task AppliesOnlyVerifiedValuesThatDifferFromReadback()
    {
        var profile = ProfileDocument.CreateDefault();
        profile.Global.Blade.KeyboardBrightness = 100;
        profile.Global.Blade.PerformanceMode = (byte)BladePerformanceMode.Custom;
        profile.Global.Blade.ChargeLimitPercent = 50;
        profile.Global.Blade.MaxFanMode = (byte)BladeMaxFanMode.Enabled;
        profile.Global.Viper.DpiX = 1600;
        profile.Global.Viper.DpiY = 1800;
        profile.Global.Viper.PollingRateHertz = 1000;
        profile.Global.Viper.IdleSeconds = 300;

        var reader = new FakeReader();
        var result = await new VerifiedProfileApplier().ApplyAsync(
            profile,
            new[] { Blade, Viper },
            new RazerDeviceTelemetry(
                80,
                BladePerformanceMode.Balanced,
                BladeFanMode.Automatic,
                null,
                80,
                70,
                500,
                800,
                800,
                180,
                Array.Empty<string>(),
                DateTimeOffset.UtcNow,
                BladeCpuBoostMode.Medium,
                BladeGpuBoostMode.Low,
                BladeMaxFanMode.Disabled),
            reader,
            isPluggedIn: true);

        Assert.True(result.Succeeded);
        Assert.Equal(7, result.AppliedCount);
        Assert.Equal((byte)100, reader.BladeBrightness);
        Assert.Equal(BladePerformanceMode.Custom, reader.BladePerformanceMode);
        Assert.Equal(50, reader.BladeChargeLimit);
        Assert.Equal(BladeMaxFanMode.Enabled, reader.BladeMaxFanMode);
        Assert.Equal(((int X, int Y)?)(1600, 1800), reader.ViperDpi);
        Assert.Equal((int?)1000, reader.ViperPollingRate);
        Assert.Equal((int?)300, reader.ViperIdleSeconds);
    }

    [Fact]
    public async Task MissingReadbackSkipsTheRequestedWrite()
    {
        var profile = ProfileDocument.CreateDefault();
        profile.Global.Viper.DpiX = 1600;

        var reader = new FakeReader();
        var result = await new VerifiedProfileApplier().ApplyAsync(
            profile,
            new[] { Viper },
            new RazerDeviceTelemetry(
                null, null, null, null, null, null, null, null, null, null,
                Array.Empty<string>(), DateTimeOffset.UtcNow),
            reader,
            isPluggedIn: null);

        Assert.False(result.Succeeded);
        Assert.Equal(0, result.AppliedCount);
        Assert.Contains(result.Errors, error => error.Contains("DPI", StringComparison.Ordinal));
        Assert.Null(reader.ViperDpi);
    }

    [Fact]
    public async Task EqualValuesAreNotWritten()
    {
        var profile = ProfileDocument.CreateDefault();
        profile.Global.Blade.KeyboardBrightness = 80;
        profile.Global.Viper.DpiX = 800;
        profile.Global.Viper.DpiY = 800;
        profile.Global.Viper.PollingRateHertz = 500;
        profile.Global.Viper.IdleSeconds = 180;

        var reader = new FakeReader();
        var result = await new VerifiedProfileApplier().ApplyAsync(
            profile,
            new[] { Blade, Viper },
            new RazerDeviceTelemetry(
                80, BladePerformanceMode.Balanced, BladeFanMode.Automatic, null, 80,
                70, 500, 800, 800, 180, Array.Empty<string>(), DateTimeOffset.UtcNow),
            reader,
            isPluggedIn: true);

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.AppliedCount);
        Assert.Null(reader.BladeBrightness);
        Assert.Null(reader.ViperDpi);
        Assert.Null(reader.ViperPollingRate);
        Assert.Null(reader.ViperIdleSeconds);
    }

    [Fact]
    public async Task SameFamilyNewPidUsesViperProfileHandler()
    {
        var profile = ProfileDocument.CreateDefault();
        profile.Global.Viper.PollingRateHertz = 1000;
        var sameFamilyDevice = Viper with { ProductId = 0xFFFE };
        var reader = new FakeReader();

        var result = await new VerifiedProfileApplier().ApplyAsync(
            profile,
            new[] { sameFamilyDevice },
            new RazerDeviceTelemetry(
                null, null, null, null, null, null, 500, null, null, null,
                Array.Empty<string>(), DateTimeOffset.UtcNow),
            reader,
            isPluggedIn: null);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.AppliedCount);
        Assert.Equal(1000, reader.ViperPollingRate);
    }

    [Fact]
    public async Task AppliesCpuAndGpuBoostOnlyInCustomMode()
    {
        var profile = ProfileDocument.CreateDefault();
        profile.Global.Blade.CpuBoostMode = (byte)BladeCpuBoostMode.High;
        profile.Global.Blade.GpuBoostMode = (byte)BladeGpuBoostMode.Medium;
        var reader = new FakeReader();

        var result = await new VerifiedProfileApplier().ApplyAsync(
            profile,
            new[] { Blade },
            Telemetry(
                bladePerformanceMode: BladePerformanceMode.Custom,
                bladeCpuBoostMode: BladeCpuBoostMode.Low,
                bladeGpuBoostMode: BladeGpuBoostMode.Low),
            reader,
            isPluggedIn: true);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.AppliedCount);
        Assert.Equal(BladeCpuBoostMode.High, reader.BladeCpuBoostMode);
        Assert.Equal(BladeGpuBoostMode.Medium, reader.BladeGpuBoostMode);
    }

    [Theory]
    [InlineData(BladeLogoMode.Off, BladeLogoMode.Static)]
    [InlineData(BladeLogoMode.Static, BladeLogoMode.Off)]
    public async Task AppliesVerifiedLogoModes(BladeLogoMode requested, BladeLogoMode current)
    {
        var profile = ProfileDocument.CreateDefault();
        profile.Global.Blade.LogoMode = (byte)requested;
        var reader = new FakeReader();

        var result = await new VerifiedProfileApplier().ApplyAsync(
            profile,
            new[] { Blade },
            Telemetry(bladeLogoMode: current),
            reader,
            isPluggedIn: true);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.AppliedCount);
        Assert.Equal(requested, reader.BladeLogoMode);
    }

    [Fact]
    public async Task AppliesCompleteDpiStageTableWhenAnyPartDiffers()
    {
        var profile = ProfileDocument.CreateDefault();
        profile.Global.Viper.DpiStages = DpiStages(activeStage: 2, 800, 1600);
        var reader = new FakeReader();

        var result = await new VerifiedProfileApplier().ApplyAsync(
            profile,
            new[] { Viper },
            Telemetry(viperDpiStages: DpiStagesTelemetry(activeStage: 1, 800, 1600)),
            reader,
            isPluggedIn: null);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.AppliedCount);
        Assert.Equal((byte)2, reader.ViperDpiStages!.ActiveStage);
        Assert.Equal(new[] { 800, 1600 }, reader.ViperDpiStages.Stages.Select(stage => stage.X));
    }

    [Fact]
    public async Task EqualCompleteDpiStageTableIsNotWritten()
    {
        var profile = ProfileDocument.CreateDefault();
        profile.Global.Viper.DpiStages = DpiStages(activeStage: 1, 800, 1600);
        var reader = new FakeReader();

        var result = await new VerifiedProfileApplier().ApplyAsync(
            profile,
            new[] { Viper },
            Telemetry(viperDpiStages: DpiStagesTelemetry(activeStage: 1, 800, 1600)),
            reader,
            isPluggedIn: null);

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.AppliedCount);
        Assert.Null(reader.ViperDpiStages);
    }

    [Theory]
    [InlineData("cpu")]
    [InlineData("gpu")]
    [InlineData("logo")]
    public async Task InvalidExpandedEnumValueBecomesVisibleError(string setting)
    {
        var profile = ProfileDocument.CreateDefault();
        if (setting == "cpu")
        {
            profile.Global.Blade.CpuBoostMode = byte.MaxValue;
        }
        else if (setting == "gpu")
        {
            profile.Global.Blade.GpuBoostMode = byte.MaxValue;
        }
        else
        {
            profile.Global.Blade.LogoMode = byte.MaxValue;
        }

        var result = await new VerifiedProfileApplier().ApplyAsync(
            profile,
            new[] { Blade },
            Telemetry(
                bladePerformanceMode: BladePerformanceMode.Custom,
                bladeCpuBoostMode: BladeCpuBoostMode.Low,
                bladeGpuBoostMode: BladeGpuBoostMode.Low,
                bladeLogoMode: BladeLogoMode.Static),
            new FakeReader(),
            isPluggedIn: true);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("255", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InvalidPerformanceModeSkipsDependentBoostWrites()
    {
        var profile = ProfileDocument.CreateDefault();
        profile.Global.Blade.PerformanceMode = byte.MaxValue;
        profile.Global.Blade.CpuBoostMode = (byte)BladeCpuBoostMode.High;
        var reader = new FakeReader();

        var result = await new VerifiedProfileApplier().ApplyAsync(
            profile,
            new[] { Blade },
            Telemetry(
                bladePerformanceMode: BladePerformanceMode.Custom,
                bladeCpuBoostMode: BladeCpuBoostMode.Low),
            reader,
            isPluggedIn: true);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("性能模式值无效", StringComparison.Ordinal));
        Assert.Single(result.Errors);
        Assert.Null(reader.BladeCpuBoostMode);
    }

    [Fact]
    public async Task BladeFailureDoesNotSuppressIndependentViperApply()
    {
        var profile = ProfileDocument.CreateDefault();
        profile.Global.Blade.KeyboardBrightness = 100;
        profile.Global.Viper.PollingRateHertz = 1000;
        var reader = new FakeReader { FailBladeBrightness = true };

        var result = await new VerifiedProfileApplier().ApplyAsync(
            profile,
            new[] { Blade, Viper },
            Telemetry(bladeKeyboardBrightness: 80, viperPollingRateHertz: 500),
            reader,
            isPluggedIn: true);

        Assert.False(result.Succeeded);
        Assert.Equal(1, result.AppliedCount);
        Assert.Contains(result.Errors, error => error.Contains("键盘亮度", StringComparison.Ordinal));
        Assert.Equal(1000, reader.ViperPollingRate);
    }

    [Fact]
    public async Task BladeFailureStopsLaterWritesForTheSameDevice()
    {
        var profile = ProfileDocument.CreateDefault();
        profile.Global.Blade.KeyboardBrightness = 100;
        profile.Global.Blade.ChargeLimitPercent = 50;
        profile.Global.Blade.LogoMode = (byte)BladeLogoMode.Off;
        var reader = new FakeReader { FailBladeBrightness = true };

        var result = await new VerifiedProfileApplier().ApplyAsync(
            profile,
            new[] { Blade },
            Telemetry(
                bladeKeyboardBrightness: 80,
                bladeLogoMode: BladeLogoMode.Static),
            reader,
            isPluggedIn: true);

        Assert.False(result.Succeeded);
        Assert.Equal(0, result.AppliedCount);
        Assert.Contains(result.Errors, error => error.Contains("键盘亮度", StringComparison.Ordinal));
        Assert.Null(reader.BladeChargeLimit);
        Assert.Null(reader.BladeLogoMode);
    }

    [Fact]
    public async Task BladeFailureStopsRemainingBladeWrites()
    {
        var profile = ProfileDocument.CreateDefault();
        profile.Global.Blade.KeyboardBrightness = 100;
        profile.Global.Blade.ChargeLimitPercent = 50;
        var reader = new FakeReader { FailBladeBrightness = true };

        var result = await new VerifiedProfileApplier().ApplyAsync(
            profile,
            new[] { Blade },
            Telemetry(bladeKeyboardBrightness: 80),
            reader,
            isPluggedIn: true);

        Assert.False(result.Succeeded);
        Assert.Equal(0, result.AppliedCount);
        Assert.Contains(result.Errors, error => error.Contains("键盘亮度", StringComparison.Ordinal));
        Assert.Null(reader.BladeChargeLimit);
    }

    private static RazerDeviceTelemetry Telemetry(
        byte? bladeKeyboardBrightness = null,
        BladePerformanceMode? bladePerformanceMode = null,
        int? viperPollingRateHertz = null,
        BladeCpuBoostMode? bladeCpuBoostMode = null,
        BladeGpuBoostMode? bladeGpuBoostMode = null,
        ViperDpiStagesTelemetry? viperDpiStages = null,
        BladeLogoMode? bladeLogoMode = null) => new(
            bladeKeyboardBrightness,
            bladePerformanceMode,
            null,
            null,
            null,
            null,
            viperPollingRateHertz,
            null,
            null,
            null,
            [],
            DateTimeOffset.UtcNow,
            bladeCpuBoostMode,
            bladeGpuBoostMode,
            ViperDpiStages: viperDpiStages,
            BladeLogoMode: bladeLogoMode);

    private static ViperDpiStagesProfile DpiStages(byte activeStage, params int[] dpi) => new()
    {
        ActiveStage = activeStage,
        Stages = dpi.Select((value, index) => new ViperDpiStageProfile
        {
            Number = checked((byte)(index + 1)),
            X = value,
            Y = value,
        }).ToList(),
    };

    private static ViperDpiStagesTelemetry DpiStagesTelemetry(byte activeStage, params int[] dpi) =>
        new(activeStage, dpi.Select((value, index) =>
            new ViperDpiStageTelemetry(checked((byte)(index + 1)), value, value)).ToArray());

    private sealed class FakeReader : IRazerDeviceTelemetryReader
    {
        public byte? BladeBrightness { get; private set; }
        public (int X, int Y)? ViperDpi { get; private set; }
        public int? ViperPollingRate { get; private set; }
        public int? ViperIdleSeconds { get; private set; }
        public BladePerformanceMode? BladePerformanceMode { get; private set; }
        public BladeCpuBoostMode? BladeCpuBoostMode { get; private set; }
        public BladeGpuBoostMode? BladeGpuBoostMode { get; private set; }
        public int? BladeChargeLimit { get; private set; }
        public BladeMaxFanMode? BladeMaxFanMode { get; private set; }
        public BladeLogoMode? BladeLogoMode { get; private set; }
        public ViperDpiStagesTelemetry? ViperDpiStages { get; private set; }
        public bool FailBladeBrightness { get; init; }

        public ValueTask<RazerDeviceTelemetry> ReadAsync(
            IReadOnlyList<DeviceDescriptor> devices,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<byte> SetBladeKeyboardBrightnessAsync(
            IReadOnlyList<DeviceDescriptor> devices,
            byte brightness,
            CancellationToken cancellationToken = default)
        {
            if (FailBladeBrightness)
            {
                throw new IOException("simulated brightness failure");
            }
            BladeBrightness = brightness;
            return ValueTask.FromResult(brightness);
        }

        public ValueTask<BladePerformanceMode> SetBladePerformanceModeAsync(
            IReadOnlyList<DeviceDescriptor> devices,
            BladePerformanceMode mode,
            CancellationToken cancellationToken = default)
        {
            BladePerformanceMode = mode;
            return ValueTask.FromResult(mode);
        }

        public ValueTask<BladeCpuBoostMode> SetBladeCpuBoostModeAsync(
            IReadOnlyList<DeviceDescriptor> devices,
            BladeCpuBoostMode mode,
            CancellationToken cancellationToken = default)
        {
            BladeCpuBoostMode = mode;
            return ValueTask.FromResult(mode);
        }

        public ValueTask<BladeGpuBoostMode> SetBladeGpuBoostModeAsync(
            IReadOnlyList<DeviceDescriptor> devices,
            BladeGpuBoostMode mode,
            CancellationToken cancellationToken = default)
        {
            BladeGpuBoostMode = mode;
            return ValueTask.FromResult(mode);
        }

        public ValueTask<BladeMaxFanMode> SetBladeMaxFanModeAsync(
            IReadOnlyList<DeviceDescriptor> devices,
            BladeMaxFanMode mode,
            CancellationToken cancellationToken = default)
        {
            BladeMaxFanMode = mode;
            return ValueTask.FromResult(mode);
        }

        public ValueTask<int> SetBladeChargeLimitAsync(
            IReadOnlyList<DeviceDescriptor> devices,
            int percent,
            CancellationToken cancellationToken = default)
        {
            BladeChargeLimit = percent;
            return ValueTask.FromResult(percent);
        }

        public ValueTask<BladeLogoMode> SetBladeLogoModeAsync(
            IReadOnlyList<DeviceDescriptor> devices,
            BladeLogoMode mode,
            CancellationToken cancellationToken = default)
        {
            BladeLogoMode = mode;
            return ValueTask.FromResult(mode);
        }

        public ValueTask<int> SetViperPollingRateAsync(
            IReadOnlyList<DeviceDescriptor> devices,
            int hertz,
            CancellationToken cancellationToken = default)
        {
            ViperPollingRate = hertz;
            return ValueTask.FromResult(hertz);
        }

        public ValueTask<(int X, int Y)> SetViperDpiAsync(
            IReadOnlyList<DeviceDescriptor> devices,
            int x,
            int y,
            CancellationToken cancellationToken = default)
        {
            ViperDpi = (x, y);
            return ValueTask.FromResult((x, y));
        }

        public ValueTask<ViperDpiStagesTelemetry> SetViperDpiStagesAsync(
            IReadOnlyList<DeviceDescriptor> devices,
            ViperDpiStagesTelemetry stages,
            CancellationToken cancellationToken = default)
        {
            ViperDpiStages = stages;
            return ValueTask.FromResult(stages);
        }

        public ValueTask<int> SetViperIdleSecondsAsync(
            IReadOnlyList<DeviceDescriptor> devices,
            int seconds,
            CancellationToken cancellationToken = default)
        {
            ViperIdleSeconds = seconds;
            return ValueTask.FromResult(seconds);
        }
    }
}
