using OpenSynapse.Windows.Audio;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class ThxHeadsetAdmissionTests
{
    [Fact]
    public void EverySupportedHeadsetPinsItsInstaller()
    {
        foreach (var productId in ThxHeadsetAdmission.SupportedProductIds)
        {
            var profile = ThxHeadsetAdmission.GetProfile(productId);
            Assert.NotNull(profile);
            Assert.NotNull(profile.Installer);
            Assert.True(profile.Installer.Length > 1_000_000);
            Assert.Equal(64, profile.Installer.Sha256.Length);
            Assert.StartsWith("https://app-assets.razer.com/", profile.InstallerUri);
            Assert.True(profile.Installer.Sha256.All(Uri.IsHexDigit));
        }
    }

    [Fact]
    public void KrakenV4InstallerMatchesTheOfficialManifest()
    {
        var installer = Assert.IsType<ThxInstallerPackage>(
            ThxHeadsetAdmission.GetProfile(0x056B)?.Installer);

        Assert.Equal(45_706_008, installer.Length);
        Assert.Equal("F55BF3C7855242EAB25719CB8E16F3C59D73442833F2A9CA6DA0B9699C51DF5E", installer.Sha256);
        Assert.Equal("/S", installer.Arguments);
    }

    [Fact]
    public void V4InstallerUsesTheV4SilentSwitch()
    {
        var installer = Assert.IsType<ThxInstallerPackage>(
            ThxHeadsetAdmission.GetProfile(0x0576)?.Installer);

        Assert.Equal(51_481_232, installer.Length);
        Assert.Equal("/s /norestart", installer.Arguments);
    }

    [Fact]
    public async Task UnpinnedInstallerIsRejectedBeforeDownload()
    {
        var result = await ThxComponentInstaller.InstallAsync(
            new ThxInstallerPackage("https://example.invalid/thx.exe", 0, string.Empty, "/S"),
            CancellationToken.None);

        Assert.Equal("ThxInstallerVerificationFailed", result);
    }

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
