using OpenSynapse.Core.Devices;

namespace OpenSynapse.Core.Tests;

public sealed class BladeFanCurveTests
{
    [Fact]
    public void MatchesProduct710CpuInterpolationAndProtocolFlooring()
    {
        var curve = CreateCurve(BladeFanCurveTemperatureMode.Cpu);

        Assert.Equal(new BladeFanTargets(2300, 2300), curve.Evaluate(40, null));
        Assert.Equal(new BladeFanTargets(2200, 2100), curve.Evaluate(50, null));
        Assert.Equal(new BladeFanTargets(2100, 1900), curve.Evaluate(60, null));
        Assert.Equal(new BladeFanTargets(2400, 2200), curve.Evaluate(66, null));
        Assert.Equal(new BladeFanTargets(3300, 3100), curve.Evaluate(90, null));
    }

    [Fact]
    public void BothModeSelectsWholeResultWithHigherCpuFanOutput()
    {
        var curve = CreateCurve(BladeFanCurveTemperatureMode.Both);

        var result = curve.Evaluate(60, 72);

        Assert.Equal(new BladeFanTargets(3900, 3700), result);
    }

    [Fact]
    public void RequiresOnlySelectedTemperatureSource()
    {
        Assert.Equal(
            new BladeFanTargets(2100, 1900),
            CreateCurve(BladeFanCurveTemperatureMode.Cpu).Evaluate(60, null));
        Assert.Equal(
            new BladeFanTargets(3900, 3700),
            CreateCurve(BladeFanCurveTemperatureMode.Gpu).Evaluate(null, 72));
        Assert.Equal(
            new BladeFanTargets(2100, 1900),
            CreateCurve(BladeFanCurveTemperatureMode.Cpu).Evaluate(60, double.NaN));
        Assert.Throws<InvalidOperationException>(
            () => CreateCurve(BladeFanCurveTemperatureMode.Both).Evaluate(60, null));
    }

    [Fact]
    public void RejectsMutableInvalidOrUnorderedInput()
    {
        var cpu = new[] { new BladeFanCurvePoint(60, 2150, 1950) };
        var curve = new BladeFanCurve(
            BladeFanCurveTemperatureMode.Cpu,
            cpu,
            [new(54, 2150, 1950)]);
        cpu[0] = new BladeFanCurvePoint(20, 5000, 5000);
        Assert.Equal(60, curve.CpuPoints[0].TemperatureCelsius);

        Assert.Throws<ArgumentException>(() => new BladeFanCurve(
            BladeFanCurveTemperatureMode.Cpu,
            [new(60, 2150, 1950), new(60, 2300, 2100)],
            [new(54, 2150, 1950)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BladeFanCurve(
            BladeFanCurveTemperatureMode.Cpu,
            [new(39, 2150, 1950)],
            [new(54, 2150, 1950)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BladeFanCurve(
            BladeFanCurveTemperatureMode.Cpu,
            [new(60, 1800, 1950)],
            [new(54, 2150, 1950)]));
    }

    internal static BladeFanCurve CreateCurve(BladeFanCurveTemperatureMode mode) => new(
        mode,
        [
            new(60, 2150, 1950),
            new(64, 2300, 2100),
            new(68, 2500, 2300),
            new(80, 3300, 3100),
        ],
        [
            new(54, 2150, 1950),
            new(60, 2500, 2300),
            new(66, 3000, 2800),
            new(72, 3900, 3700),
        ]);
}
