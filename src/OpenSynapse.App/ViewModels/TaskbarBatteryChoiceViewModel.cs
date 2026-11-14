namespace OpenSynapse.App.ViewModels;

public sealed class TaskbarBatteryChoiceViewModel
{
    public TaskbarBatteryChoiceViewModel(
        string key,
        string name,
        string glyph,
        string detail,
        bool isSelected,
        bool isEnabled)
    {
        Key = key;
        Name = name;
        Glyph = glyph;
        Detail = detail;
        IsSelected = isSelected;
        IsEnabled = isEnabled;
    }

    public string Key { get; }
    public string Name { get; }
    public string Glyph { get; }
    public string Detail { get; }
    public bool IsSelected { get; }
    public bool IsEnabled { get; }
}
