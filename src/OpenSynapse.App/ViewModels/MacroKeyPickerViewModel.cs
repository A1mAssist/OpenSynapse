using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using OpenSynapse.Core.Macros;

namespace OpenSynapse.App.ViewModels;

/// <summary>
/// One pickable key in the macro key picker.
/// </summary>
public sealed class MacroKeyOptionViewModel
{
    public MacroKeyOptionViewModel(string label, MacroInput input)
    {
        Label = label;
        Input = input;
    }

    public string Label { get; }
    public MacroInput Input { get; }
}

/// <summary>
/// The on-screen key picker, so a macro step can be added without capturing
/// global input.
/// </summary>
public sealed class MacroKeyPickerViewModel : INotifyPropertyChanged
{
    private readonly ObservableCollection<MacroKeyOptionViewModel> _keyboard = [];
    private readonly ObservableCollection<MacroKeyOptionViewModel> _mouseButtons = [];
    private readonly ObservableCollection<MacroKeyOptionViewModel> _wheels = [];
    private MacroKeyOptionViewModel? _selected;

    public MacroKeyPickerViewModel()
    {
        foreach (var choice in MacroKeyCatalog.Keyboard)
        {
            _keyboard.Add(new MacroKeyOptionViewModel(choice.Label, choice.Input));
        }

        foreach (var choice in MacroKeyCatalog.MouseButtons)
        {
            _mouseButtons.Add(new MacroKeyOptionViewModel(
                AppStrings.Text($"MacroMouseButton{choice.Input.MouseButton}"),
                choice.Input));
        }

        foreach (var choice in MacroKeyCatalog.Wheels)
        {
            _wheels.Add(new MacroKeyOptionViewModel(
                AppStrings.FormatText(
                    choice.Input.WheelDelta > 0 ? "MacroWheelUp" : "MacroWheelDown",
                    Math.Abs(choice.Input.WheelDelta)),
                choice.Input));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<MacroKeyOptionViewModel> Keyboard => _keyboard;
    public ObservableCollection<MacroKeyOptionViewModel> MouseButtons => _mouseButtons;
    public ObservableCollection<MacroKeyOptionViewModel> Wheels => _wheels;

    public MacroKeyOptionViewModel? Selected
    {
        get => _selected;
        set
        {
            if (ReferenceEquals(_selected, value))
            {
                return;
            }

            _selected = value;
            OnPropertyChanged();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
