using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class NewAccessoryCatalogTests
{
    [Theory]
    [InlineData(0x0F52, "Razer Thunderbolt 5 Dock Chroma")]
    [InlineData(0x0F59, "Razer Monitor Stand Chroma")]
    [InlineData(0x0F5A, "Razer Laptop Monitor Stand Chroma")]
    [InlineData(0x0F5C, "Razer Handheld Dock Chroma")]
    public void NewAccessoryEntriesLoadWithLightingMatrix(ushort productId, string displayName)
    {
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, productId);

        Assert.NotNull(device);
        Assert.Equal(displayName, device!.DisplayName);
        Assert.Equal(DeviceCategory.Accessory, device.Category);
        Assert.Equal(new OpenRazerMatrixDimensions(1, 12), device.MatrixDimensions);
        Assert.Equal(TimeSpan.FromMilliseconds(31), device.DeviceWait);
    }
}
