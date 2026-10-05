using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OpenSynapse.App.ViewModels;

public sealed class OpenRazerDpiStageRowViewModel : INotifyPropertyChanged
{
    private int _x;
    private int _y;
    private bool _isEditable;

    public OpenRazerDpiStageRowViewModel(byte number, int x, int y, bool isEditable)
    {
        Number = number;
        _x = x;
        _y = y;
        _isEditable = isEditable;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public byte Number { get; }
    public bool IsEditable => _isEditable;
    public string Label => AppStrings.FormatText("OpenRazerDpiStageLabel", Number);
    public int X { get => _x; set => SetField(ref _x, value); }
    public int Y { get => _y; set => SetField(ref _y, value); }

    public void RefreshLocalization() => PropertyChanged?.Invoke(this, new(string.Empty));

    public void SetEditable(bool value)
    {
        if (_isEditable == value) return;
        _isEditable = value;
        PropertyChanged?.Invoke(this, new(nameof(IsEditable)));
    }

    private void SetField(ref int field, int value, [CallerMemberName] string? name = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new(name));
    }
}
