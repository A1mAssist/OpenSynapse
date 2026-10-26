using OpenSynapse.Windows.Audio;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class ThxHeadsetAdmissionTests
{
    [Fact]
    public void UnsupportedProductStaysHidden()
    {
        var status = ThxHeadsetAdmission.Evaluate(0x9999, serviceRunning: true, nativePresent: true);

        Assert.Equal(ThxHeadsetAvailability.UnsupportedDevice, status.Availability);
        Assert.Equal("ThxHeadsetUnsupported", status.ErrorCode);
    }

    [Fact]
    public void KrakenV4UsesTheV3NativeClient()
    {
        var profile = ThxHeadsetAdmission.GetProfile(0x056B);

        Assert.NotNull(profile);
        Assert.Equal(ThxNativeVersion.V3, profile.NativeVersion);
        Assert.True(profile.HasSidetone);
    }

    [Fact]
    public void V4HeadsetsUseTheirOwnProductAndDll()
    {
        var blackShark = ThxHeadsetAdmission.GetProfile(0x0576);
        var barracuda = ThxHeadsetAdmission.GetProfile(0x0593);

        Assert.NotNull(blackShark);
        Assert.NotNull(barracuda);
        Assert.Equal(ThxNativeVersion.V4, blackShark.NativeVersion);
        Assert.Equal("razer_blacksharkv3pro_pc", blackShark.V4ProductId);
        Assert.Equal("headphones_game", blackShark.V4EndpointId);
        Assert.Equal("razer_barracudav2pro", barracuda.V4ProductId);
        Assert.Equal("headphones", barracuda.V4EndpointId);
        Assert.NotEqual(blackShark.NativeDllSha256, barracuda.NativeDllSha256);
    }

    [Fact]
    public void V4DoesNotRequireTheV3ServiceGate()
    {
        var status = ThxHeadsetAdmission.Evaluate(0x0576, serviceRunning: false, nativePresent: true);

        Assert.Equal(ThxHeadsetAvailability.Available, status.Availability);
    }

    [Fact]
    public void BothV2ProModelsUseTheSameDetection()
    {
        var status = ThxHeadsetAdmission.Evaluate(0x0555, serviceRunning: false, nativePresent: false);

        Assert.Equal(ThxHeadsetAvailability.ServiceMissing, status.Availability);
        Assert.Equal("ThxServiceMissing", status.ErrorCode);
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
