using Microsoft.UI.Xaml.Media;
using OpenSynapse.Core.Devices;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed partial class DiagnosticsWorkspace
{
    private readonly List<DiagnosticRowViewModel> _razerHidRows = [];

    internal void EnsureRazerHidRows()
    {
        foreach (var row in _razerHidRows) Rows.Remove(row);
        _razerHidRows.Clear();
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
        foreach (var row in _razerHid.Devices)
        {
            var selectedDevice = _razerHid.SelectedDevice?.InstanceId == row.Connection.InstanceId
                ? _razerHid.SelectedDevice
                : null;
            var capability = selectedDevice is { RequiresRescan: false, ProtocolTotalCount: > 0 } capabilityDetail
                ? AppStrings.FormatText(
                    capabilityDetail.ProtocolAvailableCount == capabilityDetail.ProtocolTotalCount
                        ? "ProtocolAvailableCount"
                        : "ProtocolPartiallyAvailableCount",
                    capabilityDetail.ProtocolAvailableCount,
                    capabilityDetail.ProtocolTotalCount)
                : AppStrings.Text("Text_5EAB9F51");
            var status = selectedDevice is { RequiresRescan: true } rescanDetail
                ? rescanDetail.StatusText
                : row.Connection.IsReady
                    ? AppStrings.Text("BlackSharkStatusConnected")
                    : row.Status;
            var group = new DiagnosticRowViewModel(
                row.Name,
                capability,
                status,
                row.Identity,
                row.StatusBrush);
            Rows.Add(group);
            _razerHidRows.Add(group);
            var details = selectedDevice is not null
                ? selectedDevice.ProtocolDetails
                : RazerHidDeviceViewModel.BuildProtocolDetails(row.Connection);
            foreach (var protocolDetail in details) group.AddProtocolDetail(protocolDetail);
            if (row.HasError)
            {
                group.AddIssue(new DiagnosticRowViewModel(row.Name, group.Capability,
                    AppStrings.Text("Text_6027BEB0"), row.Error, errorBrush));
            }
            if (_razerHid.SelectedDevice?.InstanceId == row.Connection.InstanceId &&
                _razerHid.SelectedDevice.HasError &&
                !StringComparer.Ordinal.Equals(_razerHid.SelectedDevice.ErrorText, row.Error))
            {
                group.AddIssue(new DiagnosticRowViewModel(row.Name, group.Capability,
                    AppStrings.Text("Text_6027BEB0"), DescribeFailedReads(_razerHid.SelectedDevice), errorBrush));
            }
        }

        foreach (var row in _razerHid.KrakenDevices)
        {
            var group = new DiagnosticRowViewModel(
                row.Name,
                row.CapabilityText,
                row.Connection.IsReady
                    ? AppStrings.Text("BlackSharkStatusConnected")
                    : row.Status,
                row.Identity,
                row.StatusBrush);
            Rows.Add(group);
            _razerHidRows.Add(group);
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
