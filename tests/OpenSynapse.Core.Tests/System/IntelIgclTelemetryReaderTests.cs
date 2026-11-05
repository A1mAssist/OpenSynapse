using OpenSynapse.Windows.Sensors;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class IntelIgclTelemetryReaderTests
{
    [Fact]
    public void NativeStructsMatchIgclX64Layout()
    {
        Assert.Equal((36, 56, 24), IntelIgclTelemetryReader.NativeStructSizes);
    }

    [Fact]
    public void ReaderConstructionAndReadNeverThrow()
    {
        using var reader = new IntelIgclTelemetryReader();
        _ = reader.Read();
    }

    [Fact]
    public void CalculatePowerWattsUsesMicrojouleAndMicrosecondDeltas()
    {
        var watts = IntelIgclTelemetryReader.CalculatePowerWatts(
            1_000_000, 2_000_000, 3_500_000, 3_000_000);

        Assert.Equal(2.5, watts);
    }

    [Fact]
    public void CalculatePowerWattsRejectsCounterResetAndZeroTimeDelta()
    {
        Assert.Null(IntelIgclTelemetryReader.CalculatePowerWatts(
            2_000_000, 2_000_000, 1_000_000, 3_000_000));
        Assert.Null(IntelIgclTelemetryReader.CalculatePowerWatts(
            1_000_000, 2_000_000, 2_000_000, 2_000_000));
        Assert.Null(IntelIgclTelemetryReader.CalculatePowerWatts(
            null, null, 2_000_000, 3_000_000));
    }
}
