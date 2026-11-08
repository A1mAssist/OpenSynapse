using OpenSynapse.Core.Displays;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Lifecycle;

namespace OpenSynapse.App.ViewModels;

public sealed partial class BladeWorkspace
{
    private readonly BladeGpuModeController _gpuMode;
    private bool _gpuModeRecoveryReconciled;

    /// <summary>
    /// Only Auto has to survive a restart; the other three modes are read back from the
    /// hardware state. Machine settings rather than a profile: the GPU route is machine-wide.
    /// </summary>
    internal BladeGpuMode? StoredGpuMode => _getMachineSettings().GpuMode;

    private ValueTask<BladeDisplayMuxStatus> ReadDisplayMuxStatusAsync(
        CancellationToken cancellationToken = default) =>
        _deviceTelemetryReader.ReadBladeDisplayMuxStatusAsync(ActiveDescriptors, cancellationToken);

    private ValueTask<BladeDisplayMuxSwitchResult> SetDisplayMuxModeAsync(
        BladeDisplayMuxMode mode,
        CancellationToken cancellationToken = default) =>
        _deviceTelemetryReader.SetBladeDisplayMuxModeAsync(ActiveDescriptors, mode, cancellationToken);

    internal ushort BladeProductId => _blade._bladeProductId;

    internal bool CanManageGpuMode => _blade._bladeProductId != 0;

    internal ValueTask<BladeGpuModeState> ReadGpuModeStateAsync(
        CancellationToken cancellationToken = default) =>
        _gpuMode.ReadStateAsync(_blade._bladeProductId, cancellationToken);

    internal async Task<BladeGpuModeApplyResult> ApplyGpuModeAsync(
        BladeGpuMode mode,
        CancellationToken cancellationToken = default)
    {
        var result = new BladeGpuModeApplyResult(BladeGpuModeApplyOutcome.Failed, mode);
        await RunDeviceOperationAsync(AppStrings.Text("GpuModeTitle/Text"), async () =>
        {
            result = await _gpuMode.ApplyAsync(
                _blade._bladeProductId, mode, CurrentPowerSource, cancellationToken);
            EnsureGpuModeApplied(result);
            if (!await SaveGpuModeAsync(mode == BladeGpuMode.Auto ? BladeGpuMode.Auto : null, cancellationToken))
            {
                result = new BladeGpuModeApplyResult(BladeGpuModeApplyOutcome.Failed, mode);
                throw new InvalidOperationException(AppStrings.Text("GpuModePreferenceSaveFailed"));
            }
        }, cancellationToken, () =>
        {
            if (result.Outcome is BladeGpuModeApplyOutcome.Success or BladeGpuModeApplyOutcome.RestartRequired)
            {
                result = result with { Outcome = BladeGpuModeApplyOutcome.Failed };
            }
        });
        return result;
    }

    internal async Task<BladeGpuModeApplyResult> RestoreDedicatedGpuAsync(
        CancellationToken cancellationToken = default)
    {
        var result = new BladeGpuModeApplyResult(BladeGpuModeApplyOutcome.Failed, BladeGpuMode.Standard);
        await RunDeviceOperationAsync(AppStrings.Text("GpuModeTitle/Text"), async () =>
        {
            // Explicit recovery must not be undone by the next battery Auto refresh.
            if (!await SaveGpuModeAsync(null, cancellationToken))
            {
                throw new InvalidOperationException(AppStrings.Text("GpuModePreferenceSaveFailed"));
            }
            result = await _gpuMode.RestoreDedicatedGpuAsync(cancellationToken);
            EnsureGpuModeApplied(result);
        }, cancellationToken);
        return result;
    }

    internal Task<BladeGpuModeApplyResult> RestoreDedicatedGpuSilentlyAsync(
        CancellationToken cancellationToken = default) =>
        _gpuMode.RestoreDedicatedGpuSilentlyAsync(cancellationToken);

    internal Task<BladeGpuModeApplyResult> RestartDedicatedGpuAsync(
        CancellationToken cancellationToken = default) =>
        _gpuMode.RestartDedicatedGpuAsync(cancellationToken);

    internal async Task<bool> RefreshGpuDisplayPolicyAsync(
        PowerSourceKind powerState, CancellationToken cancellationToken)
    {
        await ReconcileGpuModeAsync(powerState, cancellationToken);
        var state = await ReadGpuModeStateAsync(cancellationToken);
        return _display().SetEcoDisplayActive(
            state.Snapshot.PnpSwitchAllowed && state.Snapshot.MuxPresent &&
            EcoDisplayPolicy.IsPowerSaving(state.Dedicated.IsActive, powerState));
    }

    internal async Task<BladeGpuModeApplyResult?> ReconcileGpuModeAsync(
        PowerSourceKind powerState,
        CancellationToken cancellationToken = default)
    {
        if (_blade._bladeProductId == 0)
        {
            return null;
        }

        BladeGpuModeApplyResult? recovery = null;
        if (!_gpuModeRecoveryReconciled)
        {
            recovery = await _gpuMode.ReconcileAsync(_blade._bladeProductId, cancellationToken)
                .ConfigureAwait(false);
            _gpuModeRecoveryReconciled = recovery is null or { Outcome: BladeGpuModeApplyOutcome.Success };
            if (recovery is { Outcome: not BladeGpuModeApplyOutcome.Success } failure)
            {
                _diagnosticLog.TryWrite(
                    "gpu-mode",
                    $"startup recovery: {failure.Outcome}, target={failure.Target}, " +
                    $"reason={failure.BlockReason}, code={failure.ErrorCode}.");
                return recovery;
            }
        }

        // Auto is a policy, so it must be reconciled against the live adapter state after
        // boot, device refresh and resume. The controller skips an already-applied target.
        if (StoredGpuMode != BladeGpuMode.Auto)
        {
            return recovery;
        }

        var applied = await _gpuMode.ApplyAsync(
                _blade._bladeProductId,
                BladeGpuMode.Auto,
                powerState,
                cancellationToken,
                allowMuxChange: false)
            .ConfigureAwait(false);
        if (applied.Outcome is not (BladeGpuModeApplyOutcome.Success or
            BladeGpuModeApplyOutcome.RestartRequired))
        {
            _diagnosticLog.TryWrite(
                "gpu-mode",
                $"auto reconcile: {applied.Outcome}, target={applied.Target}, " +
                $"reason={applied.BlockReason}, code={applied.ErrorCode}.");
        }
        return recovery ?? applied;
    }

    private static void EnsureGpuModeApplied(BladeGpuModeApplyResult result)
    {
        if (result.Outcome is not (BladeGpuModeApplyOutcome.Success or BladeGpuModeApplyOutcome.RestartRequired))
        {
            throw new InvalidOperationException(AppStrings.FormatText(
                "GpuModeOperationNotApplied", result.Outcome, result.BlockReason, result.ErrorCode));
        }
    }

    private async Task<bool> SaveGpuModeAsync(
        BladeGpuMode? mode,
        CancellationToken cancellationToken = default)
    {
        var settings = _getMachineSettings();
        var previous = settings.GpuMode;
        settings.GpuMode = mode;
        try
        {
            if (await _saveMachineSettingsAsync(cancellationToken))
            {
                return true;
            }
            settings.GpuMode = previous;
            return false;
        }
        catch
        {
            settings.GpuMode = previous;
            throw;
        }
    }
}
