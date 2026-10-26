using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using Xunit;

namespace OpenSynapse.Core.Tests;

/// <summary>
/// The middleware mice have no upstream OpenRazer entry: their definitions come from the
/// Razer product bundles and reuse a catalogued sibling's protocol. These tests pin the
/// product ids, DPI ranges and polling rates so a definition cannot drift silently.
/// </summary>
public sealed class MiddlewareMouseCatalogTests
{
    private static readonly ushort[] WirelessPids =
    [
        0x0069, 0x0075, 0x00DD, 0x00E0, 0x00E2, 0x00E3, 0x00E5, 0x00E6,
        0x00E7, 0x00E8, 0x00EA, 0x00EB, 0x00EF, 0x00F0,
    ];

    [Theory]
    [InlineData(0x0063, 7200)]
    [InlineData(0x0069, 16000)]
    [InlineData(0x0074, 6400)]
    [InlineData(0x0075, 16000)]
    [InlineData(0x0093, 16000)]
    [InlineData(0x00DD, 45000)]
    [InlineData(0x00E0, 18000)]
    [InlineData(0x00E2, 50000)]
    [InlineData(0x00E3, 50000)]
    [InlineData(0x00E5, 50000)]
    [InlineData(0x00E6, 50000)]
    [InlineData(0x00E7, 30000)]
    [InlineData(0x00E8, 30000)]
    [InlineData(0x00EA, 33000)]
    [InlineData(0x00EB, 33000)]
    [InlineData(0x00EF, 50000)]
    [InlineData(0x00F0, 50000)]
    public void MiddlewareMiceAreCataloguedWithTheirOwnDpiRange(ushort productId, int maximumDpi)
    {
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(OpenRazerDeviceDefinition.VendorId, productId);

        Assert.NotNull(device);
        Assert.Equal(maximumDpi, device.MaximumDpi);
        Assert.Equal(DeviceCategory.Mouse, device.Category);
    }

    [Fact]
    public void MiddlewareMiceExposeDpiAndPollingCapabilities()
    {
        foreach (var productId in AllPids())
        {
            var device = OpenRazerDeviceCatalog.BuiltIn.Find(
                OpenRazerDeviceDefinition.VendorId, productId);
            Assert.NotNull(device);
            var capabilities = OpenRazerDeviceService.GetCapabilities(device);

            Assert.Contains(OpenRazerBackendCapability.DpiRead, capabilities);
            Assert.Contains(OpenRazerBackendCapability.DpiWrite, capabilities);
            Assert.Contains(OpenRazerBackendCapability.PollingRateRead, capabilities);
            Assert.Contains(OpenRazerBackendCapability.PollingRateWrite, capabilities);
            if (WirelessPids.Contains(productId))
            {
                Assert.Contains(OpenRazerBackendCapability.BatteryRead, capabilities);
            }
        }
    }

    [Fact]
    public void PollingRatesFollowTheMiddlewareDefinition()
    {
        Assert.Equal([125, 500, 1000], Find(0x00E0).PollingRates);
        Assert.Equal([125, 500, 1000, 2000, 4000, 8000], Find(0x00E2).PollingRates);
        Assert.Equal([125, 500, 1000], Find(0x00E7).PollingRates);
    }

    private static OpenRazerDeviceDefinition Find(ushort productId) =>
        OpenRazerDeviceCatalog.BuiltIn.Find(OpenRazerDeviceDefinition.VendorId, productId)
            ?? throw new InvalidOperationException($"1532:{productId:X4} is missing.");

    private static IEnumerable<ushort> AllPids() =>
    [
        0x0063, 0x0069, 0x0074, 0x0075, 0x0093, 0x00DD, 0x00E0, 0x00E2, 0x00E3,
        0x00E5, 0x00E6, 0x00E7, 0x00E8, 0x00EA, 0x00EB, 0x00EF, 0x00F0,
    ];
}
