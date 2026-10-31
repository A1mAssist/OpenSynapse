using System;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RyzenSmuTests
{
    [Theory]
    [InlineData(1UL)]   // Renoir
    [InlineData(13UL)]  // Cezanne
    [InlineData(30UL)]  // HawkPoint
    [InlineData(31UL)]  // StrixPoint
    public void MobileFamiliesUseTheMp1PowerMessages(ulong codeName)
    {
        var commands = RyzenSmuCpuPowerLimit.CommandsFor(codeName);

        Assert.NotNull(commands);
        Assert.Equal(0x14u, commands.Value.Stapm);
        Assert.Equal(0x15u, commands.Value.Fast);
        Assert.Equal(0x16u, commands.Value.Slow);
    }

    [Theory]
    [InlineData(16UL)]  // Raphael
    [InlineData(28UL)]  // DragonRange
    [InlineData(41UL)]  // StoneyRidge
    public void DesktopAndLegacyFamiliesAreRejected(ulong codeName) =>
        Assert.Null(RyzenSmuCpuPowerLimit.CommandsFor(codeName));

    [Fact]
    public void ThePowerTableMapsFastAndSlowPptToTheTwoWalls()
    {
        var table = new ulong[64];
        table[1] = Pack(65f, 0);
        table[2] = Pack(45f, 0);

        Assert.True(RyzenSmuCpuPowerLimit.TryDecode(table, out var limit, out var error));
        Assert.Null(error);
        Assert.Equal(45, limit.Pl1Watts);
        Assert.Equal(65, limit.Pl2Watts);
    }

    [Fact]
    public void AReversedTableStillYieldsOrderedWalls()
    {
        var table = new ulong[64];
        table[1] = Pack(40f, 0);
        table[2] = Pack(55f, 0);

        Assert.True(RyzenSmuCpuPowerLimit.TryDecode(table, out var limit, out _));
        Assert.Equal(40, limit.Pl1Watts);
        Assert.Equal(55, limit.Pl2Watts);
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(45f, 0f)]
    [InlineData(0f, 65f)]
    [InlineData(0.06f, 0.071f)]
    [InlineData(900f, 900f)]
    public void ImplausibleTablesAreRejected(float slow, float fast)
    {
        var table = new ulong[64];
        table[1] = Pack(fast, 0);
        table[2] = Pack(slow, 0);

        Assert.False(RyzenSmuCpuPowerLimit.TryDecode(table, out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void AShortTableIsRejected()
    {
        Assert.False(RyzenSmuCpuPowerLimit.TryDecode(new ulong[2], out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void TheClampStillCapsAmdWrites()
    {
        var limit = new CpuPowerLimit(150, 180, 0, 0).Clamp(CpuPowerLimit.MaximumWatts);

        Assert.Equal(CpuPowerLimit.MaximumWatts, limit.Pl1Watts);
        Assert.Equal(CpuPowerLimit.MaximumWatts, limit.Pl2Watts);
    }

    [Theory]
    [InlineData(15, 15000u)]
    [InlineData(45, 45000u)]
    [InlineData(95, 95000u)]
    public void WattsBecomeMilliwattsForTheMailbox(double watts, uint milliwatts) =>
        Assert.Equal(milliwatts, RyzenSmuCpuPowerLimit.ToMilliwatts(watts));

    private static ulong Pack(float low, float high) =>
        (uint)BitConverter.SingleToInt32Bits(low) |
        ((ulong)(uint)BitConverter.SingleToInt32Bits(high) << 32);
}
