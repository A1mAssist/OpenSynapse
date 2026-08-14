namespace OpenSynapse.Core.Devices;

public enum BladeFanCurveTemperatureMode
{
    Cpu,
    Gpu,
    Both,
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
    public const int MaximumPointFanSpeedRpm = 5000;
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
            _ => throw new InvalidOperationException("未知的风扇曲线温度模式。"),
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
            throw new ArgumentException($"风扇曲线必须包含 1 到 {MaximumPoints} 个节点。", parameterName);
        }

        var copy = points.ToArray();
        for (var index = 0; index < copy.Length; index++)
        {
            var point = copy[index];
            if (point.TemperatureCelsius <= minimumTemperature || point.TemperatureCelsius > 120)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    $"温度节点必须大于低温基线 {minimumTemperature} C，且不超过 120 C。");
            }
            ValidatePointSpeed(point.CpuFanSpeedRpm, parameterName);
            ValidatePointSpeed(point.GpuFanSpeedRpm, parameterName);
            if (index > 0 && point.TemperatureCelsius <= copy[index - 1].TemperatureCelsius)
            {
                throw new ArgumentException("温度节点必须严格递增。", parameterName);
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
                $"曲线节点风扇转速必须为 {MinimumPointFanSpeedRpm}..{MaximumPointFanSpeedRpm} RPM。");
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
        new($"风扇曲线缺少有效的 {sensor} 温度。");
}

public static class BladeFanLimits
{
    public const int MinimumRpm = 2000;
    public const int MaximumRpm = 5000;
    public const int StepRpm = 100;

    public static void ValidateTargetRpm(int rpm)
    {
        if (rpm is < MinimumRpm or > MaximumRpm || (rpm - MinimumRpm) % StepRpm != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rpm),
                $"风扇转速必须为 {MinimumRpm}..{MaximumRpm} RPM，步进 {StepRpm} RPM。");
        }
    }
}
