using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class BladeFanSelectorTests
{
    [Theory]
    [InlineData(0x023A)] // Blade 15 Advanced 2019
    [InlineData(0x029F)] // Blade 16 2023
    public void AuditedBladeFamiliesUseClassOne(ushort productId)
    {
        var manifest = RazerDeviceRegistry.BuiltIn.Find(0x1532, productId);
        Assert.NotNull(manifest);

        var device = Resolve(manifest!);
        Assert.Equal((byte)0x01, BladeFanTelemetryReader.GetFanClassId(device));
        Assert.Equal((byte)0x01, BladeFanTelemetryReader.GetThermalProfileId(device));
    }

    [Fact]
    public void Blade15Advanced2019WritesTheBundleFanTargetPayload()
    {
        var get = BladeFanProtocol.CreateGetTargetRequest(BladeFanProtocol.ZoneCpu);
        Assert.Equal((byte)0x01, get[RazerFeatureReport.ArgumentsOffset]);
        Assert.Equal((byte)0x01, get[RazerFeatureReport.ArgumentsOffset + 1]);

        var set = BladeFanProtocol.CreateSetTargetRequest(BladeFanProtocol.ZoneCpu, 4500);
        Assert.Equal((byte)0x01, set[RazerFeatureReport.ArgumentsOffset]);
        Assert.Equal((byte)0x01, set[RazerFeatureReport.ArgumentsOffset + 1]);
        Assert.Equal((byte)0x2D, set[RazerFeatureReport.ArgumentsOffset + 2]);
    }

    private static ResolvedRazerDevice Resolve(RazerDeviceManifest manifest) =>
        new(
            new DeviceDescriptor(
                "test",
                manifest.DisplayName,
                manifest.VendorId,
                manifest.ProductIds[0],
                DeviceAccessState.Available,
                DeviceCapabilityState.PendingValidation,
                manifest.Collection.FeatureReportLength,
                manifest.Collection.UsagePage,
                manifest.Collection.Usage,
                manifest.ProtocolFamily,
                manifest.Category),
            manifest);
}
