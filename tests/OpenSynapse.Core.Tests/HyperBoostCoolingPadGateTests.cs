using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class HyperBoostCoolingPadGateTests
{
    [Fact]
    public async Task ProfileHyperBoostFallsBackToPerformanceWithoutCoolingPad()
    {
        var reader = new RecordingReader();

        var result = await ApplyAsync(reader, coolingPadConnected: false);

        Assert.Equal(BladePerformanceMode.Performance, reader.LastMode);
        Assert.Contains(result.Errors, error => error.StartsWith("HyperBoost", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProfileHyperBoostAppliesWhenCoolingPadIsConnected()
    {
        var reader = new RecordingReader();

        var result = await ApplyAsync(reader, coolingPadConnected: true);

        Assert.Equal(BladePerformanceMode.Hyperboost, reader.LastMode);
        Assert.DoesNotContain(result.Errors, error => error.StartsWith("HyperBoost", StringComparison.Ordinal));
    }

    private static async Task<ProfileApplyResult> ApplyAsync(RecordingReader reader, bool coolingPadConnected)
    {
        var document = ProfileDocument.CreateDefault();
        document.Global.Blade.PerformanceMode = (byte)BladePerformanceMode.Hyperboost;
        var device = new DeviceDescriptor(
            "blade",
            "Blade 16",
            0x1532,
            0x02C6,
            DeviceAccessState.Available,
            DeviceCapabilityState.PendingValidation,
            91,
            1,
            2,
            "blade-710");
        var telemetry = new RazerDeviceTelemetry(
            BladeKeyboardBrightness: null,
            BladePerformanceMode: BladePerformanceMode.Balanced,
            BladeFanMode: null,
            BladeFanTargetRpm: null,
            BladeChargeLimitPercent: null,
            ViperBatteryPercent: null,
            ViperPollingRateHertz: null,
            ViperDpiX: null,
            ViperDpiY: null,
            ViperIdleSeconds: null,
            Errors: [],
            CapturedAt: DateTimeOffset.UtcNow);

        return await new VerifiedProfileApplier().ApplyAsync(
            document,
            [device],
            telemetry,
            reader,
            powerSource: PowerSourceKind.FullPowerAdapter,
            applyKeyboardBrightness: false,
            coolingPadConnected: coolingPadConnected);
    }

    private sealed class RecordingReader : IRazerDeviceTelemetryReader
    {
        public BladePerformanceMode? LastMode { get; private set; }

        public ValueTask<RazerDeviceTelemetry> ReadAsync(
            IReadOnlyList<DeviceDescriptor> devices,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<byte> SetBladeKeyboardBrightnessAsync(
            IReadOnlyList<DeviceDescriptor> devices,
            byte brightness,
            CancellationToken cancellationToken = default,
            bool verifyReadback = true) =>
            throw new NotSupportedException();

        public ValueTask<byte> ReadBladeKeyboardBrightnessAsync(
            IReadOnlyList<DeviceDescriptor> devices,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<BladePerformanceMode> SetBladePerformanceModeAsync(
            IReadOnlyList<DeviceDescriptor> devices,
            BladePerformanceMode mode,
            CancellationToken cancellationToken = default)
        {
            LastMode = mode;
            return ValueTask.FromResult(mode);
        }

        public ValueTask<BladeCpuBoostMode> SetBladeCpuBoostModeAsync(IReadOnlyList<DeviceDescriptor> devices, BladeCpuBoostMode mode, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<BladeGpuBoostMode> SetBladeGpuBoostModeAsync(IReadOnlyList<DeviceDescriptor> devices, BladeGpuBoostMode mode, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<BladeMaxFanMode> SetBladeMaxFanModeAsync(IReadOnlyList<DeviceDescriptor> devices, BladeMaxFanMode mode, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<int> SetBladeChargeLimitAsync(IReadOnlyList<DeviceDescriptor> devices, int percent, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<BladeLogoMode> SetBladeLogoModeAsync(IReadOnlyList<DeviceDescriptor> devices, BladeLogoMode mode, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<int> SetViperPollingRateAsync(IReadOnlyList<DeviceDescriptor> devices, int hertz, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<(int X, int Y)> SetViperDpiAsync(IReadOnlyList<DeviceDescriptor> devices, int x, int y, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<ViperDpiStagesTelemetry> SetViperDpiStagesAsync(IReadOnlyList<DeviceDescriptor> devices, ViperDpiStagesTelemetry stages, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<int> SetViperIdleSecondsAsync(IReadOnlyList<DeviceDescriptor> devices, int seconds, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<int> SetViperLowBatteryThresholdAsync(IReadOnlyList<DeviceDescriptor> devices, int percent, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
