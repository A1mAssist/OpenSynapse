using System.ComponentModel;
using OpenSynapse.Core.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed class BladePerformanceModeCardViewModel : INotifyPropertyChanged
{
    private bool _isSelected;

    public BladePerformanceModeCardViewModel(
        BladePerformanceMode mode,
        int index,
        string name,
        string description,
        string iconGlyph,
        bool isSelected)
    {
        Mode = mode;
        Index = index;
        Name = name;
        Description = description;
        IconGlyph = iconGlyph;
        _isSelected = isSelected;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public BladePerformanceMode Mode { get; }
    public int Index { get; }
    public string Name { get; }
    public string Description { get; }
    public string IconGlyph { get; }
    public bool IsSelected => _isSelected;

    internal void SetSelected(bool selected)
    {
        if (_isSelected == selected) return;
        _isSelected = selected;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
    }
}
