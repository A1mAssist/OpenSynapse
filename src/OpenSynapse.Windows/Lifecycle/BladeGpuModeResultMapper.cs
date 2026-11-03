using OpenSynapse.Core.Displays;

namespace OpenSynapse.Windows.Lifecycle;

internal static class BladeGpuModeResultMapper
{
    public static BladeGpuModeApplyResult Blocked(
        BladeGpuMode target,
        BladeGpuModeBlockReason reason) =>
        new(BladeGpuModeApplyOutcome.Blocked, target, reason);

    public static BladeGpuModeBlockReason GetBlockedReason(
        in BladeGpuModeSnapshot snapshot,
        DedicatedGpuModeStatus dedicated,
        BladeGpuMode target)
    {
        if (target == BladeGpuMode.Integrated && !snapshot.PnpSwitchAllowed)
        {
            return BladeGpuModeBlockReason.NotDeclared;
        }

        if (target == BladeGpuMode.Integrated && !snapshot.MuxPresent)
        {
            return BladeGpuModeBlockReason.NoMux;
        }

        if (target == BladeGpuMode.Discrete && !snapshot.MuxPresent)
        {
            return BladeGpuModeBlockReason.NoMux;
        }

        return MapDedicatedBlockReason(dedicated.BlockReason);
    }

    public static BladeGpuModeApplyResult FromEnableVerification(
        BladeGpuMode target,
        DedicatedGpuModeStatus status) =>
        status.DeviceState.IsFaulted()
            ? new BladeGpuModeApplyResult(
                BladeGpuModeApplyOutcome.Blocked,
                target,
                BladeGpuModeBlockReason.DeviceFaulted,
                status.ProblemCode)
            : new BladeGpuModeApplyResult(
                BladeGpuModeApplyOutcome.Failed,
                target,
                BladeGpuModeBlockReason.DisplayTopology);

    public static BladeGpuModeApplyResult FromDedicated(
        BladeGpuMode target,
        DedicatedGpuSwitchResult result,
        DedicatedGpuModeBlockReason blockReason) =>
        result.Outcome switch
        {
            DedicatedGpuSwitchOutcome.Success =>
                new BladeGpuModeApplyResult(BladeGpuModeApplyOutcome.Success, target),
            DedicatedGpuSwitchOutcome.RestartRequired =>
                new BladeGpuModeApplyResult(BladeGpuModeApplyOutcome.RestartRequired, target),
            DedicatedGpuSwitchOutcome.Cancelled =>
                new BladeGpuModeApplyResult(BladeGpuModeApplyOutcome.Cancelled, target),
            DedicatedGpuSwitchOutcome.NoDevice =>
                new BladeGpuModeApplyResult(BladeGpuModeApplyOutcome.NoDevice, target),
            DedicatedGpuSwitchOutcome.Blocked =>
                new BladeGpuModeApplyResult(
                    BladeGpuModeApplyOutcome.Blocked,
                    target,
                    MapDedicatedBlockReason(blockReason)),
            DedicatedGpuSwitchOutcome.DeviceFaulted =>
                new BladeGpuModeApplyResult(
                    BladeGpuModeApplyOutcome.Blocked,
                    target,
                    BladeGpuModeBlockReason.DeviceFaulted,
                    result.ExitCode),
            _ => new BladeGpuModeApplyResult(
                BladeGpuModeApplyOutcome.Failed,
                target,
                BladeGpuModeBlockReason.Unsupported,
                result.ExitCode),
        };

    public static BladeGpuModeApplyResult FromMux(
        BladeGpuMode target,
        BladeDisplayMuxSwitchResult result) =>
        result.Outcome switch
        {
            BladeDisplayMuxSwitchOutcome.Success =>
                new BladeGpuModeApplyResult(BladeGpuModeApplyOutcome.Success, target),
            BladeDisplayMuxSwitchOutcome.RestartRequired =>
                new BladeGpuModeApplyResult(BladeGpuModeApplyOutcome.RestartRequired, target),
            BladeDisplayMuxSwitchOutcome.Unsupported =>
                new BladeGpuModeApplyResult(
                    BladeGpuModeApplyOutcome.Blocked,
                    target,
                    BladeGpuModeBlockReason.NoMux),
            BladeDisplayMuxSwitchOutcome.NoDevice =>
                new BladeGpuModeApplyResult(BladeGpuModeApplyOutcome.NoDevice, target),
            BladeDisplayMuxSwitchOutcome.Restored =>
                new BladeGpuModeApplyResult(BladeGpuModeApplyOutcome.RolledBack, target),
            BladeDisplayMuxSwitchOutcome.RestoreFailed =>
                new BladeGpuModeApplyResult(BladeGpuModeApplyOutcome.RollbackFailed, target),
            _ => new BladeGpuModeApplyResult(
                BladeGpuModeApplyOutcome.Failed,
                target,
                BladeGpuModeBlockReason.Unsupported,
                result.ErrorCode),
        };

    private static BladeGpuModeBlockReason MapDedicatedBlockReason(
        DedicatedGpuModeBlockReason reason) =>
        reason switch
        {
            DedicatedGpuModeBlockReason.None => BladeGpuModeBlockReason.Unsupported,
            DedicatedGpuModeBlockReason.NoDiscreteGraphics => BladeGpuModeBlockReason.NoDevice,
            DedicatedGpuModeBlockReason.NoIntegratedGraphics => BladeGpuModeBlockReason.Unsupported,
            DedicatedGpuModeBlockReason.DiscreteGpuFaulted => BladeGpuModeBlockReason.DeviceFaulted,
            DedicatedGpuModeBlockReason.DisplayTopologyUnavailable => BladeGpuModeBlockReason.DisplayTopology,
            DedicatedGpuModeBlockReason.InternalDisplayUnavailable => BladeGpuModeBlockReason.DisplayTopology,
            DedicatedGpuModeBlockReason.InternalDisplayRouteUnavailable => BladeGpuModeBlockReason.DisplayTopology,
            DedicatedGpuModeBlockReason.InternalDisplayUsesDiscreteGpu =>
                BladeGpuModeBlockReason.InternalDisplayUnsupported,
            DedicatedGpuModeBlockReason.ExternalDisplayConnected => BladeGpuModeBlockReason.ExternalDisplay,
            _ => BladeGpuModeBlockReason.Unsupported,
        };
}
