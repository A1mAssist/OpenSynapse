using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using Xunit;

namespace OpenSynapse.Core.Tests;

/// <summary>
/// Max Fan sits in the Custom performance card and therefore follows the performance
/// power profile. These tests pin that a per-power-source value survives resolution and
/// that a model without the switch never inherits one.
/// </summary>
public sealed class BladeMaxFanProfileTests
{
    private static DeviceDescriptor Blade(ushort productId) => new(
        $"blade-{productId:X4}",
        "Razer Blade",
        0x1532,
        productId,
        DeviceAccessState.Available,
        DeviceCapabilityState.PendingValidation,
        91,
        1,
        2,
        productId == 0x02C6 ? "blade-710" : "blade-671",
        DeviceCategory.Laptop);

    [Fact]
    public void PerPowerSourceMaxFanValueOverridesTheGlobalOne()
    {
        var device = Blade(0x029F);
        var document = ProfileDocument.CreateDefault();
        document.Global.Blade.MaxFanMode = (byte)BladeMaxFanMode.Disabled;
        document.OnBattery.Blade.MaxFanMode = (byte)BladeMaxFanMode.Enabled;

        Assert.Equal(
            (byte)BladeMaxFanMode.Enabled,
            ProfileResolver.Resolve(document, device, PowerSourceKind.Battery).Blade.MaxFanMode);
        Assert.Equal(
            (byte)BladeMaxFanMode.Disabled,
            ProfileResolver.Resolve(document, device, PowerSourceKind.FullPowerAdapter).Blade.MaxFanMode);
    }

    [Fact]
    public void GlobalMaxFanValueAppliesWhenNoPowerOverrideExists()
    {
        var device = Blade(0x029F);
        var document = ProfileDocument.CreateDefault();
        document.Global.Blade.MaxFanMode = (byte)BladeMaxFanMode.Enabled;

        foreach (var source in new[]
        {
            PowerSourceKind.FullPowerAdapter,
            PowerSourceKind.LowPowerAdapter,
            PowerSourceKind.Battery,
        })
        {
            Assert.Equal(
                (byte)BladeMaxFanMode.Enabled,
                ProfileResolver.Resolve(document, device, source).Blade.MaxFanMode);
        }
    }

    /// <summary>
    /// A stale value written on a Max Fan model must not surface the switch on a model
    /// whose firmware has no such bit, so the capability row stays closed there.
    /// </summary>
    [Fact]
    public void ModelWithoutTheSwitchNeverReportsItAsAvailable()
    {
        var descriptor = Blade(0x02C6);
        var telemetry = new RazerDeviceTelemetry(
            BladeKeyboardBrightness: 128,
            BladePerformanceMode: BladePerformanceMode.Balanced,
            BladeFanMode: BladeFanMode.Automatic,
            BladeFanTargetRpm: null,
            BladeChargeLimitPercent: 80,
            ViperBatteryPercent: null,
            ViperPollingRateHertz: null,
            ViperDpiX: null,
            ViperDpiY: null,
            ViperIdleSeconds: null,
            Errors: [],
            CapturedAt: DateTimeOffset.UnixEpoch,
            BladeCpuBoostMode: BladeCpuBoostMode.High,
            BladeGpuBoostMode: BladeGpuBoostMode.High,
            BladeMaxFanMode: BladeMaxFanMode.Enabled,
            BladeCurrentFanCpuRpm: 1,
            BladeCurrentFanGpuRpm: 1);

        var summary = DeviceCapabilitySummaryCalculator.Calculate(descriptor, telemetry);

        Assert.False(BladeModelCapabilities.SupportsMaxFan(descriptor.ProductId));
        Assert.True(summary.Available < summary.Supported);
    }

    [Theory]
    [InlineData(0x029F, true)]
    [InlineData(0x02A0, true)]
    [InlineData(0x02C6, false)]
    [InlineData(0x029D, false)]
    public void OnlyAuditedModelsAdmitTheSwitch(ushort productId, bool expected) =>
        Assert.Equal(expected, BladeModelCapabilities.SupportsMaxFan(productId));

    /// <summary>
    /// The curve rides the stored fan-target protocol, which only the 2025 Blade 16 has
    /// captured. Max Fan admission says nothing about it, so the two gates stay separate.
    /// </summary>
    [Theory]
    [InlineData(0x02C6, true)]
    [InlineData(0x02C5, false)]
    [InlineData(0x02E0, false)]
    [InlineData(0x029F, false)]
    [InlineData(0x02A0, false)]
    [InlineData(0x029D, false)]
    [InlineData(0x0000, false)]
    public void OnlyAuditedModelsAdmitTheFanCurve(ushort productId, bool expected) =>
        Assert.Equal(expected, BladeModelCapabilities.SupportsFanCurve(productId));
}