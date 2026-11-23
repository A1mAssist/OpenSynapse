using System.ComponentModel;

namespace OpenSynapse.App.ViewModels;

public sealed class NommoEqBandViewModel(int frequency) : IEqualizerBand
{
    private double _level;
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Label => frequency >= 1000 ? $"{frequency / 1000d:0.#} kHz" : $"{frequency} Hz";
    public double Minimum => -9;
    public double Maximum => 9;
    public double Step => 1;
    public bool Decibels => true;
    public string LevelText => $"{Level:+0.#;-0.#;0} dB";
    public double Level
    {
        get => _level;
        set
        {
            var level = double.IsFinite(value) ? Math.Clamp(value, Minimum, Maximum) : 0;
            if (_level == level) return;
            _level = level;
            PropertyChanged?.Invoke(this, new(nameof(Level)));
            PropertyChanged?.Invoke(this, new(nameof(LevelText)));
        }
    }
}
