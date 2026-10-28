using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class BladeBoostProtocolTests
{
    [Theory]
    [InlineData(0x00, BladeCpuBoostMode.Low)]
    [InlineData(0x02, BladeCpuBoostMode.High)]
    public void CpuBoostParsesTheProfileOneEcho(byte value, BladeCpuBoostMode expected)
    {
        var response = Response(BladeThermalProtocol.ProfileId, BladeBoostProtocol.CpuCluster, value);

        Assert.Equal(expected, BladeBoostProtocol.ParseCpu(response, BladeBoostProtocol.ProfileId));
    }

    [Fact]
    public void CpuBoostParsesAProfileZeroEcho()
    {
        var response = Response(0x00, BladeBoostProtocol.CpuCluster, 0x02);

        Assert.Equal(
            BladeCpuBoostMode.High,
            BladeBoostProtocol.ParseCpu(response, BladeBoostProtocol.ProfileId));
    }

    [Fact]
    public void CpuBoostRejectsAnUnknownProfileEcho()
    {
        var response = Response(0x05, BladeBoostProtocol.CpuCluster, 0x02);

        var error = Assert.Throws<InvalidOperationException>(
            () => BladeBoostProtocol.ParseCpu(response, BladeBoostProtocol.ProfileId));
        Assert.Contains("CPU", error.Message);
    }

    [Fact]
    public void CpuBoostRejectsTheGpuCluster()
    {
        var response = Response(BladeThermalProtocol.ProfileId, BladeBoostProtocol.GpuCluster, 0x01);

        Assert.Throws<InvalidOperationException>(
            () => BladeBoostProtocol.ParseCpu(response, BladeBoostProtocol.ProfileId));
    }

    [Fact]
    public void RequestsCarryTheMiddlewareProfile()
    {
        Assert.Equal(BladeBoostProtocol.ProfileId, BladeBoostProtocol.CreateGetArguments(BladeBoostProtocol.CpuCluster)[0]);
        Assert.Equal(BladeBoostProtocol.ProfileId, BladeBoostProtocol.CreateSetArguments(BladeBoostProtocol.GpuCluster, 0x02)[0]);
    }

    private static byte[] Response(byte profileId, byte cluster, byte value)
    {
        var report = new byte[RazerFeatureReport.Length];
        report[6] = 3;
        report[9] = profileId;
        report[10] = cluster;
        report[11] = value;
        return report;
    }
}
