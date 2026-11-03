using OpenSynapse.Core.Devices;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class DeviceVisibilityPolicyTests
{
    [Fact]
    public void SleepingViperDongleIsHidden()
    {
        Assert.False(DeviceVisibilityPolicy.IsVisible(Descriptor(0x00B8, DeviceProtocolFamilies.Viper), Telemetry()));
    }

    [Fact]
    public void ViperThatAnsweredOneCapabilityStaysVisible()
    {
        Assert.True(DeviceVisibilityPolicy.IsVisible(Descriptor(0x00B8, DeviceProtocolFamilies.Viper), Telemetry(55)));
    }

    [Fact]
    public void BladeStaysVisibleWithoutTelemetry()
    {
        Assert.True(DeviceVisibilityPolicy.IsVisible(Descriptor(0x02C6, DeviceProtocolFamilies.Blade), Telemetry()));
    }

    [Fact]
    public void BladeChromaStaysVisibleWithoutTelemetry()
    {
        Assert.True(DeviceVisibilityPolicy.IsVisible(Descriptor(0x02B7, DeviceProtocolFamilies.BladeChroma), Telemetry()));
    }

    [Fact]
    public void UnknownProtocolFamilyKeepsItsDiagnosticRow()
    {
        Assert.True(DeviceVisibilityPolicy.IsVisible(Descriptor(0xFFFF, "unknown-family"), Telemetry()));
    }

    private static DeviceDescriptor Descriptor(ushort productId, string protocolFamily) =>
        new(
            $"test-{productId:X4}",
            "test",
            0x1532,
            productId,
            DeviceAccessState.Available,
            DeviceCapabilityState.PendingValidation,
            91,
            0x0001,
            0x0002,
            protocolFamily,
            protocolFamily == DeviceProtocolFamilies.Viper ? DeviceCategory.Mouse : DeviceCategory.Laptop);

    private static RazerDeviceTelemetry Telemetry(int? battery = null) =>
        new(
            null,
            null,
            null,
            null,
            null,
            battery,
            null,
            null,
            null,
            null,
            [],
            DateTimeOffset.UtcNow);
}
