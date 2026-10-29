using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

/// <summary>
/// CPU Boost is bit 0 of the Power Mode Control byte, the same report Max Fan and Local
/// Dimming use. These tests pin the bit, the read-modify-write, and the model admission
/// taken from the middleware's hasCpuBoostEnabled flag.
/// </summary>
public sealed class BladeCpuPowerBoostTests
{
    [Fact]
    public void CpuBoostOwnsBitZeroWithoutOverlappingTheOtherControls()
    {
        Assert.Equal(0x01, BladePowerModeProtocol.CpuPowerBoostBit);
        Assert.Equal(0x00, BladePowerModeProtocol.CpuPowerBoostBit & BladePowerModeProtocol.MaxFanBit);
        Assert.Equal(0x00, BladePowerModeProtocol.CpuPowerBoostBit & BladePowerModeProtocol.OneTimeFullChargeBit);
        Assert.Equal(0x00, BladePowerModeProtocol.CpuPowerBoostBit & BladePowerModeProtocol.LocalDimmingBit);
    }

    [Theory]
    [InlineData(0x00, true, 0x01)]
    [InlineData(0x00, false, 0x00)]
    [InlineData(0x0E, true, 0x0F)]
    [InlineData(0x0F, false, 0x0E)]
    public void ApplyingTheBitPreservesEveryOtherSetting(byte original, bool enabled, byte expected) =>
        Assert.Equal(expected, BladePowerModeProtocol.ApplyCpuPowerBoost(original, enabled));

    [Fact]
    public void SetRequestIsThePowerModeMaskWriteWithTheSameCommandTriple()
    {
        var request = BladePowerModeProtocol.CreateSetCpuPowerBoostRequest(
            enabled: true,
            existingPowerModeMask: 0x0A);

        Assert.Equal(0x01, request[RazerFeatureReport.DataSizeOffset]);
        Assert.Equal(0x07, request[RazerFeatureReport.CommandClassOffset]);
        Assert.Equal(0x0F, request[RazerFeatureReport.CommandIdOffset]);
        Assert.Equal(0x0B, request[RazerFeatureReport.ArgumentsOffset]);
    }

    [Theory]
    [InlineData(0x028C, true)]
    [InlineData(0x029D, true)]
    [InlineData(0x02B6, true)]
    [InlineData(0x029F, false)]
    [InlineData(0x02A0, false)]
    [InlineData(0x02B8, false)]
    [InlineData(0x02C5, false)]
    [InlineData(0x02C6, false)]
    [InlineData(0x02E0, false)]
    public void OnlyModelsPublishingHasCpuBoostEnabledAdmitTheSwitch(ushort productId, bool expected) =>
        Assert.Equal(expected, BladeModelCapabilities.SupportsCpuPowerBoost(productId));
}
