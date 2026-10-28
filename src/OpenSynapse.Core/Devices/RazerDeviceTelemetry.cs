using OpenSynapse.Core.Displays;

namespace OpenSynapse.Core.Devices;

public enum BladePerformanceMode : byte
{
    Balanced = 0x00,
    /// <summary>
    /// Wire value 0x01. Every middleware bundle names it Gaming; the 2018-2020
    /// chassis write it as Turbo in their mode table, which is the same byte.
    /// </summary>
    Gaming = 0x01,
    Performance = 0x02,
    BatterySaver = 0x03,
    Custom = 0x04,
    Silent = 0x05,
    BalancedDc = 0x06,
    Hyperboost = 0x07,
}

public static class BladePerformanceModeCycle
{
    public static BladePerformanceMode GetNext(
        BladePerformanceMode current,
        IReadOnlyList<BladePerformanceMode> orderedModes,
        IReadOnlySet<BladePerformanceMode> includedModes)
    {
        return GetNextCore(current, orderedModes, includedModes);
    }

    public static int GetNext(
        int current,
        IReadOnlyList<int> orderedRates,
        IReadOnlySet<int> includedRates) =>
        GetNextCore(current, orderedRates, includedRates);

    private static T GetNextCore<T>(
        T current,
        IReadOnlyList<T> orderedValues,
        IReadOnlySet<T> includedValues)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(orderedValues);
        ArgumentNullException.ThrowIfNull(includedValues);
        if (orderedValues.Count == 0 || includedValues.Count == 0)
        {
            throw new ArgumentException("At least one value must be included.");
        }

        var currentIndex = -1;
        for (var index = 0; index < orderedValues.Count; index++)
        {
            if (EqualityComparer<T>.Default.Equals(orderedValues[index], current))
            {
                currentIndex = index;
                break;
            }
        }
        for (var offset = 1; offset <= orderedValues.Count; offset++)
        {
            var candidate = orderedValues[(Math.Max(currentIndex, -1) + offset) % orderedValues.Count];
            if (includedValues.Contains(candidate))
            {
                return candidate;
            }
        }

        throw new ArgumentException("The included set does not contain a supported value.", nameof(includedValues));
    }
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

public enum BladeNativeDisplayMode : byte
{
    Uhd = 0,
    Fhd = 1,
}

public readonly record struct BladeGameModeTelemetry(
    byte GameMode,
    byte KeyCover,
    byte Lifted);

public readonly record struct BladeSkuHardwareConfiguration(
    bool Dds,
    bool MiniLedResolution,
    bool IllegalBatterySupport,
    byte Raw)
{
    /// <summary>The UHD/FHD selector belongs to the dual-resolution mini-LED panel.</summary>
    public bool SupportsNativeDisplayMode => MiniLedResolution;
}

public sealed record ViperDpiStageTelemetry(byte Number, int X, int Y);

public sealed record ViperDpiStagesTelemetry(
    byte ActiveStage,
    IReadOnlyList<ViperDpiStageTelemetry> Stages);

public enum ViperButtonMappingLayer : byte
{
    Normal = 0,
    HyperShift = 1,
}

public enum ViperButtonMappingFunction : byte
{
    Off = 0,
    MouseButton = 1,
    KeyboardKey = 2,
    Dpi = 6,
    MediaKey = 10,
    DoubleClick = 11,
    HyperShift = 12,
    KeyboardTurbo = 13,
    MouseTurbo = 14,
}

public sealed record ViperButtonAssignment(
    byte ProfileId,
    byte ButtonId,
    ViperButtonMappingLayer Layer,
    ViperButtonMappingFunction Function,
    IReadOnlyList<byte> FunctionData);

public sealed record DeviceCapabilitySummary(int Available, int Supported);

public static class DeviceCapabilitySummaryCalculator
{
    public static DeviceCapabilitySummary Calculate(
        DeviceDescriptor descriptor,
        RazerDeviceTelemetry telemetry)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(telemetry);
        var supportsLocalDimming =
            telemetry.BladeSkuHardwareConfiguration?.MiniLedResolution == true;
        // A shared Power Mode bit reads back on models that never implemented it, so the
        // Max Fan row is counted only where the model contract admits it.
        var supportsMaxFan = BladeModelCapabilities.SupportsMaxFan(descriptor.ProductId);
        if (descriptor.ProtocolFamily == "blade-671")
        {
            // Blade 16 (2023): its own uniform lighting reports are admitted, but the
            // 02C6 matrix lighting and panel reports are not.
            return new(
                Count(
                    telemetry.BladeKeyboardBrightness,
                    telemetry.BladeKeyboardEffect,
                    telemetry.BladePerformanceMode,
                    telemetry.BladeChargeLimitPercent,
                    telemetry.BladeCpuBoostMode,
                    telemetry.BladeGpuBoostMode,
                    supportsMaxFan ? telemetry.BladeMaxFanMode : null,
                    telemetry.BladeFanMode,
                    telemetry.BladeCurrentFanCpuRpm,
                    telemetry.BladeCurrentFanGpuRpm,
                    telemetry.BladeOneTimeFullChargeEnabled),
                10 + (supportsMaxFan ? 1 : 0));
        }

        return descriptor.ProtocolFamily switch
        {
            "blade-710" => new(
                Count(
                    telemetry.BladeKeyboardBrightness,
                    telemetry.BladePerformanceMode,
                    telemetry.BladeChargeLimitPercent,
                    telemetry.BladeCpuBoostMode,
                    telemetry.BladeGpuBoostMode,
                    supportsMaxFan ? telemetry.BladeMaxFanMode : null,
                    telemetry.BladeLogoMode,
                    telemetry.BladeFanMode,
                    telemetry.BladeCurrentFanCpuRpm,
                    telemetry.BladeCurrentFanGpuRpm,
                    telemetry.BladeAdvancedFanCpuModeRaw,
                    telemetry.BladeAdvancedFanGpuModeRaw,
                    telemetry.BladeStartupAnimationEnabled,
                    telemetry.BladeNativeDisplayMode,
                    telemetry.BladeSkuHardwareConfiguration,
                    telemetry.BladeOneTimeFullChargeEnabled,
                    telemetry.BladeAdapterPower) +
                    (supportsLocalDimming && telemetry.BladeLocalDimmingEnabled is not null ? 1 : 0),
                16 + (supportsMaxFan ? 1 : 0) + (supportsLocalDimming ? 1 : 0)),
            "blade-597" => new(
                Count(
                    telemetry.BladeKeyboardBrightness,
                    telemetry.BladePerformanceMode,
                    telemetry.BladeChargeLimitPercent,
                    telemetry.BladeFanMode),
                4),
            "viper-184" => new(
                Count(
                    telemetry.ViperBatteryPercent,
                    telemetry.ViperPollingRateHertz,
                    telemetry.ViperDpiX,
                    telemetry.ViperIdleSeconds,
                    telemetry.ViperDpiStages,
                    telemetry.ViperLowBatteryThresholdRaw),
                6),
            _ => new(0, 0),
        };
    }

    private static int Count(params object?[] values) => values.Count(value => value is not null);
}

public sealed record BladeAdapterPowerSnapshot(
    byte ActualLevel,
    byte RecommendedLevel,
    int? ActualWatts,
    int? RecommendedWatts)
{
    public bool IsLimitedAdapter =>
        ActualWatts is int actual && RecommendedWatts is int recommended &&
        actual > 0 && recommended > 0 && actual < recommended;
}

public sealed record BladeFanControlState(BladeFanMode Mode, int TargetRpm);

public sealed record BladeFanControlSnapshot(
    BladePerformanceMode PerformanceMode,
    BladeFanMode Mode,
    int CpuTargetRpm,
    int GpuTargetRpm);

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
    ViperDpiStagesTelemetry? ViperDpiStages = null,
    byte? ViperLowBatteryThresholdRaw = null,
    BladeLogoMode? BladeLogoMode = null,
    BladeGameModeTelemetry? BladeGameMode = null,
    bool? BladeStartupAnimationEnabled = null,
    BladeNativeDisplayMode? BladeNativeDisplayMode = null,
    BladeSkuHardwareConfiguration? BladeSkuHardwareConfiguration = null,
    bool? BladeOneTimeFullChargeEnabled = null,
    bool? BladeLocalDimmingEnabled = null,
    BladeLighting2023Effect? BladeKeyboardEffect = null,
    IReadOnlyDictionary<string, DeviceCapabilitySummary>? CapabilitySummaries = null,
    bool? BladeFnKeyMultimediaPrimary = null,
    string? BladeMcuVersion = null,
    BladeAdapterPowerSnapshot? BladeAdapterPower = null);

public interface IRazerDeviceTelemetryReader
{
    ValueTask<RazerDeviceTelemetry> ReadAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        CancellationToken cancellationToken = default);

    ValueTask<byte> SetBladeKeyboardBrightnessAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        byte brightness,
        CancellationToken cancellationToken = default,
        bool verifyReadback = true);

    ValueTask<byte> ReadBladeKeyboardBrightnessAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Selects one of the firmware-rendered uniform effects admitted for the Blade 16 2023.
    /// Other Blade generations drive their effects through the lighting controller instead.
    /// </summary>
    ValueTask<BladeLighting2023Effect> SetBlade2023KeyboardEffectAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeLighting2023Effect effect,
        CancellationToken cancellationToken = default);

    ValueTask<BladePerformanceMode> SetBladePerformanceModeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladePerformanceMode mode,
        CancellationToken cancellationToken = default);

    ValueTask<BladeGameModeTelemetry> SetBladeGameModeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        bool enabled,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    ValueTask<bool> SetBladeFnKeyStateAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        bool multiFunctionPrimary,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    ValueTask<BladeFanControlState> SetBladeFanAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeFanMode mode,
        int? cpuTargetRpm,
        int? gpuTargetRpm,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    ValueTask<BladeFanControlSnapshot> ReadBladeFanControlStateAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    ValueTask<BladeFanControlSnapshot> SetBladeFanTargetsAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeFanMode mode,
        int cpuTargetRpm,
        int gpuTargetRpm,
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

    ValueTask<bool> SetBladeOneTimeFullChargeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        bool enabled,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    ValueTask<bool> SetBladeLocalDimmingAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        bool enabled,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    ValueTask<bool> SetBladeStartupAnimationAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        bool enabled,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    ValueTask<BladeNativeDisplayMode> SetBladeNativeDisplayModeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeNativeDisplayMode mode,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    ValueTask<BladeDisplayMuxStatus> ReadBladeDisplayMuxStatusAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    ValueTask<BladeDisplayMuxSwitchResult> SetBladeDisplayMuxModeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeDisplayMuxMode mode,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

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

    ValueTask<int> SetViperLowBatteryThresholdAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        int percent,
        CancellationToken cancellationToken = default);

    ValueTask<byte> SetViperBatteryChemistryAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        byte chemistry,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    ValueTask<IReadOnlyList<ViperButtonAssignment>> ReadViperButtonAssignmentsAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    ValueTask<ViperButtonAssignment> SetViperButtonAssignmentAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        ViperButtonAssignment assignment,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    ValueTask<IReadOnlyList<ViperButtonAssignment>> SetViperButtonAssignmentsAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        IReadOnlyList<ViperButtonAssignment> assignments,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
