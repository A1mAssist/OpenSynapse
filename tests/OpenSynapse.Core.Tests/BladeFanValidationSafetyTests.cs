using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.Core.Tests;

public sealed class BladeFanValidationSafetyTests
{
    private static readonly DeviceDescriptor Blade = new(
        "blade-path", "Blade", 0x1532, 0x02C6,
        DeviceAccessState.Available, DeviceCapabilityState.PendingValidation, 91, 1, 2,
        "blade-710");

    [Fact]
    public async Task AppliesSameValueAndMinimalDeltaThenRestoresOriginal()
    {
        var reader = new FakeReader(new(BladeFanMode.Automatic, 3200));

        var result = await BladeFanValidation.ExecuteAsync(
            reader, [Blade], new(BladeFanMode.Automatic, 3200), 3300, 5,
            CancellationToken.None,
            static (_, _) => Task.CompletedTask);

        Assert.Null(result.OperationError);
        Assert.Null(result.RestorationError);
        Assert.Equal(new(BladeFanMode.Automatic, 3200), result.Original);
        Assert.Equal(new(BladeFanMode.Automatic, 3200), result.SameValueReadback);
        Assert.Equal(new(BladeFanMode.Manual, 3300), result.TargetReadback);
        Assert.Equal(new(BladeFanMode.Automatic, 3200), result.RestorationReadback);
        Assert.Equal(new(BladeFanMode.Automatic, 3200), reader.State);
        Assert.All(reader.CancellationTokens, token => Assert.Equal(CancellationToken.None, token));
    }

    [Fact]
    public async Task TargetReadbackFailureStillRestoresOriginal()
    {
        var reader = new FakeReader(new(BladeFanMode.Automatic, 3200)) { IgnoreTargetWrite = true };

        var result = await BladeFanValidation.ExecuteAsync(
            reader, [Blade], new(BladeFanMode.Automatic, 3200), 3300, 5,
            CancellationToken.None,
            static (_, _) => Task.CompletedTask);

        Assert.NotNull(result.OperationError);
        Assert.Null(result.RestorationError);
        Assert.Equal(new(BladeFanMode.Automatic, 3200), result.RestorationReadback);
        Assert.Equal(new(BladeFanMode.Automatic, 3200), reader.State);
    }

    [Fact]
    public async Task CancellationDuringHoldRestoresWithNonCancelableCalls()
    {
        var reader = new FakeReader(new(BladeFanMode.Automatic, 3200));

        var result = await BladeFanValidation.ExecuteAsync(
            reader, [Blade], new(BladeFanMode.Automatic, 3200), 3300, 5,
            new CancellationToken(canceled: true),
            static (_, _) => throw new OperationCanceledException("hold canceled"));

        Assert.Contains("hold canceled", result.OperationError, StringComparison.Ordinal);
        Assert.Null(result.RestorationError);
        Assert.Equal(new(BladeFanMode.Automatic, 3200), reader.State);
        Assert.Contains(reader.CancellationTokens, token => token == CancellationToken.None);
    }

    [Fact]
    public async Task RestorationFailureIsReportedSeparately()
    {
        var reader = new FakeReader(new(BladeFanMode.Automatic, 3200)) { FailNonCancelable = true };
        using var cancellation = new CancellationTokenSource();

        var result = await BladeFanValidation.ExecuteAsync(
            reader, [Blade], new(BladeFanMode.Automatic, 3200), 3300, 5,
            cancellation.Token,
            static (_, _) => Task.CompletedTask);

        Assert.Null(result.OperationError);
        Assert.Contains("restore failure", result.RestorationError, StringComparison.Ordinal);
        Assert.Equal(new(BladeFanMode.Automatic, 3200), result.Original);
    }

    [Fact]
    public void RefusesExistingEvidencePath()
    {
        var path = Path.GetTempFileName();
        try
        {
            Assert.Throws<ArgumentException>(() => BladeFanValidation.Options.Parse([
                "--blade-fan-fixed", "--target-rpm", "3300", "--hold-seconds", "5", "--output", path]));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class FakeReader(BladeFanControlState initial) : IRazerDeviceTelemetryReader
    {
        public BladeFanControlState State { get; private set; } = initial;
        public bool IgnoreTargetWrite { get; set; }
        public bool FailNonCancelable { get; set; }
        public List<CancellationToken> CancellationTokens { get; } = [];

        public ValueTask<RazerDeviceTelemetry> ReadAsync(
            IReadOnlyList<DeviceDescriptor> devices,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<byte> SetBladeKeyboardBrightnessAsync(IReadOnlyList<DeviceDescriptor> devices, byte brightness, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<BladePerformanceMode> SetBladePerformanceModeAsync(IReadOnlyList<DeviceDescriptor> devices, BladePerformanceMode mode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<BladeCpuBoostMode> SetBladeCpuBoostModeAsync(IReadOnlyList<DeviceDescriptor> devices, BladeCpuBoostMode mode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<BladeGpuBoostMode> SetBladeGpuBoostModeAsync(IReadOnlyList<DeviceDescriptor> devices, BladeGpuBoostMode mode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<BladeMaxFanMode> SetBladeMaxFanModeAsync(IReadOnlyList<DeviceDescriptor> devices, BladeMaxFanMode mode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<int> SetBladeChargeLimitAsync(IReadOnlyList<DeviceDescriptor> devices, int percent, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<BladeLogoMode> SetBladeLogoModeAsync(IReadOnlyList<DeviceDescriptor> devices, BladeLogoMode mode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<int> SetViperPollingRateAsync(IReadOnlyList<DeviceDescriptor> devices, int hertz, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<(int X, int Y)> SetViperDpiAsync(IReadOnlyList<DeviceDescriptor> devices, int x, int y, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<ViperDpiStagesTelemetry> SetViperDpiStagesAsync(IReadOnlyList<DeviceDescriptor> devices, ViperDpiStagesTelemetry stages, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<int> SetViperIdleSecondsAsync(IReadOnlyList<DeviceDescriptor> devices, int seconds, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<BladeFanControlState> SetBladeFanAsync(
            IReadOnlyList<DeviceDescriptor> devices,
            BladeFanMode mode,
            int? targetRpm,
            CancellationToken cancellationToken = default)
        {
            CancellationTokens.Add(cancellationToken);
            if (!cancellationToken.CanBeCanceled && FailNonCancelable)
            {
                throw new InvalidOperationException("restore failure");
            }
            if (mode == BladeFanMode.Manual && IgnoreTargetWrite)
            {
                IgnoreTargetWrite = false;
                return ValueTask.FromResult(State);
            }

            State = new BladeFanControlState(mode, targetRpm ?? State.TargetRpm);
            return ValueTask.FromResult(State);
        }
    }
}
