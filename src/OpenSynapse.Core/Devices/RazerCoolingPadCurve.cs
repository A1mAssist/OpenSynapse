namespace OpenSynapse.Core.Devices;

public enum RazerCoolingPadCurveMode
{
    Quiet,
    Balanced,
    Performance,
}

public enum RazerCoolingPadTemperatureSource
{
    Cpu,
    Gpu,
    CpuAndGpu,
}

/// <summary>
/// The 0F43 bundle's smart-curve tables. The middleware does not interpolate: it
/// picks the next strictly-higher breakpoint and returns that point's speed.
/// At or above the last breakpoint it returns 0; stopping the fan while hot is
/// unsafe, so this implementation clamps to the last point instead.
/// </summary>
public static class RazerCoolingPadCurve
{
    public readonly record struct Point(int Celsius, int Rpm);

    private static readonly Point[] GpuQuiet =
        [new(30, 500), new(40, 500), new(75, 1000), new(80, 1600), new(90, 1600)];
    private static readonly Point[] GpuBalanced =
        [new(30, 1000), new(80, 2200), new(90, 2200)];
    private static readonly Point[] GpuPerformance =
        [new(30, 1000), new(45, 2000), new(70, 2700), new(90, 2700)];

    private static readonly Point[] CpuQuiet =
        [new(40, 500), new(50, 500), new(85, 1000), new(90, 1600), new(100, 1600)];
    private static readonly Point[] CpuBalanced =
        [new(40, 1000), new(90, 2200), new(100, 2200)];
    private static readonly Point[] CpuPerformance =
        [new(40, 1000), new(55, 2000), new(80, 2700), new(100, 2700)];

    public static ReadOnlySpan<Point> GpuPoints(RazerCoolingPadCurveMode mode) => mode switch
    {
        RazerCoolingPadCurveMode.Quiet => GpuQuiet,
        RazerCoolingPadCurveMode.Balanced => GpuBalanced,
        RazerCoolingPadCurveMode.Performance => GpuPerformance,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    public static ReadOnlySpan<Point> CpuPoints(RazerCoolingPadCurveMode mode) => mode switch
    {
        RazerCoolingPadCurveMode.Quiet => CpuQuiet,
        RazerCoolingPadCurveMode.Balanced => CpuBalanced,
        RazerCoolingPadCurveMode.Performance => CpuPerformance,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    /// <summary>Next strictly-higher breakpoint, clamped to the last point.</summary>
    public static int Step(ReadOnlySpan<Point> points, double celsius)
    {
        if (points.IsEmpty)
        {
            throw new ArgumentException("A curve needs at least one point.", nameof(points));
        }

        foreach (var point in points)
        {
            if (point.Celsius > celsius)
            {
                return point.Rpm;
            }
        }

        return points[^1].Rpm;
    }

    /// <summary>
    /// Resolves a target speed. CPU+GPU uses the max of both step lookups and
    /// requires both temperatures, matching the middleware's no-fallback behavior.
    /// </summary>
    public static int? TargetRpm(
        RazerCoolingPadCurveMode mode,
        RazerCoolingPadTemperatureSource source,
        double? cpuCelsius,
        double? gpuCelsius) => source switch
        {
            RazerCoolingPadTemperatureSource.Cpu =>
                cpuCelsius is { } cpu ? Step(CpuPoints(mode), cpu) : null,
            RazerCoolingPadTemperatureSource.Gpu =>
                gpuCelsius is { } gpu ? Step(GpuPoints(mode), gpu) : null,
            RazerCoolingPadTemperatureSource.CpuAndGpu when
                cpuCelsius is { } cpu && gpuCelsius is { } gpu =>
                Math.Max(Step(CpuPoints(mode), cpu), Step(GpuPoints(mode), gpu)),
            _ => null,
        };
}
