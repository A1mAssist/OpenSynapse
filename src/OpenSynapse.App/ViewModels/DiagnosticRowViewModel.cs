using System.ComponentModel;
using Microsoft.UI.Xaml.Media;

namespace OpenSynapse.App.ViewModels;

public sealed class DiagnosticRowViewModel(
    string device,
    string capability,
    string status,
    string detail,
    Brush statusBrush) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Device => AppStrings.Get(device);
    public string Capability => AppStrings.Get(capability);
    public string Status => AppStrings.Get(status);
    public string Detail => AppStrings.Get(detail);
    public Brush StatusBrush { get; } = statusBrush;

    public void RefreshLocalization() => PropertyChanged?.Invoke(this, new(string.Empty));
}
