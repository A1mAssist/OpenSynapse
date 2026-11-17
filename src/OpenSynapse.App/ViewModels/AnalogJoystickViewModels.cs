using System.ComponentModel;

namespace OpenSynapse.App.ViewModels;

public sealed record AnalogJoystickButtonChoice(string Name, byte? ButtonId);

public sealed class AnalogJoystickKeyViewModel : INotifyPropertyChanged
{
    private AnalogJoystickButtonChoice _selectedChoice;

    internal AnalogJoystickKeyViewModel(
        byte keyId,
        string label,
        IReadOnlyList<AnalogJoystickButtonChoice> choices)
    {
        KeyId = keyId;
        Label = label;
        Choices = choices;
        _selectedChoice = choices.FirstOrDefault(choice => choice.ButtonId is null) ?? choices[0];
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public byte KeyId { get; }
    public string Label { get; }
    public IReadOnlyList<AnalogJoystickButtonChoice> Choices { get; }
    internal byte? LoadedButtonId { get; private set; }

    public AnalogJoystickButtonChoice SelectedChoice
    {
        get => _selectedChoice;
        set
        {
            if (value is null || ReferenceEquals(_selectedChoice, value))
            {
                return;
            }

            _selectedChoice = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedChoice)));
        }
    }

    internal void SetLoaded(byte? buttonId)
    {
        LoadedButtonId = buttonId;
        _selectedChoice = Choices.FirstOrDefault(choice => choice.ButtonId == buttonId) ??
            Choices.First(choice => choice.ButtonId is null);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedChoice)));
    }

    internal void Select(byte? buttonId) =>
        SelectedChoice = Choices.FirstOrDefault(choice => choice.ButtonId == buttonId) ??
            Choices.First(choice => choice.ButtonId is null);
}
