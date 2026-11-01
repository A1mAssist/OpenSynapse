using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class Blade0270Tests
{
    [Fact]
    public void ManifestOwnsTheChargeLimiterAndOmitsTheGamingModeReportTheFirmwareRejects()
    {
        var manifest = RazerDeviceRegistry.BuiltIn.Find(0x1532, 0x0270);

        Assert.NotNull(manifest);
        Assert.True(manifest.Capabilities.ContainsKey("charge-limit.get"));
        Assert.True(manifest.Capabilities.ContainsKey("charge-limit.set"));
        Assert.DoesNotContain("gaming-mode.set", manifest.Capabilities.Keys);
    }

    [Fact]
    public void ChargeLimiterUsesTheSameReportsAsTheRestOfTheFamily()
    {
        var manifest = RazerDeviceRegistry.BuiltIn.Find(0x1532, 0x0270)!;

        var get = manifest.GetRequiredCapability("charge-limit.get");
        var set = manifest.GetRequiredCapability("charge-limit.set");

        Assert.Equal((byte)0x07, get.CommandClass);
        Assert.Equal((byte)0x92, get.CommandId);
        Assert.Equal((byte)0x00, get.Arguments.Span[0]);
        Assert.Equal((byte)0x07, set.CommandClass);
        Assert.Equal((byte)0x12, set.CommandId);
        Assert.Equal(0xB2, BladePowerTelemetryShared.EncodeChargeLimit(50));
        Assert.Equal(50, BladePowerTelemetryShared.DecodeChargeLimit(0xB2));
    }
}