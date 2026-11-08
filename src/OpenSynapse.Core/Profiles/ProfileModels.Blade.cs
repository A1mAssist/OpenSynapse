using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Displays;

namespace OpenSynapse.Core.Profiles;

public sealed class BladeProfileSettings
{
    public const string Product710DefaultMappingPreset = "product710-default";

    public byte? KeyboardBrightness { get; set; }
    public byte? PerformanceMode { get; set; }
    public byte? FanMode { get; set; }
    public int? FanTargetRpm { get; set; }
    public BladeFanCurveProfile? FanCurve { get; set; }
    public int? ChargeLimitPercent { get; set; }
    public int? RefreshRateHertz { get; set; }
    public byte? MaxFanMode { get; set; }
    public byte? CpuBoostMode { get; set; }
    public byte? GpuBoostMode { get; set; }
    public byte? LogoMode { get; set; }
    public bool? GamingModeEnabled { get; set; }
    public bool? SnapTapEnabled { get; set; }
    public string? MappingPreset { get; set; }
    public bool? LightingEnabled { get; set; }
    public bool? ChromaOverrideEnabled { get; set; }
    public int? DisplayBrightnessPercent { get; set; }
    public bool? StartupAnimationEnabled { get; set; }
    public int? AutoSleepSeconds { get; set; }
    public int? TimeToDimSeconds { get; set; }
    public bool? IndicatorLedEnabled { get; set; }
    public bool? GpuCardDetectionEnabled { get; set; }
    public bool? NativeDisplayModeEnabled { get; set; }
    public bool? LocalDimmingEnabled { get; set; }
    public byte? CpuPowerBoostMode { get; set; }
    public int? GpuCoreClockOffsetMhz { get; set; }
    public int? GpuMemoryClockOffsetMhz { get; set; }
    public int? GpuCoreClockLimitMhz { get; set; }
    public int? CpuCurveOffset { get; set; }
    public int? CpuPowerLimitWatts { get; set; }
    // Legacy v3 migration input; machine-settings.json owns the live GPU mode.
    public BladeGpuMode? GpuMode { get; set; }

    internal void ApplySafeDefaults()
    {
        FanCurve?.ApplySafeDefaults();
        if (MappingPreset is not null &&
            !StringComparer.Ordinal.Equals(MappingPreset, Product710DefaultMappingPreset))
        {
            throw new InvalidDataException($"Unsupported Blade mapping preset: {MappingPreset}.");
        }
        MouseProfileSettings.ValidatePercent(DisplayBrightnessPercent, nameof(DisplayBrightnessPercent));
        MouseProfileSettings.ValidateRange(AutoSleepSeconds, 0, 86_400, nameof(AutoSleepSeconds));
        MouseProfileSettings.ValidateRange(TimeToDimSeconds, 0, 86_400, nameof(TimeToDimSeconds));
        MouseProfileSettings.ValidateRange(CpuPowerBoostMode, 0, 1, nameof(CpuPowerBoostMode));
        MouseProfileSettings.ValidateRange(
            GpuCoreClockOffsetMhz,
            -500,
            200,
            nameof(GpuCoreClockOffsetMhz));
        MouseProfileSettings.ValidateRange(
            GpuMemoryClockOffsetMhz,
            -500,
            2000,
            nameof(GpuMemoryClockOffsetMhz));
        if (GpuCoreClockLimitMhz is not (null or 0 or (>= 400 and <= 3000)))
        {
            throw new InvalidDataException("GpuCoreClockLimitMhz must be 0 or between 400 and 3000.");
        }
        MouseProfileSettings.ValidateRange(CpuCurveOffset, -30, 0, nameof(CpuCurveOffset));
        if (CpuPowerLimitWatts is not (null or 0 or (>= 8 and <= 100)))
        {
            throw new InvalidDataException("CpuPowerLimitWatts must be 0 or between 8 and 100.");
        }
    }
}

public sealed class BladeFanCurveProfile
{
    public BladeFanCurveTemperatureMode TemperatureMode { get; set; }
    public List<BladeFanCurvePoint> CpuPoints { get; set; } = [];
    public List<BladeFanCurvePoint> GpuPoints { get; set; } = [];
    public int MinimumCpuTemperatureCelsius { get; set; } =
        BladeFanCurve.DefaultMinimumCpuTemperatureCelsius;
    public int MinimumGpuTemperatureCelsius { get; set; } =
        BladeFanCurve.DefaultMinimumGpuTemperatureCelsius;
    public int MinimumFanSpeedRpm { get; set; } = BladeFanCurve.DefaultMinimumFanSpeedRpm;

    public BladeFanCurve CreateCurve() => new(
        TemperatureMode,
        CpuPoints,
        GpuPoints,
        MinimumCpuTemperatureCelsius,
        MinimumGpuTemperatureCelsius,
        MinimumFanSpeedRpm);

    public static BladeFanCurveProfile FromCurve(BladeFanCurve curve)
    {
        ArgumentNullException.ThrowIfNull(curve);
        return new()
        {
            TemperatureMode = curve.TemperatureMode,
            CpuPoints = curve.CpuPoints.ToList(),
            GpuPoints = curve.GpuPoints.ToList(),
            MinimumCpuTemperatureCelsius = curve.MinimumCpuTemperatureCelsius,
            MinimumGpuTemperatureCelsius = curve.MinimumGpuTemperatureCelsius,
            MinimumFanSpeedRpm = curve.MinimumFanSpeedRpm,
        };
    }

    internal BladeFanCurveProfile Clone()
    {
        ApplySafeDefaults();
        return new()
        {
            TemperatureMode = TemperatureMode,
            CpuPoints = CpuPoints.ToList(),
            GpuPoints = GpuPoints.ToList(),
            MinimumCpuTemperatureCelsius = MinimumCpuTemperatureCelsius,
            MinimumGpuTemperatureCelsius = MinimumGpuTemperatureCelsius,
            MinimumFanSpeedRpm = MinimumFanSpeedRpm,
        };
    }

    internal void ApplySafeDefaults()
    {
        CpuPoints ??= [];
        GpuPoints ??= [];
    }
}
