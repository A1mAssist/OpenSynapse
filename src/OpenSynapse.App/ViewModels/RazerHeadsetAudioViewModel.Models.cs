using System.ComponentModel;

namespace OpenSynapse.App.ViewModels;

public sealed class RazerHeadsetEqBandViewModel : INotifyPropertyChanged
{
    private double _level;

    public RazerHeadsetEqBandViewModel(
        int frequency,
        double minimum,
        double maximum,
        double step,
        bool decibels)
    {
        Label = frequency >= 1_000 ? $"{frequency / 1_000d:0.#} kHz" : $"{frequency} Hz";
        Minimum = minimum;
        Maximum = maximum;
        Step = step;
        Decibels = decibels;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string Label { get; }
    public double Minimum { get; }
    public double Maximum { get; }
    public double Step { get; }
    public bool Decibels { get; }
    public string LevelText => Decibels ? $"{Level:+0.#;-0.#;0} dB" : $"{Math.Round(Level)}";

    public double Level
    {
        get => _level;
        set
        {
            var normalized = double.IsFinite(value) ? Math.Clamp(value, Minimum, Maximum) : Minimum;
            if (Math.Abs(_level - normalized) < 0.01)
            {
                return;
            }

            _level = normalized;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Level)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LevelText)));
        }
    }
}

public sealed class RazerHeadsetThxBandViewModel : INotifyPropertyChanged
{
    private double _level;

    public RazerHeadsetThxBandViewModel(int frequency)
    {
        Label = frequency >= 1_000 ? $"{frequency / 1_000d:0.#} kHz" : $"{frequency} Hz";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string Label { get; }
    public string LevelText => $"{Level:+0.0;-0.0;0.0} dB";

    public double Level
    {
        get => _level;
        set
        {
            var normalized = double.IsFinite(value) ? Math.Clamp(value, -12, 12) : 0;
            if (Math.Abs(_level - normalized) < 0.01)
            {
                return;
            }

            _level = normalized;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Level)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LevelText)));
        }
    }
}
