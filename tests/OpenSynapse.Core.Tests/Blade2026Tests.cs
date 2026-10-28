using OpenSynapse.Core.Displays;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class Blade2026Tests
{
    [Theory]
    [InlineData(0x02E0, "Razer Blade 16 (2026)", 17)]
    [InlineData(0x02E1, "Razer Blade 18 (2026)", 19)]
    public void The2026ManifestsExposeTheBladeControlChannel(
        ushort productId,
        string displayName,
        int columns)
    {
        var manifest = RazerDeviceRegistry.BuiltIn.Find(0x1532, productId);

        Assert.NotNull(manifest);
        Assert.Equal("blade-671", manifest!.ProtocolFamily);
        Assert.Equal(displayName, manifest.DisplayName);
        Assert.Equal(6, manifest.MatrixRows);
        Assert.Equal(columns, manifest.MatrixColumns);
        Assert.Equal(0x1F, manifest.GetRequiredCapability("thermal-state.get").TransactionId);
        Assert.Equal(0x0D, manifest.GetRequiredCapability("fan-target.get").CommandClass);
        Assert.Equal(0x81, manifest.GetRequiredCapability("fan-target.get").CommandId);
        Assert.Equal(0x88, manifest.GetRequiredCapability("current-fan-rpm.get").CommandId);
        Assert.Equal(0x92, manifest.GetRequiredCapability("charge-limit.get").CommandId);
        Assert.Equal(0x8C, manifest.GetRequiredCapability("adapter-power.get").CommandId);
        Assert.Equal(0x89, manifest.GetRequiredCapability("display-mux.get").CommandId);
        Assert.Equal(0x8F, manifest.GetRequiredCapability("sku-hardware-configuration.get").CommandId);
        Assert.True(manifest.GetRequiredCapability("sku-hardware-configuration.get")
            .AllowRemainingPacketsMismatch);
    }

    [Fact]
    public void Blade16_2026KeepsHyperBoostAndTheFanControls()
    {
        Assert.True(BladeModelCapabilities.SupportsHyperBoost(0x02E0));
        Assert.False(BladeModelCapabilities.SupportsMaxFan(0x02E0));
        Assert.True(BladeModelCapabilities.SupportsFixedFan(0x02E0));
        Assert.True(BladeModelCapabilities.SupportsFanCurve(0x02E0));
        Assert.Equal(3300, BladeModelCapabilities.MinimumFanTargetRpmFor(0x02E0));
        Assert.Equal(5400, BladeModelCapabilities.MaximumFanTargetRpmFor(0x02E0));
        Assert.Equal(6, BladeModelCapabilities.PerformanceModesFor(0x02E0).Count);
        Assert.Contains(BladePerformanceMode.Hyperboost, BladeModelCapabilities.PerformanceModesFor(0x02E0));
    }

    [Fact]
    public void Blade18_2026KeepsTheFanControlsWithoutHyperBoost()
    {
        Assert.False(BladeModelCapabilities.SupportsHyperBoost(0x02E1));
        Assert.False(BladeModelCapabilities.SupportsMaxFan(0x02E1));
        Assert.True(BladeModelCapabilities.SupportsFixedFan(0x02E1));
        Assert.True(BladeModelCapabilities.SupportsFanCurve(0x02E1));
        Assert.Equal(3300, BladeModelCapabilities.MinimumFanTargetRpmFor(0x02E1));
        Assert.Equal(5400, BladeModelCapabilities.MaximumFanTargetRpmFor(0x02E1));
        Assert.Equal(6, BladeModelCapabilities.PerformanceModesFor(0x02E1).Count);
        Assert.Contains(BladePerformanceMode.Turbo, BladeModelCapabilities.PerformanceModesFor(0x02E1));
        Assert.DoesNotContain(BladePerformanceMode.Hyperboost, BladeModelCapabilities.PerformanceModesFor(0x02E1));
    }

    [Fact]
    public void The2026FamilyUsesTheSharedBladeTransportReports()
    {
        var manifest = RazerDeviceRegistry.BuiltIn.Find(0x1532, 0x02E0);
        Assert.NotNull(manifest);

        Assert.Equal(0x0D, manifest!.GetRequiredCapability("thermal-state.set").CommandClass);
        Assert.Equal(0x02, manifest.GetRequiredCapability("thermal-state.set").CommandId);
        Assert.Equal(0x07, manifest.GetRequiredCapability("power-mode.get").CommandClass);
        Assert.Equal(0x8F, manifest.GetRequiredCapability("power-mode.get").CommandId);
        Assert.Equal(0x09, manifest.GetRequiredCapability("display-mux.set").CommandId);
        Assert.True(manifest.GetRequiredCapability("adapter-power.get").AllowRemainingPacketsMismatch);
        Assert.Equal(RazerFeatureReport.Length, 91);
    }

    [Theory]
    [InlineData(0x02E0, 17)]
    [InlineData(0x02E1, 19)]
    public void The2026ManifestsOwnTheNativeKeyboardReports(ushort productId, int columns)
    {
        var manifest = RazerDeviceRegistry.BuiltIn.Find(0x1532, productId);
        Assert.NotNull(manifest);

        Assert.Equal((ushort)0x0001, manifest!.Collection.UsagePage);
        Assert.Equal((ushort)0x0002, manifest.Collection.Usage);
        Assert.Equal((ushort)RazerFeatureReport.Length, manifest.Collection.FeatureReportLength);
        Assert.Equal(6, manifest.MatrixRows);
        Assert.Equal(columns, manifest.MatrixColumns);
        Assert.Contains("keyboard-brightness.get", manifest.Capabilities.Keys);
        Assert.Contains("keyboard-brightness.set", manifest.Capabilities.Keys);
        Assert.Contains("keyboard-effect.get", manifest.Capabilities.Keys);
        Assert.Contains("keyboard-effect.set", manifest.Capabilities.Keys);
    }

    [Fact]
    public void The2026ThermalSelectorsUseProfileAndClassOne()
    {
        Assert.True(BladeModelCapabilities.UsesBlade2026ThermalSelector(0x02E0));
        Assert.True(BladeModelCapabilities.UsesBlade2026ThermalSelector(0x02E1));
        Assert.False(BladeModelCapabilities.UsesBlade2026ThermalSelector(0x02C6));
        Assert.False(BladeModelCapabilities.UsesBlade2026ThermalSelector(0x029F));

        var get = BladeFanProtocol.CreateGetTargetRequest(BladeFanProtocol.ZoneCpu, 0x01);
        Assert.Equal(0x01, get[RazerFeatureReport.ArgumentsOffset]);
        Assert.Equal(BladeFanProtocol.ZoneCpu, get[RazerFeatureReport.ArgumentsOffset + 1]);

        var set = BladeFanProtocol.CreateSetTargetRequest(
            BladeFanProtocol.ZoneCpu,
            4200,
            5400,
            0x01);
        Assert.Equal(0x01, set[RazerFeatureReport.ArgumentsOffset]);
        Assert.Equal(0x2A, set[RazerFeatureReport.ArgumentsOffset + 2]);

        var response = get.ToArray();
        response[1] = 0x02;
        response[RazerFeatureReport.ArgumentsOffset + 2] = 0x2A;
        response[89] = RazerFeatureReport.CalculateCrc(response);
        Assert.Equal(
            4200,
            BladeFanProtocol.ParseTarget(
                response,
                BladeFanProtocol.ZoneCpu,
                get,
                5400,
                0x01));
    }

    [Fact]
    public void The2026LightingStaysOnTheNativeMatrixPath()
    {
        Assert.False(BladeLighting2023Admitted.UsesFirmwareKeyboardLighting(0x02E0));
        Assert.False(BladeLighting2023Admitted.UsesFirmwareKeyboardLighting(0x02E1));

        var frame = Enumerable.Repeat(new RazerRgb(1, 2, 3), 6 * 17).ToArray();
        var requests = BladeLightingLedMatrixProtocol.CreateMatrixFrameRequests(frame);
        Assert.Equal(7, requests.Length);
        Assert.All(requests[..6], request => Assert.Equal(0x03, request[7]));
        Assert.Equal(0x02, requests[6][8]);
    }

    [Fact]
    public void The2026MuxPolicyKeepsDdsOutOfTheSwitcher()
    {
        var capabilities = BladeDisplayMuxCapabilities.Hybrid |
            BladeDisplayMuxCapabilities.Discrete |
            BladeDisplayMuxCapabilities.DynamicDisplaySwitch;

        Assert.Equal(
            BladeDisplayMuxCapabilities.Hybrid | BladeDisplayMuxCapabilities.Discrete,
            BladeDisplayMuxModes.AdmittedCapabilities(capabilities, allowDds: false));
        Assert.False(BladeDisplayMuxModes.Supports(
            capabilities, BladeDisplayMuxMode.Dds, allowDds: false));
        Assert.True(BladeDisplayMuxModes.CanSwitch(capabilities, allowDds: false));
        Assert.Equal(
            new[] { BladeDisplayMuxMode.Hybrid, BladeDisplayMuxMode.Discrete },
            BladeDisplayMuxModes.GetSupportedModes(capabilities, allowDds: false));
    }
}
