using Microsoft.UI.Xaml.Media;
using OpenSynapse.Core.Devices;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed partial class DiagnosticsWorkspace
{
    private readonly List<DiagnosticRowViewModel> _openRazerRows = [];

    internal void EnsureRazerHidRows()
    {
        foreach (var row in _openRazerRows) Rows.Remove(row);
        _openRazerRows.Clear();
        if (Rows.Count == 1 && Rows[0].Device == AppStrings.Text("Text_CAF15352")) Rows.Clear();
        if (Rows.Count == 0) AddCurrentDeviceRows();
        AddRazerHidRows(
            new Dictionary<string, DiagnosticRowViewModel>(StringComparer.OrdinalIgnoreCase),
            new SolidColorBrush(Color.FromArgb(255, 255, 107, 107)));
        if (Rows.Count == 0) AddFallbackRow();
    }

    private void AddCurrentDeviceRows()
    {
        foreach (var row in _currentDevices())
        {
            Rows.Add(CreateDeviceRow(row));
        }
    }

    private void AddRazerHidRows(
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
            _openRazerRows.Add(group);
            var details = _openRazer.SelectedDevice?.InstanceId == row.Connection.InstanceId
                ? _openRazer.SelectedDevice.ProtocolDetails
                : RazerHidDeviceViewModel.BuildProtocolDetails(row.Connection);
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
            _openRazerRows.Add(group);
            if (!string.IsNullOrWhiteSpace(row.Error))
            {
                group.AddIssue(new DiagnosticRowViewModel(row.Name, group.Capability,
                    AppStrings.Text("Text_6027BEB0"), row.Error, errorBrush));
            }
        }
    }

    private void AddFallbackRow() => Rows.Add(new DiagnosticRowViewModel(
        AppStrings.Text("Text_CAF15352"),
        AppStrings.Text("Text_DA035EAB"),
        AppStrings.Text("Text_76BEF6E0"),
        AppStrings.Text("Text_4DC4534C"),
        new SolidColorBrush(Color.FromArgb(255, 255, 181, 71))));
}
