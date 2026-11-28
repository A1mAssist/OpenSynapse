using OpenSynapse.Core.Macros;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Input;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidMappingViewModel
{
    /// <summary>What a row needs when its target key is also bound to a host trigger.</summary>
    private sealed record HostRemapPlan(
        HostRemapOutcome Outcome,
        byte TargetButtonId,
        MacroInput? TargetInput,
        MacroDefinition? Owner,
        (byte FunctionId, byte[] Data)? Native);

    /// <summary>
    /// Decides the single-level outcome for a "Mouse button" row without touching the
    /// device, so the save loop and the trigger-claim pass always agree.
    /// </summary>
    private HostRemapPlan PreviewHostRemap(RazerHidMappingRowViewModel row)
    {
        // Only a mouse key the hook can actually see can be self-mapped. Anything
        // else keeps the old firmware write rather than turning into a dead key.
        if (_deviceKind != RazerMappingDeviceKind.Mouse ||
            row.Group != RazerHidMappingGroup.MouseButton ||
            !RazerHidMappingActions.TryGetMouseButtonId(row.OptionIndex, out var targetButtonId))
        {
            return new HostRemapPlan(HostRemapOutcome.FirmwareChain, 0, null, null, null);
        }

        var sourceInput = _softwareTriggerResolver?.Invoke(row.ButtonId);
        if (sourceInput is null ||
            !RazerSoftwareMacroAssignmentPolicy.CanAssign(sourceInput) ||
            sourceInput.Kind == MacroInputKind.MouseButton && sourceInput.MouseButton > 10)
        {
            return new HostRemapPlan(HostRemapOutcome.FirmwareChain, 0, null, null, null);
        }

        var targetInput = _softwareTriggerResolver?.Invoke(targetButtonId);
        var owner = targetInput is null ? null : FindHostOwner(targetInput, LayerIndex == 1);
        var outcome = HostRemapPolicy.Decide(
            row.ButtonId, targetButtonId, targetInput, owner is not null);
        return new HostRemapPlan(outcome, targetButtonId, targetInput, owner, null);
    }

    private Dictionary<RazerHidMappingRowViewModel, HostRemapPlan> BuildHostRemapPlans(
        IReadOnlyList<RazerHidMappingRowViewModel> assignmentRows)
    {
        var plans = new Dictionary<RazerHidMappingRowViewModel, HostRemapPlan>();
        foreach (var row in assignmentRows)
        {
            var plan = PreviewHostRemap(row);
            if (plan.Outcome == HostRemapOutcome.FirmwareChain)
            {
                continue;
            }

            if (plan.Outcome == HostRemapOutcome.RestoreNativeAssignment)
            {
                plan = NativeAssignmentFor(plan) is { } native
                    ? plan with { Native = native }
                    : plan with { Outcome = HostRemapOutcome.Unsupported };
            }

            plans[row] = plan;
        }

        return plans;
    }

    /// <summary>
    /// The assignment a private key had before a macro took it over. When that
    /// assignment is the same private key, replaying it would re-enter the macro
    /// it is bound to, so there is nothing safe to restore.
    /// </summary>
    private (byte FunctionId, byte[] Data)? NativeAssignmentFor(HostRemapPlan plan)
    {
        if (plan.Owner is not { } owner || plan.TargetInput is not { } input)
        {
            return null;
        }

        var binding = SoftwareBindingFor(owner, input, LayerIndex == 1);
        if (binding is null || binding.ButtonId != plan.TargetButtonId)
        {
            return null;
        }

        if (input.Kind == MacroInputKind.RazerKey &&
            binding.OriginalFunctionId == RazerHidMappingFunctions.RazerKey &&
            binding.OriginalData is [var code, ..] &&
            code == input.RazerKeyCode)
        {
            return null;
        }

        return (binding.OriginalFunctionId, binding.OriginalData.ToArray());
    }

    private async Task WriteHostRemapAsync(
        RazerHidMappingRowViewModel row,
        HostRemapPlan plan,
        IReadOnlyList<AssignmentBackup> backups,
        IReadOnlyList<ClearedSoftwareTrigger> clears,
        CancellationToken cancellationToken)
    {
        if (plan.Outcome == HostRemapOutcome.RestoreNativeAssignment)
        {
            var native = plan.Native!.Value;
            await _transport.SetAssignmentAsync(
                    row.ButtonId, Mode, native.FunctionId, native.Data, cancellationToken)
                .ConfigureAwait(true);
            return;
        }

        if (_softwareTriggerBinder is null ||
            _softwareTriggerResolver?.Invoke(row.ButtonId) is not { } sourceInput ||
            !RazerSoftwareMacroAssignmentPolicy.CanAssign(sourceInput))
        {
            throw new InvalidOperationException(
                AppStrings.Text("RazerMappingMacroSoftwareUnavailable"));
        }

        // The key reports itself so the hook sees the press, then the host injects
        // the target's native button once. The target's macro never sees this press.
        var (functionId, data) = BindingAssignment(sourceInput, row.ButtonId);
        await _transport.SetAssignmentAsync(row.ButtonId, Mode, functionId, data, cancellationToken)
            .ConfigureAwait(true);

        var error = await _softwareTriggerBinder(
                NativeMouseButtonCatalog.MacroId(plan.TargetButtonId),
                sourceInput,
                MacroTriggerMode.Press,
                MacroPlaybackMode.Once,
                1,
                LayerIndex == 1,
                CreateSoftwareBinding(row, backups, clears),
                cancellationToken)
            .ConfigureAwait(true);
        if (error is not null)
        {
            throw new InvalidOperationException(error);
        }
    }

    /// <summary>
    /// The assignment a host remap leaves behind: a mouse key reports itself so the
    /// hook sees it, a private key is switched off so its macro owns the press.
    /// </summary>
    private static bool IsHostRemapAssignment(
        RazerMappingAssignment? assignment,
        byte buttonId,
        MacroInput sourceInput) =>
        assignment switch
        {
            { FunctionId: RazerHidMappingFunctions.ButtonCode } selfMapped =>
                selfMapped.Data is [var mapped] && mapped == buttonId,
            { FunctionId: RazerHidMappingFunctions.Off } =>
                RazerSoftwareMacroAssignmentPolicy.NeedsOff(sourceInput),
            _ => false,
        };
}
