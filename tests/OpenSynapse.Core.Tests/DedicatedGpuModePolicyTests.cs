using OpenSynapse.Core.Displays;
using OpenSynapse.Windows.Lifecycle;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class DedicatedGpuModePolicyTests
{
    [Fact]
    public void AHybridLaptopWithTheInternalPanelOnIntegratedGraphicsCanDisableTheDiscreteGpu()
    {
        var snapshot = Snapshot();

        Assert.True(DedicatedGpuModePolicy.IsSupported(snapshot));
        Assert.Equal(DedicatedGpuModeBlockReason.None, DedicatedGpuModePolicy.GetBlockReason(snapshot));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void MissingIntegratedOrDiscreteGraphicsIsUnsupported(bool integrated, bool discrete)
    {
        var snapshot = Snapshot() with
        {
            HasIntegratedGraphics = integrated,
            HasDiscreteGraphics = discrete,
        };

        Assert.False(DedicatedGpuModePolicy.IsSupported(snapshot));
        Assert.Equal(
            integrated ? DedicatedGpuModeBlockReason.NoDiscreteGraphics
                : DedicatedGpuModeBlockReason.NoIntegratedGraphics,
            DedicatedGpuModePolicy.GetBlockReason(snapshot));
    }

    [Fact]
    public void AnInternalPanelDrivenByTheDiscreteGpuIsUnsupported()
    {
        var snapshot = Snapshot() with { InternalDisplayUsesDiscreteGpu = true };

        Assert.False(DedicatedGpuModePolicy.IsSupported(snapshot));
        Assert.Equal(
            DedicatedGpuModeBlockReason.InternalDisplayUsesDiscreteGpu,
            DedicatedGpuModePolicy.GetBlockReason(snapshot));
    }

    [Theory]
    [InlineData(false, true, true, DedicatedGpuModeBlockReason.DisplayTopologyUnavailable)]
    [InlineData(true, false, true, DedicatedGpuModeBlockReason.InternalDisplayUnavailable)]
    [InlineData(true, true, false, DedicatedGpuModeBlockReason.InternalDisplayRouteUnavailable)]
    public void UnknownOrMissingDisplayTopologyBlocksDisabling(
        bool topologyKnown,
        bool internalDisplayActive,
        bool routeKnown,
        DedicatedGpuModeBlockReason expected)
    {
        var snapshot = Snapshot() with
        {
            DisplayTopologyKnown = topologyKnown,
            HasActiveInternalDisplay = internalDisplayActive,
            InternalDisplayRouteKnown = routeKnown,
        };

        Assert.True(DedicatedGpuModePolicy.IsSupported(snapshot));
        Assert.Equal(expected, DedicatedGpuModePolicy.GetBlockReason(snapshot));
    }

    [Fact]
    public void AnExternalDisplayBlocksDisablingButNotReEnabling()
    {
        var enabled = Snapshot() with { HasExternalDisplay = true };
        var disabled = enabled with { DiscreteGpuEnabled = false };

        Assert.Equal(DedicatedGpuModeBlockReason.ExternalDisplayConnected,
            DedicatedGpuModePolicy.GetBlockReason(enabled));
        Assert.Equal(DedicatedGpuModeBlockReason.None,
            DedicatedGpuModePolicy.GetBlockReason(disabled));
    }

    [Fact]
    public void ADisabledDiscreteGpuRemainsRecoverableWhenTopologyCannotBeRead()
    {
        var snapshot = Snapshot() with
        {
            DiscreteGpuEnabled = false,
            DisplayTopologyKnown = false,
            HasActiveInternalDisplay = false,
            InternalDisplayRouteKnown = false,
        };

        Assert.True(DedicatedGpuModePolicy.IsSupported(snapshot));
        Assert.Equal(DedicatedGpuModeBlockReason.None, DedicatedGpuModePolicy.GetBlockReason(snapshot));
    }

    [Fact]
    public void TheElevatedHelperDoesNotSwallowNormalStartupOrUnknownStates()
    {
        Assert.Null(WindowsDedicatedGpuMode.TryRunElevatedCommand([]));
        Assert.Equal(1, WindowsDedicatedGpuMode.TryRunElevatedCommand(
            ["--set-dedicated-gpu", "bogus"]));
    }

    private static DedicatedGpuModeSnapshot Snapshot() => new(
        HasIntegratedGraphics: true,
        HasDiscreteGraphics: true,
        DiscreteGpuEnabled: true,
        DisplayTopologyKnown: true,
        HasActiveInternalDisplay: true,
        InternalDisplayRouteKnown: true,
        InternalDisplayUsesDiscreteGpu: false,
        HasExternalDisplay: false);
}