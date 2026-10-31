using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed class ViperButtonAssignmentRowViewModel : INotifyPropertyChanged
{
    private static readonly ViperMappingActionGroup[] GroupOrder =
    [
        ViperMappingActionGroup.Mouse,
        ViperMappingActionGroup.Keyboard,
        ViperMappingActionGroup.Dpi,
        ViperMappingActionGroup.Profile,
        ViperMappingActionGroup.Media,
        ViperMappingActionGroup.Power,
        ViperMappingActionGroup.Scroll,
        ViperMappingActionGroup.RazerKey,
        ViperMappingActionGroup.WindowsShortcut,
        ViperMappingActionGroup.HyperShift,
        ViperMappingActionGroup.Turbo,
    ];

    private static readonly byte[] TurboButtonCodes = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

    private int _selectedActionGroupIndex;
    private int _selectedActionIndex;
    private double _keyboardModifierValue;
    private double _keyboardUsageValue = 4;
    private double _dpiClutchX = 800;
    private double _dpiClutchY = 800;
    private double _turboRate = 7;
    private double _profileId = 1;
    private int _turboButtonIndex;
    private bool _isUnsupported;

    public ViperButtonAssignmentRowViewModel(ViperButtonAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        Assignment = assignment;
        RestoreEditorFromAssignment();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void RefreshLocalization() => PropertyChanged?.Invoke(this, new(string.Empty));

    public IReadOnlyList<string> ActionGroupOptions =>
        GroupOrder.Select(GroupName).ToArray();

    public int SelectedActionGroupIndex
    {
        get => _selectedActionGroupIndex;
        set
        {
            var clamped = Math.Clamp(value, 0, GroupOrder.Length - 1);
            if (_selectedActionGroupIndex == clamped)
            {
                return;
            }

            _selectedActionGroupIndex = clamped;
            _selectedActionIndex = 0;
            _isUnsupported = false;
            NotifyActionChanged();
        }
    }

    public IReadOnlyList<string> ActionOptions =>
        DefinitionsInSelectedGroup.Select(ActionName).ToArray();

    public int SelectedActionIndex
    {
        get => _selectedActionIndex;
        set
        {
            var count = DefinitionsInSelectedGroup.Count;
            var clamped = count == 0 ? 0 : Math.Clamp(value, 0, count - 1);
            if (_selectedActionIndex == clamped)
            {
                return;
            }

            _selectedActionIndex = clamped;
            _isUnsupported = false;
            NotifyActionChanged();
        }
    }

    public string ButtonText => FormatButton(Assignment.ButtonId);
    public string LayerText => Assignment.Layer == ViperButtonMappingLayer.Normal
        ? AppStrings.Text("Text_190B5DB1")
        : "HyperShift";
    public string CurrentActionText => Describe(Assignment);
    public ViperButtonAssignment Assignment { get; private set; }

    public double KeyboardModifierValue
    {
        get => _keyboardModifierValue;
        set => SetByte(ref _keyboardModifierValue, value);
    }

    public double KeyboardUsageValue
    {
        get => _keyboardUsageValue;
        set => SetByte(ref _keyboardUsageValue, value);
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

    public double TurboRate
    {
        get => _turboRate;
        set => SetRate(ref _turboRate, value);
    }

    public double ProfileId
    {
        get => _profileId;
        set
        {
            var normalized = double.IsFinite(value) ? Math.Clamp(Math.Round(value), 1, 5) : _profileId;
            if (_profileId == normalized)
            {
                return;
            }

            _profileId = normalized;
            PropertyChanged?.Invoke(this, new(nameof(ProfileId)));
        }
    }

    public IReadOnlyList<string> TurboButtonOptions => TurboButtonCodes.Select(FormatButton).ToArray();

    public int TurboButtonIndex
    {
        get => _turboButtonIndex;
        set
        {
            var clamped = Math.Clamp(value, 0, TurboButtonCodes.Length - 1);
            if (_turboButtonIndex == clamped)
            {
                return;
            }

            _turboButtonIndex = clamped;
            PropertyChanged?.Invoke(this, new(nameof(TurboButtonIndex)));
            PropertyChanged?.Invoke(this, new(nameof(CanApply)));
        }
    }

    public Visibility KeyboardParameterVisibility => Definition?.UsesKeyboard == true
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility DpiClutchParameterVisibility => Definition?.UsesDpiClutch == true
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility TurboButtonParameterVisibility => Definition?.UsesTurboButton == true
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility TurboRateVisibility => Definition?.UsesTurboRate == true
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility TurboKeyRateVisibility => Definition is { UsesTurboRate: true, UsesTurboButton: false }
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility ProfileIdVisibility => Definition?.UsesProfileId == true
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility DoubleClickParameterVisibility =>
        Definition?.Function == ViperButtonMappingFunction.DoubleClick
            ? Visibility.Visible
            : Visibility.Collapsed;

    public bool CanApply => !_isUnsupported &&
        TryCreateAssignment(out var requested) &&
        !AssignmentsEqual(Assignment, requested);

    public ViperButtonAssignment CreateAssignment()
    {
        // An unknown Synapse assignment (for example a macro) is preserved verbatim.
        if (_isUnsupported)
        {
            return Assignment;
        }
        if (!TryCreateAssignment(out var assignment))
        {
            throw new InvalidOperationException(AppStrings.Text("Text_8F4E16B7"));
        }
        return assignment;
    }

    public void Apply(ViperButtonAssignment assignment)
    {
        Assignment = assignment;
        RestoreEditorFromAssignment();
        NotifyActionChanged();
    }

    public void RestoreSelection()
    {
        RestoreEditorFromAssignment();
        NotifyActionChanged();
    }

    private ViperMappingActionDefinition? Definition
    {
        get
        {
            var index = GlobalActionIndex;
            return index >= 0 && index < ViperMappingActions.Definitions.Count
                ? ViperMappingActions.Definitions[index]
                : null;
        }
    }

    private IReadOnlyList<ViperMappingActionDefinition> DefinitionsInSelectedGroup
    {
        get
        {
            var group = GroupOrder[Math.Clamp(_selectedActionGroupIndex, 0, GroupOrder.Length - 1)];
            return ViperMappingActions.Definitions
                .Where(definition => definition.Group == group)
                .ToArray();
        }
    }

    private int GlobalActionIndex
    {
        get
        {
            var group = GroupOrder[Math.Clamp(_selectedActionGroupIndex, 0, GroupOrder.Length - 1)];
            var start = ViperMappingActions.GroupStart(group);
            return start < 0 ? -1 : start + _selectedActionIndex;
        }
    }

    private bool TryCreateAssignment(out ViperButtonAssignment assignment)
    {
        if (_isUnsupported)
        {
            assignment = Assignment;
            return false;
        }

        var global = GlobalActionIndex;
        if (global < 0 || !ViperMappingActions.TryApply(
            Assignment,
            global,
            checked((byte)KeyboardModifierValue),
            checked((byte)KeyboardUsageValue),
            checked((int)DpiClutchX),
            checked((int)DpiClutchY),
            checked((int)TurboRate),
            TurboButtonCodes[Math.Clamp(_turboButtonIndex, 0, TurboButtonCodes.Length - 1)],
            out assignment,
            checked((int)ProfileId)))
        {
            assignment = Assignment;
            return false;
        }
        return true;
    }

    private void RestoreEditorFromAssignment()
    {
        var global = ViperMappingActions.IndexOf(Assignment);
        _isUnsupported = global < 0;
        if (_isUnsupported)
        {
            _selectedActionGroupIndex = 0;
            _selectedActionIndex = 0;
            return;
        }

        var definition = ViperMappingActions.Definitions[global];
        _selectedActionGroupIndex = Array.IndexOf(GroupOrder, definition.Group);
        var start = ViperMappingActions.GroupStart(definition.Group);
        _selectedActionIndex = start < 0 ? 0 : global - start;

        if (definition.UsesProfileId && Assignment.FunctionData.Count >= 2)
        {
            _profileId = Assignment.FunctionData[1];
        }

        if (Assignment.Function is ViperButtonMappingFunction.KeyboardKey or
            ViperButtonMappingFunction.KeyboardTurbo &&
            Assignment.FunctionData.Count >= 2)
        {
            _keyboardModifierValue = Assignment.FunctionData[0];
            _keyboardUsageValue = Assignment.FunctionData[1];
        }

        if (Assignment.Function == ViperButtonMappingFunction.Dpi &&
            Assignment.FunctionData.Count == 5 && Assignment.FunctionData[0] == 5)
        {
            _dpiClutchX = (Assignment.FunctionData[1] << 8) | Assignment.FunctionData[2];
            _dpiClutchY = (Assignment.FunctionData[3] << 8) | Assignment.FunctionData[4];
        }

        if (Assignment.Function is ViperButtonMappingFunction.KeyboardTurbo or
            ViperButtonMappingFunction.MouseTurbo)
        {
            if (Assignment.Function == ViperButtonMappingFunction.KeyboardTurbo &&
                Assignment.FunctionData.Count >= 4)
            {
                _turboRate = IntervalToRate(Assignment.FunctionData[2], Assignment.FunctionData[3]);
            }
            else if (Assignment.Function == ViperButtonMappingFunction.MouseTurbo &&
                Assignment.FunctionData.Count >= 3)
            {
                _turboRate = IntervalToRate(Assignment.FunctionData[1], Assignment.FunctionData[2]);
            }

            if (Assignment.Function == ViperButtonMappingFunction.MouseTurbo &&
                Assignment.FunctionData.Count >= 1)
            {
                var buttonIndex = Array.IndexOf(TurboButtonCodes, Assignment.FunctionData[0]);
                _turboButtonIndex = buttonIndex < 0 ? 0 : buttonIndex;
            }
        }
    }

    private void NotifyActionChanged()
    {
        PropertyChanged?.Invoke(this, new(nameof(SelectedActionGroupIndex)));
        PropertyChanged?.Invoke(this, new(nameof(ActionOptions)));
        PropertyChanged?.Invoke(this, new(nameof(SelectedActionIndex)));
        PropertyChanged?.Invoke(this, new(nameof(CurrentActionText)));
        PropertyChanged?.Invoke(this, new(nameof(KeyboardParameterVisibility)));
        PropertyChanged?.Invoke(this, new(nameof(DpiClutchParameterVisibility)));
        PropertyChanged?.Invoke(this, new(nameof(TurboButtonParameterVisibility)));
        PropertyChanged?.Invoke(this, new(nameof(TurboRateVisibility)));
        PropertyChanged?.Invoke(this, new(nameof(TurboKeyRateVisibility)));
        PropertyChanged?.Invoke(this, new(nameof(ProfileIdVisibility)));
        PropertyChanged?.Invoke(this, new(nameof(DoubleClickParameterVisibility)));
        PropertyChanged?.Invoke(this, new(nameof(CanApply)));
    }

    private void SetByte(ref double field, double value)
    {
        var normalized = double.IsFinite(value)
            ? Math.Clamp(Math.Round(value), byte.MinValue, byte.MaxValue)
            : field;
        if (field == normalized)
        {
            return;
        }

        field = normalized;
        PropertyChanged?.Invoke(this, new(string.Empty));
        PropertyChanged?.Invoke(this, new(nameof(CanApply)));
    }

    private void SetDpi(ref double field, double value)
    {
        var normalized = double.IsFinite(value)
            ? Math.Clamp(Math.Round(value / 50) * 50, 100, 30000)
            : field;
        if (field == normalized)
        {
            return;
        }

        field = normalized;
        PropertyChanged?.Invoke(this, new(string.Empty));
        PropertyChanged?.Invoke(this, new(nameof(CanApply)));
    }

    private void SetRate(ref double field, double value)
    {
        var normalized = double.IsFinite(value) ? Math.Clamp(Math.Round(value), 1, 50) : field;
        if (field == normalized)
        {
            return;
        }

        field = normalized;
        PropertyChanged?.Invoke(this, new(string.Empty));
        PropertyChanged?.Invoke(this, new(nameof(CanApply)));
    }

    private static double IntervalToRate(byte high, byte low)
    {
        var interval = Math.Max(1, (high << 8) | low);
        return Math.Clamp(Math.Round(1000d / interval), 1, 50);
    }

    private static string GroupName(ViperMappingActionGroup group) => group switch
    {
        ViperMappingActionGroup.Mouse => AppStrings.Text("ViperMappingGroupMouse"),
        ViperMappingActionGroup.Keyboard => AppStrings.Text("ViperMappingGroupKeyboard"),
        ViperMappingActionGroup.Dpi => AppStrings.Text("ViperMappingGroupDpi"),
        ViperMappingActionGroup.Profile => AppStrings.Text("ViperMappingGroupProfile"),
        ViperMappingActionGroup.Media => AppStrings.Text("ViperMappingGroupMedia"),
        ViperMappingActionGroup.Power => AppStrings.Text("ViperMappingGroupPower"),
        ViperMappingActionGroup.Scroll => AppStrings.Text("ViperMappingGroupScroll"),
        ViperMappingActionGroup.RazerKey => AppStrings.Text("ViperMappingGroupRazer"),
        ViperMappingActionGroup.WindowsShortcut => AppStrings.Text("ViperMappingGroupWindows"),
        ViperMappingActionGroup.HyperShift => AppStrings.Text("ViperMappingGroupHyperShift"),
        ViperMappingActionGroup.Turbo => AppStrings.Text("ViperMappingGroupTurbo"),
        _ => group.ToString(),
    };

    private static string ActionName(ViperMappingActionDefinition definition) =>
        definition.Function == ViperButtonMappingFunction.MouseButton
            ? FormatButton(definition.Value)
            : AppStrings.Text(definition.NameKey);

    private static string Describe(ViperButtonAssignment assignment)
    {
        var index = ViperMappingActions.IndexOf(assignment);
        if (index >= 0)
        {
            var definition = ViperMappingActions.Definitions[index];
            var name = ActionName(definition);
            if (definition.UsesProfileId && assignment.FunctionData.Count >= 2)
            {
                return $"{name} {assignment.FunctionData[1]}";
            }

            if (definition.UsesDpiClutch && assignment.FunctionData.Count == 5)
            {
                var x = (assignment.FunctionData[1] << 8) | assignment.FunctionData[2];
                var y = (assignment.FunctionData[3] << 8) | assignment.FunctionData[4];
                return $"{name} {x}×{y}";
            }

            if (definition.Function == ViperButtonMappingFunction.MouseTurbo &&
                assignment.FunctionData.Count >= 1)
            {
                return $"{name} {FormatButton(assignment.FunctionData[0])}";
            }

            return name;
        }

        return assignment.Function switch
        {
            ViperButtonMappingFunction.MacroTypeI when assignment.FunctionData.Count >= 3 =>
                $"Macro {assignment.FunctionData[0]:X2}{assignment.FunctionData[1]:X2} ×{assignment.FunctionData[2]}",
            ViperButtonMappingFunction.MacroTypeII when assignment.FunctionData.Count >= 2 =>
                $"Macro {assignment.FunctionData[0]:X2}{assignment.FunctionData[1]:X2} (hold)",
            ViperButtonMappingFunction.MacroTypeIII when assignment.FunctionData.Count >= 2 =>
                $"Macro {assignment.FunctionData[0]:X2}{assignment.FunctionData[1]:X2} (toggle)",
            ViperButtonMappingFunction.MacroTypeIV when assignment.FunctionData.Count >= 2 =>
                $"Macro {assignment.FunctionData[0]:X2}{assignment.FunctionData[1]:X2} (sequence)",
            _ => assignment.FunctionData.Count == 0
                ? assignment.Function.ToString()
                : $"{assignment.Function} · {Convert.ToHexString(assignment.FunctionData.ToArray())}",
        };
    }

    private static string FormatButton(byte buttonId) => buttonId switch
    {
        1 => AppStrings.Text("Text_794A1C95"),
        2 => AppStrings.Text("Text_78980344"),
        3 => AppStrings.Text("Text_6D40E761"),
        4 => AppStrings.Text("Text_61DF3243"),
        5 => AppStrings.Text("Text_290DA6F9"),
        9 => AppStrings.Text("Text_10DDDE56"),
        10 => AppStrings.Text("Text_11DDDFE9"),
        96 => AppStrings.Text("Text_3A5819C1"),
        _ => AppStrings.FormatText("MouseControlNumber", buttonId),
    };

    private static bool AssignmentsEqual(ViperButtonAssignment left, ViperButtonAssignment right) =>
        left.ProfileId == right.ProfileId &&
        left.ButtonId == right.ButtonId &&
        left.Layer == right.Layer &&
        left.Function == right.Function &&
        left.FunctionData.SequenceEqual(right.FunctionData);
}
