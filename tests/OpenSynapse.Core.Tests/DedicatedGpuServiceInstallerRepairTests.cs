using System.ComponentModel;
using OpenSynapse.Windows.Lifecycle;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class DedicatedGpuServiceInstallerRepairTests
{
    [Fact]
    public void CorrectExistingServiceNeedsNoWritesOrElevation()
    {
        var service = new Service();
        Assert.True(service.Ensure(mayChange: false));
        Assert.Empty(service.Commands);
        Assert.Equal(0, service.Stops);
    }

    [Theory]
    [InlineData("path")]
    [InlineData("sid")]
    [InlineData("account")]
    [InlineData("type")]
    [InlineData("start")]
    [InlineData("dacl")]
    public void ExistingWrongConfigurationIsRepairedAndReadBack(string field)
    {
        var service = new Service();
        service.Current = field switch
        {
            "path" => service.Current! with { BinaryPath = @"C:\Old\OpenSynapse.App.exe" },
            "sid" => service.Current! with { BinaryPath = service.Command.Replace(Service.Sid, "S-1-5-21-11-22-33-9999") },
            "account" => service.Current! with { Account = "LocalService" },
            "type" => service.Current! with { ServiceType = 0x110 },
            "start" => service.Current! with { StartType = 2 },
            _ => service.Current! with { Dacl = "D:(A;;GA;;;WD)" },
        };
        Assert.True(service.Ensure());
        Assert.True(service.Current!.Matches(service.Command, DedicatedGpuModeServiceInstaller.ServiceSddl));
        Assert.Contains(service.Commands, c => c[0] == (field == "dacl" ? "sdset" : "config"));
        Assert.DoesNotContain(service.Commands, c => c[0] is "start" or "delete");
    }

    [Fact]
    public void WrongRunningServiceIsStoppedBeforeReconfiguration()
    {
        var service = new Service();
        service.Current = service.Current! with { BinaryPath = "old", IsRunning = true };
        Assert.True(service.Ensure());
        Assert.Equal(1, service.Stops);
        Assert.Equal("stop", service.Events[0]);
        Assert.Equal("config", service.Events[1]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedOrUnconfirmedStopPreventsReconfiguration(bool reportedSuccess)
    {
        var service = new Service();
        service.Current = service.Current! with { BinaryPath = "old", IsRunning = true };
        service.StopResult = reportedSuccess;
        service.StopChangesState = false;
        Assert.False(service.Ensure());
        Assert.Empty(service.Commands);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("config")]
    [InlineData("sdset")]
    [InlineData("description")]
    public void EveryScFailureIsReturnedInsteadOfReportingRegistrationSuccess(string operation)
    {
        var service = new Service { FailOperation = operation };
        service.Current = operation == "create" ? null : service.Current! with { BinaryPath = "old", Dacl = "D:(A;;GA;;;WD)" };
        Assert.False(service.Ensure());
        Assert.Contains(service.Commands, c => c[0] == operation);
    }

    [Fact]
    public void SuccessfulScExitWithoutEffectiveConfigurationChangeIsFailure()
    {
        var service = new Service { IgnoreChanges = true };
        service.Current = service.Current! with { BinaryPath = "old" };
        Assert.False(service.Ensure());
    }

    [Fact]
    public void CreationRaceStillValidatesAndRepairsTheExistingService()
    {
        var service = new Service { Current = null, CreateAlreadyExists = true };
        Assert.True(service.Ensure());
        Assert.Contains(service.Commands, c => c[0] == "config");
    }

    [Fact]
    public void MissingServiceIsCreatedSecuredAndVerifiedWithoutStartingIt()
    {
        var service = new Service { Current = null };
        Assert.True(service.Ensure());
        Assert.Contains(service.Commands, c => c[0] == "create");
        Assert.Contains(service.Commands, c => c[0] == "sdset");
        Assert.DoesNotContain(service.Commands, c => c[0] == "start");
    }

    [Fact]
    public void AnUnprotectedExecutableCannotBecomeALocalSystemService()
    {
        var service = new Service();
        Assert.False(service.Ensure(isProtected: false));
        Assert.Empty(service.Commands);
        Assert.Equal(0, service.Reads);
    }

    [Fact]
    public void AnUnprivilegedRepairDoesNotIssueAnyCommands()
    {
        var service = new Service();
        service.Current = service.Current! with { BinaryPath = "old" };
        Assert.False(service.Ensure(mayChange: false));
        Assert.Empty(service.Commands);
    }

    [Fact]
    public void AccessDeniedDuringQueryIsNotTreatedAsMissingOrCreated()
    {
        var commands = 0;
        Assert.Throws<Win32Exception>(() => DedicatedGpuModeServiceInstaller.EnsureInstalled(Service.Executable, Service.Sid,
            true, _ => true, () => throw new Win32Exception(5), _ => { commands++; return new(0, ""); }, () => true));
        Assert.Equal(0, commands);
    }

    [Theory]
    [InlineData("O:BAD:(A;;FA;;;BA)(A;;FA;;;SY)(A;;FR;;;BU)", true)]
    [InlineData("O:BAD:(A;;FA;;;BA)(A;;FW;;;BU)", false)]
    [InlineData("O:BAD:(A;;FA;;;BA)(A;;DC;;;BU)", false)]
    [InlineData("O:BUD:(A;;FR;;;BU)(A;;FA;;;BA)", false)]
    [InlineData("O:BAD:NO_ACCESS_CONTROL", false)]
    public void WritableOrUserOwnedFileSystemDescriptorsAreNotProtected(string sddl, bool expected) =>
        Assert.Equal(expected, DedicatedGpuServiceConfiguration.IsProtectedDescriptor(sddl));

    private sealed class Service
    {
        internal const string Executable = @"C:\Program Files\OpenSynapse\current\OpenSynapse.App.exe";
        internal const string Sid = "S-1-5-21-11-22-33-1001";
        internal string Command => $"\"{Executable}\" {DedicatedGpuModeServiceProtocol.ServiceArgument} {DedicatedGpuModeServiceProtocol.ServiceUserArgument} {Sid}";
        internal DedicatedGpuServiceConfiguration? Current;
        internal readonly List<string[]> Commands = [];
        internal readonly List<string> Events = [];
        internal string? FailOperation;
        internal bool StopResult = true, StopChangesState = true, IgnoreChanges, CreateAlreadyExists;
        internal int Reads, Stops;
        internal Service() => Current = Correct();
        private DedicatedGpuServiceConfiguration Correct() => new(Command, "LocalSystem", 3, 0x10, 1, DedicatedGpuModeServiceInstaller.ServiceSddl, false);
        internal bool Ensure(bool mayChange = true, bool isProtected = true) => DedicatedGpuModeServiceInstaller.EnsureInstalled(
            Executable, Sid, mayChange, _ => isProtected, () => { Reads++; return Current; }, Run, Stop);
        private bool Stop()
        {
            Stops++; Events.Add("stop");
            if (StopChangesState && Current is not null) Current = Current with { IsRunning = false };
            return StopResult;
        }
        private DedicatedGpuModeServiceInstaller.ScResult Run(string[] command)
        {
            Commands.Add(command); Events.Add(command[0]);
            if (command[0] == FailOperation) return new(5, "denied");
            if (command[0] == "create")
            {
                Current = Correct() with { Dacl = "D:(A;;GA;;;WD)" };
                if (CreateAlreadyExists) { Current = Current with { BinaryPath = "old" }; return new(1073, "exists"); }
            }
            if (!IgnoreChanges && command[0] == "config") Current = Correct() with { Dacl = Current!.Dacl };
            if (!IgnoreChanges && command[0] == "sdset") Current = Current! with { Dacl = DedicatedGpuModeServiceInstaller.ServiceSddl };
            return new(0, string.Empty);
        }
    }
}
