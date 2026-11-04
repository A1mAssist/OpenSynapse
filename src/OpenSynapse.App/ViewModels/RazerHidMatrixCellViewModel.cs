using System.ComponentModel;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed class RazerHidMatrixCellViewModel : INotifyPropertyChanged
{
    private Color _color = Color.FromArgb(255, 0, 255, 102);

    public RazerHidMatrixCellViewModel(IReadOnlyList<(int Row, int Column)> devicePositions)
    {
        DevicePositions = devicePositions;
        var first = devicePositions.Count > 0 ? devicePositions[0] : (Row: 0, Column: 0);
        Row = first.Row;
        Column = first.Column;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<(int Row, int Column)> DevicePositions { get; }
    public int Row { get; }
    public int Column { get; }
    public bool IsPlaceholder => DevicePositions.Count == 0;
    public bool IsInteractive => !IsPlaceholder;
    public double Opacity => IsPlaceholder ? 0 : 1;
    public string AutomationName => IsPlaceholder
        ? string.Empty
        : AppStrings.FormatText("RazerHidMatrixCellLabel", Row + 1, Column + 1);
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
