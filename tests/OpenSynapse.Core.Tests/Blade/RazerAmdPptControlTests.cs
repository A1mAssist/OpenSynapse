using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerAmdPptControlTests
{
    [Fact]
    public void Trusted125WBaselineRestoresAfter15WWithoutTheUser100WClamp()
    {
        var current = 125d;
        var writes = new List<uint>();
        var control = New(150, () => (current, null), (watts, enabled) =>
        {
            Assert.Equal(1, enabled); writes.Add(watts); current = watts; return 0;
        });
        Assert.Equal(15, control.Apply(Limit(15)).Pl1Watts);
        Assert.Equal(125, control.RestoreTo(125, 125).Pl1Watts);
        Assert.Equal(new uint[] { 15, 125 }, writes);
        Assert.Equal(100, control.Read().MaximumWatts);
    }

    [Fact]
    public void FirmwareRestoreAlsoPreservesThe125WBaseline()
    {
        var current = 15d;
        var control = New(150, () => (current, null), (watts, _) => { current = watts; return 0; });
        Assert.Equal(125, control.RestoreFirmware().Pl1Watts);
    }

    [Theory]
    [InlineData(125, 100)]
    [InlineData(1000, 100)]
    [InlineData(-100, 8)]
    public void NormalWritesRetainTheOriginalSafetyEnvelope(double input, double expected)
    {
        var current = 0d;
        var control = New(150, () => (current, null), (watts, _) => { current = watts; return 0; });
        Assert.Equal(expected, control.Apply(Limit(input)).Pl1Watts);
    }

    [Theory]
    [InlineData(125, 125, 100)]
    [InlineData(151, 151, 150)]
    [InlineData(125, 151, 150)]
    [InlineData(251, 251, 300)]
    [InlineData(0, 0, 150)]
    [InlineData(double.NaN, 125, 150)]
    [InlineData(125, double.PositiveInfinity, 150)]
    public void InvalidOrOutOfCapabilityBaselinesAreRejectedWithoutWriting(double pl1, double pl2, double capability)
    {
        var writes = 0;
        var control = New(capability, () => (125d, null), (_, _) => { writes++; return 0; });
        Assert.Equal(CpuPowerLimitAvailability.Failed, control.RestoreTo(pl1, pl2).Availability);
        Assert.Equal(0, writes);
    }

    [Fact]
    public void DriverClampingA125WRestoreTo100WIsNotReportedAsSuccess()
    {
        var control = New(150, () => (100d, null), (_, _) => 0);
        Assert.Equal(CpuPowerLimitAvailability.Failed, control.RestoreTo(125, 125).Availability);
    }

    [Fact]
    public void DriverFailureAndReadbackFailureRemainFailures()
    {
        Assert.Equal(CpuPowerLimitAvailability.Failed, New(150, () => (125d, null), (_, _) => 5).RestoreFirmware().Availability);
        Assert.Equal(CpuPowerLimitAvailability.Failed, New(150, () => (null, "read failed"), (_, _) => 0).RestoreFirmware().Availability);
    }

    [Theory]
    [InlineData(125f, 150d, true)]
    [InlineData(8f, 150d, true)]
    [InlineData(1250f, 150d, false)]
    [InlineData(1500f, 150d, false)]
    [InlineData(151f, 150d, false)]
    [InlineData(0f, 150d, false)]
    [InlineData(float.NaN, 150d, false)]
    [InlineData(float.PositiveInfinity, 150d, false)]
    public void RawWattReadingsAreValidatedWithoutGuessingAnotherUnit(float raw, double ceiling, bool expected)
    {
        Assert.Equal(expected, RazerAmdPowerLimitController.TryDecodeWatts(raw, ceiling, out var watts, out var error));
        if (expected)
        {
            Assert.Equal((double)raw, watts);
            Assert.Null(error);
        }
        else
        {
            Assert.Equal(0, watts);
            Assert.False(string.IsNullOrWhiteSpace(error));
        }
    }

    private static CpuPowerLimit Limit(double watts) => new(watts, watts, 0.001, 0.25);
    private static RazerAmdPptControl New(double maximum, Func<(double? Watts, string? Error)> read, Func<uint, byte, int> write) =>
        new(Limit(125), 8, maximum, read, write, _ => { });
}
