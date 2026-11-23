using System.ComponentModel;

namespace OpenSynapse.App.ViewModels;

public interface IEqualizerBand : INotifyPropertyChanged
{
    string Label { get; }
    string LevelText { get; }
    double Level { get; set; }
    double Minimum { get; }
    double Maximum { get; }
    double Step { get; }
    bool Decibels { get; }
}
