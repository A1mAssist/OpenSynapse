using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;

namespace OpenSynapse.Core.Tests;

public sealed class ProfileResolverTests
{
    private static DeviceDescriptor BladeDevice() => new(
        "blade",
        "Blade",
        0x1532,
        0x02C6,
        DeviceAccessState.Available,
        DeviceCapabilityState.PendingValidation,
        91,
        0x01,
        0x02,
        "blade-710");

    private static DeviceDescriptor ViperDevice() => new(
        "mouse",
        "Viper",
        0x1532,
        0x00B8,
        DeviceAccessState.Available,
        DeviceCapabilityState.PendingValidation,
        91,
        0x01,
        0x02,
        "viper-184");

    [Fact]
    public void DeviceAndPowerOverridesWinOverGlobalValues()
    {
        var document = ProfileDocument.CreateDefault();
        document.Global.Viper.DpiX = 800;
        document.Devices["1532:00B8"] = new DeviceProfileSettings { Viper = new() { DpiX = 1600 } };
        document.PluggedIn.Viper.DpiX = 3200;

        var resolved = ProfileResolver.Resolve(document, ViperDevice(), isPluggedIn: true);

        Assert.Equal(3200, resolved.Viper.DpiX);
    }

    [Fact]
    public void MissingOverridesRemainNullInsteadOfInventingWrites()
    {
        var resolved = ProfileResolver.Resolve(
            ProfileDocument.CreateDefault(),
            ViperDevice(),
            isPluggedIn: false);

        Assert.Null(resolved.Viper.DpiX);
        Assert.Null(resolved.Blade.KeyboardBrightness);
    }

    [Fact]
    public void DeviceKeyLookupIsCaseInsensitiveAndBatteryOverrideIsSelected()
    {
        var document = ProfileDocument.CreateDefault();
        document.Global.Viper.DpiY = 800;
        document.Devices["1532:00b8"] = new DeviceProfileSettings { Viper = new() { DpiY = 1600 } };
        document.OnBattery.Viper.DpiY = 2400;

        var resolved = ProfileResolver.Resolve(document, ViperDevice(), isPluggedIn: false);

        Assert.Equal("1532:00B8", ProfileResolver.GetDeviceKey(ViperDevice()));
        Assert.Equal(2400, resolved.Viper.DpiY);
    }

    [Fact]
    public void FieldsAreMergedIndependentlyAcrossPrecedenceLevels()
    {
        var document = ProfileDocument.CreateDefault();
        document.Global.Blade.KeyboardBrightness = 10;
        document.Global.Viper.DpiX = 800;
        document.Devices["1532:00B8"] = new DeviceProfileSettings
        {
            Viper = new() { DpiY = 1600 },
        };
        document.OnBattery.Blade.KeyboardBrightness = 20;
        document.OnBattery.Viper.PollingRateHertz = 125;

        var resolved = ProfileResolver.Resolve(document, ViperDevice(), isPluggedIn: false);

        Assert.Equal((byte)20, resolved.Blade.KeyboardBrightness);
        Assert.Equal(800, resolved.Viper.DpiX);
        Assert.Equal(1600, resolved.Viper.DpiY);
        Assert.Equal(125, resolved.Viper.PollingRateHertz);
    }

    [Fact]
    public void PowerLightingOverrideMergesParametersWithoutDefaultOffClobberingGlobalEffect()
    {
        var document = ProfileDocument.CreateDefault();
        document.Global.Lighting.Effect = "static";
        document.Global.Lighting.Parameters["color"] = "#99DD72";
        document.PluggedIn.Lighting.Parameters["speed"] = "fast";

        var resolved = ProfileResolver.Resolve(document, ViperDevice(), isPluggedIn: true);

        Assert.Equal("static", resolved.Lighting.Effect);
        Assert.Equal("#99DD72", resolved.Lighting.Parameters["color"]);
        Assert.Equal("fast", resolved.Lighting.Parameters["speed"]);
    }

    [Fact]
    public void UnknownPowerStateDoesNotApplyEitherPowerOverride()
    {
        var document = ProfileDocument.CreateDefault();
        document.Global.Viper.DpiX = 800;
        document.Devices["1532:00B8"] = new DeviceProfileSettings { Viper = new() { DpiX = 1600 } };
        document.PluggedIn.Viper.DpiX = 3200;
        document.OnBattery.Viper.DpiX = 6400;

        var resolved = ProfileResolver.Resolve(document, ViperDevice(), isPluggedIn: null);

        Assert.Equal(1600, resolved.Viper.DpiX);
    }

    [Fact]
    public void RefreshRateUsesPowerOverrideOnlyWhenPowerStateIsKnown()
    {
        var document = ProfileDocument.CreateDefault();
        document.Global.Blade.RefreshRateHertz = 240;
        document.Devices["1532:02C6"] = new DeviceProfileSettings
        {
            Blade = new() { RefreshRateHertz = 165 },
        };
        document.PluggedIn.Blade.RefreshRateHertz = 240;
        document.OnBattery.Blade.RefreshRateHertz = 60;
        document.OnBattery.Blade.MaxFanMode = (byte)BladeMaxFanMode.Disabled;

        Assert.Equal(240, ProfileResolver.Resolve(document, BladeDevice(), true).Blade.RefreshRateHertz);
        Assert.Equal(60, ProfileResolver.Resolve(document, BladeDevice(), false).Blade.RefreshRateHertz);
        Assert.Equal((byte)BladeMaxFanMode.Disabled, ProfileResolver.Resolve(document, BladeDevice(), false).Blade.MaxFanMode);
        Assert.Equal(165, ProfileResolver.Resolve(document, BladeDevice(), null).Blade.RefreshRateHertz);
    }
}
