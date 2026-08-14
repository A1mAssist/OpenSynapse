namespace OpenSynapse.Core.Devices;

public enum BladePerformanceMode : byte
{
    Balanced = 0x00,
    Performance = 0x02,
    Custom = 0x04,
    Silent = 0x05,
    Battery = 0x06,
    Hyperboost = 0x07,
}

public enum BladeFanMode : byte
{
    Automatic = 0x00,
    Manual = 0x01,
}

public enum BladeCpuBoostMode : byte
{
    Low = 0x00,
    Medium = 0x01,
    High = 0x02,
    Boost = 0x03,
    Undervolt = 0x04,
}

public enum BladeGpuBoostMode : byte
{
    Low = 0x00,
    Medium = 0x01,
    High = 0x02,
}

public enum BladeMaxFanMode : byte
{
    Disabled = 0x00,
    Enabled = 0x02,
}

public sealed record ViperDpiStageTelemetry(byte Number, int X, int Y);

public sealed record ViperDpiStagesTelemetry(
    byte ActiveStage,
    IReadOnlyList<ViperDpiStageTelemetry> Stages);

public sealed record BladeFanControlState(BladeFanMode Mode, int TargetRpm);

public sealed record RazerDeviceTelemetry(
    byte? BladeKeyboardBrightness,
    BladePerformanceMode? BladePerformanceMode,
    BladeFanMode? BladeFanMode,
    int? BladeFanTargetRpm,
    int? BladeChargeLimitPercent,
    int? ViperBatteryPercent,
    int? ViperPollingRateHertz,
    int? ViperDpiX,
    int? ViperDpiY,
    int? ViperIdleSeconds,
    IReadOnlyList<string> Errors,
    DateTimeOffset CapturedAt,
    BladeCpuBoostMode? BladeCpuBoostMode = null,
    BladeGpuBoostMode? BladeGpuBoostMode = null,
    BladeMaxFanMode? BladeMaxFanMode = null,
    int? BladeCurrentFanCpuRpm = null,
    int? BladeCurrentFanGpuRpm = null,
    byte? BladeAdvancedFanCpuModeRaw = null,
    byte? BladeAdvancedFanGpuModeRaw = null,
    int? BladeWiredBatteryPercent = null,
    byte? BladeChargingStatusRaw = null,
    byte? BladeAutoSleepRaw = null,
    int? BladeTimeToSleepSeconds = null,
    ViperDpiStagesTelemetry? ViperDpiStages = null,
    byte? ViperLowBatteryThresholdRaw = null,
    BladeLogoMode? BladeLogoMode = null);

public interface IRazerDeviceTelemetryReader
{
    ValueTask<RazerDeviceTelemetry> ReadAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        CancellationToken cancellationToken = default);

    ValueTask<byte> SetBladeKeyboardBrightnessAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        byte brightness,
        CancellationToken cancellationToken = default);

    ValueTask<BladePerformanceMode> SetBladePerformanceModeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladePerformanceMode mode,
        CancellationToken cancellationToken = default);

    ValueTask<BladeFanControlState> SetBladeFanAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeFanMode mode,
        int? targetRpm,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    ValueTask<BladeCpuBoostMode> SetBladeCpuBoostModeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeCpuBoostMode mode,
        CancellationToken cancellationToken = default);

    ValueTask<BladeGpuBoostMode> SetBladeGpuBoostModeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeGpuBoostMode mode,
        CancellationToken cancellationToken = default);

    ValueTask<BladeMaxFanMode> SetBladeMaxFanModeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeMaxFanMode mode,
        CancellationToken cancellationToken = default);

    ValueTask<int> SetBladeChargeLimitAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        int percent,
        CancellationToken cancellationToken = default);

    ValueTask<BladeLogoMode> SetBladeLogoModeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeLogoMode mode,
        CancellationToken cancellationToken = default);

    ValueTask<int> SetViperPollingRateAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        int hertz,
        CancellationToken cancellationToken = default);

    ValueTask<(int X, int Y)> SetViperDpiAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        int x,
        int y,
        CancellationToken cancellationToken = default);

    ValueTask<ViperDpiStagesTelemetry> SetViperDpiStagesAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        ViperDpiStagesTelemetry stages,
        CancellationToken cancellationToken = default);

    ValueTask<int> SetViperIdleSecondsAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        int seconds,
        CancellationToken cancellationToken = default);
}
