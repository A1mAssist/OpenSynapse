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

    private static readonly (string NameKey, ushort Code)[] MediaKeyOptions =
    [
        ("MacroMediaMute", 0x00E2),
        ("MacroMediaVolumeDown", 0x00EA),
        ("MacroMediaVolumeUp", 0x00E9),
        ("MacroMediaPrevious", 0x00B6),
        ("MacroMediaPlayPause", 0x00CD),
        ("MacroMediaNext", 0x00B5),
        ("MacroMediaStop", 0x00B7),
    ];

    public IReadOnlyList<string> ActionOptions =>
    [
        .. AppStrings.Texts(
            "Text_39B523BD", "Text_794A1C95", "Text_78980344", "Text_6D40E761", "Text_61DF3243",
            "Text_290DA6F9", "Text_10DDDE56", "Text_11DDDFE9", "Text_80A48F18", "Text_45977270",
            "Text_D9D5D89A"),
        .. MediaKeyOptions.Select(option => AppStrings.Text(option.NameKey)),
    ];
    public string ButtonText => FormatButton(Assignment.ButtonId);
    public string LayerText => Assignment.Layer == ViperButtonMappingLayer.Normal ? AppStrings.Text("Text_190B5DB1") : "HyperShift";
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
            throw new InvalidOperationException(AppStrings.Text("Text_8F4E16B7"));
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
        if (SelectedActionIndex >= 11 && SelectedActionIndex < 11 + MediaKeyOptions.Length)
        {
            var code = MediaKeyOptions[SelectedActionIndex - 11].Code;
            assignment = Assignment with
            {
                Function = ViperButtonMappingFunction.MediaKey,
                FunctionData = [(byte)(code >> 8), (byte)code],
            };
            return true;
        }

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
            10 => (ViperButtonMappingFunction.Dpi, new byte[] { 6 }),
            _ => default,
        };
        if (SelectedActionIndex is < 0 or > 10)
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
        if (assignment.Function == ViperButtonMappingFunction.Dpi &&
            assignment.FunctionData.Count == 1 && assignment.FunctionData[0] == 6)
        {
            return 10;
        }
        if (assignment.Function == ViperButtonMappingFunction.MediaKey &&
            assignment.FunctionData.Count == 2)
        {
            var code = (ushort)((assignment.FunctionData[0] << 8) | assignment.FunctionData[1]);
            var mediaIndex = Array.FindIndex(MediaKeyOptions, option => option.Code == code);
            if (mediaIndex >= 0)
            {
                return 11 + mediaIndex;
            }
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
            ViperButtonMappingFunction.Off => AppStrings.Text("Text_39B523BD"),
            ViperButtonMappingFunction.MouseButton when assignment.FunctionData.Count == 1 =>
                FormatButton(assignment.FunctionData[0]),
            ViperButtonMappingFunction.KeyboardKey when assignment.FunctionData.Count == 2 =>
                AppStrings.FormatText("KeyboardMappingDescription",
                    assignment.FunctionData[0],
                    assignment.FunctionData[1]),
            ViperButtonMappingFunction.DoubleClick when assignment.FunctionData.Count == 1 &&
                assignment.FunctionData[0] == 1 => AppStrings.Text("Text_45977270"),
            ViperButtonMappingFunction.Dpi when assignment.FunctionData.Count == 1 &&
                assignment.FunctionData[0] == 6 => AppStrings.Text("Text_D9D5D89A"),
            ViperButtonMappingFunction.MediaKey when assignment.FunctionData.Count == 2 =>
                FormatMediaKey(assignment.FunctionData[0], assignment.FunctionData[1]),
            _ => assignment.FunctionData.Count == 0
                ? assignment.Function.ToString()
                : $"{assignment.Function} · {Convert.ToHexString(assignment.FunctionData.ToArray())}",
        };

    private static string FormatMediaKey(byte high, byte low)
    {
        var code = (ushort)((high << 8) | low);
        var index = Array.FindIndex(MediaKeyOptions, option => option.Code == code);
        return index >= 0
            ? AppStrings.Text(MediaKeyOptions[index].NameKey)
            : $"Media key 0x{code:X4}";
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
