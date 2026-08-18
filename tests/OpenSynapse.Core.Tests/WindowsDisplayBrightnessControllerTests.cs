using OpenSynapse.Windows.Displays;

namespace OpenSynapse.Core.Tests;

public sealed class WindowsDisplayBrightnessControllerTests
{
    [Theory]
    [InlineData(0.5, true, 0.6)]
    [InlineData(0.5, false, 0.4)]
    [InlineData(0.95, true, 1.0)]
    [InlineData(0.05, false, 0.0)]
    public void StepsAndClampsSystemBrightness(
        double current,
        bool increase,
        double expected) =>
        Assert.Equal(
            expected,
            WindowsDisplayBrightnessController.CalculateStep(current, increase),
            precision: 10);
}
