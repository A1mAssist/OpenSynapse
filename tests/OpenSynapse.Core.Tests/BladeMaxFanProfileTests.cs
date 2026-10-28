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
    [InlineData(0x029D, true)]
    public void OnlyAuditedModelsAdmitTheSwitch(ushort productId, bool expected) =>
        Assert.Equal(expected, BladeModelCapabilities.SupportsMaxFan(productId));

    /// <summary>
    /// The curve rides the stored fan-target protocol. Models whose middleware exposes
    /// the 710 fan-target reports admit it; the two gates stay separate from Max Fan.
    /// </summary>
    [Theory]
    [InlineData(0x02C6, true)]
    [InlineData(0x02C5, true)]
    [InlineData(0x02E0, true)]
    [InlineData(0x029F, false)]
    [InlineData(0x02A0, false)]
    [InlineData(0x029D, true)]
    [InlineData(0x0000, false)]
    public void OnlyAuditedModelsAdmitTheFanCurve(ushort productId, bool expected) =>
        Assert.Equal(expected, BladeModelCapabilities.SupportsFanCurve(productId));

    [Theory]
    [InlineData(0x02C6, true)]
    [InlineData(0x0245, true)]
    [InlineData(0x029F, true)]
    [InlineData(0x02A0, false)]
    [InlineData(0x028C, true)]
    [InlineData(0x0233, false)]
    [InlineData(0x0234, true)]
    [InlineData(0x028A, true)]
    [InlineData(0x029E, true)]
    [InlineData(0x0255, false)]
    [InlineData(0x0000, false)]
    public void OnlyAuditedModelsAdmitFixedFanWrites(ushort productId, bool expected) =>
        Assert.Equal(expected, BladeModelCapabilities.SupportsFixedFan(productId));

    /// <summary>
    /// The Blade 16 (2023) middleware drives a fixed speed from every performance mode,
    /// so no custom-mode gate applies. Its official modes reach 5200 RPM and Custom
    /// reaches 5300, which is also the ceiling the write guard allows.
    /// </summary>
    [Fact]
    public void TheBlade16_2023AdmitsAFixedSpeedWithoutTheCustomModeGate()
    {
        const ushort productId = 0x029F;

        Assert.True(BladeModelCapabilities.SupportsFixedFan(productId));
        Assert.False(BladeModelCapabilities.FixedFanRequiresCustomMode(productId));
        Assert.Equal(3400, BladeModelCapabilities.MinimumFanTargetRpmFor(productId));
        Assert.Equal(5300, BladeModelCapabilities.MaximumFanTargetRpmFor(productId));

        Assert.Equal(5300, BladeModelCapabilities.MaximumWritableFanTargetRpmFor(productId));
    }

    /// <summary>
    /// The Blade 16 (2025) official Performance and Hyperboost modes run up to 5400
    /// RPM and the official middleware writes the same rpm/100 value, so the slider
    /// has to offer that instead of stopping at 5000.
    /// </summary>
    [Fact]
    public void TheBlade16_2025OffersTheOfficialFanSpeedCeiling()
    {
        const ushort productId = 0x02C6;

        Assert.Equal(5400, BladeModelCapabilities.MaximumFanTargetRpmFor(productId));
        Assert.Equal(5400, BladeModelCapabilities.MaximumWritableFanTargetRpmFor(productId));
        Assert.Equal(5400, BladeFanLimits.MaximumRpm);
        Assert.Equal(5400, BladeFanCurve.MaximumPointFanSpeedRpm);
    }

    /// <summary>
    /// Every model that accepts a fixed fan target has to carry the ceiling its own
    /// official bundle declares, because the shared report takes the same rpm/100
    /// value the official middleware writes.
    /// </summary>
    [Theory]
    [InlineData(0x02C6, 5400)] // Blade 16 (2025)
    [InlineData(0x0245, 5300)] // Blade 15 (2019)
    [InlineData(0x029F, 5300)] // Blade 16 (2023)
    [InlineData(0x028C, 5400)] // Blade 14 (2022)
    [InlineData(0x029D, 5400)] // Blade 14 (2023)
    [InlineData(0x02B6, 5400)] // Blade 14 (2024)
    [InlineData(0x02B7, 5400)] // Blade 16 (2024)
    [InlineData(0x02B8, 5300)] // Blade 18 (2024)
    [InlineData(0x02C5, 5400)] // Blade 14 (2025)
    [InlineData(0x02C7, 5400)] // Blade 18 (2025)
    [InlineData(0x02E0, 5400)] // Blade 16 (2026)
    [InlineData(0x0234, 5300)] // Blade Pro 17 (2019)
    [InlineData(0x0239, 5300)] // Blade Stealth (2019)
    [InlineData(0x023A, 5300)] // Blade 15 Advanced (2019)
    [InlineData(0x0252, 5500)] // Blade Stealth (Early 2020)
    [InlineData(0x0259, 5600)] // Blade Stealth (Late 2020)
    [InlineData(0x027A, 5400)] // Blade Base (Early 2022)
    [InlineData(0x028A, 5300)] // Blade 15 Advanced (Early 2022)
    [InlineData(0x028B, 5300)] // Blade 17 (2022)
    [InlineData(0x029E, 5300)] // Blade 15 (2023)
    public void EveryFixedFanModelCarriesItsOfficialCeiling(ushort productId, int expectedRpm)
    {
        Assert.True(BladeModelCapabilities.SupportsFixedFan(productId));
        Assert.Equal(expectedRpm, BladeModelCapabilities.MaximumFanTargetRpmFor(productId));
        Assert.Equal(expectedRpm, BladeModelCapabilities.MaximumWritableFanTargetRpmFor(productId));
    }
}
