using Microsoft.UI.Xaml.Media;
using OpenSynapse.Core.Devices;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed partial class DiagnosticsWorkspace
{
    internal void EnsureOpenRazerRows()
    {
        if (Rows.Count > 0 && !(Rows.Count == 1 && Rows[0].Device == "Windows HID")) return;
        Rows.Clear();
        AddOpenRazerRows(
            new Dictionary<string, DiagnosticRowViewModel>(StringComparer.OrdinalIgnoreCase),
            new SolidColorBrush(Color.FromArgb(255, 255, 107, 107)));
        if (Rows.Count == 0) AddFallbackRow();
    }

    private void AddOpenRazerRows(
        Dictionary<string, DiagnosticRowViewModel> groups,
        Brush errorBrush)
    {
        foreach (var row in _openRazer.Devices)
        {
            var group = new DiagnosticRowViewModel(
                row.Name,
                AppStrings.Text("Text_5EAB9F51"),
                row.Status,
                row.Identity,
                row.StatusBrush);
            Rows.Add(group);
            var details = _openRazer.SelectedDevice?.InstanceId == row.Connection.InstanceId
                ? _openRazer.SelectedDevice.ProtocolDetails
                : OpenRazerDeviceViewModel.BuildProtocolDetails(row.Connection);
            foreach (var detail in details) group.AddProtocolDetail(detail);
            if (row.HasError)
            {
                group.AddIssue(new DiagnosticRowViewModel(row.Name, group.Capability,
                    AppStrings.Text("Text_6027BEB0"), row.Error, errorBrush));
            }
            if (_openRazer.SelectedDevice?.InstanceId == row.Connection.InstanceId &&
                _openRazer.SelectedDevice.HasError &&
                !StringComparer.Ordinal.Equals(_openRazer.SelectedDevice.ErrorText, row.Error))
            {
                group.AddIssue(new DiagnosticRowViewModel(row.Name, group.Capability,
                    AppStrings.Text("Text_6027BEB0"), DescribeFailedReads(_openRazer.SelectedDevice), errorBrush));
            }
        }

        foreach (var row in _openRazer.KrakenDevices)
        {
            var group = new DiagnosticRowViewModel(
                row.Name,
                AppStrings.Text("Text_5EAB9F51"),
                row.Status,
                row.Identity,
                row.StatusBrush);
            Rows.Add(group);
            if (!string.IsNullOrWhiteSpace(row.Error))
            {
                group.AddIssue(new DiagnosticRowViewModel(row.Name, group.Capability,
                    AppStrings.Text("Text_6027BEB0"), row.Error, errorBrush));
            }
        }
    }

    private void AddFallbackRow() => Rows.Add(new DiagnosticRowViewModel(
        "Windows HID",
        AppStrings.Text("Text_DA035EAB"),
        AppStrings.Text("Text_76BEF6E0"),
        AppStrings.Text("Text_4DC4534C"),
        new SolidColorBrush(Color.FromArgb(255, 255, 181, 71))));
}
