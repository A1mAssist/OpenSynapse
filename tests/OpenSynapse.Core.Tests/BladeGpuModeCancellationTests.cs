using OpenSynapse.Core.Displays;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Displays;
using OpenSynapse.Windows.Lifecycle;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class BladeGpuModeCancellationTests
{
    [Fact]
    public async Task CancellationWaitsForTheOldHelperThenRestoresBeforeTheNextOperation()
    {
        using var hardware = new Hardware();
        using var cancel = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        hardware.Set = async (enabled, token) =>
        {
            hardware.Events.Add(enabled ? "enable" : "dispatch-disable");
            if (!enabled)
            {
                return await WindowsDedicatedGpuMode.RunElevatedHelperAsync(() => CompleteHelper(), token);
            }
            Assert.False(token.CanBeCanceled);
            hardware.Disabled = false;
            return new(DedicatedGpuSwitchOutcome.Success);
        };
        async Task<DedicatedGpuSwitchResult> CompleteHelper()
        {
            entered.TrySetResult();
            await release.Task;
            hardware.Disabled = true;
            hardware.Events.Add("terminal-disable");
            return new(DedicatedGpuSwitchOutcome.Success);
        }
        var controller = hardware.Controller();
        var apply = hardware.Integrated(controller, cancel.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancel.Cancel();
            var next = controller.RestoreDedicatedGpuAsync();
            Assert.False(apply.IsCompleted);
            Assert.False(next.IsCompleted);
            Assert.True(File.Exists(hardware.Marker));
            Assert.Equal(["dispatch-disable"], hardware.Events);
            release.TrySetResult();
            var result = await apply.WaitAsync(TimeSpan.FromSeconds(5));
            await next.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(BladeGpuModeApplyOutcome.RolledBack, result.Outcome);
            Assert.Equal(["dispatch-disable", "terminal-disable", "enable"], hardware.Events);
            Assert.False(hardware.Disabled);
            Assert.False(File.Exists(hardware.Marker));
        }
        finally { release.TrySetResult(); await apply; }
    }

    [Fact]
    public async Task FailedTerminalDisableMayHaveChangedHardwareAndMustRestore()
    {
        using var hardware = new Hardware();
        hardware.Set = (enabled, token) =>
        {
            hardware.Disabled = !enabled;
            hardware.Events.Add(enabled ? "restore" : "failed-disable");
            if (enabled) Assert.False(token.CanBeCanceled);
            return Task.FromResult(new DedicatedGpuSwitchResult(
                enabled ? DedicatedGpuSwitchOutcome.Success : DedicatedGpuSwitchOutcome.Failed));
        };
        var result = await hardware.Integrated(hardware.Controller());
        Assert.Equal(BladeGpuModeApplyOutcome.RolledBack, result.Outcome);
        Assert.Equal(["failed-disable", "restore"], hardware.Events);
        Assert.False(hardware.Disabled);
        Assert.False(File.Exists(hardware.Marker));
    }

    [Fact]
    public async Task CancellationBeforeDispatchClearsTheMarkerWithoutPretendingToRestore()
    {
        using var hardware = new Hardware();
        hardware.Set = (_, _) => Task.FromResult(new DedicatedGpuSwitchResult(DedicatedGpuSwitchOutcome.Cancelled));
        var result = await hardware.Integrated(hardware.Controller());
        Assert.Equal(BladeGpuModeApplyOutcome.Cancelled, result.Outcome);
        Assert.False(hardware.Disabled);
        Assert.False(File.Exists(hardware.Marker));
    }

    [Fact]
    public async Task ACancelledPostCheckDoesNotUseTheCancelledTokenForRecovery()
    {
        using var hardware = new Hardware();
        using var cancel = new CancellationTokenSource();
        hardware.Set = (enabled, token) =>
        {
            hardware.Disabled = !enabled;
            if (!enabled) cancel.Cancel();
            else Assert.False(token.CanBeCanceled);
            return Task.FromResult(new DedicatedGpuSwitchResult(DedicatedGpuSwitchOutcome.Success));
        };
        var result = await hardware.Integrated(hardware.Controller(), cancel.Token);
        Assert.Equal(BladeGpuModeApplyOutcome.RolledBack, result.Outcome);
        Assert.False(hardware.Disabled);
        Assert.False(File.Exists(hardware.Marker));
    }

    [Fact]
    public async Task RecoveryFailureRetainsTheMarkerAndReportsRollbackFailed()
    {
        using var hardware = new Hardware();
        using var cancel = new CancellationTokenSource();
        hardware.Set = (enabled, _) =>
        {
            if (enabled) return Task.FromResult(new DedicatedGpuSwitchResult(DedicatedGpuSwitchOutcome.Failed));
            hardware.Disabled = true;
            cancel.Cancel();
            return Task.FromResult(new DedicatedGpuSwitchResult(DedicatedGpuSwitchOutcome.Success));
        };
        var result = await hardware.Integrated(hardware.Controller(), cancel.Token);
        Assert.Equal(BladeGpuModeApplyOutcome.RollbackFailed, result.Outcome);
        Assert.True(hardware.Disabled);
        Assert.True(File.Exists(hardware.Marker));
    }

    [Fact]
    public async Task AConfirmedFailedServiceResponsePrecedesRecoveryAndNeverFallsBackToUac()
    {
        using var hardware = new Hardware();
        var requests = new List<string>();
        hardware.Set = async (enabled, token) =>
        {
            var response = await DedicatedGpuModeServiceClient.SendForResponseAsync(
                new(1, "set", enabled), token, _ => Task.FromResult(true), (request, _) =>
                {
                    Assert.Equal("set", request.Operation);
                    requests.Add(request.Enabled == true ? "enable" : "disable");
                    if (request.Operation == "set" && request.Enabled == false)
                    {
                        hardware.Disabled = true;
                        return Task.FromResult<DedicatedGpuModeServiceResponse?>(
                            new(1, true, DedicatedGpuSwitchOutcome.Failed, 1, null, null));
                    }
                    if (request.Operation == "set") hardware.Disabled = false;
                    return Task.FromResult<DedicatedGpuModeServiceResponse?>(new(1, true, DedicatedGpuSwitchOutcome.Success, 0, null, null));
                }, (_, _) => throw new InvalidOperationException("must not retry"));
            Assert.NotNull(response);
            return new(response.Outcome, response.ExitCode);
        };
        var result = await hardware.Integrated(hardware.Controller());
        Assert.Equal(BladeGpuModeApplyOutcome.RolledBack, result.Outcome);
        Assert.Equal(["disable", "enable"], requests);
        Assert.False(hardware.Disabled);
        Assert.False(File.Exists(hardware.Marker));
    }

    [Fact]
    public async Task UnknownDispatchRetainsTheMarkerAndBlocksAllSubsequentWritesAcrossControllers()
    {
        using var hardware = new Hardware();
        var dispatches = 0;
        hardware.Set = (_, _) =>
        {
            dispatches++;
            throw new DedicatedGpuOperationUncertainException("lost response");
        };
        var controller = hardware.Controller();
        var first = await hardware.Integrated(controller);
        Assert.Equal(BladeGpuModeApplyOutcome.RollbackFailed, first.Outcome);
        Assert.Equal(997, first.ErrorCode);
        Assert.True(File.Exists(hardware.Marker));
        // A late writer can still disable now; neither an immediate read nor recovery is safe.
        hardware.Disabled = true;
        foreach (var current in new[] { controller, hardware.Controller() })
        {
            var outcomes = new[]
            {
                await hardware.Integrated(current),
                await current.ApplyAsync(0x02C6, BladeGpuMode.Discrete, PowerSourceKind.FullPowerAdapter),
                await current.RestoreDedicatedGpuAsync(),
                await current.RestoreDedicatedGpuSilentlyAsync(),
                await current.RestartDedicatedGpuAsync(),
                (await current.ReconcileAsync(0x02C6))!.Value,
            };
            Assert.All(outcomes, result =>
            {
                Assert.Equal(BladeGpuModeApplyOutcome.RollbackFailed, result.Outcome);
                Assert.Equal(997, result.ErrorCode);
            });
        }
        Assert.Equal(1, dispatches);
        Assert.True(hardware.Disabled);
        Assert.True(File.Exists(hardware.Marker));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnableOrRestartAlsoRecordsItsUnknownWriterBeforeDispatch(bool restart)
    {
        using var hardware = new Hardware { Disabled = true };
        hardware.Set = (_, _) =>
        {
            Assert.True(File.Exists(hardware.Marker));
            throw new DedicatedGpuOperationUncertainException("terminal state lost");
        };
        var controller = hardware.Controller(restart ? _ => hardware.Set(true, CancellationToken.None) : null);
        var result = restart ? await controller.RestartDedicatedGpuAsync() : await controller.RestoreDedicatedGpuAsync();
        Assert.Equal(BladeGpuModeApplyOutcome.RollbackFailed, result.Outcome);
        Assert.True(File.Exists(hardware.Marker));
        Assert.Equal(997, (await hardware.Controller().RestoreDedicatedGpuAsync()).ErrorCode);
    }

    [Fact]
    public async Task AKnownTerminalRollbackFailureCanUseTheExistingRecoveryPathInTheSameController()
    {
        using var hardware = new Hardware();
        hardware.Set = (enabled, _) =>
        {
            hardware.Disabled = true;
            return Task.FromResult(new DedicatedGpuSwitchResult(DedicatedGpuSwitchOutcome.Failed));
        };
        var controller = hardware.Controller();
        Assert.Equal(BladeGpuModeApplyOutcome.RollbackFailed, (await hardware.Integrated(controller)).Outcome);
        hardware.Set = (enabled, _) =>
        {
            hardware.Disabled = !enabled;
            return Task.FromResult(new DedicatedGpuSwitchResult(DedicatedGpuSwitchOutcome.Success));
        };
        Assert.Equal(BladeGpuModeApplyOutcome.Success, (await controller.ReconcileAsync(0x02C6))!.Value.Outcome);
        Assert.False(File.Exists(hardware.Marker));
        Assert.False(hardware.Disabled);
    }

    [Fact]
    public async Task SilentRecoveryFinishesVerificationAfterItsWriteDespiteCallerCancellation()
    {
        using var hardware = new Hardware { Disabled = true };
        using var cancel = new CancellationTokenSource();
        hardware.Set = (enabled, _) =>
        {
            hardware.Disabled = !enabled;
            cancel.Cancel();
            return Task.FromResult(new DedicatedGpuSwitchResult(DedicatedGpuSwitchOutcome.Success));
        };
        var result = await hardware.Controller().RestoreDedicatedGpuSilentlyAsync(cancel.Token);
        Assert.Equal(BladeGpuModeApplyOutcome.Success, result.Outcome);
        Assert.False(hardware.Disabled);
        Assert.False(File.Exists(hardware.Marker));
    }

    [Fact]
    public async Task ARecoveryCancelledBeforeDispatchDoesNotLeaveANewPendingRecord()
    {
        using var hardware = new Hardware { Disabled = true };
        hardware.Set = (_, _) => Task.FromResult(new DedicatedGpuSwitchResult(DedicatedGpuSwitchOutcome.Cancelled));
        var result = await hardware.Controller().RestoreDedicatedGpuAsync();
        Assert.Equal(BladeGpuModeApplyOutcome.Cancelled, result.Outcome);
        Assert.True(hardware.Disabled);
        Assert.False(File.Exists(hardware.Marker));
    }

    [Fact]
    public async Task UnknownRollbackAlsoBlocksLaterWritesRatherThanRacingANewOperation()
    {
        using var hardware = new Hardware();
        var writes = 0;
        hardware.Set = (enabled, _) =>
        {
            writes++;
            if (enabled) throw new DedicatedGpuOperationUncertainException("restore response lost");
            hardware.Disabled = true;
            return Task.FromResult(new DedicatedGpuSwitchResult(DedicatedGpuSwitchOutcome.Failed));
        };
        var controller = hardware.Controller();
        Assert.Equal(997, (await hardware.Integrated(controller)).ErrorCode);
        Assert.Equal(997, (await controller.RestoreDedicatedGpuAsync()).ErrorCode);
        Assert.Equal(2, writes);
        Assert.True(File.Exists(hardware.Marker));
    }

    [Fact]
    public async Task LostServiceResponseLatchesTheControllerWithoutAReadOrFallback()
    {
        using var hardware = new Hardware();
        var requests = 0;
        hardware.Set = async (enabled, token) =>
        {
            var response = await DedicatedGpuModeServiceClient.SendForResponseAsync(new(1, "set", enabled),
                token, _ => Task.FromResult(true), (_, _) =>
                {
                    requests++;
                    throw new DedicatedGpuOperationUncertainException("write dispatched, response lost");
                }, (_, _) => throw new InvalidOperationException("must not retry"));
            throw new InvalidOperationException($"must not return to fallback: {response}");
        };
        var controller = hardware.Controller();
        Assert.Equal(997, (await hardware.Integrated(controller)).ErrorCode);
        Assert.Equal(997, (await controller.RestoreDedicatedGpuAsync()).ErrorCode);
        Assert.Equal(1, requests);
        Assert.True(File.Exists(hardware.Marker));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AVerifiedNewBootReachesRecoveryAndClearsTheMarker(bool reuseController)
    {
        using var hardware = new Hardware();
        hardware.Set = (_, _) => throw new DedicatedGpuOperationUncertainException("response lost");
        var controller = hardware.Controller();
        Assert.Equal(997, (await hardware.Integrated(controller)).ErrorCode);
        hardware.Disabled = true;
        hardware.BootId = 101;
        var writes = 0;
        hardware.Set = (enabled, _) =>
        {
            Assert.True(enabled);
            writes++;
            hardware.Disabled = false;
            return Task.FromResult(new DedicatedGpuSwitchResult(DedicatedGpuSwitchOutcome.Success));
        };
        var result = await (reuseController ? controller : hardware.Controller()).ReconcileAsync(0x02C6);
        Assert.Equal(BladeGpuModeApplyOutcome.Success, result!.Value.Outcome);
        Assert.Equal(1, writes);
        Assert.False(hardware.Disabled);
        Assert.False(File.Exists(hardware.Marker));
    }

    [Fact]
    public async Task ANewBootRecoveryThatBecomesUnknownIsStampedBeforeDispatchAndCannotRepeat()
    {
        using var hardware = new Hardware();
        var writes = 0;
        hardware.Set = (_, _) =>
        {
            writes++;
            using var record = System.Text.Json.JsonDocument.Parse(File.ReadAllText(hardware.Marker));
            Assert.Equal(hardware.BootId, record.RootElement.GetProperty("BootId").GetUInt32());
            throw new DedicatedGpuOperationUncertainException("response lost");
        };
        Assert.Equal(997, (await hardware.Integrated(hardware.Controller())).ErrorCode);
        hardware.BootId = 101;
        var recovery = hardware.Controller();
        Assert.Equal(997, (await recovery.ReconcileAsync(0x02C6))!.Value.ErrorCode);
        foreach (var controller in new[] { recovery, hardware.Controller() })
            Assert.Equal(997, (await controller.ReconcileAsync(0x02C6))!.Value.ErrorCode);
        Assert.Equal(2, writes);
        Assert.True(File.Exists(hardware.Marker));
    }

    [Fact]
    public async Task LegacyMarkerBindsWithoutWritingHardwareThenRecoversAfterAVerifiedNewBoot()
    {
        using var hardware = new Hardware { Disabled = true };
        Directory.CreateDirectory(Path.GetDirectoryName(hardware.Marker)!);
        File.WriteAllText(hardware.Marker, "{\"CreatedAt\":\"2026-11-02T00:00:00Z\"}");
        var writes = 0;
        hardware.Set = (enabled, _) =>
        {
            writes++;
            hardware.Disabled = !enabled;
            return Task.FromResult(new DedicatedGpuSwitchResult(DedicatedGpuSwitchOutcome.Success));
        };
        Assert.Equal(997, (await hardware.Controller().ReconcileAsync(0x02C6))!.Value.ErrorCode);
        Assert.Equal(0, writes);
        Assert.True(File.Exists(hardware.Marker));
        hardware.BootId = 101;
        Assert.Equal(BladeGpuModeApplyOutcome.Success,
            (await hardware.Controller().ReconcileAsync(0x02C6))!.Value.Outcome);
        Assert.Equal(1, writes);
        Assert.False(File.Exists(hardware.Marker));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(99u)]
    [InlineData(100u)]
    public async Task MissingUnchangedOrRegressedBootIdNeverProvesAnOldWriterHasStopped(uint? bootId)
    {
        using var hardware = new Hardware();
        var writes = 0;
        hardware.Set = (_, _) =>
        {
            writes++;
            throw new DedicatedGpuOperationUncertainException("response lost");
        };
        var controller = hardware.Controller();
        Assert.Equal(997, (await hardware.Integrated(controller)).ErrorCode);
        hardware.BootId = bootId;
        foreach (var current in new[] { controller, hardware.Controller() })
            Assert.Equal(997, (await current.RestoreDedicatedGpuAsync()).ErrorCode);
        Assert.Equal(1, writes);
        Assert.True(File.Exists(hardware.Marker));
    }

    [Fact]
    public async Task AnUnreadableBootIdPreventsTheFirstDisableFromBeingDispatched()
    {
        using var hardware = new Hardware { BootId = null };
        hardware.Set = (_, _) => throw new InvalidOperationException("must not dispatch");
        Assert.Equal(BladeGpuModeApplyOutcome.Failed,
            (await hardware.Integrated(hardware.Controller())).Outcome);
        Assert.False(File.Exists(hardware.Marker));
    }

    [Fact]
    public async Task AfterRebootARunningDeviceStatusAloneDoesNotSkipTheRecoveryWrite()
    {
        using var hardware = new Hardware();
        hardware.Set = (_, _) => throw new DedicatedGpuOperationUncertainException("response lost");
        Assert.Equal(997, (await hardware.Integrated(hardware.Controller())).ErrorCode);
        hardware.BootId = 101;
        var writes = 0;
        hardware.Set = (enabled, _) =>
        {
            Assert.True(enabled);
            writes++;
            return Task.FromResult(new DedicatedGpuSwitchResult(DedicatedGpuSwitchOutcome.Success));
        };
        Assert.Equal(BladeGpuModeApplyOutcome.Success,
            (await hardware.Controller().RestoreDedicatedGpuAsync()).Outcome);
        Assert.Equal(1, writes);
        Assert.False(File.Exists(hardware.Marker));
    }

    [Fact]
    public async Task AfterRebootRestartWithAPendingMarkerUsesVerifiedEnableRecovery()
    {
        using var hardware = new Hardware();
        hardware.Set = (_, _) => throw new DedicatedGpuOperationUncertainException("response lost");
        Assert.Equal(997, (await hardware.Integrated(hardware.Controller())).ErrorCode);
        hardware.BootId = 101;
        hardware.Disabled = true;
        var writes = 0;
        hardware.Set = (enabled, _) =>
        {
            Assert.True(enabled);
            writes++;
            hardware.Disabled = false;
            return Task.FromResult(new DedicatedGpuSwitchResult(DedicatedGpuSwitchOutcome.Success));
        };
        Assert.Equal(BladeGpuModeApplyOutcome.Success,
            (await hardware.Controller().RestartDedicatedGpuAsync()).Outcome);
        Assert.Equal(1, writes);
        Assert.False(File.Exists(hardware.Marker));
    }

    [Fact]
    public async Task ANewBootRecoveryDoesNotClearTheRecordUntilTopologyIsVerified()
    {
        using var hardware = new Hardware();
        hardware.Set = (_, _) => throw new DedicatedGpuOperationUncertainException("response lost");
        Assert.Equal(997, (await hardware.Integrated(hardware.Controller())).ErrorCode);
        hardware.BootId = 101;
        hardware.TopologyHealthy = false;
        hardware.Set = (_, _) => Task.FromResult(new DedicatedGpuSwitchResult(DedicatedGpuSwitchOutcome.Success));
        var controller = hardware.Controller();
        Assert.Equal(BladeGpuModeApplyOutcome.Blocked, (await controller.ReconcileAsync(0x02C6))!.Value.Outcome);
        Assert.True(File.Exists(hardware.Marker));
        hardware.TopologyHealthy = true;
        Assert.Equal(BladeGpuModeApplyOutcome.Success, (await controller.ReconcileAsync(0x02C6))!.Value.Outcome);
        Assert.False(File.Exists(hardware.Marker));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{\"BootId\":\"untrusted\"}")]
    public async Task MalformedRecordsCannotProveANewBootAndAreNotDeleted(string record)
    {
        using var hardware = new Hardware();
        Directory.CreateDirectory(Path.GetDirectoryName(hardware.Marker)!);
        File.WriteAllText(hardware.Marker, record);
        hardware.Set = (_, _) => throw new InvalidOperationException("must not dispatch");
        var controller = hardware.Controller();
        hardware.BootId = 101;
        Assert.Equal(997, (await controller.RestoreDedicatedGpuAsync()).ErrorCode);
        Assert.Equal(record, File.ReadAllText(hardware.Marker));
    }

    private sealed class Hardware : IDisposable
    {
        internal bool Disabled;
        internal bool TopologyHealthy = true;
        internal uint? BootId = 100;
        internal readonly List<string> Events = [];
        internal readonly string Marker = Path.Combine(Path.GetTempPath(), "OpenSynapseTests", $"cancel-{Guid.NewGuid():N}.json");
        internal Func<bool, CancellationToken, Task<DedicatedGpuSwitchResult>> Set = (_, _) =>
            Task.FromResult(new DedicatedGpuSwitchResult(DedicatedGpuSwitchOutcome.Success));
        internal DedicatedGpuModeStatus Status() => new(true, Disabled, true, DedicatedGpuModeBlockReason.None,
            Disabled ? DedicatedGpuDeviceState.Disabled : DedicatedGpuDeviceState.Enabled);
        internal BladeGpuModeController Controller(Func<CancellationToken, Task<DedicatedGpuSwitchResult>>? restart = null) => new(
            _ => ValueTask.FromResult(new BladeDisplayMuxStatus(true, true, BladeDisplayMuxMode.Hybrid,
                BladeDisplayMuxBlockReason.None, BladeDisplayMuxCapabilities.Hybrid | BladeDisplayMuxCapabilities.Discrete)),
            (_, _) => throw new InvalidOperationException("unexpected MUX write"), Status,
            (enabled, token) => Set(enabled, token),
            async (enabled, token) => await Set(enabled, token),
            restart ?? (_ => throw new InvalidOperationException("unexpected restart")),
            () => new(TopologyHealthy, TopologyHealthy, true, "Intel", false),
            (_, _) => Task.CompletedTask, TimeSpan.Zero, TimeSpan.Zero, Marker, () => BootId);
        internal Task<BladeGpuModeApplyResult> Integrated(BladeGpuModeController controller, CancellationToken token = default) =>
            controller.ApplyAsync(0x02C6, BladeGpuMode.Integrated, PowerSourceKind.Battery, token);
        public void Dispose() { if (File.Exists(Marker)) File.Delete(Marker); }
    }
}
