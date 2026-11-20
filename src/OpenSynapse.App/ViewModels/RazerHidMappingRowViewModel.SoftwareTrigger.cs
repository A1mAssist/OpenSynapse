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
    private int _loadedSoftwareTriggerModeIndex = -1;
    private int _loadedExecutionModeIndex = -1;
    private double _loadedExecutionRepeatCount = 1;

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
            // ComboBox pushes -1 while its items are replaced; clamping that
            // to 0 would rewrite HoldRepeat as Hold.
            if (value < 0) return;
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
            if (value < 0) return;
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

    /// <summary>
    /// The mapping page used to write every software row on Save, so a page
    /// opened before a macro-editor change could silently put the old mode
    /// back. Only rows the user actually changed are written now.
    /// </summary>
    internal bool HasSoftwareModeChanges =>
        _softwareTriggerModeIndex != _loadedSoftwareTriggerModeIndex ||
        _executionModeIndex != _loadedExecutionModeIndex ||
        _executionRepeatCount != _loadedExecutionRepeatCount;

    internal void MarkSoftwareModeLoaded()
    {
        _loadedSoftwareTriggerModeIndex = _softwareTriggerModeIndex;
        _loadedExecutionModeIndex = _executionModeIndex;
        _loadedExecutionRepeatCount = _executionRepeatCount;
    }
}
