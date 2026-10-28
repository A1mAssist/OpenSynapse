using System.Buffers.Binary;
using OpenSynapse.Windows.Lifecycle;
using Xunit;

namespace OpenSynapse.Core.Tests;

/// <summary>
/// The NVAPI request block must stay byte-identical to the one Razer's bladeNative.dll
/// submits: version 0x11C94, one pstate, one clock, the clock domain and a kHz delta.
/// </summary>
public sealed class GpuClockOffsetTests
{
    [Fact]
    public void CoreRequestMatchesTheRazerBlock()
    {
        var parameters = NvApiGpuClockOffset.CreateParameters(clockDomain: 0, deltaMegahertz: 150);

        Assert.Equal(0x1CF8, parameters.Length);
        Assert.Equal(0x11C94u, ReadUInt32(parameters, 0x00));
        Assert.Equal(1u, ReadUInt32(parameters, 0x08));
        Assert.Equal(1u, ReadUInt32(parameters, 0x0C));
        Assert.Equal(0u, ReadUInt32(parameters, 0x14));
        Assert.Equal(0u, ReadUInt32(parameters, 0x1C));
        Assert.Equal(150_000u, ReadUInt32(parameters, 0x28));
    }

    [Fact]
    public void MemoryRequestUsesDomainFour()
    {
        var parameters = NvApiGpuClockOffset.CreateParameters(clockDomain: 4, deltaMegahertz: 1000);

        Assert.Equal(4u, ReadUInt32(parameters, 0x1C));
        Assert.Equal(1_000_000u, ReadUInt32(parameters, 0x28));
    }

    [Theory]
    [InlineData(-900, -500)]
    [InlineData(-1, -1)]
    [InlineData(0, 0)]
    [InlineData(150, 150)]
    [InlineData(250, 200)]
    public void CoreOffsetsAreClampedToTheLaptopEnvelope(int input, int expected) =>
        Assert.Equal(expected, NvApiGpuClockOffset.ClampCore(input));

    [Theory]
    [InlineData(-900, -500)]
    [InlineData(0, 0)]
    [InlineData(2000, 2000)]
    [InlineData(2500, 2000)]
    public void MemoryOffsetsAreClampedToTheSupportedEnvelope(int input, int expected) =>
        Assert.Equal(expected, NvApiGpuClockOffset.ClampMemory(input));

    [Fact]
    public void ClockOffsetOperationsRouteToTheirActions()
    {
        var status = new DedicatedGpuModeServiceRequest(
            DedicatedGpuModeServiceProtocol.Version,
            DedicatedGpuModeServiceProtocol.GpuClockOffsetStatusOperation,
            Enabled: null);
        Assert.Equal(
            DedicatedGpuModeServiceAction.GpuClockOffsetStatus,
            DedicatedGpuModeServiceRequestParser.Parse(status));

        var set = new DedicatedGpuModeServiceRequest(
            DedicatedGpuModeServiceProtocol.Version,
            DedicatedGpuModeServiceProtocol.SetGpuClockOffsetOperation,
            Enabled: null,
            CoreOffsetMhz: 100,
            MemoryOffsetMhz: 500);
        Assert.Equal(
            DedicatedGpuModeServiceAction.SetGpuClockOffset,
            DedicatedGpuModeServiceRequestParser.Parse(set));
    }

    [Fact]
    public void AClockOffsetWriteWithoutBothValuesIsRejected()
    {
        var request = new DedicatedGpuModeServiceRequest(
            DedicatedGpuModeServiceProtocol.Version,
            DedicatedGpuModeServiceProtocol.SetGpuClockOffsetOperation,
            Enabled: null,
            CoreOffsetMhz: 100,
            MemoryOffsetMhz: null);

        Assert.Equal(
            DedicatedGpuModeServiceAction.Unsupported,
            DedicatedGpuModeServiceRequestParser.Parse(request));
    }

    private static uint ReadUInt32(byte[] buffer, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset, sizeof(uint)));
}
