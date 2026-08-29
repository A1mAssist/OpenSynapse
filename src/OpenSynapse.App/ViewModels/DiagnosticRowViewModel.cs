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

    public string Device => device;
    public string Capability => capability;
    public string Status => status;
    public string Detail => detail;
    public Brush StatusBrush { get; } = statusBrush;

    public void RefreshLocalization() => PropertyChanged?.Invoke(this, new(string.Empty));
}
