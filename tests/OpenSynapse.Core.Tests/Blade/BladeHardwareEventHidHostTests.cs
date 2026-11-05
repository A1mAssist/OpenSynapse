using global::OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class BladeHardwareEventHidHostTests
{
    [Fact]
    public async Task SelectsExactCollectionsAndRoutesOnlyCol04Report04()
    {
        const string col04 = @"\\?\HID#VID_1532&PID_02C6&MI_01&Col04#A";
        const string col05 = @"\\?\HID#VID_1532&PID_02C6&MI_01&Col05#A";
        var devices = new[]
        {
            Device(col04, 0x1532, 0x02C6),
            Device(col05, 0x1532, 0x02C6),
            Device(@"\\?\HID#VID_1532&PID_02C6&MI_01&Col040#A", 0x1532, 0x02C6),
            Device(@"\\?\HID#VID_1532&PID_02C6&MI_00&Col04#A", 0x1532, 0x02C6),
            Device(@"\\?\HID#VID_1532&PID_02C7&MI_01&Col04#A", 0x1532, 0x02C7),
        };

        var selected = BladeHardwareEventHidHost.SelectProduct710Endpoints(devices);

        Assert.Equal(100, BladeHardwareEventHidHost.ReportLength);
        Assert.Equal([col04, col05], selected.Select(static device => device.Id));
        await using var host = new BladeHardwareEventHidHost(static _ => { });
        Assert.Empty(host.ProcessReport(col05, [0x04, 0x03]));
        Assert.Empty(host.ProcessReport(col04, [0x05, 0x03]));
        Assert.Equal(
            new BladeMappingInputEvent(BladeMappingInputKind.RazerKey, 0x03, true),
            Assert.Single(host.ProcessReport(col04, [0x04, 0x03])));
        Assert.Equal(
            new BladeMappingInputEvent(BladeMappingInputKind.RazerKey, 0x03, false),
            Assert.Single(host.ProcessReport(col04, [0x04, 0x00])));
    }

    private static DeviceDescriptor Device(string id, ushort vendorId, ushort productId) =>
        new(
            id,
            "test",
            vendorId,
            productId,
            DeviceAccessState.Available,
            DeviceCapabilityState.Blocked,
            0,
            0,
            0,
            "test");
}
