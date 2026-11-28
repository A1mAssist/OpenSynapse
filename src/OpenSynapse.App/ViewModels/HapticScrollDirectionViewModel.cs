using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using OpenSynapse.Core.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed class HapticScrollPointViewModel(int index, HapticScrollControlPoint point) : INotifyPropertyChanged
{
    private double _x = point.X;
    private double _y = point.Y;
    public event PropertyChangedEventHandler? PropertyChanged;
    public int Index { get; } = index;
    public string Label => AppStrings.Text(Index switch
    {
        0 => "HapticCurveStart", 1 => "HapticCurveRise", 2 => "HapticCurveMiddle",
        3 => "HapticCurveFall", _ => "HapticCurveEnd",
    });
    public bool CanEdit => Index is > 0 and < 4;
    public double X { get => _x; set => Set(ref _x, value); }
    public double Y { get => _y; set => Set(ref _y, value); }
    private void Set(ref double field, double value, [CallerMemberName] string? name = null)
    {
        if (field.Equals(value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new(name));
    }
}

public sealed class HapticScrollDirectionViewModel : INotifyPropertyChanged
{
    private double _steps;
    private double _tension;
    public event PropertyChangedEventHandler? PropertyChanged;
    public HapticScrollDirectionViewModel(string title, HapticScrollDirection direction)
    {
        Title = title;
        _steps = direction.Steps; _tension = direction.Tension;
        Points = new(direction.Points.Select((point, i) => new HapticScrollPointViewModel(i, point)));
        foreach (var point in Points) point.PropertyChanged += (_, _) => PropertyChanged?.Invoke(this, new(nameof(Points)));
    }
    public string Title { get; }
    public ObservableCollection<HapticScrollPointViewModel> Points { get; }
    public double Steps { get => _steps; set => Set(ref _steps, value); }
    public double Tension { get => _tension; set => Set(ref _tension, value); }
    private void Set(ref double field, double value, [CallerMemberName] string? name = null)
    {
        if (field.Equals(value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new(name));
    }
    public HapticScrollDirection Capture()
    {
        static byte Integer(double value)
        {
            if (!double.IsFinite(value) || value != Math.Round(value) || value is < 0 or > 255)
                throw new InvalidDataException(AppStrings.Text("HapticCurveInvalid"));
            return (byte)value;
        }
        var direction = new HapticScrollDirection
        {
            Steps = Integer(Steps), Tension = Integer(Tension),
            Points = Points.Select(p => new HapticScrollControlPoint(Integer(p.X), Integer(p.Y))).ToList(),
        };
        direction.Validate();
        return direction;
    }
}
