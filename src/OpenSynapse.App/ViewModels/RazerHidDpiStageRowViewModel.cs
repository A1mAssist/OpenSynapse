using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed class RazerHidDpiStageRowViewModel : INotifyPropertyChanged
{
    private int _x;
    private int _y;
    private bool _isEditable;
    private bool _isActive;

    public RazerHidDpiStageRowViewModel(byte number, int x, int y, bool isEditable)
        : this(number, x, y, 0, isEditable)
    {
    }

    public RazerHidDpiStageRowViewModel(byte number, int x, int y, int z, bool isEditable)
    {
        Number = number;
        _x = x;
        _y = y;
        Z = z;
        _isEditable = isEditable;
        StageBrush = new SolidColorBrush(StageColor(number));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public byte Number { get; }
    public int Z { get; }
    public bool IsEditable => _isEditable;
    public bool IsActive => _isActive;
    public string Label => AppStrings.FormatText("RazerHidDpiStageLabel", Number);
    // Synapse marks stages 1..5 red, green, blue, cyan and yellow.
    public SolidColorBrush StageBrush { get; }
    public int X { get => _x; set => SetField(ref _x, value); }
    public int Y { get => _y; set => SetField(ref _y, value); }

    public void RefreshLocalization() => PropertyChanged?.Invoke(this, new(string.Empty));

    public void SetEditable(bool value)
    {
        if (_isEditable == value) return;
        _isEditable = value;
        PropertyChanged?.Invoke(this, new(nameof(IsEditable)));
    }

    public void SetActive(bool value)
    {
        if (_isActive == value) return;
        _isActive = value;
        PropertyChanged?.Invoke(this, new(nameof(IsActive)));
    }

    private void SetField(ref int field, int value, [CallerMemberName] string? name = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new(name));
    }

    private static Color StageColor(byte number) => number switch
    {
        1 => Color.FromArgb(255, 0xFF, 0x00, 0x00),
        2 => Color.FromArgb(255, 0x00, 0xFF, 0x00),
        3 => Color.FromArgb(255, 0x00, 0x7E, 0xF5),
        4 => Color.FromArgb(255, 0x00, 0xFF, 0xFF),
        5 => Color.FromArgb(255, 0xFF, 0xFF, 0x00),
        _ => Color.FromArgb(255, 0x80, 0x80, 0x80),
    };
}
