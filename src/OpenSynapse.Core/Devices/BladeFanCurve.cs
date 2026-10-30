namespace OpenSynapse.Core.Devices;

public enum BladeFanCurveTemperatureMode
{
    Cpu,
    Gpu,
    Both,
    /// <summary>CPU temperature drives the CPU fan; GPU temperature drives the GPU fan.</summary>
    Independent,
}

public readonly record struct BladeFanCurvePoint(
    int TemperatureCelsius,
    int CpuFanSpeedRpm,
    int GpuFanSpeedRpm);

public readonly record struct BladeFanTargets(int CpuRpm, int GpuRpm);

public sealed class BladeFanCurve
{
    public const int DefaultMinimumCpuTemperatureCelsius = 40;
    public const int DefaultMinimumGpuTemperatureCelsius = 30;
    public const int DefaultMinimumFanSpeedRpm = 2300;
    public const int MinimumPointFanSpeedRpm = 1900;
    /// <summary>
    /// Highest speed a curve point may carry. The official middleware drives the same
    /// fan-target report as rpm/100 and its Performance and Hyperboost modes run up to
    /// 5400 RPM, so a curve may ask for that too; the per-model ceiling in
    /// BladeModelCapabilities still guards the write itself.
    /// </summary>
    public const int MaximumPointFanSpeedRpm = 5400;
    private const int MaximumPoints = 32;

    public BladeFanCurve(
        BladeFanCurveTemperatureMode temperatureMode,
        IReadOnlyList<BladeFanCurvePoint> cpuPoints,
        IReadOnlyList<BladeFanCurvePoint> gpuPoints,
        int minimumCpuTemperatureCelsius = DefaultMinimumCpuTemperatureCelsius,
        int minimumGpuTemperatureCelsius = DefaultMinimumGpuTemperatureCelsius,
        int minimumFanSpeedRpm = DefaultMinimumFanSpeedRpm)
    {
        if (!Enum.IsDefined(temperatureMode))
        {
            throw new ArgumentOutOfRangeException(nameof(temperatureMode));
        }
        ValidateBaseline(minimumCpuTemperatureCelsius, nameof(minimumCpuTemperatureCelsius));
        ValidateBaseline(minimumGpuTemperatureCelsius, nameof(minimumGpuTemperatureCelsius));
        ValidatePointSpeed(minimumFanSpeedRpm, nameof(minimumFanSpeedRpm));

        TemperatureMode = temperatureMode;
        MinimumCpuTemperatureCelsius = minimumCpuTemperatureCelsius;
        MinimumGpuTemperatureCelsius = minimumGpuTemperatureCelsius;
        MinimumFanSpeedRpm = minimumFanSpeedRpm;
        CpuPoints = CopyAndValidate(cpuPoints, minimumCpuTemperatureCelsius, nameof(cpuPoints));
        GpuPoints = CopyAndValidate(gpuPoints, minimumGpuTemperatureCelsius, nameof(gpuPoints));
    }

    public BladeFanCurveTemperatureMode TemperatureMode { get; }
    public IReadOnlyList<BladeFanCurvePoint> CpuPoints { get; }
    public IReadOnlyList<BladeFanCurvePoint> GpuPoints { get; }
    public int MinimumCpuTemperatureCelsius { get; }
    public int MinimumGpuTemperatureCelsius { get; }
    public int MinimumFanSpeedRpm { get; }

    public BladeFanTargets Evaluate(double? cpuTemperatureCelsius, double? gpuTemperatureCelsius)
    {
        return TemperatureMode switch
        {
            BladeFanCurveTemperatureMode.Cpu =>
                cpuTemperatureCelsius is double cpuValue
                    ? EvaluateCurve(cpuValue, CpuPoints, MinimumCpuTemperatureCelsius)
                    : throw MissingTemperature("CPU"),
            BladeFanCurveTemperatureMode.Gpu =>
                gpuTemperatureCelsius is double gpuValue
                    ? EvaluateCurve(gpuValue, GpuPoints, MinimumGpuTemperatureCelsius)
                    : throw MissingTemperature("GPU"),
            BladeFanCurveTemperatureMode.Both =>
                cpuTemperatureCelsius is not double cpuBoth ||
                gpuTemperatureCelsius is not double gpuBoth
                    ? throw MissingTemperature("CPU/GPU")
                    : SelectHigherCpuOutput(
                        EvaluateCurve(cpuBoth, CpuPoints, MinimumCpuTemperatureCelsius),
                        EvaluateCurve(gpuBoth, GpuPoints, MinimumGpuTemperatureCelsius)),
            BladeFanCurveTemperatureMode.Independent =>
                cpuTemperatureCelsius is not double cpuIndependent ||
                gpuTemperatureCelsius is not double gpuIndependent
                    ? throw MissingTemperature("CPU/GPU")
                    : new BladeFanTargets(
                        EvaluateCurve(cpuIndependent, CpuPoints, MinimumCpuTemperatureCelsius).CpuRpm,
                        EvaluateCurve(gpuIndependent, GpuPoints, MinimumGpuTemperatureCelsius).GpuRpm),
            _ => throw new InvalidOperationException("Unknown fan curve temperature mode."),
        };
    }

    private static BladeFanTargets SelectHigherCpuOutput(
        BladeFanTargets cpu,
        BladeFanTargets gpu) => cpu.CpuRpm >= gpu.CpuRpm ? cpu : gpu;

    private BladeFanTargets EvaluateCurve(
        double temperature,
        IReadOnlyList<BladeFanCurvePoint> points,
        int minimumTemperature)
    {
        if (!double.IsFinite(temperature))
        {
            throw new ArgumentOutOfRangeException(nameof(temperature));
        }

        var first = points[0];
        if (temperature <= first.TemperatureCelsius)
        {
            if (temperature <= minimumTemperature)
            {
                return Normalize(MinimumFanSpeedRpm, MinimumFanSpeedRpm);
            }

            var factor = (temperature - minimumTemperature) /
                (first.TemperatureCelsius - minimumTemperature);
            return Normalize(
                Lerp(MinimumFanSpeedRpm, first.CpuFanSpeedRpm, factor),
                Lerp(MinimumFanSpeedRpm, first.GpuFanSpeedRpm, factor));
        }

        for (var index = 0; index < points.Count - 1; index++)
        {
            var lower = points[index];
            var upper = points[index + 1];
            if (temperature > upper.TemperatureCelsius)
            {
                continue;
            }

            var factor = (temperature - lower.TemperatureCelsius) /
                (upper.TemperatureCelsius - lower.TemperatureCelsius);
            return Normalize(
                Lerp(lower.CpuFanSpeedRpm, upper.CpuFanSpeedRpm, factor),
                Lerp(lower.GpuFanSpeedRpm, upper.GpuFanSpeedRpm, factor));
        }

        var last = points[^1];
        return Normalize(last.CpuFanSpeedRpm, last.GpuFanSpeedRpm);
    }

    private static IReadOnlyList<BladeFanCurvePoint> CopyAndValidate(
        IReadOnlyList<BladeFanCurvePoint> points,
        int minimumTemperature,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(points, parameterName);
        if (points.Count is < 1 or > MaximumPoints)
        {
            throw new ArgumentException($"Fan curve must contain between 1 and {MaximumPoints} points.", parameterName);
        }

        var copy = points.ToArray();
        for (var index = 0; index < copy.Length; index++)
        {
            var point = copy[index];
            if (point.TemperatureCelsius <= minimumTemperature || point.TemperatureCelsius > 120)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    $"Temperature point must be greater than the low-temperature baseline of {minimumTemperature} C and no greater than 120 C.");
            }
            ValidatePointSpeed(point.CpuFanSpeedRpm, parameterName);
            ValidatePointSpeed(point.GpuFanSpeedRpm, parameterName);
            if (index > 0 && point.TemperatureCelsius <= copy[index - 1].TemperatureCelsius)
            {
                throw new ArgumentException("Temperature points must be strictly increasing.", parameterName);
            }
        }
        return Array.AsReadOnly(copy);
    }

    private static void ValidateBaseline(int temperature, string parameterName)
    {
        if (temperature is < 0 or > 119)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void ValidatePointSpeed(int rpm, string parameterName)
    {
        if (rpm is < MinimumPointFanSpeedRpm or > MaximumPointFanSpeedRpm)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"Curve point fan speed must be between {MinimumPointFanSpeedRpm} and {MaximumPointFanSpeedRpm} RPM.");
        }
    }

    private static BladeFanTargets Normalize(double cpuRpm, double gpuRpm) => new(
        ToProtocolRpm(cpuRpm),
        ToProtocolRpm(gpuRpm));

    private static int ToProtocolRpm(double rpm)
    {
        var roundedLikeJavaScript = Math.Floor(rpm + 0.5);
        var protocolRpm = checked((int)Math.Floor(roundedLikeJavaScript / 100) * 100);
        return Math.Clamp(protocolRpm, MinimumPointFanSpeedRpm, MaximumPointFanSpeedRpm);
    }

    private static double Lerp(double start, double end, double factor) =>
        start + (end - start) * factor;

    private static InvalidOperationException MissingTemperature(string sensor) =>
        new($"Fan curve requires a valid {sensor} temperature.");

    /// <summary>
    /// Returns a curve whose fan targets do not exceed the model's writable ceiling.
    /// The editor publishes a global 5400 RPM maximum, but some ECs stop lower.
    /// </summary>
    public BladeFanCurve ClampMaximumFanSpeed(int maximumRpm)
    {
        if (maximumRpm is < MinimumPointFanSpeedRpm or > MaximumPointFanSpeedRpm)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRpm));
        }

        if (maximumRpm == MaximumPointFanSpeedRpm)
        {
            return this;
        }

        static BladeFanCurvePoint Clamp(BladeFanCurvePoint point, int maximum) =>
            point with
            {
                CpuFanSpeedRpm = Math.Min(point.CpuFanSpeedRpm, maximum),
                GpuFanSpeedRpm = Math.Min(point.GpuFanSpeedRpm, maximum),
            };

        return new BladeFanCurve(
            TemperatureMode,
            CpuPoints.Select(point => Clamp(point, maximumRpm)).ToArray(),
            GpuPoints.Select(point => Clamp(point, maximumRpm)).ToArray(),
            MinimumCpuTemperatureCelsius,
            MinimumGpuTemperatureCelsius,
            Math.Min(MinimumFanSpeedRpm, maximumRpm));
    }

    /// <summary>
    /// Conservative starting curve: near-idle while the machine is cool, then a steady
    /// climb to full speed before the chip reaches its thermal limit. Both sensors share
    /// one shape because the GPU runs hotter than the CPU under the same load.
    /// </summary>
    public static BladeFanCurve CreateDefault() => new(
        BladeFanCurveTemperatureMode.Both,
        DefaultPoints,
        DefaultPoints);

    // Temperature, CPU RPM, GPU RPM. The 92 C point keeps fans full before throttling.
    private static readonly BladeFanCurvePoint[] DefaultPoints =
    [
        new(50, 2300, 2300),
        new(65, 2800, 2900),
        new(75, 3500, 3700),
        new(85, 4300, 4600),
        new(92, 5400, 5400),
    ];
}

public static class BladeFanLimits
{
    public const int MinimumRpm = 2000;
    /// <summary>
    /// Highest speed the shared fan-target report can carry. The official middleware
    /// writes the same rpm/100 value and its own Performance and Hyperboost modes run
    /// up to 5400 RPM, so this is the ceiling the firmware accepts rather than a guess.
    /// </summary>
    public const int MaximumRpm = 5400;
    public const int StepRpm = 100;

    public static void ValidateTargetRpm(int rpm)
    {
        ValidateTargetRpm(rpm, MaximumRpm);
    }

    public static void ValidateTargetRpm(int rpm, int maximumRpm)
    {
        if (rpm is < MinimumRpm || rpm > maximumRpm || (rpm - MinimumRpm) % StepRpm != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rpm),
                $"Fan speed must be between {MinimumRpm} and {maximumRpm} RPM in increments of {StepRpm} RPM.");
        }
    }
}
