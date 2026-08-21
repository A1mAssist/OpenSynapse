using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class BladeGamingModeTests
{
    [Fact]
    public void Product710UsesTheSynapseGameModeSetWithoutUnsupportedReadsOrLedWrites()
    {
        var manifest = RazerDeviceRegistry.BuiltIn.Find(0x1532, 0x02C6);

        Assert.NotNull(manifest);
        Assert.DoesNotContain("gaming-mode.get", manifest.Capabilities.Keys);
        Assert.DoesNotContain("gaming-mode-led.set", manifest.Capabilities.Keys);
        var setter = manifest.GetRequiredCapability("gaming-mode.set");
        Assert.Equal(0x04, setter.MaximumDataSize);
        Assert.Equal(0x00, setter.CommandClass);
        Assert.Equal(0x08, setter.CommandId);

        var request = BladeSynapsePolicyProtocol.CreateSetGameModeRequest(enabled: true);
        Assert.Equal(0x01, request[RazerFeatureReport.ArgumentsOffset]);
    }

    [Fact]
    public void GamingModeFollowsExistingProfilePrecedence()
    {
        var document = ProfileDocument.CreateDefault();
        var device = new DeviceDescriptor(
            "blade",
            "Blade 16",
            0x1532,
            0x02C6,
            DeviceAccessState.Available,
            DeviceCapabilityState.PendingValidation,
            91,
            1,
            2,
            "blade-710");
        document.Global.Blade.GamingModeEnabled = false;
        document.Devices[ProfileResolver.GetDeviceKey(device)] = new DeviceProfileSettings
        {
            Blade = new BladeProfileSettings { GamingModeEnabled = true },
        };
        document.PluggedIn.Blade.GamingModeEnabled = false;

        Assert.False(ProfileResolver.Resolve(document, device, true).Blade.GamingModeEnabled);
        Assert.True(ProfileResolver.Resolve(document, device, false).Blade.GamingModeEnabled);
        Assert.True(document.Clone().Devices[ProfileResolver.GetDeviceKey(device)]
            .Blade.GamingModeEnabled);
    }
}
