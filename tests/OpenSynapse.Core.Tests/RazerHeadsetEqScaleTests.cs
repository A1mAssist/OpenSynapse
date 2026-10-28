using OpenSynapse.Core.Devices;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerHeadsetEqScaleTests
{
    [Theory]
    [InlineData(-12.0, 0)]
    [InlineData(-11.0, 0)]
    [InlineData(-9.6, 1)]
    [InlineData(0.0, 5)]
    [InlineData(9.6, 9)]
    [InlineData(12.0, 10)]
    [InlineData(13.0, 10)]
    [InlineData(-13.0, 0)]
    [InlineData(double.NaN, 5)]
    public void MapsDecibelsToTheTenFirmwareSteps(double decibels, byte step) =>
        Assert.Equal(step, RazerHeadsetEqScale.ToStep(decibels));

    [Theory]
    [InlineData(0, -12.0)]
    [InlineData(5, 0.0)]
    [InlineData(10, 12.0)]
    public void MapsFirmwareStepsBackToDecibels(byte step, double decibels) =>
        Assert.Equal(decibels, RazerHeadsetEqScale.ToDecibels(step), 3);

    [Theory]
    [InlineData(-9.7, -9.6)]
    [InlineData(3.0, 2.4)]
    [InlineData(100.0, 12.0)]
    public void SnapsToTheNearestFirmwareStep(double decibels, double snapped) =>
        Assert.Equal(snapped, RazerHeadsetEqScale.Snap(decibels), 3);

    [Fact]
    public void RoundTripsEveryStep()
    {
        for (var step = RazerHeadsetEqScale.MinimumStep; step <= RazerHeadsetEqScale.MaximumStep; step++)
        {
            Assert.Equal(step, RazerHeadsetEqScale.ToStep(RazerHeadsetEqScale.ToDecibels(step)));
        }
    }
}