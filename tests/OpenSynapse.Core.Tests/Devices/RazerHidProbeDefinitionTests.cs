using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerHidProbeDefinitionTests
{
    [Fact]
    public void CatalogExternalProbeCarriesOnlyIdentity()
    {
        var probe = RazerHidDeviceService.CreateProbeDefinition(
            0xBEEF, "Razer Test Keyboard", DeviceCategory.Keyboard);

        Assert.Equal(0xBEEF, probe.ProductId);
        Assert.Equal("Razer Test Keyboard", probe.DisplayName);
        Assert.Equal(DeviceCategory.Keyboard, probe.Category);
        Assert.Empty(probe.SourceCapabilities);
        Assert.Empty(probe.Transactions);
        Assert.Empty(probe.BuilderArgumentPrefixes);
        Assert.Empty(probe.CommandSequences);
        Assert.Empty(RazerHidDeviceService.GetCapabilities(probe));
    }

    [Fact]
    public void UnknownProbeFallsBackToMouseAndReadableName()
    {
        var probe = RazerHidDeviceService.CreateProbeDefinition(0xBEEF, null, DeviceCategory.Unknown);

        Assert.Equal(DeviceCategory.Mouse, probe.Category);
        Assert.Equal("Razer 1532:BEEF", probe.DisplayName);
    }
}