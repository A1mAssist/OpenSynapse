using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using Xunit;

namespace OpenSynapse.Core.Tests;

/// <summary>
/// The battery optimizer is the 2022+ presentation of the charge limiter. These
/// tests pin the bundle flag set, the one-byte report layout, and the models that
/// must stay closed because their bundle never declares the panel.
/// </summary>
public sealed class BladeBatteryOptimizerTests
{
    [Theory]
    [InlineData(0x028A)]
    [InlineData(0x028B)]
    [InlineData(0x028C)]
    [InlineData(0x029D)]
    [InlineData(0x029E)]
    [InlineData(0x029F)]
    [InlineData(0x02A0)]
    [InlineData(0x02B6)]
    [InlineData(0x02B7)]
    [InlineData(0x02B8)]
    [InlineData(0x02C5)]
    [InlineData(0x02C6)]
    [InlineData(0x02C7)]
    [InlineData(0x02E0)]
    [InlineData(0x02E1)]
    public void EveryModelWithTheBundleFlagAlsoHasTheChargeLimiterReports(ushort productId)
    {
        Assert.True(BladeBatteryOptimizerCapabilities.IsSupported(productId));

        var manifest = RazerDeviceRegistry.BuiltIn.Find(0x1532, productId);
        Assert.NotNull(manifest);
        foreach (var capability in new[]
                 {
                     CapabilityIds.ChargeLimitGet,
                     CapabilityIds.ChargeLimitSet,
                     CapabilityIds.PowerModeGet,
                     CapabilityIds.PowerModeSet,
                 })
        {
            Assert.True(
                manifest!.Capabilities.ContainsKey(capability),
                $"{productId:X4} is missing {capability}.");
        }
    }

    [Theory]
    [InlineData(0x0233)]
    [InlineData(0x0245)]
    [InlineData(0x0255)]
    public void ModelsWithoutTheBundleFlagDoNotGetTheOptimizerCard(ushort productId)
    {
        Assert.False(BladeBatteryOptimizerCapabilities.IsSupported(productId));
    }

    [Theory]
    [InlineData(0x02B6, true)]
    [InlineData(0x02B7, true)]
    [InlineData(0x02B8, true)]
    [InlineData(0x02C5, true)]
    [InlineData(0x02C6, true)]
    [InlineData(0x02C7, true)]
    [InlineData(0x02E0, true)]
    [InlineData(0x02E1, true)]
    [InlineData(0x028A, false)]
    [InlineData(0x02A0, false)]
    public void OnlyTheDeclaringBundlesExposeTheOneTimeOverride(
        ushort productId,
        bool expected) =>
        Assert.Equal(expected, BladeBatteryOptimizerCapabilities.SupportsChargingOverride(productId));

    [Theory]
    [InlineData(0x02E0, true)]
    [InlineData(0x02E1, true)]
    [InlineData(0x02C6, false)]
    public void The2026BundlesReadTheOptimizerFromFirmware(
        ushort productId,
        bool expected) =>
        Assert.Equal(expected, BladeBatteryOptimizerCapabilities.UsesFirmwareState(productId));

    [Theory]
    [InlineData(0x02E0)]
    [InlineData(0x02C6)]
    public void TheOptimizerUsesTheDeclaredChargeLimiterReports(ushort productId)
    {
        var manifest = RazerDeviceRegistry.BuiltIn.Find(0x1532, productId);
        Assert.NotNull(manifest);

        var get = manifest!.GetRequiredCapability(CapabilityIds.ChargeLimitGet);
        Assert.Equal((byte)0x07, get.CommandClass);
        Assert.Equal((byte)0x92, get.CommandId);
        Assert.Equal((byte)0x01, get.MaximumDataSize);

        var set = manifest.GetRequiredCapability(CapabilityIds.ChargeLimitSet);
        Assert.Equal((byte)0x07, set.CommandClass);
        Assert.Equal((byte)0x12, set.CommandId);
        Assert.Equal((byte)0x01, set.MaximumDataSize);

        Assert.Equal(
            (byte)0x8F,
            manifest.GetRequiredCapability(CapabilityIds.PowerModeGet).CommandId);
        Assert.Equal(
            (byte)0x0F,
            manifest.GetRequiredCapability(CapabilityIds.PowerModeSet).CommandId);
    }

    [Theory]
    [InlineData(true, 50, 0xB2)]
    [InlineData(true, 55, 0xB7)]
    [InlineData(true, 60, 0xBC)]
    [InlineData(true, 65, 0xC1)]
    [InlineData(true, 70, 0xC6)]
    [InlineData(true, 75, 0xCB)]
    [InlineData(true, 80, 0xD0)]
    [InlineData(false, 50, 0x32)]
    [InlineData(false, 80, 0x50)]
    [InlineData(false, 75, 0x4B)]
    public void TheReportPacksEnabledIntoTheTopBit(
        bool enabled,
        int thresholdPercent,
        byte expected) =>
        Assert.Equal(expected, BladeBatteryOptimizerProtocol.Encode(enabled, thresholdPercent));

    [Theory]
    [InlineData((byte)0xD0, true, 80)]
    [InlineData((byte)0x50, false, 80)]
    [InlineData((byte)0x4B, false, 75)]
    [InlineData((byte)0xB2, true, 50)]
    public void TheReportDecodesTheFlagAndThreshold(
        byte raw,
        bool expectedEnabled,
        int expectedThreshold)
    {
        var state = BladeBatteryOptimizerProtocol.Decode(raw);

        Assert.Equal(expectedEnabled, state.IsEnabled);
        Assert.Equal(expectedThreshold, state.ThresholdPercent);
        Assert.False(state.OneTimeOverrideEnabled);
    }

    [Theory]
    [InlineData(49)]
    [InlineData(51)]
    [InlineData(81)]
    [InlineData(100)]
    public void AThresholdOutsideTheAuditedSetIsRejected(int thresholdPercent) =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BladeBatteryOptimizerProtocol.Encode(true, thresholdPercent));

    [Fact]
    public void ADisabledOptimizerStillCarriesTheFirmwareThreshold()
    {
        var state = BladeBatteryOptimizerProtocol.Decode(0x4B, oneTimeOverrideEnabled: true);

        Assert.False(state.IsEnabled);
        Assert.Equal(75, state.ThresholdPercent);
        Assert.True(state.OneTimeOverrideEnabled);
    }
}
