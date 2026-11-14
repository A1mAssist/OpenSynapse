using Microsoft.UI.Xaml;
using OpenSynapse.Core.Macros;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidMappingRowViewModel
{
    private static readonly MacroTriggerMode[] SoftwareTriggerModes =
    [
        MacroTriggerMode.Hold,
        MacroTriggerMode.Toggle,
        MacroTriggerMode.HoldRepeat,
    ];

    private int _softwareTriggerModeIndex;
    private int _executionModeIndex;
    private double _executionRepeatCount = 1;

    public IReadOnlyList<string> TriggerModeOptions => AppStrings.Texts(
        "RazerMappingMacroTriggerHold",
        "RazerMappingMacroPlaybackToggle",
        "RazerMappingMacroPlaybackHoldRepeat");

    public MacroTriggerMode TriggerMode =>
        SoftwareTriggerModes[
            Math.Clamp(_softwareTriggerModeIndex, 0, SoftwareTriggerModes.Length - 1)];

    public int SoftwareTriggerModeIndex
    {
        get => _softwareTriggerModeIndex;
        set
        {
            var clamped = Math.Clamp(value, 0, SoftwareTriggerModes.Length - 1);
            if (_softwareTriggerModeIndex == clamped) return;
            _softwareTriggerModeIndex = clamped;
            if (TriggerMode == MacroTriggerMode.HoldRepeat)
            {
                _executionModeIndex = (int)MacroPlaybackMode.RepeatUntilStopped;
            }
            else if (TriggerMode != MacroTriggerMode.Toggle && _executionModeIndex == 2)
            {
                _executionModeIndex = 0;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(TriggerMode));
            OnPropertyChanged(nameof(ExecutionModeIndex));
            OnPropertyChanged(nameof(ExecutionMode));
            OnPropertyChanged(nameof(ExecutionOptions));
            OnPropertyChanged(nameof(ExecutionRepeatVisibility));
            OnPropertyChanged(nameof(EffectiveExecutionRepeatCount));
        }
    }

    public IReadOnlyList<string> ExecutionOptions => TriggerMode switch
    {
        MacroTriggerMode.Toggle => AppStrings.Texts(
            "RazerMappingMacroPlaybackOnce",
            "RazerMappingMacroPlaybackRepeat",
            "RazerMappingMacroPlaybackContinuous"),
        MacroTriggerMode.HoldRepeat => AppStrings.Texts(
            "RazerMappingMacroPlaybackContinuous"),
        _ => AppStrings.Texts(
            "RazerMappingMacroPlaybackOnce",
            "RazerMappingMacroPlaybackRepeat"),
    };

    public int ExecutionModeIndex
    {
        get => _executionModeIndex;
        set
        {
            var clamped = Math.Clamp(value, 0, 2);
            if (TriggerMode == MacroTriggerMode.HoldRepeat)
            {
                clamped = (int)MacroPlaybackMode.RepeatUntilStopped;
            }
            else if (TriggerMode != MacroTriggerMode.Toggle && clamped == 2)
            {
                clamped = 0;
            }

            if (_executionModeIndex == clamped) return;
            _executionModeIndex = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ExecutionMode));
            OnPropertyChanged(nameof(ExecutionRepeatVisibility));
            OnPropertyChanged(nameof(EffectiveExecutionRepeatCount));
        }
    }

    public MacroPlaybackMode ExecutionMode => (MacroPlaybackMode)_executionModeIndex;

    public double ExecutionRepeatCount
    {
        get => _executionRepeatCount;
        set
        {
            var normalized = double.IsFinite(value)
                ? Math.Clamp(Math.Round(value), 1, MacroDefinition.MaxRepeatCount)
                : _executionRepeatCount;
            if (_executionRepeatCount == normalized) return;
            _executionRepeatCount = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EffectiveExecutionRepeatCount));
        }
    }

    public int EffectiveExecutionRepeatCount =>
        ExecutionMode == MacroPlaybackMode.Repeat
            ? checked((int)ExecutionRepeatCount)
            : 1;

    public Visibility ExecutionRepeatVisibility =>
        ExecutionMode == MacroPlaybackMode.Repeat
            ? Visibility.Visible
            : Visibility.Collapsed;
}
