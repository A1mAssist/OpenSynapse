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
        profile.Global.Blade.PerformanceMode = (byte)BladePerformanceMode.Performance;
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
        Assert.Equal(BladePerformanceMode.Performance, reader.BladePerformanceMode);
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

    private sealed class FakeReader : IRazerDeviceTelemetryReader
    {
        public byte? BladeBrightness { get; private set; }
        public (int X, int Y)? ViperDpi { get; private set; }
        public int? ViperPollingRate { get; private set; }
        public int? ViperIdleSeconds { get; private set; }
        public BladePerformanceMode? BladePerformanceMode { get; private set; }
        public int? BladeChargeLimit { get; private set; }
        public BladeMaxFanMode? BladeMaxFanMode { get; private set; }

        public ValueTask<RazerDeviceTelemetry> ReadAsync(
            IReadOnlyList<DeviceDescriptor> devices,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<byte> SetBladeKeyboardBrightnessAsync(
            IReadOnlyList<DeviceDescriptor> devices,
            byte brightness,
            CancellationToken cancellationToken = default)
        {
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
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(mode);

        public ValueTask<BladeGpuBoostMode> SetBladeGpuBoostModeAsync(
            IReadOnlyList<DeviceDescriptor> devices,
            BladeGpuBoostMode mode,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(mode);

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
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(mode);

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
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(stages);

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
