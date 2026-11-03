using System.Diagnostics;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Displays;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Displays;

namespace OpenSynapse.Windows.Lifecycle;

internal readonly record struct BladeGpuModeState(
    BladeGpuModeSnapshot Snapshot,
    DedicatedGpuModeStatus Dedicated,
    WindowsDisplayTopologySnapshot Topology);

/// <summary>
/// The only place that executes a GPU mode change. It owns the ordering between the
/// PnP adapter switch and the firmware MUX, checks the display topology after each
/// destructive step, and restores the previous route when a display disappears.
/// </summary>
internal sealed class BladeGpuModeController
{
    private static readonly TimeSpan DefaultPostCheckTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(400);

    private readonly Func<CancellationToken, ValueTask<BladeDisplayMuxStatus>> _readMux;
    private readonly Func<BladeDisplayMuxMode, CancellationToken, ValueTask<BladeDisplayMuxSwitchResult>> _setMux;
    private readonly Func<DedicatedGpuModeStatus> _getDedicatedStatus;
    private readonly Func<bool, CancellationToken, Task<DedicatedGpuSwitchResult>> _setDedicatedEnabled;
    private readonly Func<bool, CancellationToken, Task<DedicatedGpuSwitchResult?>> _trySetDedicatedEnabledSilently;
    private readonly Func<CancellationToken, Task<DedicatedGpuSwitchResult>> _restartDedicated;
    private readonly Func<WindowsDisplayTopologySnapshot> _readTopology;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly TimeSpan _postCheckTimeout;
    private readonly TimeSpan _pollInterval;
    private readonly GpuModeRecoveryMarkerStore _recoveryMarker;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private bool _operationUncertain;

    internal BladeGpuModeController(
        Func<CancellationToken, ValueTask<BladeDisplayMuxStatus>> readMux,
        Func<BladeDisplayMuxMode, CancellationToken, ValueTask<BladeDisplayMuxSwitchResult>> setMux,
        Func<DedicatedGpuModeStatus> getDedicatedStatus,
        Func<bool, CancellationToken, Task<DedicatedGpuSwitchResult>> setDedicatedEnabled,
        Func<bool, CancellationToken, Task<DedicatedGpuSwitchResult?>> trySetDedicatedEnabledSilently,
        Func<CancellationToken, Task<DedicatedGpuSwitchResult>> restartDedicated,
        Func<WindowsDisplayTopologySnapshot> readTopology,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        TimeSpan? postCheckTimeout = null,
        TimeSpan? pollInterval = null,
        string? recoveryMarkerPath = null, Func<uint?>? readBootId = null)
    {
        _recoveryMarker = new GpuModeRecoveryMarkerStore(recoveryMarkerPath, readBootId);
        _operationUncertain = _recoveryMarker.Exists && !_recoveryMarker.CanRecoverAfterRestart();
        ArgumentNullException.ThrowIfNull(setDedicatedEnabled);
        ArgumentNullException.ThrowIfNull(trySetDedicatedEnabledSilently);
        ArgumentNullException.ThrowIfNull(restartDedicated);
        _readMux = readMux ?? throw new ArgumentNullException(nameof(readMux));
        _setMux = setMux ?? throw new ArgumentNullException(nameof(setMux));
        _getDedicatedStatus = getDedicatedStatus ?? throw new ArgumentNullException(nameof(getDedicatedStatus));
        _setDedicatedEnabled = (enabled, token) => TrackWriteAsync(() => setDedicatedEnabled(enabled, token));
        _trySetDedicatedEnabledSilently = (enabled, token) => TrackWriteAsync(() => trySetDedicatedEnabledSilently(enabled, token));
        _restartDedicated = token => TrackWriteAsync(() => restartDedicated(token));
        _readTopology = readTopology ?? throw new ArgumentNullException(nameof(readTopology));
        _delay = delay ?? ((span, token) => Task.Delay(span, token));
        _postCheckTimeout = postCheckTimeout ?? DefaultPostCheckTimeout;
        _pollInterval = pollInterval ?? DefaultPollInterval;
    }

    internal async ValueTask<BladeGpuModeState> ReadStateAsync(
        ushort productId,
        CancellationToken cancellationToken = default)
    {
        var dedicated = _getDedicatedStatus();
        BladeDisplayMuxStatus mux;
        try
        {
            mux = await _readMux(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
        {
            mux = default;
        }

        return new BladeGpuModeState(
            BuildSnapshot(productId, dedicated, mux),
            dedicated,
            _readTopology());
    }

    internal Task<BladeGpuModeApplyResult> ApplyAsync(
        ushort productId,
        BladeGpuMode requested,
        PowerSourceKind powerSource,
        CancellationToken cancellationToken = default,
        bool allowMuxChange = true) =>
        RunOperationAsync(() => ApplyCoreAsync(productId, requested, powerSource, cancellationToken, allowMuxChange),
            cancellationToken, UncertainResult(requested));

    private async Task<BladeGpuModeApplyResult> ApplyCoreAsync(
        ushort productId, BladeGpuMode requested, PowerSourceKind powerSource,
        CancellationToken cancellationToken, bool allowMuxChange)
    {
        if (!Enum.IsDefined(requested))
        {
            throw new ArgumentOutOfRangeException(nameof(requested));
        }
        if (_recoveryMarker.Exists) return UncertainResult(requested);

        var state = await ReadStateAsync(productId, cancellationToken).ConfigureAwait(false);
        var target = requested == BladeGpuMode.Auto
            ? BladeGpuModes.ResolveAutoTarget(state.Snapshot, powerSource)
            : requested;
        if (!BladeGpuModes.IsAvailable(state.Snapshot, target))
        {
            return BladeGpuModeResultMapper.Blocked(target, BladeGpuModeResultMapper.GetBlockedReason(state.Snapshot, state.Dedicated, target));
        }

        if (target != BladeGpuMode.Discrete && state.Snapshot.MuxPresent &&
            state.Snapshot.Mux.Mode == BladeDisplayMuxMode.Discrete)
        {
            if (!allowMuxChange)
            {
                return BladeGpuModeResultMapper.Blocked(target, BladeGpuModeBlockReason.DiscreteRouteLocked);
            }

            // Release the panel route first; never disable the adapter before the restart.
            var release = await EnableAndRouteAsync(BladeGpuMode.Standard, state, cancellationToken)
                .ConfigureAwait(false);
            return release with { Target = target };
        }

        return BladeGpuModes.GetCurrentMode(state.Snapshot) == target
            ? new BladeGpuModeApplyResult(BladeGpuModeApplyOutcome.Success, target)
            : target == BladeGpuMode.Integrated
                ? await DisableDedicatedAsync(state, cancellationToken).ConfigureAwait(false)
                : await EnableAndRouteAsync(target, state, cancellationToken).ConfigureAwait(false);
    }

    internal Task<BladeGpuModeApplyResult> RestoreDedicatedGpuAsync(CancellationToken cancellationToken = default) =>
        RunOperationAsync(() => RestoreDedicatedGpuCoreAsync(cancellationToken), cancellationToken, UncertainResult(BladeGpuMode.Standard));

    private async Task<BladeGpuModeApplyResult> RestoreDedicatedGpuCoreAsync(CancellationToken cancellationToken)
    {
        var status = _getDedicatedStatus();
        if (status.BlockReason == DedicatedGpuModeBlockReason.NoDiscreteGraphics)
        {
            return new BladeGpuModeApplyResult(BladeGpuModeApplyOutcome.NoDevice, BladeGpuMode.Standard);
        }

        if (status.DeviceState.IsFaulted())
        {
            var restart = await _restartDedicated(cancellationToken).ConfigureAwait(false);
            if (restart.Outcome is not (DedicatedGpuSwitchOutcome.Success or
                DedicatedGpuSwitchOutcome.RestartRequired))
            {
                return BladeGpuModeResultMapper.FromDedicated(BladeGpuMode.Standard, restart, status.BlockReason);
            }

            status = _getDedicatedStatus();
            if (status.DeviceState.IsFaulted())
            {
                return new BladeGpuModeApplyResult(
                    BladeGpuModeApplyOutcome.RestartRequired,
                    BladeGpuMode.Standard,
                    BladeGpuModeBlockReason.DeviceFaulted,
                    status.ProblemCode);
            }
        }

        if (!status.IsActive && !_recoveryMarker.Exists)
        {
            _recoveryMarker.Clear();
            return new BladeGpuModeApplyResult(BladeGpuModeApplyOutcome.Success, BladeGpuMode.Standard);
        }

        var result = await _setDedicatedEnabled(true, cancellationToken).ConfigureAwait(false);
        if (result.Outcome is not (DedicatedGpuSwitchOutcome.Success or
            DedicatedGpuSwitchOutcome.RestartRequired))
        {
            return BladeGpuModeResultMapper.FromDedicated(BladeGpuMode.Standard, result, status.BlockReason);
        }

        var verified = await WaitForAsync(() =>
        {
            var current = _getDedicatedStatus();
            return IsDedicatedGpuRunning(current) &&
                IsTopologyHealthy(_readTopology(), requireNoExternal: false);
        }, CancellationToken.None).ConfigureAwait(false);
        if (!verified)
        {
            return BladeGpuModeResultMapper.FromEnableVerification(
                BladeGpuMode.Standard,
                _getDedicatedStatus());
        }

        _recoveryMarker.Clear();
        return BladeGpuModeResultMapper.FromDedicated(BladeGpuMode.Standard, result, status.BlockReason);
    }

    internal Task<BladeGpuModeApplyResult> RestoreDedicatedGpuSilentlyAsync(CancellationToken cancellationToken = default) =>
        RunOperationAsync(() => RestoreDedicatedGpuSilentlyCoreAsync(cancellationToken), cancellationToken, UncertainResult(BladeGpuMode.Standard));

    private async Task<BladeGpuModeApplyResult> RestoreDedicatedGpuSilentlyCoreAsync(CancellationToken cancellationToken)
    {
        var status = _getDedicatedStatus();
        if (status.BlockReason == DedicatedGpuModeBlockReason.NoDiscreteGraphics)
        {
            return new BladeGpuModeApplyResult(BladeGpuModeApplyOutcome.NoDevice, BladeGpuMode.Standard);
        }

        if (!status.IsActive && !_recoveryMarker.Exists)
        {
            return new BladeGpuModeApplyResult(BladeGpuModeApplyOutcome.Success, BladeGpuMode.Standard);
        }

        return SilentRestoreResult(await TryRestoreDedicatedAsync(true, cancellationToken, silently: true)
            .ConfigureAwait(false));
    }

    internal Task<BladeGpuModeApplyResult> RestartDedicatedGpuAsync(CancellationToken cancellationToken = default) =>
        RunOperationAsync(() => RestartDedicatedGpuCoreAsync(cancellationToken), cancellationToken, UncertainResult(BladeGpuMode.Standard));

    private async Task<BladeGpuModeApplyResult> RestartDedicatedGpuCoreAsync(CancellationToken cancellationToken)
    {
        if (_recoveryMarker.Exists) return await RestoreDedicatedGpuCoreAsync(cancellationToken).ConfigureAwait(false);
        var result = await _restartDedicated(cancellationToken).ConfigureAwait(false);
        if (result.Outcome is DedicatedGpuSwitchOutcome.Success or DedicatedGpuSwitchOutcome.RestartRequired)
            _recoveryMarker.Clear();
        return BladeGpuModeResultMapper.FromDedicated(BladeGpuMode.Standard, result, DedicatedGpuModeBlockReason.None);
    }

    /// <summary>
    /// Reconciles states that older builds could leave behind. A dGPU disabled on a
    /// model without an admitted MUX is re-enabled silently; user-initiated integrated
    /// mode on a MUX model is left alone. Unknown writers require a verified new boot.
    /// </summary>
    internal Task<BladeGpuModeApplyResult?> ReconcileAsync(
        ushort productId,
        CancellationToken cancellationToken = default) =>
        RunOperationAsync<BladeGpuModeApplyResult?>(() => ReconcileCoreAsync(productId, cancellationToken),
            cancellationToken, UncertainResult(BladeGpuMode.Standard));

    private async Task<BladeGpuModeApplyResult?> ReconcileCoreAsync(ushort productId, CancellationToken cancellationToken)
    {
        if (productId == 0)
        {
            return null;
        }

        if (_recoveryMarker.Exists)
        {
            return SilentRestoreResult(await TryRestoreDedicatedAsync(true, cancellationToken, silently: true)
                .ConfigureAwait(false));
        }

        var status = _getDedicatedStatus();
        if (!status.IsActive || BladeGpuModelCapabilities.SupportsDedicatedGpuPnpSwitch(productId))
        {
            return null;
        }

        return SilentRestoreResult(await TryRestoreDedicatedAsync(true, cancellationToken, silently: true)
            .ConfigureAwait(false));
    }

    private static BladeGpuModeApplyResult SilentRestoreResult(bool restored) => new(
        restored ? BladeGpuModeApplyOutcome.Success : BladeGpuModeApplyOutcome.Blocked,
        BladeGpuMode.Standard, restored ? BladeGpuModeBlockReason.None : BladeGpuModeBlockReason.Unsupported);

    private static BladeGpuModeApplyResult UncertainResult(BladeGpuMode target) =>
        new(BladeGpuModeApplyOutcome.RollbackFailed, target, BladeGpuModeBlockReason.Unsupported,
            DedicatedGpuOperationUncertainException.PendingError);

    private async Task<T> TrackWriteAsync<T>(Func<Task<T>> write)
    {
        var created = !_recoveryMarker.Exists;
        if (!_recoveryMarker.TryPrepareWrite())
            throw new IOException("The GPU recovery record could not be saved; no operation was dispatched.");
        try
        {
            var result = await write().ConfigureAwait(false);
            if (created && (result is null || result is DedicatedGpuSwitchResult
                { Outcome: DedicatedGpuSwitchOutcome.Cancelled or DedicatedGpuSwitchOutcome.Blocked or
                    DedicatedGpuSwitchOutcome.NoDevice or DedicatedGpuSwitchOutcome.DeviceFaulted }))
                _recoveryMarker.Clear();
            return result;
        }
        catch (OperationCanceledException)
        {
            if (created) _recoveryMarker.Clear();
            throw;
        }
        catch (DedicatedGpuOperationUncertainException) { throw; }
        catch (Exception exception)
        {
            throw new DedicatedGpuOperationUncertainException("GPU write terminal state is unknown.", exception);
        }
    }

    private async Task<T> RunOperationAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken, T uncertainResult)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_operationUncertain && !_recoveryMarker.CanRecoverAfterRestart()) return uncertainResult;
            _operationUncertain = false;
            return await operation().ConfigureAwait(false);
        }
        catch (DedicatedGpuOperationUncertainException)
        {
            _operationUncertain = true;
            return uncertainResult;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task<BladeGpuModeApplyResult> DisableDedicatedAsync(
        BladeGpuModeState state,
        CancellationToken cancellationToken)
    {
        if (!_recoveryMarker.TryWrite(state.Snapshot))
        {
            return new BladeGpuModeApplyResult(BladeGpuModeApplyOutcome.Failed, BladeGpuMode.Integrated);
        }

        DedicatedGpuSwitchResult result;
        try
        {
            result = await _setDedicatedEnabled(false, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The backends only propagate cancellation before dispatch.
            result = new DedicatedGpuSwitchResult(DedicatedGpuSwitchOutcome.Cancelled);
        }
        if (result.Outcome is DedicatedGpuSwitchOutcome.Cancelled or DedicatedGpuSwitchOutcome.Blocked or
            DedicatedGpuSwitchOutcome.NoDevice or DedicatedGpuSwitchOutcome.DeviceFaulted)
        {
            _recoveryMarker.Clear();
            return BladeGpuModeResultMapper.FromDedicated(BladeGpuMode.Integrated, result, state.Dedicated.BlockReason);
        }

        var verified = false;
        try
        {
            verified = result.Outcome is (DedicatedGpuSwitchOutcome.Success or DedicatedGpuSwitchOutcome.RestartRequired) &&
                await WaitForAsync(() =>
            {
                var current = _getDedicatedStatus();
                return current.IsActive && IsTopologyHealthy(_readTopology(), requireNoExternal: true);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The write is terminal; now undo it rather than claiming hardware was cancelled.
        }

        if (!verified)
        {
            var recovered = await TryRestoreDedicatedAsync(true, CancellationToken.None)
                .ConfigureAwait(false);
            return RollbackResult(BladeGpuMode.Integrated, recovered);
        }

        _recoveryMarker.Clear();
        return BladeGpuModeResultMapper.FromDedicated(BladeGpuMode.Integrated, result, state.Dedicated.BlockReason);
    }

    private async Task<BladeGpuModeApplyResult> EnableAndRouteAsync(
        BladeGpuMode target,
        BladeGpuModeState state,
        CancellationToken cancellationToken)
    {
        var before = _readTopology();
        var dgpuWasDisabled = !state.Snapshot.DedicatedGpuEnabled;
        var restartRequired = false;

        if (dgpuWasDisabled)
        {
            var enable = await _setDedicatedEnabled(true, cancellationToken).ConfigureAwait(false);
            if (enable.Outcome is not (DedicatedGpuSwitchOutcome.Success or
                DedicatedGpuSwitchOutcome.RestartRequired))
            {
                return BladeGpuModeResultMapper.FromDedicated(target, enable, state.Dedicated.BlockReason);
            }

            restartRequired = enable.Outcome == DedicatedGpuSwitchOutcome.RestartRequired;
            bool verified;
            try
            {
                verified = await WaitForAsync(() =>
                {
                    var current = _getDedicatedStatus();
                    return IsDedicatedGpuRunning(current) &&
                        PreservesTopology(before, _readTopology());
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                verified = false;
            }

            if (!verified)
            {
                var recovered = await TryRestoreDedicatedAsync(false, CancellationToken.None)
                    .ConfigureAwait(false);
                return RollbackResult(target, recovered);
            }
        }

        var mux = state.Snapshot.Mux;
        if ((target == BladeGpuMode.Standard && (!state.Snapshot.MuxPresent ||
                mux.Mode is BladeDisplayMuxMode.Hybrid or BladeDisplayMuxMode.Dds)) ||
            (target == BladeGpuMode.Discrete && mux.Mode == BladeDisplayMuxMode.Discrete))
        {
            _recoveryMarker.Clear();
            return restartRequired
                ? new BladeGpuModeApplyResult(BladeGpuModeApplyOutcome.RestartRequired, target)
                : new BladeGpuModeApplyResult(BladeGpuModeApplyOutcome.Success, target);
        }

        BladeDisplayMuxMode? targetMux = target == BladeGpuMode.Standard
            ? BladeDisplayMuxModes.GetStandardMode(mux.Capabilities)
            : BladeDisplayMuxModes.Supports(mux.Capabilities, BladeDisplayMuxMode.Discrete)
                ? BladeDisplayMuxMode.Discrete
                : null;
        if (targetMux is null)
        {
            return BladeGpuModeResultMapper.Blocked(target, BladeGpuModeBlockReason.NoMux);
        }

        var originalMux = mux.Mode;
        var muxResult = await _setMux(targetMux.Value, cancellationToken).ConfigureAwait(false);
        if (muxResult.Outcome is BladeDisplayMuxSwitchOutcome.Restored or
            BladeDisplayMuxSwitchOutcome.RestoreFailed)
        {
            return RollbackResult(target, muxResult.Outcome == BladeDisplayMuxSwitchOutcome.Restored);
        }
        if (muxResult.Outcome is not (BladeDisplayMuxSwitchOutcome.Success or
            BladeDisplayMuxSwitchOutcome.RestartRequired))
        {
            return BladeGpuModeResultMapper.FromMux(target, muxResult);
        }

        bool topologyVerified;
        try
        {
            topologyVerified = await WaitForAsync(
                () => PreservesTopology(before, _readTopology()),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            topologyVerified = false;
        }

        if (!topologyVerified)
        {
            var recovered = await TryRestoreMuxAsync(originalMux, CancellationToken.None)
                .ConfigureAwait(false);
            return RollbackResult(target, recovered);
        }

        _recoveryMarker.Clear();
        return restartRequired || muxResult.Outcome == BladeDisplayMuxSwitchOutcome.RestartRequired
            ? new BladeGpuModeApplyResult(BladeGpuModeApplyOutcome.RestartRequired, target)
            : new BladeGpuModeApplyResult(BladeGpuModeApplyOutcome.Success, target);
    }

    private async Task<bool> TryRestoreDedicatedAsync(bool enabled, CancellationToken cancellationToken, bool silently = false)
    {
        try
        {
            var result = silently
                ? await _trySetDedicatedEnabledSilently(enabled, cancellationToken).ConfigureAwait(false)
                : await _setDedicatedEnabled(enabled, cancellationToken).ConfigureAwait(false);
            if (result is not { Outcome: DedicatedGpuSwitchOutcome.Success or
                DedicatedGpuSwitchOutcome.RestartRequired })
            {
                return false;
            }

            var verified = await WaitForAsync(() =>
            {
                var current = _getDedicatedStatus();
                return (enabled ? IsDedicatedGpuRunning(current) : current.IsActive) &&
                    IsTopologyHealthy(_readTopology(), requireNoExternal: !enabled);
            }, CancellationToken.None).ConfigureAwait(false);
            if (verified) _recoveryMarker.Clear();
            return verified;
        }
        catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
        {
            return false;
        }
    }

    private async Task<bool> TryRestoreMuxAsync(
        BladeDisplayMuxMode original,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _setMux(original, cancellationToken).ConfigureAwait(false);
            if (result.Outcome is not (BladeDisplayMuxSwitchOutcome.Success or
                BladeDisplayMuxSwitchOutcome.RestartRequired))
            {
                return false;
            }

            var elapsed = Stopwatch.StartNew();
            while (true)
            {
                try
                {
                    if ((await _readMux(cancellationToken).ConfigureAwait(false)).Mode == original)
                    {
                        return true;
                    }
                }
                catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
                {
                    return false;
                }

                if (elapsed.Elapsed >= _postCheckTimeout)
                {
                    return false;
                }

                await _delay(_pollInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
        {
            return false;
        }
    }

    private async Task<bool> WaitForAsync(Func<bool> predicate, CancellationToken cancellationToken)
    {
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (predicate())
            {
                return true;
            }

            if (elapsed.Elapsed >= _postCheckTimeout)
            {
                return false;
            }

            await _delay(_pollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private static BladeGpuModeApplyResult RollbackResult(BladeGpuMode target, bool recovered) =>
        new(recovered ? BladeGpuModeApplyOutcome.RolledBack : BladeGpuModeApplyOutcome.RollbackFailed, target);

    private static BladeGpuModeSnapshot BuildSnapshot(
        ushort productId,
        DedicatedGpuModeStatus dedicated,
        BladeDisplayMuxStatus mux)
    {
        var faulted = dedicated.DeviceState.IsFaulted();
        return new BladeGpuModeSnapshot(
            DedicatedGpuSupported: !faulted &&
                dedicated.IsSupported &&
                dedicated.BlockReason == DedicatedGpuModeBlockReason.None,
            DedicatedGpuEnabled: !dedicated.IsActive,
            Mux: mux,
            PnpSwitchAllowed: BladeGpuModelCapabilities.SupportsDedicatedGpuPnpSwitch(productId));
    }

    private static bool IsDedicatedGpuRunning(DedicatedGpuModeStatus status) =>
        !status.IsActive && !status.DeviceState.IsFaulted();

    private static bool IsTopologyHealthy(
        WindowsDisplayTopologySnapshot topology,
        bool requireNoExternal) =>
        topology.IsKnown &&
        topology.HasActiveInternalDisplay &&
        (!requireNoExternal || !topology.HasExternalDisplay);

    private static bool PreservesTopology(
        WindowsDisplayTopologySnapshot before,
        WindowsDisplayTopologySnapshot after) =>
        after.IsKnown &&
        after.HasActiveInternalDisplay &&
        (!before.HasExternalDisplay || after.HasExternalDisplay);

}
