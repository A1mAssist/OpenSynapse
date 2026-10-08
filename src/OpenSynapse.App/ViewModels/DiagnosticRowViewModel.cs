using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Xaml;
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
    public ObservableCollection<DiagnosticRowViewModel> Issues { get; } = new();
    public ObservableCollection<DeviceProtocolDetailViewModel> ProtocolDetails { get; } = new();
    public Visibility IssueCountVisibility => Issues.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public string IssueCountText => AppStrings.FormatText("DiagnosticIssueCount", Issues.Count);
    public Visibility ProtocolDetailsVisibility => ProtocolDetails.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public void AddIssue(DiagnosticRowViewModel issue)
    {
        Issues.Add(issue);
        PropertyChanged?.Invoke(this, new(nameof(IssueCountVisibility)));
        PropertyChanged?.Invoke(this, new(nameof(IssueCountText)));
    }

    public void AddProtocolDetail(DeviceProtocolDetailViewModel detail)
    {
        ProtocolDetails.Add(detail);
        PropertyChanged?.Invoke(this, new(nameof(ProtocolDetailsVisibility)));
    }

    public void RefreshLocalization()
    {
        PropertyChanged?.Invoke(this, new(string.Empty));
        foreach (var detail in ProtocolDetails) detail.RefreshLocalization();
        foreach (var issue in Issues) issue.RefreshLocalization();
    }
}
