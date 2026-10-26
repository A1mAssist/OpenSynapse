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
        var disabled = enabled with { DiscreteGpuState = DedicatedGpuDeviceState.Disabled };

        Assert.Equal(DedicatedGpuModeBlockReason.ExternalDisplayConnected,
            DedicatedGpuModePolicy.GetBlockReason(enabled));
        Assert.Equal(DedicatedGpuModeBlockReason.None,
            DedicatedGpuModePolicy.GetBlockReason(disabled));
    }

    [Fact]
    public void ADisabledDiscreteGpuRemainsRecoverableEvenIfTheRouteLooksStale()
    {
        var snapshot = Snapshot() with
        {
            DiscreteGpuState = DedicatedGpuDeviceState.Disabled,
            InternalDisplayUsesDiscreteGpu = true,
        };

        Assert.True(DedicatedGpuModePolicy.IsSupported(snapshot));
        Assert.Equal(DedicatedGpuModeBlockReason.None, DedicatedGpuModePolicy.GetBlockReason(snapshot));
    }

    [Fact]
    public void ADisabledDiscreteGpuRemainsRecoverableWhenTopologyCannotBeRead()
    {
        var snapshot = Snapshot() with
        {
            DiscreteGpuState = DedicatedGpuDeviceState.Disabled,
            DisplayTopologyKnown = false,
            HasActiveInternalDisplay = false,
            InternalDisplayRouteKnown = false,
        };

        Assert.True(DedicatedGpuModePolicy.IsSupported(snapshot));
        Assert.Equal(DedicatedGpuModeBlockReason.None, DedicatedGpuModePolicy.GetBlockReason(snapshot));
    }

    [Fact]
    public void MachineWideServiceIsOnlyAcceptedFromProgramFiles()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var machineExe = Path.Combine(
            programFiles,
            "OpenSynapse",
            "current",
            "OpenSynapse.App.exe");
        var portableExe = Path.Combine(
            localAppData,
            "OpenSynapse",
            "current",
            "OpenSynapse.App.exe");
        var lookalike = programFiles + "Evil\\OpenSynapse\\OpenSynapse.App.exe";

        Assert.True(DedicatedGpuModeServiceInstaller.IsMachineInstallPath(machineExe));
        Assert.False(DedicatedGpuModeServiceInstaller.IsMachineInstallPath(portableExe));
        Assert.False(DedicatedGpuModeServiceInstaller.IsMachineInstallPath(lookalike));
    }

    [Fact]
    public void TheElevatedHelperDoesNotSwallowNormalStartupOrUnknownStates()
    {
        Assert.Null(WindowsDedicatedGpuMode.TryRunElevatedCommand([]));
        Assert.Equal(1, WindowsDedicatedGpuMode.TryRunElevatedCommand(
            ["--set-dedicated-gpu", "bogus"]));
    }

    [Fact]
    public void TheElevatedHelperRecognisesTheRestartCommand()
    {
        // The restart command takes no argument; anything else is malformed.
        Assert.Equal(1, WindowsDedicatedGpuMode.TryRunElevatedCommand(
            ["--restart-dedicated-gpu", "extra"]));
        Assert.Null(WindowsDedicatedGpuMode.TryRunElevatedCommand(
            ["--unknown-command"]));
    }

    [Fact]
    public void AFaultedDiscreteGpuBlocksDisablingButStaysRecoverable()
    {
        var snapshot = Snapshot() with { DiscreteGpuState = DedicatedGpuDeviceState.Faulted };

        Assert.Equal(
            DedicatedGpuModeBlockReason.DiscreteGpuFaulted,
            DedicatedGpuModePolicy.GetBlockReason(snapshot));
    }

    [Fact]
    public void AFaultedDiscreteGpuKeepsTheSectionVisibleSoTheRestartStaysReachable()
    {
        var snapshot = Snapshot() with { DiscreteGpuState = DedicatedGpuDeviceState.Faulted };

        // The adapter is present, so the section stays visible to offer the restart.
        Assert.True(DedicatedGpuModePolicy.IsSupported(snapshot));
    }

    [Theory]
    [InlineData(0, DedicatedGpuDeviceState.Enabled)]
    [InlineData(22, DedicatedGpuDeviceState.Disabled)]
    [InlineData(43, DedicatedGpuDeviceState.Faulted)]
    [InlineData(10, DedicatedGpuDeviceState.Faulted)]
    [InlineData(14, DedicatedGpuDeviceState.Faulted)]
    [InlineData(5, DedicatedGpuDeviceState.Faulted)]
    public void AProblemCodeMapsOntoTheMatchingAdapterState(
        int problemCode,
        DedicatedGpuDeviceState expected)
    {
        Assert.Equal(expected, DedicatedGpuDeviceInfo.StateFromProblemCode(problemCode));
    }

    [Fact]
    public void OnlyTheDisabledProblemCodeCountsAsDisabled()
    {
        // Code 43 is a driver failure, not a deliberate power-off. Treating it as
        // disabled would report "already disabled" for an adapter that is faulted.
        Assert.Equal(
            DedicatedGpuDeviceState.Faulted,
            DedicatedGpuDeviceInfo.StateFromProblemCode(DedicatedGpuProblemCodes.FailedPostStart));
        Assert.Equal(
            DedicatedGpuDeviceState.Disabled,
            DedicatedGpuDeviceInfo.StateFromProblemCode(DedicatedGpuProblemCodes.Disabled));
    }

    [Fact]
    public void ARestartRequestIsRoutedToTheRestartAction()
    {
        // The restart carries no enable flag, so it must be resolved before the
        // set action, which would otherwise reject it as a malformed set.
        var request = new DedicatedGpuModeServiceRequest(
            DedicatedGpuModeServiceProtocol.Version,
            DedicatedGpuModeServiceProtocol.RestartOperation,
            Enabled: null);

        Assert.Equal(
            DedicatedGpuModeServiceAction.Restart,
            DedicatedGpuModeServiceRequestParser.Parse(request));
    }

    [Fact]
    public void ASetRequestNeedsAnEnableFlag()
    {
        var withFlag = new DedicatedGpuModeServiceRequest(
            DedicatedGpuModeServiceProtocol.Version,
            DedicatedGpuModeServiceProtocol.SetOperation,
            Enabled: false);
        var withoutFlag = new DedicatedGpuModeServiceRequest(
            DedicatedGpuModeServiceProtocol.Version,
            DedicatedGpuModeServiceProtocol.SetOperation,
            Enabled: null);

        Assert.Equal(
            DedicatedGpuModeServiceAction.Set,
            DedicatedGpuModeServiceRequestParser.Parse(withFlag));
        Assert.Equal(
            DedicatedGpuModeServiceAction.Unsupported,
            DedicatedGpuModeServiceRequestParser.Parse(withoutFlag));
    }

    [Fact]
    public void AStatusRequestNeedsNoFlag()
    {
        var request = new DedicatedGpuModeServiceRequest(
            DedicatedGpuModeServiceProtocol.Version,
            DedicatedGpuModeServiceProtocol.StatusOperation,
            Enabled: null);

        Assert.Equal(
            DedicatedGpuModeServiceAction.Status,
            DedicatedGpuModeServiceRequestParser.Parse(request));
    }

    [Fact]
    public void AnUnknownOperationOrVersionIsUnsupported()
    {
        var unknownOperation = new DedicatedGpuModeServiceRequest(
            DedicatedGpuModeServiceProtocol.Version,
            "bogus",
            Enabled: true);
        var wrongVersion = new DedicatedGpuModeServiceRequest(
            DedicatedGpuModeServiceProtocol.Version + 1,
            DedicatedGpuModeServiceProtocol.StatusOperation,
            Enabled: null);

        Assert.Equal(
            DedicatedGpuModeServiceAction.Unsupported,
            DedicatedGpuModeServiceRequestParser.Parse(unknownOperation));
        Assert.Equal(
            DedicatedGpuModeServiceAction.Unsupported,
            DedicatedGpuModeServiceRequestParser.Parse(wrongVersion));
    }

    private static DedicatedGpuModeSnapshot Snapshot() => new(
        HasIntegratedGraphics: true,
        HasDiscreteGraphics: true,
        DiscreteGpuState: DedicatedGpuDeviceState.Enabled,
        DisplayTopologyKnown: true,
        HasActiveInternalDisplay: true,
        InternalDisplayRouteKnown: true,
        InternalDisplayUsesDiscreteGpu: false,
        HasExternalDisplay: false);
}
