using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed class ViperButtonAssignmentRowViewModel : INotifyPropertyChanged
{
    private static readonly byte[] MouseButtonCodes = [1, 2, 3, 4, 5, 9, 10];
    private int _selectedActionIndex;
    private double _keyboardModifierValue;
    private double _keyboardUsageValue = 4;

    public ViperButtonAssignmentRowViewModel(ViperButtonAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        Assignment = assignment;
        RestoreEditorFromAssignment();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void RefreshLocalization() => PropertyChanged?.Invoke(this, new(string.Empty));

    public IReadOnlyList<string> ActionOptions => AppStrings.Get(
        "关闭", "左键", "右键", "滚轮按下", "后退", "前进", "滚轮向上", "滚轮向下", "键盘按键", "双击");
    public string ButtonText => FormatButton(Assignment.ButtonId);
    public string LayerText => Assignment.Layer == ViperButtonMappingLayer.Normal ? AppStrings.Get("普通") : "HyperShift";
    public string CurrentActionText => FormatAction(Assignment);
    public ViperButtonAssignment Assignment { get; private set; }
    public int SelectedActionIndex
    {
        get => _selectedActionIndex;
        set
        {
            if (_selectedActionIndex == value)
            {
                return;
            }
            _selectedActionIndex = value;
            PropertyChanged?.Invoke(this, new(nameof(SelectedActionIndex)));
            PropertyChanged?.Invoke(this, new(nameof(KeyboardParameterVisibility)));
            PropertyChanged?.Invoke(this, new(nameof(DoubleClickParameterVisibility)));
            PropertyChanged?.Invoke(this, new(nameof(CanApply)));
        }
    }
    public double KeyboardModifierValue
    {
        get => _keyboardModifierValue;
        set => SetKeyboardByte(ref _keyboardModifierValue, value);
    }
    public double KeyboardUsageValue
    {
        get => _keyboardUsageValue;
        set => SetKeyboardByte(ref _keyboardUsageValue, value);
    }
    public Visibility KeyboardParameterVisibility => SelectedActionIndex == 8
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility DoubleClickParameterVisibility => SelectedActionIndex == 9
        ? Visibility.Visible
        : Visibility.Collapsed;
    public bool CanApply => TryCreateAssignment(out var requested) && !AssignmentsEqual(Assignment, requested);

    public ViperButtonAssignment CreateAssignment()
    {
        if (!TryCreateAssignment(out var assignment))
        {
            throw new InvalidOperationException(AppStrings.Get("请选择已验证的板载映射动作。"));
        }
        return assignment;
    }

    public void Apply(ViperButtonAssignment assignment)
    {
        Assignment = assignment;
        RestoreEditorFromAssignment();
        NotifyEditorChanged();
    }

    public void RestoreSelection()
    {
        RestoreEditorFromAssignment();
        NotifyEditorChanged();
    }

    private bool TryCreateAssignment(out ViperButtonAssignment assignment)
    {
        var (function, data) = SelectedActionIndex switch
        {
            0 => (ViperButtonMappingFunction.Off, Array.Empty<byte>()),
            >= 1 and <= 7 => (
                ViperButtonMappingFunction.MouseButton,
                new byte[] { MouseButtonCodes[SelectedActionIndex - 1] }),
            8 => (
                ViperButtonMappingFunction.KeyboardKey,
                new byte[] { checked((byte)KeyboardModifierValue), checked((byte)KeyboardUsageValue) }),
            9 => (ViperButtonMappingFunction.DoubleClick, new byte[] { 1 }),
            _ => default,
        };
        if (SelectedActionIndex is < 0 or > 9)
        {
            assignment = Assignment;
            return false;
        }

        assignment = Assignment with { Function = function, FunctionData = data };
        return true;
    }

    private void RestoreEditorFromAssignment()
    {
        _selectedActionIndex = GetActionIndex(Assignment);
        if (Assignment.Function == ViperButtonMappingFunction.KeyboardKey &&
            Assignment.FunctionData.Count == 2)
        {
            _keyboardModifierValue = Assignment.FunctionData[0];
            _keyboardUsageValue = Assignment.FunctionData[1];
        }
        else
        {
            _keyboardModifierValue = 0;
            _keyboardUsageValue = 4;
        }
    }

    private void SetKeyboardByte(ref double field, double value, [CallerMemberName] string? propertyName = null)
    {
        var normalized = double.IsFinite(value) ? Math.Clamp(Math.Round(value), byte.MinValue, byte.MaxValue) : field;
        if (field == normalized)
        {
            return;
        }
        field = normalized;
        PropertyChanged?.Invoke(this, new(propertyName));
        PropertyChanged?.Invoke(this, new(nameof(CanApply)));
    }

    private void NotifyEditorChanged()
    {
        PropertyChanged?.Invoke(this, new(nameof(CurrentActionText)));
        PropertyChanged?.Invoke(this, new(nameof(SelectedActionIndex)));
        PropertyChanged?.Invoke(this, new(nameof(KeyboardModifierValue)));
        PropertyChanged?.Invoke(this, new(nameof(KeyboardUsageValue)));
        PropertyChanged?.Invoke(this, new(nameof(KeyboardParameterVisibility)));
        PropertyChanged?.Invoke(this, new(nameof(DoubleClickParameterVisibility)));
        PropertyChanged?.Invoke(this, new(nameof(CanApply)));
    }

    private static int GetActionIndex(ViperButtonAssignment assignment)
    {
        if (assignment.Function == ViperButtonMappingFunction.Off)
        {
            return 0;
        }
        if (assignment.Function == ViperButtonMappingFunction.KeyboardKey &&
            assignment.FunctionData.Count == 2)
        {
            return 8;
        }
        if (assignment.Function == ViperButtonMappingFunction.DoubleClick &&
            assignment.FunctionData.Count == 1 && assignment.FunctionData[0] == 1)
        {
            return 9;
        }
        if (assignment.Function != ViperButtonMappingFunction.MouseButton ||
            assignment.FunctionData.Count != 1)
        {
            return -1;
        }

        var index = Array.IndexOf(MouseButtonCodes, assignment.FunctionData[0]);
        return index < 0 ? -1 : index + 1;
    }

    private static string FormatAction(ViperButtonAssignment assignment) =>
        assignment.Function switch
        {
            ViperButtonMappingFunction.Off => AppStrings.Get("关闭"),
            ViperButtonMappingFunction.MouseButton when assignment.FunctionData.Count == 1 =>
                FormatButton(assignment.FunctionData[0]),
            ViperButtonMappingFunction.KeyboardKey when assignment.FunctionData.Count == 2 =>
                AppStrings.FormatText("KeyboardMappingDescription",
                    assignment.FunctionData[0],
                    assignment.FunctionData[1]),
            ViperButtonMappingFunction.DoubleClick when assignment.FunctionData.Count == 1 &&
                assignment.FunctionData[0] == 1 => AppStrings.Get("双击"),
            _ => assignment.FunctionData.Count == 0
                ? assignment.Function.ToString()
                : $"{assignment.Function} · {Convert.ToHexString(assignment.FunctionData.ToArray())}",
        };

    private static string FormatButton(byte buttonId) => buttonId switch
    {
        1 => AppStrings.Get("左键"),
        2 => AppStrings.Get("右键"),
        3 => AppStrings.Get("滚轮按下"),
        4 => AppStrings.Get("后退"),
        5 => AppStrings.Get("前进"),
        9 => AppStrings.Get("滚轮向上"),
        10 => AppStrings.Get("滚轮向下"),
        96 => AppStrings.Get("DPI 切换键"),
        _ => AppStrings.FormatText("MouseControlNumber", buttonId),
    };

    private static bool AssignmentsEqual(ViperButtonAssignment left, ViperButtonAssignment right) =>
        left.ProfileId == right.ProfileId &&
        left.ButtonId == right.ButtonId &&
        left.Layer == right.Layer &&
        left.Function == right.Function &&
        left.FunctionData.SequenceEqual(right.FunctionData);
}
