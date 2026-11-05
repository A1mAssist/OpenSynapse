using OpenSynapse.Core.Displays;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Displays;
using OpenSynapse.Windows.Lifecycle;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class BladeGpuModeControllerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnotherSwitchOrRecoveryWaitsForTheWholeTransaction(bool switchMux)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hardware = new FakeHardware
        {
            BeforeSetEnabledAsync = async (enabled, token) =>
            {
                if (enabled) return;
                entered.TrySetResult();
                await release.Task.WaitAsync(token);
            },
        };
        var controller = CreateController(hardware);
        var first = controller.ApplyAsync(0x02C6, BladeGpuMode.Integrated, PowerSourceKind.Battery);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var next = switchMux
                ? controller.ApplyAsync(0x02C6, BladeGpuMode.Discrete, PowerSourceKind.FullPowerAdapter)
                : controller.RestoreDedicatedGpuAsync();
            Assert.False(next.IsCompleted);
            Assert.Equal(0, hardware.MuxCalls);
            release.TrySetResult();
            await Task.WhenAll(first, next).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(DedicatedGpuDeviceState.Enabled, hardware.DedicatedState);
        }
        finally
        {
            release.TrySetResult();
            await first;
        }
    }

    [Theory]
    [InlineData(BladeGpuMode.Integrated)]
    [InlineData(BladeGpuMode.Standard)]
    [InlineData(BladeGpuMode.Auto)]
    public async Task LeavingDiscreteDirectOnlyReleasesMuxAndRequiresRestart(BladeGpuMode mode)
    {
        var hardware = new FakeHardware
        {
            MuxMode = BladeDisplayMuxMode.Discrete,
            DedicatedBlockReason = DedicatedGpuModeBlockReason.InternalDisplayUsesDiscreteGpu,
        };
        var result = await CreateController(hardware).ApplyAsync(0x02C6, mode, PowerSourceKind.Battery);

        Assert.Equal(BladeGpuModeApplyOutcome.RestartRequired, result.Outcome);
        Assert.Equal(BladeDisplayMuxMode.Hybrid, hardware.MuxMode);
        Assert.Equal(1, hardware.MuxCalls);
        Assert.Equal(0, hardware.DisableCalls);
        Assert.Equal(0, hardware.EnableCalls);
    }

    [Theory]
    [InlineData(PowerSourceKind.Battery)]
    [InlineData(PowerSourceKind.FullPowerAdapter)]
    public async Task BackgroundAutoNeverRewritesADiscreteMux(PowerSourceKind power)
    {
        var hardware = new FakeHardware { MuxMode = BladeDisplayMuxMode.Discrete };
        var result = await CreateController(hardware).ApplyAsync(
            0x02C6, BladeGpuMode.Auto, power, allowMuxChange: false);

        Assert.Equal(BladeGpuModeApplyOutcome.Blocked, result.Outcome);
        Assert.Equal(BladeGpuModeBlockReason.DiscreteRouteLocked, result.BlockReason);
        Assert.Equal(0, hardware.MuxCalls);
        Assert.Equal(0, hardware.DisableCalls);
    }

    [Fact]
    public async Task AnUnreadableMuxBlocksDisablingTheGpu()
    {
        var hardware = new FakeHardware { MuxReadable = false };
        var result = await CreateController(hardware).ApplyAsync(
            0x02C6, BladeGpuMode.Integrated, PowerSourceKind.Battery);

        Assert.Equal(BladeGpuModeApplyOutcome.Blocked, result.Outcome);
        Assert.Equal(BladeGpuModeBlockReason.NoMux, result.BlockReason);
        Assert.Equal(0, hardware.DisableCalls);
    }

    [Fact]
    public async Task IntegratedIsBlockedWhenTheModelDoesNotDeclarePnpSwitch()
    {
        var hardware = new FakeHardware();
        var controller = CreateController(hardware);

        var result = await controller.ApplyAsync(
            0x0270,
            BladeGpuMode.Integrated,
            PowerSourceKind.Battery);

        Assert.Equal(BladeGpuModeApplyOutcome.Blocked, result.Outcome);
        Assert.Equal(BladeGpuModeBlockReason.NotDeclared, result.BlockReason);
        Assert.Equal(0, hardware.DisableCalls);
        Assert.Equal(0, hardware.EnableCalls);
    }

    [Fact]
    public async Task AutoOnBatteryDisablesTheDiscreteGpu()
    {
        var hardware = new FakeHardware();
        var controller = CreateController(hardware);

        var result = await controller.ApplyAsync(
            0x02C6,
            BladeGpuMode.Auto,
            PowerSourceKind.Battery);

        Assert.Equal(BladeGpuModeApplyOutcome.Success, result.Outcome);
        Assert.Equal(BladeGpuMode.Integrated, result.Target);
        Assert.Equal(1, hardware.DisableCalls);
        Assert.Equal(DedicatedGpuDeviceState.Disabled, hardware.DedicatedState);
    }

    [Fact]
    public async Task AnActiveExternalDisplayBlocksIntegratedBeforeAnyPnpWrite()
    {
        var hardware = new FakeHardware
        {
            DedicatedBlockReason = DedicatedGpuModeBlockReason.ExternalDisplayConnected,
        };
        var controller = CreateController(hardware);

        var result = await controller.ApplyAsync(
            0x02C6,
            BladeGpuMode.Integrated,
            PowerSourceKind.Battery);

        Assert.Equal(BladeGpuModeApplyOutcome.Blocked, result.Outcome);
        Assert.Equal(BladeGpuModeBlockReason.ExternalDisplay, result.BlockReason);
        Assert.Equal(0, hardware.DisableCalls);
        Assert.Equal(0, hardware.EnableCalls);
    }

    [Fact]
    public async Task DisablingTheDiscreteGpuIsRolledBackWhenTheInternalDisplayDisappears()
    {
        var hardware = new FakeHardware { FailDisableTopology = true };
        var controller = CreateController(hardware);

        var result = await controller.ApplyAsync(
            0x02C6,
            BladeGpuMode.Integrated,
            PowerSourceKind.Battery);

        Assert.Equal(BladeGpuModeApplyOutcome.RolledBack, result.Outcome);
        Assert.Equal(DedicatedGpuDeviceState.Enabled, hardware.DedicatedState);
        Assert.True(hardware.InternalActive);
        Assert.Equal(1, hardware.DisableCalls);
        Assert.Equal(1, hardware.EnableCalls);
        Assert.False(File.Exists(hardware.MarkerPath));
    }

    [Fact]
    public async Task MuxChangeIsRolledBackWhenAnExternalDisplayDisappears()
    {
        var hardware = new FakeHardware
        {
            ExternalActive = true,
            FailMuxTopology = true,
        };
        var controller = CreateController(hardware);

        var result = await controller.ApplyAsync(
            0x02C6,
            BladeGpuMode.Discrete,
            PowerSourceKind.FullPowerAdapter);

        Assert.Equal(BladeGpuModeApplyOutcome.RolledBack, result.Outcome);
        Assert.Equal(BladeDisplayMuxMode.Hybrid, hardware.MuxMode);
        Assert.True(hardware.ExternalActive);
        Assert.Equal(2, hardware.MuxCalls);
    }

    [Fact]
    public async Task ADisabledGpuOnAMuxModelIsNotReenabledAtStartup()
    {
        var hardware = new FakeHardware { DedicatedState = DedicatedGpuDeviceState.Disabled };
        var controller = CreateController(hardware);

        var result = await controller.ReconcileAsync(0x02C6);

        Assert.Null(result);
        Assert.Equal(0, hardware.EnableCalls);
    }

    [Fact]
    public async Task ADisabledGpuOnANonMuxModelIsRecoveredAtStartup()
    {
        var hardware = new FakeHardware { DedicatedState = DedicatedGpuDeviceState.Disabled };
        var controller = CreateController(hardware);

        var result = await controller.ReconcileAsync(0x0270);

        Assert.NotNull(result);
        Assert.Equal(BladeGpuModeApplyOutcome.Success, result.Value.Outcome);
        Assert.Equal(DedicatedGpuDeviceState.Enabled, hardware.DedicatedState);
    }

    [Fact]
    public async Task APreviousProcessMarkerCannotProveItsOldWriterHasTerminated()
    {
        var hardware = new FakeHardware { DedicatedState = DedicatedGpuDeviceState.Disabled };
        Directory.CreateDirectory(Path.GetDirectoryName(hardware.MarkerPath)!);
        File.WriteAllText(hardware.MarkerPath, "{\"CreatedAt\":\"2026-11-02T00:00:00Z\"}");
        var controller = CreateController(hardware);

        try
        {
            var result = await controller.ReconcileAsync(0x02C6);

            Assert.NotNull(result);
            Assert.Equal(BladeGpuModeApplyOutcome.RollbackFailed, result.Value.Outcome);
            Assert.Equal(997, result.Value.ErrorCode);
            Assert.Equal(DedicatedGpuDeviceState.Disabled, hardware.DedicatedState);
            Assert.Equal(0, hardware.EnableCalls);
            Assert.True(File.Exists(hardware.MarkerPath));
        }
        finally
        {
            if (File.Exists(hardware.MarkerPath))
            {
                File.Delete(hardware.MarkerPath);
            }
        }
    }

    [Fact]
    public async Task ADiscreteGpuThatFaultsAfterEnableRollsBackInsteadOfReportingSuccess()
    {
        var hardware = new FakeHardware
        {
            DedicatedState = DedicatedGpuDeviceState.Disabled,
            FaultOnEnable = true,
        };
        var controller = CreateController(hardware);

        var result = await controller.ApplyAsync(
            0x02C6,
            BladeGpuMode.Standard,
            PowerSourceKind.FullPowerAdapter);

        Assert.Equal(BladeGpuModeApplyOutcome.RolledBack, result.Outcome);
        Assert.Equal(DedicatedGpuDeviceState.Disabled, hardware.DedicatedState);
        Assert.Equal(1, hardware.EnableCalls);
        Assert.Equal(1, hardware.DisableCalls);
    }

    [Fact]
    public async Task RestoringAFaultedDiscreteGpuReportsTheProblemCode()
    {
        var hardware = new FakeHardware
        {
            DedicatedState = DedicatedGpuDeviceState.Disabled,
            FaultOnEnable = true,
        };
        var controller = CreateController(hardware);

        var result = await controller.RestoreDedicatedGpuAsync();

        Assert.Equal(BladeGpuModeApplyOutcome.Blocked, result.Outcome);
        Assert.Equal(BladeGpuModeBlockReason.DeviceFaulted, result.BlockReason);
        Assert.Equal(DedicatedGpuProblemCodes.FailedPostStart, result.ErrorCode);
        Assert.Equal(DedicatedGpuDeviceState.Faulted, hardware.DedicatedState);
    }

    [Fact]
    public async Task EnablingFailureRollsBackToTheIntegratedState()
    {
        var hardware = new FakeHardware
        {
            DedicatedState = DedicatedGpuDeviceState.Disabled,
            FailEnableTopology = true,
        };
        var controller = CreateController(hardware);

        var result = await controller.ApplyAsync(
            0x02C6,
            BladeGpuMode.Standard,
            PowerSourceKind.FullPowerAdapter);

        Assert.Equal(BladeGpuModeApplyOutcome.RolledBack, result.Outcome);
        Assert.Equal(DedicatedGpuDeviceState.Disabled, hardware.DedicatedState);
        Assert.Equal(1, hardware.EnableCalls);
        Assert.Equal(1, hardware.DisableCalls);
    }

    private static BladeGpuModeController CreateController(FakeHardware hardware) =>
        new(
            hardware.ReadMuxAsync,
            hardware.SetMuxAsync,
            hardware.GetStatus,
            hardware.SetEnabledAsync,
            hardware.TrySetEnabledSilentlyAsync,
            hardware.RestartAsync,
            hardware.ReadTopology,
            delay: (_, _) => Task.CompletedTask,
            postCheckTimeout: TimeSpan.Zero,
            pollInterval: TimeSpan.Zero,
            recoveryMarkerPath: hardware.MarkerPath, readBootId: () => 100);

    private sealed class FakeHardware
    {
        public DedicatedGpuDeviceState DedicatedState { get; set; } = DedicatedGpuDeviceState.Enabled;
        public DedicatedGpuModeBlockReason DedicatedBlockReason { get; set; } = DedicatedGpuModeBlockReason.None;
        public bool FaultOnEnable { get; set; }
        public Func<bool, CancellationToken, Task>? BeforeSetEnabledAsync { get; set; }
        public bool MuxReadable { get; set; } = true;
        public int ProblemCode { get; private set; }
        public BladeDisplayMuxMode MuxMode { get; set; } = BladeDisplayMuxMode.Hybrid;
        public bool TopologyKnown { get; set; } = true;
        public bool InternalActive { get; set; } = true;
        public bool ExternalActive { get; set; }
        public bool FailDisableTopology { get; set; }
        public bool FailEnableTopology { get; set; }
        public bool FailMuxTopology { get; set; }
        public int DisableCalls { get; private set; }
        public int EnableCalls { get; private set; }
        public int MuxCalls { get; private set; }
        public string MarkerPath { get; } = Path.Combine(
            Path.GetTempPath(),
            "OpenSynapseTests",
            $"gpu-mode-{Guid.NewGuid():N}.json");

        public DedicatedGpuModeStatus GetStatus() => new(
            IsSupported: DedicatedBlockReason is not (
                DedicatedGpuModeBlockReason.NoDiscreteGraphics or
                DedicatedGpuModeBlockReason.NoIntegratedGraphics),
            IsActive: DedicatedState.IsDisabled(),
            CanChange: !DedicatedState.IsFaulted() &&
                DedicatedBlockReason == DedicatedGpuModeBlockReason.None,
            BlockReason: DedicatedState.IsFaulted()
                ? DedicatedGpuModeBlockReason.DiscreteGpuFaulted
                : DedicatedBlockReason,
            DeviceState: DedicatedState,
            ProblemCode: ProblemCode);

        public async Task<DedicatedGpuSwitchResult> SetEnabledAsync(
            bool enabled,
            CancellationToken cancellationToken)
        {
            if (BeforeSetEnabledAsync is { } before) await before(enabled, cancellationToken);
            if (enabled)
            {
                EnableCalls++;
                DedicatedState = FaultOnEnable
                    ? DedicatedGpuDeviceState.Faulted
                    : DedicatedGpuDeviceState.Enabled;
                ProblemCode = FaultOnEnable ? DedicatedGpuProblemCodes.FailedPostStart : 0;
                InternalActive = !FailEnableTopology;
                return new DedicatedGpuSwitchResult(DedicatedGpuSwitchOutcome.Success);
            }

            DisableCalls++;
            DedicatedState = DedicatedGpuDeviceState.Disabled;
            ProblemCode = DedicatedGpuProblemCodes.Disabled;
            InternalActive = !FailDisableTopology;
            ExternalActive = false;
            return new DedicatedGpuSwitchResult(DedicatedGpuSwitchOutcome.Success);
        }

        public async Task<DedicatedGpuSwitchResult?> TrySetEnabledSilentlyAsync(
            bool enabled,
            CancellationToken cancellationToken) =>
            await SetEnabledAsync(enabled, cancellationToken);

        public Task<DedicatedGpuSwitchResult> RestartAsync(CancellationToken cancellationToken)
        {
            DedicatedState = DedicatedGpuDeviceState.Enabled;
            InternalActive = true;
            return Task.FromResult(new DedicatedGpuSwitchResult(DedicatedGpuSwitchOutcome.Success));
        }

        public ValueTask<BladeDisplayMuxStatus> ReadMuxAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(!MuxReadable ? default : new BladeDisplayMuxStatus(
                IsSupported: true,
                CanChange: true,
                Mode: MuxMode,
                BlockReason: BladeDisplayMuxBlockReason.None,
                Capabilities: BladeDisplayMuxCapabilities.Hybrid | BladeDisplayMuxCapabilities.Discrete));

        public ValueTask<BladeDisplayMuxSwitchResult> SetMuxAsync(
            BladeDisplayMuxMode mode,
            CancellationToken cancellationToken)
        {
            MuxCalls++;
            MuxMode = mode;
            if (FailMuxTopology)
            {
                ExternalActive = mode == BladeDisplayMuxMode.Hybrid;
            }

            return ValueTask.FromResult(new BladeDisplayMuxSwitchResult(
                BladeDisplayMuxSwitchOutcome.RestartRequired));
        }

        public WindowsDisplayTopologySnapshot ReadTopology() => new(
            IsKnown: TopologyKnown,
            HasActiveInternalDisplay: InternalActive,
            InternalDisplayRouteKnown: true,
            InternalAdapterName: "Intel",
            HasExternalDisplay: ExternalActive);
    }
}
