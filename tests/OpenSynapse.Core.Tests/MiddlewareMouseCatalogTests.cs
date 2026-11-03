using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using Xunit;

namespace OpenSynapse.Core.Tests;

/// <summary>
/// The middleware mice have no upstream RazerHid entry: their definitions come from the
/// Razer product bundles and reuse a catalogued sibling's protocol. These tests pin the
/// product ids, DPI ranges and polling rates so a definition cannot drift silently.
/// </summary>
public sealed class MiddlewareMouseCatalogTests
{
    private static readonly ushort[] WirelessPids =
    [
        0x0069, 0x0075, 0x00DD, 0x00E0, 0x00E2, 0x00E3, 0x00E5, 0x00E6,
        0x00E7, 0x00E8, 0x00EA, 0x00EB, 0x00EF, 0x00F0,
        0xB005, 0xB007, 0xB00A, 0xB00B,
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
    [InlineData(0x00A2, 8500)]
    [InlineData(0xB005, 33000)]
    [InlineData(0xB007, 33000)]
    [InlineData(0xB00A, 35000)]
    [InlineData(0xB00B, 35000)]
    public void MiddlewareMiceAreCataloguedWithTheirOwnDpiRange(ushort productId, int maximumDpi)
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(RazerHidDeviceDefinition.VendorId, productId);

        Assert.NotNull(device);
        Assert.Equal(maximumDpi, device.MaximumDpi);
        Assert.Equal(DeviceCategory.Mouse, device.Category);
    }

    [Fact]
    public void MiddlewareMiceExposeDpiAndPollingCapabilities()
    {
        foreach (var productId in AllPids())
        {
            var device = RazerHidDeviceCatalog.BuiltIn.Find(
                RazerHidDeviceDefinition.VendorId, productId);
            Assert.NotNull(device);
            var capabilities = RazerHidDeviceService.GetCapabilities(device);

            Assert.Contains(RazerHidBackendCapability.DpiRead, capabilities);
            if (productId == 0x00A2)
                Assert.DoesNotContain(capabilities, capability => capability.ToString().EndsWith("Write", StringComparison.Ordinal));
            else
                Assert.Contains(RazerHidBackendCapability.DpiWrite, capabilities);
            Assert.Contains(RazerHidBackendCapability.PollingRateRead, capabilities);
            if (device.PollingRates.Count > 0 && productId != 0x00A2)
            {
                Assert.Contains(RazerHidBackendCapability.PollingRateWrite, capabilities);
            }
            else
            {
                // 0063 reuses 006A's read builder but the middleware has no rate ladder.
                Assert.DoesNotContain(RazerHidBackendCapability.PollingRateWrite, capabilities);
            }
            if (WirelessPids.Contains(productId))
            {
                Assert.Contains(RazerHidBackendCapability.BatteryRead, capabilities);
            }
        }
    }

    [Fact]
    public void MiddlewareMiceDoNotInheritUnevidencedWrites()
    {
        foreach (var productId in AllPids())
        {
            var device = RazerHidDeviceCatalog.BuiltIn.Find(RazerHidDeviceDefinition.VendorId, productId)!;
            var writeBuilders = device.Transactions.Keys.Concat(device.CommandSequences.Keys)
                .Where(builder => !builder.Contains("_get_", StringComparison.Ordinal)).ToArray();

            Assert.DoesNotContain(writeBuilders, builder => builder.Contains("profile_debounce", StringComparison.Ordinal));
            Assert.DoesNotContain(writeBuilders, builder => builder.Contains("indicator_led", StringComparison.Ordinal));
            Assert.DoesNotContain(writeBuilders, builder => builder.Contains("time_to_dim", StringComparison.Ordinal));
            Assert.DoesNotContain(writeBuilders, builder => builder.Contains("gpu_card_detection", StringComparison.Ordinal));
            Assert.DoesNotContain(writeBuilders, builder => builder.Contains("chroma_tap", StringComparison.Ordinal));
            Assert.DoesNotContain(writeBuilders, builder => builder.Contains("force_chroma", StringComparison.Ordinal));
            Assert.DoesNotContain(writeBuilders, builder => builder.Contains("battery_discharge_curve", StringComparison.Ordinal));
            Assert.DoesNotContain(writeBuilders, builder => builder.Contains("battery_charge_limiter", StringComparison.Ordinal));
            Assert.DoesNotContain(writeBuilders, builder => builder.Contains("auto_sleep", StringComparison.Ordinal));
            Assert.DoesNotContain(writeBuilders, builder => builder.Contains("lk_scroll", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void PollingRatesFollowTheMiddlewareDefinition()
    {
        Assert.Equal([125, 500, 1000], Find(0x00E0).PollingRates);
        Assert.Equal([125, 500, 1000], Find(0x0074).PollingRates);
        Assert.Empty(Find(0x00A2).PollingRates);
        Assert.Equal([125, 500, 1000, 2000, 4000, 8000], Find(0x00E2).PollingRates);
        Assert.Equal([125, 500, 1000], Find(0x00E7).PollingRates);
        // Viper V4 HyperSpeed and DeathAdder V4 HyperSpeed declare the 8 kHz ladder;
        // the Basilisk V3 Pro 35K Xbox edition declares 125/500/1000 only.
        Assert.Equal([125, 500, 1000, 2000, 4000, 8000], Find(0xB005).PollingRates);
        Assert.Equal([125, 500, 1000, 2000, 4000, 8000], Find(0xB007).PollingRates);
        Assert.Equal([125, 500, 1000], Find(0xB00A).PollingRates);
        Assert.Equal([125, 500, 1000], Find(0xB00B).PollingRates);
    }

    [Fact]
    public void OrochiV2KeepsOfficialPointerFeaturesButDoesNotInheritCobraLighting()
    {
        var device = Find(0x00E0);
        var capabilities = RazerHidDeviceService.GetCapabilities(device);

        Assert.Contains(RazerHidBackendCapability.BatteryRead, capabilities);
        Assert.Contains(RazerHidBackendCapability.DpiWrite, capabilities);
        Assert.Contains(RazerHidBackendCapability.PollingRateWrite, capabilities);
        Assert.DoesNotContain(RazerHidBackendCapability.BrightnessWrite, capabilities);
        Assert.DoesNotContain(RazerHidBackendCapability.LightingEffectWrite, capabilities);
        Assert.Empty(RazerHidDeviceService.GetSupportedLedZones(device));
        Assert.False(device.SupportsIndependentDpi);
    }

    [Theory]
    [InlineData(0x00AA)]
    [InlineData(0x00AB)]
    public void BasiliskV3ProUsesTheOfficialPollingAndMappingHint(ushort productId)
    {
        var device = Find(productId);
        var capabilities = RazerHidDeviceService.GetCapabilities(device);

        Assert.Equal([125, 500, 1000], device.PollingRates);
        Assert.True(RazerHidDeviceCatalog.HasOnboardMapping(productId));
        Assert.DoesNotContain(RazerHidBackendCapability.MappingRead, capabilities);
        Assert.DoesNotContain(RazerHidBackendCapability.MappingWrite, capabilities);
    }

    private static RazerHidDeviceDefinition Find(ushort productId) =>
        RazerHidDeviceCatalog.BuiltIn.Find(RazerHidDeviceDefinition.VendorId, productId)
            ?? throw new InvalidOperationException($"1532:{productId:X4} is missing.");

    private static IEnumerable<ushort> AllPids() =>
    [
        0x0063, 0x0069, 0x0074, 0x0075, 0x0093, 0x00DD, 0x00E0, 0x00E2, 0x00E3,
        0x00E5, 0x00E6, 0x00E7, 0x00E8, 0x00EA, 0x00EB, 0x00EF, 0x00F0, 0x00A2,
        0xB005, 0xB007, 0xB00A, 0xB00B,
    ];
}
