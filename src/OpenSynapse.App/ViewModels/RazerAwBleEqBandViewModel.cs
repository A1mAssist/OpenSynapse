using System.ComponentModel;

namespace OpenSynapse.App.ViewModels;

/// <summary>One of the ten custom speaker EQ bands in the bundle's fixed order.</summary>
public sealed class RazerAwBleEqBandViewModel(int frequency, sbyte level) : IEqualizerBand
{
    private double _level = level;

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Frequency { get; } = frequency;
    public string Label => Frequency >= 1_000 ? $"{Frequency / 1_000d:0.#} kHz" : $"{Frequency} Hz";
    public double Minimum => -5;
    public double Maximum => 5;
    public double Step => 1;
    public bool Decibels => true;

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
