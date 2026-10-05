using System.ComponentModel;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public enum DeviceSelectorItemKind
{
    Blade,
    Viper,
    OpenRazer,
    Kraken,
}

public sealed class DeviceSelectorItemViewModel : INotifyPropertyChanged
{
    public DeviceSelectorItemViewModel(
        DeviceSelectorItemKind kind,
        string name,
        string iconGlyph,
        object? source = null)
    {
        Kind = kind;
        Name = name;
        IconGlyph = iconGlyph;
        Source = source;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public DeviceSelectorItemKind Kind { get; }
    public string Name { get; }
    public string IconGlyph { get; }
    public object? Source { get; }
    public string AutomationName => Name;
    public Brush SelectorBackground { get; private set; } =
        new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));

    public void SetSelectorBackground(Brush background)
    {
        SelectorBackground = background;
        PropertyChanged?.Invoke(this, new(nameof(SelectorBackground)));
    }
}
