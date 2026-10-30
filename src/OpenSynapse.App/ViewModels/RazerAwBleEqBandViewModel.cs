using System.ComponentModel;

namespace OpenSynapse.App.ViewModels;

/// <summary>One of the ten custom speaker EQ bands in the bundle's fixed order.</summary>
public sealed class RazerAwBleEqBandViewModel(int frequency, sbyte level) : INotifyPropertyChanged
{
    private double _level = level;

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Frequency { get; } = frequency;
    public string Label => $"{Frequency} Hz";

    public double Level
    {
        get => _level;
        set
        {
            var clamped = Math.Clamp(value, -5d, 5d);
            if (_level == clamped)
            {
                return;
            }

            _level = clamped;
            PropertyChanged?.Invoke(this, new(nameof(Level)));
            PropertyChanged?.Invoke(this, new(nameof(LevelText)));
        }
    }

    public string LevelText => $"{Math.Round(Level):+0;-0;0} dB";
}
