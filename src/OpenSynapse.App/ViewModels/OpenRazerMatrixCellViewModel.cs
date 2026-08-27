using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed class OpenRazerMatrixCellViewModel : INotifyPropertyChanged
{
    private Color _color = Color.FromArgb(255, 0, 255, 102);

    public OpenRazerMatrixCellViewModel(byte row, byte column)
    {
        Row = row;
        Column = column;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public byte Row { get; }
    public byte Column { get; }
    public string AutomationName => AppStrings.FormatText("OpenRazerMatrixCellLabel", Row + 1, Column + 1);
    public Color Color
    {
        get => _color;
        set
        {
            if (_color == value) return;
            _color = value;
            PropertyChanged?.Invoke(this, new(nameof(Color)));
            PropertyChanged?.Invoke(this, new(nameof(Brush)));
        }
    }
    public Brush Brush => new SolidColorBrush(Color);
    public void RefreshLocalization() => PropertyChanged?.Invoke(this, new(string.Empty));
}
