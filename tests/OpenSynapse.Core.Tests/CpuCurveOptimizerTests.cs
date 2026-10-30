using OpenSynapse.Core.Devices;
using Xunit;

namespace OpenSynapse.Core.Tests;

/// <summary>
/// The SMU takes the offset against a 0x100000 base, so a negative step count is a
/// subtraction. These values are the ones the card is allowed to send.
/// </summary>
public sealed class CpuCurveOptimizerTests
{
    [Theory]
    [InlineData(0, 0x100000)]
    [InlineData(-20, 0x0FFFEC)]
    [InlineData(-30, 0x0FFFE2)]
    public void TheOffsetIsEncodedAgainstTheBase(int steps, uint expected) =>
        Assert.Equal(expected, new CpuCurveOptimizer(steps).ToSmuArgument());

    [Theory]
    [InlineData(5, 0)]
    [InlineData(-99, -30)]
    public void ValuesOutsideTheSliderAreClamped(int steps, int expected) =>
        Assert.Equal(expected, new CpuCurveOptimizer(steps).Clamp().Steps);
}
