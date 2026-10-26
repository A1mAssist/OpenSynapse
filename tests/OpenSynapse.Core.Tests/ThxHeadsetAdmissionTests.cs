using OpenSynapse.Windows.Audio;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class ThxHeadsetAdmissionTests
{
    [Fact]
    public void UnsupportedProductStaysHidden()
    {
        var status = ThxHeadsetAdmission.Evaluate(0x0555, serviceRunning: true, nativePresent: true);

        Assert.Equal(ThxHeadsetAvailability.UnsupportedDevice, status.Availability);
        Assert.Equal("ThxHeadsetUnsupported", status.ErrorCode);
    }

    [Fact]
    public void MissingServiceOffersTheInstaller()
    {
        var status = ThxHeadsetAdmission.Evaluate(0x0528, serviceRunning: false, nativePresent: true);

        Assert.Equal(ThxHeadsetAvailability.ServiceMissing, status.Availability);
        Assert.Equal("ThxServiceMissing", status.ErrorCode);
    }

    [Fact]
    public void MissingNativeClientOffersTheComponentDownload()
    {
        var status = ThxHeadsetAdmission.Evaluate(0x0528, serviceRunning: true, nativePresent: false);

        Assert.Equal(ThxHeadsetAvailability.NativeMissing, status.Availability);
        Assert.Equal("ThxNativeMissing", status.ErrorCode);
    }

    [Fact]
    public void ServiceAndClientTogetherAreAvailable()
    {
        var status = ThxHeadsetAdmission.Evaluate(0x0528, serviceRunning: true, nativePresent: true);

        Assert.Equal(ThxHeadsetAvailability.Available, status.Availability);
        Assert.Null(status.ErrorCode);
    }
}
