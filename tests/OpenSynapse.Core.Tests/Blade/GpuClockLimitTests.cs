using System.Buffers.Binary;
using OpenSynapse.Windows.Lifecycle;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class GpuClockLimitTests
{
    [Fact]
    public void ClockLimitRequestsRouteToTheirOwnActions()
    {
        var status = new DedicatedGpuModeServiceRequest(
            DedicatedGpuModeServiceProtocol.Version,
            DedicatedGpuModeServiceProtocol.GpuClockLimitStatusOperation,
            Enabled: null);
        var set = new DedicatedGpuModeServiceRequest(
            DedicatedGpuModeServiceProtocol.Version,
            DedicatedGpuModeServiceProtocol.SetGpuClockLimitOperation,
            Enabled: null,
            ClockLimitMhz: 2400);

        Assert.Equal(
            DedicatedGpuModeServiceAction.GpuClockLimitStatus,
            DedicatedGpuModeServiceRequestParser.Parse(status));
        Assert.Equal(
            DedicatedGpuModeServiceAction.SetGpuClockLimit,
            DedicatedGpuModeServiceRequestParser.Parse(set));
    }

    [Fact]
    public void ASetClockLimitRequestNeedsAValue()
    {
        var request = new DedicatedGpuModeServiceRequest(
            DedicatedGpuModeServiceProtocol.Version,
            DedicatedGpuModeServiceProtocol.SetGpuClockLimitOperation,
            Enabled: null);

        Assert.Equal(
            DedicatedGpuModeServiceAction.Unsupported,
            DedicatedGpuModeServiceRequestParser.Parse(request));
    }

    [Theory]
    [InlineData(100, 400)]
    [InlineData(400, 400)]
    [InlineData(1800, 1800)]
    [InlineData(3000, 3000)]
    [InlineData(5000, 3000)]
    public void ClampStaysInsideTheDriverEnvelope(int requested, int expected) =>
        Assert.Equal(expected, NvApiGpuClockLimit.Clamp(requested));

    [Fact]
    public void TheMaximumResetsAndLowerValuesSetACeiling()
    {
        foreach (var megahertz in new[] { 3000, 5000 })
        {
            var reset = NvApiGpuClockLimit.BuildArguments(megahertz);
            Assert.Single(reset);
            Assert.Equal("-rgc", reset[0]);
        }

        var ceiling = NvApiGpuClockLimit.BuildArguments(1800);
        Assert.Equal(2, ceiling.Length);
        Assert.Equal("-lgc", ceiling[0]);
        Assert.Equal("0,1800", ceiling[1]);

        var clamped = NvApiGpuClockLimit.BuildArguments(100);
        Assert.Equal(2, clamped.Length);
        Assert.Equal("-lgc", clamped[0]);
        Assert.Equal("0,400", clamped[1]);
    }

    [Fact]
    public void TheRequestBlockMatchesThePrivateBoostLockLayout()
    {
        var parameters = NvApiGpuClockLimit.CreateParameters();

        Assert.Equal(780, parameters.Length);
        Assert.Equal(
            0x2030Cu,
            BinaryPrimitives.ReadUInt32LittleEndian(parameters));
        Assert.Equal(
            0u,
            BinaryPrimitives.ReadUInt32LittleEndian(parameters.AsSpan(8, 4)));
    }
}
