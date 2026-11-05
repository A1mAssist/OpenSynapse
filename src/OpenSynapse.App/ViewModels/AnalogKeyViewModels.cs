using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OpenSynapse.App.ViewModels;

public sealed class AnalogKeyKeyViewModel : INotifyPropertyChanged
{
    private bool _isSelected;
    private double _actuationMillimeters = 2.0;
    private bool _rapidTriggerEnabled;
    private double _rapidTriggerSensitivityMillimeters = 0.3;

    public AnalogKeyKeyViewModel(byte keyId, string label, double width)
    {
        KeyId = keyId;
        Label = label;
        Width = width;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public byte KeyId { get; }
    public string Label { get; }
    public double Width { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public double ActuationMillimeters
    {
        get => _actuationMillimeters;
        set => SetField(ref _actuationMillimeters, Math.Clamp(value, 0.1, 4.0));
    }

    public bool RapidTriggerEnabled
    {
        get => _rapidTriggerEnabled;
        set => SetField(ref _rapidTriggerEnabled, value);
    }

    public double RapidTriggerSensitivityMillimeters
    {
        get => _rapidTriggerSensitivityMillimeters;
        set => SetField(ref _rapidTriggerSensitivityMillimeters, Math.Clamp(value, 0.1, 4.0));
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class AnalogKeyRowViewModel
{
    public AnalogKeyRowViewModel(int row, IReadOnlyList<AnalogKeyKeyViewModel> keys)
    {
        Row = row;
        Keys = keys;
    }

    public int Row { get; }
    public IReadOnlyList<AnalogKeyKeyViewModel> Keys { get; }
}

public sealed class AnalogSnapTapPairViewModel : INotifyPropertyChanged
{
    private AnalogKeyKeyViewModel? _key1;
    private AnalogKeyKeyViewModel? _key2;
    private byte _mode;

    public AnalogSnapTapPairViewModel(
        IReadOnlyList<AnalogKeyKeyViewModel> choices,
        AnalogKeyKeyViewModel? key1 = null,
        AnalogKeyKeyViewModel? key2 = null,
        byte mode = 0)
    {
        Choices = choices;
        _key1 = key1;
        _key2 = key2;
        _mode = mode;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<AnalogKeyKeyViewModel> Choices { get; }
    public AnalogKeyKeyViewModel? Key1
    {
        get => _key1;
        set => SetField(ref _key1, value);
    }

    public AnalogKeyKeyViewModel? Key2
    {
        get => _key2;
        set => SetField(ref _key2, value);
    }

    public byte Mode
    {
        get => _mode;
        set => SetField(ref _mode, value);
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
