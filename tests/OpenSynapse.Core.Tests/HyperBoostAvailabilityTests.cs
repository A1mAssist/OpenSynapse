using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class HyperBoostCoolingPadGateTests
{
    [Theory]
    [InlineData(0x029D)]
    [InlineData(0x029F)]
    [InlineData(0x02B7)]
    [InlineData(0x02C6)]
    public void HyperBoostIsOnlyOfferedOnTheModelsWhoseMiddlewareListsIt(ushort productId) =>
        Assert.Contains(
            BladePerformanceMode.Hyperboost,
            BladeModelCapabilities.PerformanceModesFor(productId));

    [Theory]
    [InlineData(0x028C)]
    [InlineData(0x0245)]
    [InlineData(0x02A0)]
    [InlineData(0x02B6)]
    [InlineData(0x02B8)]
    [InlineData(0x02C5)]
    [InlineData(0x02C7)]
    [InlineData(0x02E0)]
    public void HyperBoostStaysOutOfThePerformanceListOnEveryOtherModel(ushort productId) =>
        Assert.DoesNotContain(
            BladePerformanceMode.Hyperboost,
            BladeModelCapabilities.PerformanceModesFor(productId));

    [Fact]
    public async Task ProfileHyperBoostAppliesWithCoolingPadOnTheFullPowerAdapter()
    {
        var reader = new RecordingReader();

        var result = await ApplyAsync(
            reader,
            adapterPower: new BladeAdapterPowerSnapshot(0, 0, 280, 280),
            coolingPadConnected: true);

        Assert.Equal(BladePerformanceMode.Hyperboost, reader.LastMode);
        Assert.DoesNotContain(result.Errors, error => error.StartsWith("HyperBoost", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProfileHyperBoostAppliesWithoutCoolingPadAfterDisclaimerAcceptance()
    {
        var reader = new RecordingReader();

        var result = await ApplyAsync(
            reader,
            adapterPower: new BladeAdapterPowerSnapshot(0, 0, 280, 280),
            coolingPadConnected: false,
            padlessHyperBoostAccepted: true);

        Assert.Equal(BladePerformanceMode.Hyperboost, reader.LastMode);
        Assert.DoesNotContain(result.Errors, error => error.StartsWith("HyperBoost", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProfileHyperBoostFallsBackWithoutTheCoolingPadBecauseItCannotAsk()
    {
        var reader = new RecordingReader();

        var result = await ApplyAsync(
            reader,
            adapterPower: new BladeAdapterPowerSnapshot(0, 0, 280, 280),
            coolingPadConnected: false);

        Assert.Equal(BladePerformanceMode.Performance, reader.LastMode);
        Assert.Contains(result.Errors, error => error.StartsWith("HyperBoost", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProfileHyperBoostAppliesWithoutAnyAdapterReading()
    {
        var reader = new RecordingReader();

        var result = await ApplyAsync(
            reader,
            adapterPower: null,
            coolingPadConnected: true);

        Assert.Equal(BladePerformanceMode.Hyperboost, reader.LastMode);
        Assert.DoesNotContain(result.Errors, error => error.StartsWith("HyperBoost", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProfileHyperBoostAppliesOnALimitedAdapter()
    {
        var reader = new RecordingReader();

        var result = await ApplyAsync(
            reader,
            adapterPower: new BladeAdapterPowerSnapshot(0, 0, 230, 280),
            coolingPadConnected: false,
            padlessHyperBoostAccepted: true);

        Assert.Equal(BladePerformanceMode.Hyperboost, reader.LastMode);
        Assert.DoesNotContain(result.Errors, error => error.StartsWith("HyperBoost", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProfileHyperBoostFallsBackWithoutTheCoolingPadAndWithoutAcceptance()
    {
        var reader = new RecordingReader();

        var result = await ApplyAsync(
            reader,
            adapterPower: new BladeAdapterPowerSnapshot(0, 0, 230, 280),
            coolingPadConnected: false);

        Assert.Equal(BladePerformanceMode.Performance, reader.LastMode);
        Assert.Contains(result.Errors, error => error.StartsWith("HyperBoost", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProfileHyperBoostFallsBackOnAModelWithoutTheMode()
    {
        var reader = new RecordingReader();

        var result = await ApplyAsync(
            reader,
            adapterPower: new BladeAdapterPowerSnapshot(0, 0, 280, 280),
            coolingPadConnected: true,
            productId: 0x02B6);

        Assert.Equal(BladePerformanceMode.Performance, reader.LastMode);
        Assert.Contains(result.Errors, error => error.StartsWith("HyperBoost", StringComparison.Ordinal));
    }

    [Fact]
    public void HyperBoostIsAdmittedForTheModelsThatListIt()
    {
        Assert.True(BladeModelCapabilities.SupportsHyperBoost(0x02C6));
        Assert.True(BladeModelCapabilities.SupportsHyperBoost(0x029D));
        Assert.True(BladeModelCapabilities.SupportsHyperBoost(0x02B7));
        Assert.False(BladeModelCapabilities.SupportsHyperBoost(0x02B6));
        Assert.False(BladeModelCapabilities.SupportsHyperBoost(0x02E0));
        Assert.False(BladeModelCapabilities.SupportsHyperBoost(0xFFFF));
    }

    [Fact]
    public void HyperBoostOnlyNeedsTheModelToListIt()
    {
        Assert.True(BladeModelCapabilities.IsHyperBoostAvailable(0x02C6));
        Assert.False(BladeModelCapabilities.IsHyperBoostAvailable(0x02B6));
        Assert.False(BladeModelCapabilities.IsHyperBoostAvailable(0x02E0));
    }

    private static async Task<ProfileApplyResult> ApplyAsync(
        RecordingReader reader,
        BladeAdapterPowerSnapshot? adapterPower,
        bool coolingPadConnected = true,
        bool padlessHyperBoostAccepted = false,
        ushort productId = 0x02C6)
    {
        var document = ProfileDocument.CreateDefault();
        document.Global.Blade.PerformanceMode = (byte)BladePerformanceMode.Hyperboost;
        var device = new DeviceDescriptor(
            "blade",
            "Blade 16",
            0x1532,
            productId,
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
            CapturedAt: DateTimeOffset.UtcNow,
            BladeAdapterPower: adapterPower);

        return await new VerifiedProfileApplier().ApplyAsync(
            document,
            [device],
            telemetry,
            reader,
            powerSource: PowerSourceKind.FullPowerAdapter,
            applyKeyboardBrightness: false,
            coolingPadConnected: coolingPadConnected,
            padlessHyperBoostAccepted: padlessHyperBoostAccepted);
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

        public ValueTask<BladeLighting2023Effect> SetBlade2023KeyboardEffectAsync(IReadOnlyList<DeviceDescriptor> devices, BladeLighting2023Effect effect, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

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
