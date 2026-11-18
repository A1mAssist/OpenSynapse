using OpenSynapse.Core.Macros;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Input;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidMappingViewModel
{
    /// <summary>
    /// Binds a key captured on the macro page. Private Razer keys need the
    /// device assignment turned off first, otherwise the macro and the native
    /// action both fire. The original assignment is stored in the binding so
    /// clearing the macro can restore it.
    /// </summary>
    internal async Task<string?> BindSoftwareMacroAsync(
        string macroId,
        MacroInput input,
        MacroTriggerMode triggerMode,
        MacroPlaybackMode playbackMode,
        int repeatCount,
        bool hyperShiftLayer,
        MacroSoftwareBinding? existingBinding,
        CancellationToken cancellationToken)
    {
        if (_softwareTriggerBinder is null || _softwareTriggerResolver is null)
        {
            return AppStrings.Text("RazerMappingMacroSoftwareUnavailable");
        }
        if (IsBusy)
        {
            return AppStrings.Text("RazerMappingBusy");
        }
        ErrorText = string.Empty;
        if (hyperShiftLayer && !_canObserveHyperShiftLayer)
        {
            return AppStrings.Text("RazerMappingMacroSoftwareUnavailable");
        }
        if (string.IsNullOrWhiteSpace(input.InstanceId))
        {
            input.InstanceId = DeviceInstanceId;
        }
        else if (!string.IsNullOrWhiteSpace(DeviceInstanceId) &&
                 !StringComparer.OrdinalIgnoreCase.Equals(input.InstanceId, DeviceInstanceId))
        {
            return AppStrings.Text("RazerMappingMacroSoftwareUnavailable");
        }

        var layer = (byte)(hyperShiftLayer ? 1 : 0);
        var layerChanged = LayerIndex != layer;
        if (layerChanged)
        {
            _layerIndex = layer;
            OnPropertyChanged(nameof(LayerIndex));
            OnPropertyChanged(nameof(HyperShiftSoftwareUnavailableVisibility));
        }
        var readRows = !_loaded || layerChanged;
        if (readRows)
        {
            await ReadAsync(cancellationToken).ConfigureAwait(true);
            if (!_loaded)
            {
                return ErrorText.Length > 0
                    ? ErrorText
                    : AppStrings.Text("RazerHidMappingReadFailed");
            }
        }
        else
        {
            await RefreshMacroContextAsync(cancellationToken).ConfigureAwait(true);
        }
        var row = existingBinding is { } bound
            ? Rows.FirstOrDefault(candidate => candidate.ButtonId == bound.ButtonId)
            : Rows.FirstOrDefault(candidate =>
                ResolveSoftwareTrigger(candidate.ButtonId) is { } candidateInput &&
                candidateInput.Matches(input));
        if (row is null)
        {
            return AppStrings.Text("RazerMappingMacroTriggerMissing");
        }
        if (!row.CanUseSoftwareMacro)
        {
            return AppStrings.Text("RazerMappingMacroSoftwareUnavailable");
        }

        var macroIndex = LocalMacroIndex(macroId);
        if (macroIndex < 0)
        {
            return AppStrings.Text("MacroTriggerMacroMissing");
        }
        if (existingBinding is { } existing &&
            (existing.ProductId != _productId ||
             existing.Layer != layer ||
             (!string.IsNullOrWhiteSpace(existing.InstanceId) &&
              !StringComparer.OrdinalIgnoreCase.Equals(existing.InstanceId, DeviceInstanceId))))
        {
            return AppStrings.Text("RazerMappingMacroSoftwareUnavailable");
        }

        var macro = _localMacros[macroIndex];
        row.GroupIndex = (int)RazerHidMappingGroup.Macro;
        row.MacroTargetIndex = (int)MacroExecutionTarget.Software;
        row.OptionIndex = macroIndex;
        row.SoftwareTriggerModeIndex = SoftwareTriggerModeIndex(triggerMode);
        row.ExecutionModeIndex = (int)playbackMode;
        row.ExecutionRepeatCount = Math.Clamp(repeatCount, 1, MacroDefinition.MaxRepeatCount);

        RazerMappingAssignment? assignment = null;
        var binding = existingBinding;
        try
        {
            if (binding is null)
            {
                assignment = await _transport.GetAssignmentAsync(
                        row.ButtonId, Mode, cancellationToken)
                    .ConfigureAwait(true);
                var previous = row.SoftwareMacroId is { } oldMacroId
                    ? _localMacros.FirstOrDefault(candidate =>
                        StringComparer.Ordinal.Equals(candidate.Id, oldMacroId)) is { } oldMacro
                        ? SoftwareBindingFor(oldMacro, input, hyperShiftLayer)
                        : null
                    : null;
                binding = previous is not null &&
                    previous.Layer == Mode &&
                    previous.ButtonId == row.ButtonId &&
                    IsAssignmentWrittenByBinding(row, assignment)
                    ? new MacroSoftwareBinding
                    {
                        ProductId = _productId,
                        InstanceId = previous.InstanceId ?? DeviceInstanceId,
                        Layer = Mode,
                        ButtonId = row.ButtonId,
                        OriginalFunctionId = previous.OriginalFunctionId,
                        OriginalData = previous.OriginalData.ToList(),
                    }
                    : new MacroSoftwareBinding
                {
                    ProductId = _productId,
                    InstanceId = DeviceInstanceId,
                    Layer = Mode,
                    ButtonId = row.ButtonId,
                    OriginalFunctionId = assignment.FunctionId,
                    OriginalData = assignment.Data.ToList(),
                };
            }

            if (RazerSoftwareMacroAssignmentPolicy.NeedsDeviceAssignment(input))
            {
                assignment ??= await _transport.GetAssignmentAsync(
                        row.ButtonId, Mode, cancellationToken)
                    .ConfigureAwait(true);
                var (functionId, data) = BindingAssignment(input, row.ButtonId);
                if (assignment.FunctionId != functionId ||
                    !assignment.Data.SequenceEqual(data))
                {
                    await _transport.SetAssignmentAsync(
                            row.ButtonId, Mode, functionId, data, cancellationToken)
                        .ConfigureAwait(true);
                }
            }
            else if (NativeKeyboardAssignment(row.ButtonId) is { } native)
            {
                assignment ??= await _transport.GetAssignmentAsync(
                        row.ButtonId, Mode, cancellationToken)
                    .ConfigureAwait(true);
                if (NeedsNativeKeyboardAssignment(assignment) &&
                    (assignment.FunctionId != native.FunctionId ||
                     !assignment.Data.SequenceEqual(native.Data)))
                {
                    await _transport.SetAssignmentAsync(
                            row.ButtonId, Mode, native.FunctionId, native.Data, cancellationToken)
                        .ConfigureAwait(true);
                }
            }

            var error = await _softwareTriggerBinder(
                    macroId,
                    input,
                    triggerMode,
                    playbackMode,
                    Math.Clamp(repeatCount, 1, MacroDefinition.MaxRepeatCount),
                    hyperShiftLayer,
                    binding,
                    cancellationToken)
                .ConfigureAwait(true);
            if (error is not null)
            {
                await RestoreBindingAssignmentAsync(row.ButtonId, assignment, cancellationToken)
                    .ConfigureAwait(true);
                await ReloadAfterBindFailureAsync().ConfigureAwait(true);
                return error;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await RestoreBindingAssignmentAsync(row.ButtonId, assignment, CancellationToken.None)
                .ConfigureAwait(true);
            throw;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            await RestoreBindingAssignmentAsync(row.ButtonId, assignment, CancellationToken.None)
                .ConfigureAwait(true);
            await ReloadAfterBindFailureAsync().ConfigureAwait(true);
            _log?.Invoke($"software macro bind failed: {exception}");
            return exception.Message;
        }

        row.CurrentActionText = AppStrings.FormatText(
            "RazerMappingMacroSoftwareApplied", macro.Name);
        row.SoftwareMacroId = macroId;
        row.SetMacroSlotWarning(string.Empty);
        row.MarkSoftwareModeLoaded();
        row.LoadedOptionIndex = macroIndex;
        StatusText = AppStrings.FormatText("RazerMappingMacroSoftwareApplied", macro.Name);
        return null;
    }

    private static (byte FunctionId, byte[] Data) BindingAssignment(MacroInput input, byte buttonId) =>
        RazerSoftwareMacroAssignmentPolicy.NeedsOff(input)
            ? (RazerHidMappingFunctions.Off, [])
            : (RazerHidMappingFunctions.ButtonCode, [buttonId]);

    private bool IsAssignmentWrittenByBinding(
        RazerHidMappingRowViewModel row,
        RazerMappingAssignment assignment)
    {
        if (_softwareTriggerResolver?.Invoke(row.ButtonId) is not { } input)
        {
            return false;
        }

        var (functionId, data) = BindingAssignment(input, row.ButtonId);
        return assignment.FunctionId == functionId && assignment.Data.SequenceEqual(data);
    }

    private async Task RestoreBindingAssignmentAsync(
        byte buttonId,
        RazerMappingAssignment? assignment,
        CancellationToken cancellationToken)
    {
        if (assignment is null)
        {
            return;
        }

        try
        {
            await _transport.SetAssignmentAsync(
                    buttonId,
                    Mode,
                    assignment.FunctionId,
                    assignment.Data,
                    cancellationToken)
                .ConfigureAwait(true);
        }
        catch (Exception exception) when (IsExpected(exception) ||
                                           exception is OperationCanceledException)
        {
            _log?.Invoke($"mapping button {buttonId} restore failed: {exception.Message}");
        }
    }

    private async Task ReloadAfterBindFailureAsync()
    {
        try
        {
            await ReadAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _log?.Invoke($"mapping reload after bind failure failed: {exception.Message}");
        }
    }
}
