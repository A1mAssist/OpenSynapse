using OpenSynapse.Core.Macros;
using OpenSynapse.Windows.Input;
using OpenSynapse.Windows.Lifecycle;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class ElevatedMacroTests
{
    [Fact]
    public void IntegrityLevelsAboveMediumCountAsElevated()
    {
        Assert.False(ProcessIntegrityLevel.IsElevated(ProcessIntegrityLevel.Medium));
        Assert.False(ProcessIntegrityLevel.IsElevated(0));
        Assert.True(ProcessIntegrityLevel.IsElevated(ProcessIntegrityLevel.High));
        Assert.True(ProcessIntegrityLevel.IsElevated(ProcessIntegrityLevel.System));
    }

    [Fact]
    public void TheRunningProcessReportsAtLeastMediumIntegrity()
    {
        // The test host runs as a normal desktop application, so its own
        // integrity level must be readable. A zero would mean the token
        // query failed and the elevated check would accept any agent.
        var level = ProcessIntegrityLevel.Current();

        Assert.True(
            level >= ProcessIntegrityLevel.Medium,
            $"Expected at least medium integrity, got {level}.");
    }

    [Fact]
    public void TheServiceRoutesTheMacroAgentOperations()
    {
        Assert.Equal(
            DedicatedGpuModeServiceAction.EnsureMacroAgent,
            DedicatedGpuModeServiceRequestParser.Parse(new DedicatedGpuModeServiceRequest(
                DedicatedGpuModeServiceProtocol.Version,
                DedicatedGpuModeServiceProtocol.EnsureMacroAgentOperation,
                Enabled: null)));

        Assert.Equal(
            DedicatedGpuModeServiceAction.StartMacroAgent,
            DedicatedGpuModeServiceRequestParser.Parse(new DedicatedGpuModeServiceRequest(
                DedicatedGpuModeServiceProtocol.Version,
                DedicatedGpuModeServiceProtocol.StartMacroAgentOperation,
                Enabled: null)));
    }

    [Fact]
    public void TheServiceRejectsAnUnknownVersionForTheMacroAgent()
    {
        Assert.Equal(
            DedicatedGpuModeServiceAction.Unsupported,
            DedicatedGpuModeServiceRequestParser.Parse(new DedicatedGpuModeServiceRequest(
                DedicatedGpuModeServiceProtocol.Version + 1,
                DedicatedGpuModeServiceProtocol.EnsureMacroAgentOperation,
                Enabled: null)));
    }

    [Fact]
    public void TheAgentRequestCarriesTheMacroInputVerbatim()
    {
        var input = new MacroInput
        {
            Kind = MacroInputKind.Keyboard,
            VirtualKey = 0x41,
            ScanCode = 0x1E,
            Extended = true,
        };
        var request = new ElevatedMacroRequest
        {
            Kind = ElevatedMacroCommandKind.Input,
            Input = input,
            IsDown = true,
        };

        Assert.True(request.Input!.Matches(input));
        Assert.True(request.IsDown);
        Assert.Equal(ElevatedMacroProtocol.Version, request.Version);
    }

    [Fact]
    public void TheAgentRejectsDeviceScopedInputsItCannotReplay()
    {
        // Naga side buttons and Razer keys have no keyboard representation, so
        // the sink must keep them on the direct path instead of asking the
        // agent to replay them as keystrokes.
        var sideButton = new MacroInput
        {
            Kind = MacroInputKind.NagaSideButton,
            SideButton = 1,
        };
        var razerKey = new MacroInput
        {
            Kind = MacroInputKind.RazerKey,
            DeviceId = 1,
            RazerKeyCode = 0x02,
        };

        Assert.False(new MacroInput
        {
            Kind = MacroInputKind.NagaSideButton,
            SideButton = 1,
        }.IsValid(allowPrimaryMouseButton: false));

        Assert.False(razerKey.IsValid(
            allowPrimaryMouseButton: false,
            allowDeviceButton: false));

        // The sink routes only the kinds the agent can express as keyboard or
        // mouse events.
        Assert.True(sideButton.IsValid(allowPrimaryMouseButton: false, allowNagaSideButton: true));
    }
}
