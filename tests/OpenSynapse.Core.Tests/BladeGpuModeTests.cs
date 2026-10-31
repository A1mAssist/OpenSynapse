using OpenSynapse.Core.Displays;
using OpenSynapse.Core.Sensors;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class BladeGpuModeTests
{
    private static BladeDisplayMuxStatus Mux(
        BladeDisplayMuxCapabilities capabilities,
        BladeDisplayMuxMode mode,
        bool switchable = true) =>
        new(
            IsSupported: switchable,
            CanChange: switchable,
            Mode: mode,
            BlockReason: switchable
                ? BladeDisplayMuxBlockReason.None
                : BladeDisplayMuxBlockReason.MissingCapability,
            Capabilities: capabilities);

    [Fact]
    public void AnOptimusLaptopWithoutMuxOffersIntegratedStandardAndAuto()
    {
        var snapshot = new BladeGpuModeSnapshot(
            DedicatedGpuSupported: true,
            DedicatedGpuEnabled: true,
            Mux: default);

        Assert.Equal(
            [BladeGpuMode.Integrated, BladeGpuMode.Standard, BladeGpuMode.Auto],
            BladeGpuModes.GetAvailableModes(snapshot));
        Assert.Equal(BladeGpuMode.Standard, BladeGpuModes.GetCurrentMode(snapshot));
    }

    [Fact]
    public void ADisabledDiscreteGpuReadsAsIntegrated()
    {
        var snapshot = new BladeGpuModeSnapshot(
            DedicatedGpuSupported: true,
            DedicatedGpuEnabled: false,
            Mux: Mux(
                BladeDisplayMuxCapabilities.Hybrid | BladeDisplayMuxCapabilities.Discrete,
                BladeDisplayMuxMode.Hybrid));

        Assert.Equal(BladeGpuMode.Integrated, BladeGpuModes.GetCurrentMode(snapshot));
    }

    [Fact]
    public void ASwitchableMuxOffersAllFourModes()
    {
        var snapshot = new BladeGpuModeSnapshot(
            DedicatedGpuSupported: true,
            DedicatedGpuEnabled: true,
            Mux: Mux(
                BladeDisplayMuxCapabilities.Hybrid | BladeDisplayMuxCapabilities.Discrete,
                BladeDisplayMuxMode.Hybrid));

        Assert.Equal(BladeGpuModes.All, BladeGpuModes.GetAvailableModes(snapshot));
    }

    [Fact]
    public void ADiscreteDirectRouteReadsAsDiscrete()
    {
        var snapshot = new BladeGpuModeSnapshot(
            DedicatedGpuSupported: true,
            DedicatedGpuEnabled: true,
            Mux: Mux(
                BladeDisplayMuxCapabilities.Hybrid | BladeDisplayMuxCapabilities.Discrete,
                BladeDisplayMuxMode.Discrete));

        Assert.Equal(BladeGpuMode.Discrete, BladeGpuModes.GetCurrentMode(snapshot));
        Assert.True(BladeGpuModes.IsAvailable(snapshot, BladeGpuMode.Standard));
    }

    [Fact]
    public void AMuxStuckOnDiscreteStillReportsDiscreteButHidesStandardAndAuto()
    {
        var snapshot = new BladeGpuModeSnapshot(
            DedicatedGpuSupported: true,
            DedicatedGpuEnabled: true,
            Mux: Mux(
                BladeDisplayMuxCapabilities.Discrete,
                BladeDisplayMuxMode.Discrete,
                switchable: false));

        Assert.Equal(
            [BladeGpuMode.Integrated, BladeGpuMode.Discrete],
            BladeGpuModes.GetAvailableModes(snapshot));
        Assert.Equal(BladeGpuMode.Discrete, BladeGpuModes.GetCurrentMode(snapshot));
    }

    [Fact]
    public void ADdsRouteWithoutHybridCapabilityStillOffersStandardAsTheCurrentMode()
    {
        // 0D:8A can report Discrete only while 0D:89 says DDS; the panel is already on the
        // iGPU route, so Standard must stay selectable or the selector would show nothing.
        var snapshot = new BladeGpuModeSnapshot(
            DedicatedGpuSupported: true,
            DedicatedGpuEnabled: true,
            Mux: Mux(BladeDisplayMuxCapabilities.Discrete, BladeDisplayMuxMode.Dds, switchable: false));

        Assert.Equal(
            [BladeGpuMode.Integrated, BladeGpuMode.Standard, BladeGpuMode.Auto],
            BladeGpuModes.GetAvailableModes(snapshot));
        Assert.Equal(BladeGpuMode.Standard, BladeGpuModes.GetCurrentMode(snapshot));
    }

    [Fact]
    public void AHybridOnlyMuxHasNoDiscreteMode()
    {
        var snapshot = new BladeGpuModeSnapshot(
            DedicatedGpuSupported: true,
            DedicatedGpuEnabled: true,
            Mux: Mux(BladeDisplayMuxCapabilities.Hybrid, BladeDisplayMuxMode.Hybrid));

        Assert.False(BladeGpuModes.IsAvailable(snapshot, BladeGpuMode.Discrete));
    }

    [Fact]
    public void AMachineWithoutADiscreteGpuOffersNoModes()
    {
        var snapshot = new BladeGpuModeSnapshot(
            DedicatedGpuSupported: false,
            DedicatedGpuEnabled: true,
            Mux: default);

        Assert.Empty(BladeGpuModes.GetAvailableModes(snapshot));
    }

    [Theory]
    [InlineData(PowerSourceKind.Battery, BladeGpuMode.Integrated)]
    [InlineData(PowerSourceKind.FullPowerAdapter, BladeGpuMode.Standard)]
    [InlineData(PowerSourceKind.LowPowerAdapter, BladeGpuMode.Standard)]
    [InlineData(PowerSourceKind.Unknown, BladeGpuMode.Standard)]
    public void AutoFollowsThePowerSource(PowerSourceKind source, BladeGpuMode expected) =>
        Assert.Equal(expected, BladeGpuModes.ResolveAutoTarget(source));
}
