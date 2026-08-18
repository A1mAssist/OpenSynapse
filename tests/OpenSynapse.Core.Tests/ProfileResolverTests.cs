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

    [Fact]
    public void ExpandedSettingsUsePowerDeviceGlobalPrecedenceAndCloneDpiStages()
    {
        var document = ProfileDocument.CreateDefault();
        document.Global.Blade.CpuBoostMode = (byte)BladeCpuBoostMode.Low;
        document.Global.Blade.GpuBoostMode = (byte)BladeGpuBoostMode.Low;
        document.Global.Blade.LogoMode = (byte)BladeLogoMode.Static;
        document.Global.Viper.DpiStages = DpiStages(800);
        document.Devices["1532:02C6"] = new DeviceProfileSettings
        {
            Blade = new BladeProfileSettings
            {
                CpuBoostMode = (byte)BladeCpuBoostMode.Medium,
                GpuBoostMode = (byte)BladeGpuBoostMode.Medium,
                LogoMode = (byte)BladeLogoMode.Off,
            },
            Viper = new ViperProfileSettings { DpiStages = DpiStages(1600) },
        };
        document.PluggedIn.Blade.CpuBoostMode = (byte)BladeCpuBoostMode.High;
        document.PluggedIn.Blade.GpuBoostMode = (byte)BladeGpuBoostMode.High;
        document.PluggedIn.Blade.LogoMode = (byte)BladeLogoMode.Static;
        document.PluggedIn.Viper.DpiStages = DpiStages(3200);

        var pluggedIn = ProfileResolver.Resolve(document, BladeDevice(), isPluggedIn: true);
        var unknownPower = ProfileResolver.Resolve(document, BladeDevice(), isPluggedIn: null);

        Assert.Equal((byte)BladeCpuBoostMode.High, pluggedIn.Blade.CpuBoostMode);
        Assert.Equal((byte)BladeGpuBoostMode.High, pluggedIn.Blade.GpuBoostMode);
        Assert.Equal((byte)BladeLogoMode.Static, pluggedIn.Blade.LogoMode);
        Assert.Equal(3200, pluggedIn.Viper.DpiStages!.Stages[0].X);
        Assert.Equal((byte)BladeCpuBoostMode.Medium, unknownPower.Blade.CpuBoostMode);
        Assert.Equal((byte)BladeGpuBoostMode.Medium, unknownPower.Blade.GpuBoostMode);
        Assert.Equal((byte)BladeLogoMode.Off, unknownPower.Blade.LogoMode);
        Assert.Equal(1600, unknownPower.Viper.DpiStages!.Stages[0].X);

        pluggedIn.Viper.DpiStages.Stages[0].X = 6400;
        Assert.Equal(3200, document.PluggedIn.Viper.DpiStages!.Stages[0].X);
    }

    [Fact]
    public void LightingMergesGlobalDeviceAndPowerOverrides()
    {
        var document = ProfileDocument.CreateDefault();
        document.Global.Lighting.Effect = "static";
        document.Global.Lighting.Parameters["color"] = "111111";
        document.Global.Lighting.Parameters["speed"] = "slow";
        document.Devices["1532:02C6"] = new DeviceProfileSettings
        {
            Lighting = new LightingProfile
            {
                Effect = "wave",
                Parameters = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["color"] = "222222",
                    ["direction"] = "right",
                },
            },
        };
        document.PluggedIn.Lighting.Effect = "fire";
        document.PluggedIn.Lighting.Parameters["direction"] = "left";

        var pluggedIn = ProfileResolver.Resolve(document, BladeDevice(), isPluggedIn: true);
        var unknownPower = ProfileResolver.Resolve(document, BladeDevice(), isPluggedIn: null);

        Assert.Equal("fire", pluggedIn.Lighting.Effect);
        Assert.Equal("222222", pluggedIn.Lighting.Parameters["color"]);
        Assert.Equal("slow", pluggedIn.Lighting.Parameters["speed"]);
        Assert.Equal("left", pluggedIn.Lighting.Parameters["direction"]);
        Assert.Equal("wave", unknownPower.Lighting.Effect);
        Assert.Equal("right", unknownPower.Lighting.Parameters["direction"]);
    }

    [Fact]
    public void FanCurveUsesPowerDeviceGlobalPrecedenceAndIsDeepCloned()
    {
        var document = ProfileDocument.CreateDefault();
        document.Global.Blade.FanCurve = FanCurve(2400);
        document.Devices["1532:02C6"] = new DeviceProfileSettings
        {
            Blade = new BladeProfileSettings { FanCurve = FanCurve(3000) },
        };
        document.PluggedIn.Blade.FanCurve = FanCurve(4200);

        var resolved = ProfileResolver.Resolve(document, BladeDevice(), isPluggedIn: true);

        Assert.NotNull(resolved.Blade.FanCurve);
        Assert.Equal(4200, resolved.Blade.FanCurve!.CpuPoints[0].CpuFanSpeedRpm);
        resolved.Blade.FanCurve.CpuPoints[0] = resolved.Blade.FanCurve.CpuPoints[0] with
        {
            CpuFanSpeedRpm = 5000,
        };
        Assert.Equal(4200, document.PluggedIn.Blade.FanCurve!.CpuPoints[0].CpuFanSpeedRpm);
    }

    private static ViperDpiStagesProfile DpiStages(int dpi) => new()
    {
        ActiveStage = 1,
        Stages = [new ViperDpiStageProfile { Number = 1, X = dpi, Y = dpi }],
    };

    private static BladeFanCurveProfile FanCurve(int cpuRpm) => new()
    {
        TemperatureMode = BladeFanCurveTemperatureMode.Cpu,
        CpuPoints = [new(60, cpuRpm, cpuRpm)],
        GpuPoints = [new(60, cpuRpm, cpuRpm)],
    };
}
