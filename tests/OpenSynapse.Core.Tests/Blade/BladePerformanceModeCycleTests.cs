using OpenSynapse.Core.Devices;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class BladePerformanceModeCycleTests
{
    private static readonly BladePerformanceMode[] Modes =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.Performance,
        BladePerformanceMode.Custom,
        BladePerformanceMode.Silent,
        BladePerformanceMode.Hyperboost,
    ];

    [Fact]
    public void SkipsExcludedModesAndWraps()
    {
        HashSet<BladePerformanceMode> included =
            [BladePerformanceMode.Balanced, BladePerformanceMode.Silent];

        Assert.Equal(
            BladePerformanceMode.Silent,
            BladePerformanceModeCycle.GetNext(BladePerformanceMode.Balanced, Modes, included));
        Assert.Equal(
            BladePerformanceMode.Balanced,
            BladePerformanceModeCycle.GetNext(BladePerformanceMode.Silent, Modes, included));
    }

    [Fact]
    public void RejectsAnEmptyCycle() => Assert.Throws<ArgumentException>(() =>
        BladePerformanceModeCycle.GetNext(
            BladePerformanceMode.Balanced,
            Modes,
            new HashSet<BladePerformanceMode>()));

    [Fact]
    public void RefreshRateCycleSkipsExcludedRatesAndWraps()
    {
        int[] rates = [60, 120, 240];
        HashSet<int> included = [60, 240];

        Assert.Equal(240, BladePerformanceModeCycle.GetNext(60, rates, included));
        Assert.Equal(60, BladePerformanceModeCycle.GetNext(240, rates, included));
    }
}
