using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using OpenSynapse.Core.Macros;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

/// <summary>One entry in the group selector; enum values stay stable for the action encoder.</summary>
public sealed record RazerMappingGroupOption(RazerHidMappingGroup Group, string Name);

internal static class RazerMappingText
{
    private static readonly string[] GroupKeys =
    [
        "RazerMappingGroupKeep",
        "RazerMappingGroupOff",
        "RazerMappingGroupMouseButton",
        "RazerMappingGroupKeyboardKey",
        "RazerMappingGroupDpi",
        "RazerMappingGroupProfile",
        "RazerMappingGroupMediaKey",
        "RazerMappingGroupPowerKey",
        "RazerMappingGroupDoubleClick",
        "RazerMappingGroupScrollWheelMode",
        "RazerMappingGroupRazerKey",
        "RazerMappingGroupWindowsShortcut",
        "RazerMappingGroupHyperShiftToggle",
        "RazerMappingGroupMacro",
        "RazerMappingGroupTurboKey",
        "RazerMappingGroupTurboButton",
        "RazerMappingGroupDpiClutch",
        "RazerMappingGroupProfileSpecific",
    ];

    public static IReadOnlyList<RazerMappingGroupOption> GroupOptions(bool includeMacro) =>
        GroupKeys
            .Select((key, index) => new RazerMappingGroupOption(
                (RazerHidMappingGroup)index, AppStrings.Text(key)))
            .Where(option => includeMacro || option.Group != RazerHidMappingGroup.Macro)
            .ToArray();
}

/// <summary>One selectable onboard macro slot.</summary>
public sealed record RazerMacroSlotOption(byte Id, string Name);

public sealed partial class RazerHidMappingRowViewModel : INotifyPropertyChanged
{
    private readonly IReadOnlyList<RazerHidMacroOption> _localMacros;
    private readonly bool _canUseSoftwareMacro;
    private bool _canUseOnboardMacro;
    private readonly IReadOnlyList<RazerMappingGroupOption> _groupOptions;
    private readonly IRazerMappingActionCatalog _catalog;
    private int _groupIndex;
    private int _optionIndex;
    private int _modifierIndex;
    private double _turboRate = 7;
    private double _profileId = 1;
    private double _dpiClutchX = 800;
    private double _dpiClutchY = 800;
    private int _macroPlaybackIndex;
    private int _macroTargetIndex;
    private int _softwareTriggerModeIndex;
    private double _macroRepeatCount = 1;
    private string _macroErrorText = string.Empty;
    private string _macroSlotWarningText = string.Empty;
    private IReadOnlyList<string> _optionNames = [];
    private IReadOnlyList<RazerMacroSlotOption> _macroSlots = [];
    private int _selectedMacroSlotIndex;
    private string _currentActionText = "--";
    private string _buttonName = string.Empty;

    internal RazerHidMappingRowViewModel(
        byte buttonId,
        IReadOnlyList<RazerHidMacroOption> localMacros,
        bool canUseOnboardMacro,
        bool canUseSoftwareMacro,
        IRazerMappingActionCatalog catalog)
    {
        _localMacros = localMacros;
        _canUseOnboardMacro = canUseOnboardMacro && localMacros.Count > 0;
        _canUseSoftwareMacro = canUseSoftwareMacro && localMacros.Count > 0;
        _macroTargetIndex = _canUseOnboardMacro ? (int)MacroExecutionTarget.Onboard : (int)MacroExecutionTarget.Software;
        _catalog = catalog;
        _groupOptions = RazerMappingText.GroupOptions(_canUseOnboardMacro || _canUseSoftwareMacro);
        ButtonId = buttonId;
        ButtonName = catalog.DescribeButton(buttonId);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public byte ButtonId { get; }
    public string ButtonName
    {
        get => _buttonName;
        private set
        {
            if (_buttonName == value) return;
            _buttonName = value;
            OnPropertyChanged();
        }
    }

    internal void SetButtonName(string name) => ButtonName = name;

    internal static void ApplyButtonNames(
        IEnumerable<RazerHidMappingRowViewModel> rows,
        IReadOnlyDictionary<byte, string>? names)
    {
        if (names is not { Count: > 0 }) return;

        foreach (var row in rows)
        {
            if (names.TryGetValue(row.ButtonId, out var name))
            {
                row.SetButtonName(name);
            }
        }
    }
    public IReadOnlyList<string> GroupNames => _groupOptions.Select(option => option.Name).ToArray();
    public IReadOnlyList<string> ModifierNames => RazerHidMappingActions.ModifierNames;
    public string CurrentActionText
    {
        get => _currentActionText;
        internal set
        {
            if (_currentActionText == value) return;
            _currentActionText = value;
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<string> OptionNames
    {
        get => _optionNames;
        private set
        {
            _optionNames = value;
            OnPropertyChanged();
        }
    }

    public RazerHidMappingGroup Group => (RazerHidMappingGroup)_groupIndex;

    public int GroupOptionIndex
    {
        get
        {
            for (var index = 0; index < _groupOptions.Count; index++)
            {
                if ((int)_groupOptions[index].Group == _groupIndex) return index;
            }
            return 0;
        }
        set
        {
            if (_groupOptions.Count == 0) return;
            var clamped = Math.Clamp(value, 0, _groupOptions.Count - 1);
            var group = (int)_groupOptions[clamped].Group;
            if (_groupIndex == group) return;
            GroupIndex = group;
        }
    }

    public int GroupIndex
    {
        get => _groupIndex;
        set
        {
            if (_groupIndex == value) return;
            _groupIndex = value;
            OptionNames = _catalog.OptionNames(
                (RazerHidMappingGroup)value,
                _localMacros);
            _optionIndex = 0;
            SetMacroValidation(string.Empty);
            SetMacroSlotWarning(string.Empty);
            OnPropertyChanged();
            OnPropertyChanged(nameof(OptionIndex));
            OnPropertyChanged(nameof(ActionHeader));
            OnPropertyChanged(nameof(ModifierVisibility));
            OnPropertyChanged(nameof(TurboRateVisibility));
            OnPropertyChanged(nameof(ProfileIdVisibility));
            OnPropertyChanged(nameof(DpiClutchVisibility));
            OnPropertyChanged(nameof(OptionVisibility));
            OnPropertyChanged(nameof(MacroPlaybackVisibility));
            OnPropertyChanged(nameof(MacroRepeatVisibility));
            OnPropertyChanged(nameof(MacroSlotVisibility));
            OnPropertyChanged(nameof(MacroNoSlotVisibility));
            OnPropertyChanged(nameof(MacroTargetVisibility));
            OnPropertyChanged(nameof(SoftwareTriggerVisibility));
            OnPropertyChanged(nameof(OnboardBehaviorVisibility));
            OnPropertyChanged(nameof(MacroSoftwareHintVisibility));
            OnPropertyChanged(nameof(MacroOnboardHintVisibility));
            OnPropertyChanged(nameof(CanApply));
            OnPropertyChanged(nameof(GroupOptionIndex));
        }
    }

    public int OptionIndex
    {
        get => _optionIndex;
        set
        {
            if (_optionIndex == value) return;
            _optionIndex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanApply));
        }
    }

    public int ModifierIndex
    {
        get => _modifierIndex;
        set
        {
            if (_modifierIndex == value) return;
            _modifierIndex = value;
            OnPropertyChanged();
        }
    }

    public double TurboRate
    {
        get => _turboRate;
        set
        {
            var normalized = double.IsFinite(value) ? Math.Clamp(Math.Round(value), 1, 50) : _turboRate;
            if (_turboRate == normalized) return;
            _turboRate = normalized;
            OnPropertyChanged();
        }
    }

    public double ProfileId
    {
        get => _profileId;
        set
        {
            var normalized = double.IsFinite(value) ? Math.Clamp(Math.Round(value), 1, 5) : _profileId;
            if (_profileId == normalized) return;
            _profileId = normalized;
            OnPropertyChanged();
        }
    }

    public double DpiClutchX
    {
        get => _dpiClutchX;
        set => SetDpi(ref _dpiClutchX, value);
    }

    public double DpiClutchY
    {
        get => _dpiClutchY;
        set => SetDpi(ref _dpiClutchY, value);
    }

    public bool CanUseSoftwareMacro => _canUseSoftwareMacro;

    public bool CanUseOnboardMacro => _canUseOnboardMacro;

    public MacroExecutionTarget MacroTarget => (MacroExecutionTarget)_macroTargetIndex;

    public int MacroTargetIndex
    {
        get => _macroTargetIndex;
        set
        {
            var target = (MacroExecutionTarget)Math.Clamp(value, 0, 1);
            if (target == MacroExecutionTarget.Software && !_canUseSoftwareMacro)
            {
                target = MacroExecutionTarget.Onboard;
            }
            else if (target == MacroExecutionTarget.Onboard && !_canUseOnboardMacro)
            {
                target = MacroExecutionTarget.Software;
            }

            var normalized = (int)target;
            if (_macroTargetIndex == normalized) return;
            _macroTargetIndex = normalized;
            SetMacroValidation(string.Empty);
            SetMacroSlotWarning(string.Empty);
            OnPropertyChanged();
            OnPropertyChanged(nameof(MacroTarget));
            OnPropertyChanged(nameof(MacroPlaybackVisibility));
            OnPropertyChanged(nameof(MacroRepeatVisibility));
            OnPropertyChanged(nameof(MacroSlotVisibility));
            OnPropertyChanged(nameof(MacroNoSlotVisibility));
            OnPropertyChanged(nameof(SoftwareTriggerVisibility));
            OnPropertyChanged(nameof(OnboardBehaviorVisibility));
            OnPropertyChanged(nameof(MacroSoftwareHintVisibility));
            OnPropertyChanged(nameof(MacroOnboardHintVisibility));
            OnPropertyChanged(nameof(CanApply));
        }
    }

    public IReadOnlyList<string> MacroTargetOptions => AppStrings.Texts(
        "RazerMappingMacroTargetSoftware", "RazerMappingMacroTargetOnboard");

    public IReadOnlyList<string> SoftwareTriggerOptions => AppStrings.Texts(
        "RazerMappingMacroPlaybackOnce", "RazerMappingMacroPlaybackToggle",
        "RazerMappingMacroTriggerHold", "RazerMappingMacroPlaybackSequence",
        "RazerMappingMacroPlaybackHoldRepeat");

    public int SoftwareTriggerModeIndex
    {
        get => _softwareTriggerModeIndex;
        set
        {
            var clamped = Math.Clamp(value, 0, SoftwareTriggerOptions.Count - 1);
            if (_softwareTriggerModeIndex == clamped) return;
            _softwareTriggerModeIndex = clamped;
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<string> MacroPlaybackOptions => AppStrings.Texts(
        "RazerMappingMacroPlaybackOnce", "RazerMappingMacroPlaybackRepeat",
        "RazerMappingMacroPlaybackHold", "RazerMappingMacroPlaybackToggle",
        "RazerMappingMacroPlaybackSequence");

    public int MacroPlaybackIndex
    {
        get => _macroPlaybackIndex;
        set
        {
            var clamped = Math.Clamp(value, 0, MacroPlaybackOptions.Count - 1);
            if (_macroPlaybackIndex == clamped) return;
            _macroPlaybackIndex = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MacroRepeatVisibility));
        }
    }

    public double MacroRepeatCount
    {
        get => _macroRepeatCount;
        set
        {
            var normalized = double.IsFinite(value)
                ? Math.Clamp(Math.Round(value), 1, byte.MaxValue)
                : _macroRepeatCount;
            if (_macroRepeatCount == normalized) return;
            _macroRepeatCount = normalized;
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<RazerMacroSlotOption> MacroSlots
    {
        get => _macroSlots;
        private set
        {
            _macroSlots = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MacroSlotNames));
            OnPropertyChanged(nameof(MacroSlotVisibility));
            OnPropertyChanged(nameof(MacroNoSlotVisibility));
            OnPropertyChanged(nameof(CanApply));
        }
    }

    public IReadOnlyList<string> MacroSlotNames => MacroSlots.Select(slot => slot.Name).ToArray();

    public int SelectedMacroSlotIndex
    {
        get => _selectedMacroSlotIndex;
        set
        {
            var clamped = Math.Clamp(value, 0, Math.Max(0, MacroSlots.Count - 1));
            if (_selectedMacroSlotIndex == clamped) return;
            _selectedMacroSlotIndex = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanApply));
        }
    }

    public byte? AssignedMacroSlot { get; private set; }

    /// <summary>The software macro this row showed on the last read.</summary>
    internal string? SoftwareMacroId { get; set; }

    public byte SelectedMacroSlotId => MacroSlots.Count == 0
        ? (byte)0
        : MacroSlots[Math.Clamp(_selectedMacroSlotIndex, 0, MacroSlots.Count - 1)].Id;

    public Visibility ModifierVisibility => _groupIndex == (int)RazerHidMappingGroup.KeyboardKey ||
        _groupIndex == (int)RazerHidMappingGroup.TurboKey
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility TurboRateVisibility => _groupIndex == (int)RazerHidMappingGroup.TurboKey ||
        _groupIndex == (int)RazerHidMappingGroup.TurboButton
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility ProfileIdVisibility => _groupIndex == (int)RazerHidMappingGroup.ProfileSpecific
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility DpiClutchVisibility => _groupIndex == (int)RazerHidMappingGroup.DpiClutch
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility MacroTargetVisibility =>
        _groupIndex == (int)RazerHidMappingGroup.Macro && _canUseSoftwareMacro && _canUseOnboardMacro
            ? Visibility.Visible
            : Visibility.Collapsed;
    public Visibility SoftwareTriggerVisibility =>
        _groupIndex == (int)RazerHidMappingGroup.Macro && MacroTarget == MacroExecutionTarget.Software
            ? Visibility.Visible
            : Visibility.Collapsed;
    public Visibility OnboardBehaviorVisibility =>
        _groupIndex == (int)RazerHidMappingGroup.Macro && MacroTarget == MacroExecutionTarget.Onboard
            ? Visibility.Visible
            : Visibility.Collapsed;
    public Visibility MacroPlaybackVisibility => OnboardBehaviorVisibility;
    public Visibility MacroRepeatVisibility =>
        OnboardBehaviorVisibility == Visibility.Visible &&
        _macroPlaybackIndex == (int)RazerHidMacroPlayback.Repeat
            ? Visibility.Visible
            : Visibility.Collapsed;
    public Visibility MacroSoftwareHintVisibility => SoftwareTriggerVisibility;
    public Visibility MacroOnboardHintVisibility => OnboardBehaviorVisibility;
    public Visibility MacroErrorVisibility =>
        _groupIndex == (int)RazerHidMappingGroup.Macro && _macroErrorText.Length > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    public string MacroErrorText => _macroErrorText;
    public Visibility MacroSlotWarningVisibility =>
        _groupIndex == (int)RazerHidMappingGroup.Macro && _macroSlotWarningText.Length > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    public string MacroSlotWarningText => _macroSlotWarningText;
    public Visibility OptionVisibility => _groupIndex == (int)RazerHidMappingGroup.Keep
        ? Visibility.Collapsed
        : Visibility.Visible;
    public Visibility MacroSlotVisibility =>
        _groupIndex == (int)RazerHidMappingGroup.Macro &&
        MacroTarget == MacroExecutionTarget.Onboard && _canUseOnboardMacro && MacroSlots.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    public Visibility MacroNoSlotVisibility =>
        _groupIndex == (int)RazerHidMappingGroup.Macro &&
        MacroTarget == MacroExecutionTarget.Onboard && _canUseOnboardMacro && MacroSlots.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    public string ActionHeader => Group == RazerHidMappingGroup.Macro
        ? AppStrings.Text("RazerMappingMacroHeader")
        : AppStrings.Text("RazerMappingActionColumn");
    public bool CanApply => _groupIndex > 0 &&
        (Group != RazerHidMappingGroup.Macro ||
         _localMacros.Count > 0 &&
         !HasMacroError &&
         (MacroTarget == MacroExecutionTarget.Software
             ? _canUseSoftwareMacro
             : _canUseOnboardMacro && MacroSlots.Count > 0));

    public bool HasMacroError => _macroErrorText.Length > 0;

    internal void SetMacroValidation(string message)
    {
        if (string.Equals(_macroErrorText, message, StringComparison.Ordinal)) return;
        _macroErrorText = message;
        OnPropertyChanged(nameof(MacroErrorText));
        OnPropertyChanged(nameof(MacroErrorVisibility));
        OnPropertyChanged(nameof(CanApply));
    }

    internal void SetMacroSlotWarning(string message)
    {
        if (string.Equals(_macroSlotWarningText, message, StringComparison.Ordinal)) return;
        _macroSlotWarningText = message;
        OnPropertyChanged(nameof(MacroSlotWarningText));
        OnPropertyChanged(nameof(MacroSlotWarningVisibility));
    }

    internal void SetOnboardAvailability(bool available)
    {
        var normalized = available && _localMacros.Count > 0;
        if (_canUseOnboardMacro == normalized) return;
        _canUseOnboardMacro = normalized;
        if (!normalized && MacroTarget == MacroExecutionTarget.Onboard && _canUseSoftwareMacro)
        {
            _macroTargetIndex = (int)MacroExecutionTarget.Software;
        }

        OnPropertyChanged(nameof(CanUseOnboardMacro));
        OnPropertyChanged(nameof(MacroTargetIndex));
        OnPropertyChanged(nameof(MacroTarget));
        OnPropertyChanged(nameof(MacroTargetVisibility));
        OnPropertyChanged(nameof(MacroPlaybackVisibility));
        OnPropertyChanged(nameof(MacroRepeatVisibility));
        OnPropertyChanged(nameof(MacroSlotVisibility));
        OnPropertyChanged(nameof(MacroNoSlotVisibility));
        OnPropertyChanged(nameof(SoftwareTriggerVisibility));
        OnPropertyChanged(nameof(OnboardBehaviorVisibility));
        OnPropertyChanged(nameof(MacroSoftwareHintVisibility));
        OnPropertyChanged(nameof(MacroOnboardHintVisibility));
        OnPropertyChanged(nameof(CanApply));
        if (!normalized && !_canUseSoftwareMacro && Group == RazerHidMappingGroup.Macro)
        {
            GroupIndex = 0;
        }
    }

    internal void SetMacroSlots(
        IReadOnlyList<RazerMacroSlotOption> freeSlots,
        byte? assignedSlot,
        byte? preferredSlot)
    {
        AssignedMacroSlot = assignedSlot;
        var slots = freeSlots.ToList();
        if (assignedSlot is { } assigned && slots.All(slot => slot.Id != assigned))
        {
            slots.Insert(0, new RazerMacroSlotOption(assigned, FormatSlotName(assigned)));
        }

        MacroSlots = slots;
        var selected = assignedSlot is { } current
            ? slots.FindIndex(slot => slot.Id == current)
            : preferredSlot is { } preferred
                ? slots.FindIndex(slot => slot.Id == preferred)
                : 0;
        SelectedMacroSlotIndex = Math.Max(0, selected);
    }

    public RazerHidMappingAction? CreateAction() =>
        Group switch
        {
            RazerHidMappingGroup.Macro when MacroTarget == MacroExecutionTarget.Onboard =>
                RazerHidMappingActions.CreateMacroAction(
                SelectedMacroSlotId,
                (RazerHidMacroPlayback)_macroPlaybackIndex,
                checked((int)MacroRepeatCount)),
            RazerHidMappingGroup.Macro => null,
            RazerHidMappingGroup.Keep => null,
            var group => _catalog.CreateAction(
                group,
                _optionIndex,
                RazerHidMappingActions.ModifierMasks[Math.Clamp(
                    _modifierIndex, 0, RazerHidMappingActions.ModifierMasks.Count - 1)],
                null,
                checked((int)TurboRate),
                checked((int)ProfileId),
                checked((int)DpiClutchX),
                checked((int)DpiClutchY),
                (RazerHidMacroPlayback)_macroPlaybackIndex,
                checked((int)MacroRepeatCount)),
        };

    private static string FormatSlotName(byte id) => AppStrings.FormatText("RazerMappingMacroSlot", id);

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void SetDpi(ref double field, double value)
    {
        var normalized = double.IsFinite(value)
            ? Math.Clamp(Math.Round(value / 50) * 50, 100, 30000)
            : field;
        if (field == normalized) return;
        field = normalized;
        OnPropertyChanged();
    }
}
