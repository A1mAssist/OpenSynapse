using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

/// <summary>
/// Whole-catalog capability guard. The UI trusts GetCapabilities as the source of truth,
/// so a capability that is declared from shared middleware data must not leak onto a
/// device that has no hardware evidence for it.
/// </summary>
public sealed class RazerHidCapabilityAuditTests
{
    [Fact]
    public void BatteryOnlyControlsRequireBatteryTelemetry()
    {
        RazerHidBackendCapability[] batteryOnly =
        [
            RazerHidBackendCapability.LowBatteryThresholdRead,
            RazerHidBackendCapability.LowBatteryThresholdWrite,
            RazerHidBackendCapability.BatteryChargeLimiterRead,
            RazerHidBackendCapability.BatteryChargeLimiterWrite,
            RazerHidBackendCapability.BatteryDischargeCurveRead,
            RazerHidBackendCapability.BatteryDischargeCurveWrite,
        ];

        foreach (var device in RazerHidDeviceCatalog.BuiltIn.Devices)
        {
            if (device.Category == DeviceCategory.Laptop) continue;

            var capabilities = RazerHidDeviceService.GetCapabilities(device);
            if (capabilities.Contains(RazerHidBackendCapability.BatteryRead) ||
                capabilities.Contains(RazerHidBackendCapability.ChargingRead))
            {
                continue;
            }

            var leaked = batteryOnly.Where(capabilities.Contains).ToArray();
            Assert.True(leaked.Length == 0,
                $"1532:{device.ProductId:X4} {device.DisplayName} published battery-only controls without battery telemetry: {string.Join(", ", leaked)}");
        }
    }

    [Fact]
    public void LkScrollWheelModeOnlyPublishesForScrollWheelProducts()
    {
        foreach (var device in RazerHidDeviceCatalog.BuiltIn.Devices)
        {
            var capabilities = RazerHidDeviceService.GetCapabilities(device);
            if (!capabilities.Contains(RazerHidBackendCapability.LkScrollWheelModeWrite))
            {
                continue;
            }

            Assert.True(device.HasSourceCapability("get_scroll_smart_reel") ||
                device.HasSourceCapability("set_scroll_smart_reel"),
                $"1532:{device.ProductId:X4} {device.DisplayName} published LK scroll mode without a scroll-wheel capability.");
        }

        var naga = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0053)!;
        var deathAdder = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0098)!;
        Assert.DoesNotContain(RazerHidBackendCapability.LkScrollWheelModeWrite,
            RazerHidDeviceService.GetCapabilities(naga));
        Assert.DoesNotContain(RazerHidBackendCapability.LkScrollWheelModeWrite,
            RazerHidDeviceService.GetCapabilities(deathAdder));
    }

    [Fact]
    public void ScrollModePublishesOnlyWhenTheDeviceDeclaresBothSourceAndBuilder()
    {
        foreach (var device in RazerHidDeviceCatalog.BuiltIn.Devices)
        {
            var capabilities = RazerHidDeviceService.GetCapabilities(device);
            Assert.Equal(
                device.HasSourceCapability("get_scroll_mode") &&
                device.Transactions.ContainsKey("razer_chroma_misc_get_scroll_mode"),
                capabilities.Contains(RazerHidBackendCapability.ScrollModeRead));
            Assert.Equal(
                device.HasSourceCapability("set_scroll_mode") &&
                device.Transactions.ContainsKey("razer_chroma_misc_set_scroll_mode"),
                capabilities.Contains(RazerHidBackendCapability.ScrollModeWrite));
        }
    }

    [Fact]
    public void EveryWritablePollingControlPublishesAnEncodableRateLadder()
    {
        foreach (var device in RazerHidDeviceCatalog.BuiltIn.Devices)
        {
            var capabilities = RazerHidDeviceService.GetCapabilities(device);
            if (!capabilities.Contains(RazerHidBackendCapability.PollingRateWrite))
            {
                continue; // A read-only probe may report the current rate without a writable ladder.
            }

            Assert.True(device.PollingRates.Count > 0,
                $"1532:{device.ProductId:X4} {device.DisplayName} published polling write without rates.");
            foreach (var rate in device.PollingRates)
            {
                var exception = Record.Exception(() => RazerHidMouseProtocol.SetPollingRate(device, rate));
                Assert.True(exception is null,
                    $"1532:{device.ProductId:X4} {device.DisplayName} cannot encode {rate} Hz: {exception}");
            }
        }
    }
}
