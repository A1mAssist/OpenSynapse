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

    [Theory]
    [InlineData(1UL, 0x55u)]   // Renoir
    [InlineData(22UL, 0x55u)]  // Lucienne
    [InlineData(10UL, 0x4Cu)]  // Rembrandt
    [InlineData(13UL, 0x4Cu)]  // Cezanne
    [InlineData(23UL, 0x4Cu)]  // Phoenix
    [InlineData(24UL, 0x4Cu)]  // Phoenix 2
    [InlineData(30UL, 0x4Cu)]  // Hawk Point
    [InlineData(31UL, 0x4Cu)]  // Strix Point
    public void OnlyDocumentedFamiliesGetACurveCommand(ulong codeName, uint message) =>
        Assert.Equal(message, OpenSynapse.Windows.Devices.RyzenSmuCurveOptimizer.MessageFor(codeName));

    [Theory]
    [InlineData(25UL)]  // Mendocino: no Curve Optimizer evidence
    [InlineData(32UL)]  // Strix Halo: no Curve Optimizer evidence
    [InlineData(33UL)]  // Krackan Point: no Curve Optimizer evidence
    [InlineData(34UL)]  // Krackan Point 2: no Curve Optimizer evidence
    [InlineData(99UL)]
    public void UnknownFamiliesAreRejected(ulong codeName) =>
        Assert.Null(OpenSynapse.Windows.Devices.RyzenSmuCurveOptimizer.MessageFor(codeName));
}
