using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OpenSynapse.App.ViewModels;

public sealed class ViperDpiStageRowViewModel : INotifyPropertyChanged
{
    private double _x;
    private double _y;

    public ViperDpiStageRowViewModel(int number, int x, int y)
    {
        Number = number;
        _x = x;
        _y = y;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Number { get; }
    public double X
    {
        get => _x;
        set => SetDpi(ref _x, value);
    }
    public double Y
    {
        get => _y;
        set => SetDpi(ref _y, value);
    }

    private void SetDpi(ref double field, double value, [CallerMemberName] string? propertyName = null)
    {
        if (!double.IsFinite(value))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return;
        }

        var normalized = Math.Clamp(Math.Round(value / 50, MidpointRounding.AwayFromZero) * 50, 100, 30000);
        if (field == normalized)
        {
            return;
        }

        field = normalized;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
