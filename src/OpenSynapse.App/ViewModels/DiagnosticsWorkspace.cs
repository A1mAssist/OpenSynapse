using System.Collections.ObjectModel;
using System.Text;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.Core.Devices;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed partial class DiagnosticsWorkspace
{
    private readonly RazerHidWorkspace _openRazer;
    private readonly IReadOnlyList<string> _startupDiagnostics;
    private readonly Func<string> _lastRefreshText;
    private readonly Func<string> _telemetryTimeText;
    private readonly Func<IReadOnlyList<DeviceRowViewModel>> _currentDevices;

    internal DiagnosticsWorkspace(
        RazerHidWorkspace openRazer,
        IReadOnlyList<string> startupDiagnostics,
        Func<string> lastRefreshText,
        Func<string> telemetryTimeText,
        Func<IEnumerable<AccessoryDiagnostic>> readAccessories,
        Func<IReadOnlyList<DeviceRowViewModel>> currentDevices)
    {
        _openRazer = openRazer;
        _startupDiagnostics = startupDiagnostics;
        _lastRefreshText = lastRefreshText;
        _telemetryTimeText = telemetryTimeText;
        _readAccessories = readAccessories;
        _currentDevices = currentDevices;
    }

    public ObservableCollection<DiagnosticRowViewModel> Rows { get; } = new();

    public string CreateReport()
    {
        EnsureRazerHidRows();
        RefreshAccessoryRows();
        var report = new StringBuilder();
        report.AppendLine(AppStrings.Text("DiagnosticsReportTitle"));
        report.AppendLine(AppStrings.FormatText("DiagnosticsReportDeviceRefresh", _lastRefreshText()));
        report.AppendLine(AppStrings.FormatText("DiagnosticsReportDeviceTelemetry", _telemetryTimeText()));
        report.AppendLine();
        foreach (var row in Rows)
        {
            AppendDiagnostic(report, row, 0);
        }

        return report.ToString().TrimEnd();
    }

    /// <summary>
    /// Refreshes only the readout values. Error rows stay in place so a live
    /// tachometer refresh cannot erase a communication failure from diagnostics.
    /// </summary>
    internal void UpdateDeviceTelemetry(
        IReadOnlyList<DeviceDescriptor> descriptors,
        RazerDeviceTelemetry telemetry)
    {
        if (Rows.Count == 0) return;
        var updates = descriptors
            .Select(descriptor => new DeviceRowViewModel(descriptor, telemetry))
            .GroupBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().ProtocolDetails, StringComparer.OrdinalIgnoreCase);
        foreach (var row in Rows.Where(row => row.ProtocolDetails.Count > 0))
        {
            if (updates.TryGetValue(row.Device, out var details))
            {
                row.UpdateProtocolDetails(details);
            }
        }
    }

    internal void Rebuild(
        DeviceSnapshot snapshot,
        RazerDeviceTelemetry telemetry,
        IReadOnlyList<string> errors)
    {
        Rows.Clear();
        _openRazerRows.Clear();
        var groups = new Dictionary<string, DiagnosticRowViewModel>(StringComparer.OrdinalIgnoreCase);
        var errorBrush = new SolidColorBrush(Color.FromArgb(255, 255, 107, 107));
        var bladeName = snapshot.Devices.FirstOrDefault(device => DeviceProtocolFamilies.IsBlade(device.ProtocolFamily))?.Name
            ?? "Razer Blade";
        var viperName = snapshot.Devices.FirstOrDefault(device => device.ProtocolFamily == DeviceProtocolFamilies.Viper)?.Name
            ?? "Razer Viper";
        foreach (var device in snapshot.Devices)
        {
            var row = new DeviceRowViewModel(device, telemetry);
            var group = CreateDeviceRow(row);
            Rows.Add(group);
            groups.TryAdd(row.Name, group);
        }

        AddRazerHidRows(groups, errorBrush);

        foreach (var error in errors)
        {
            var separator = FindDiagnosticSeparator(error);
            var rawCapability = separator > 0 ? error[..separator] : string.Empty;
            var (capability, deviceName) = LocalizeTelemetryError(rawCapability, bladeName, viperName);
            var profilePrefix = AppStrings.FormatText("ProfileApplyError", string.Empty);
            var profileDetail = error.StartsWith(profilePrefix, StringComparison.Ordinal)
                ? error[profilePrefix.Length..].TrimStart()
                : string.Empty;
            if (profileDetail.StartsWith("Blade", StringComparison.OrdinalIgnoreCase) ||
                error.StartsWith(AppStrings.FormatText("LightingError", string.Empty), StringComparison.Ordinal) ||
                error.StartsWith(AppStrings.FormatText("FanControlError", string.Empty), StringComparison.Ordinal))
            {
                deviceName = bladeName;
                capability = rawCapability;
            }
            else if (profileDetail.StartsWith("Viper", StringComparison.OrdinalIgnoreCase) ||
                error.StartsWith(AppStrings.FormatText("ViperMappingProfileError", string.Empty), StringComparison.Ordinal) ||
                error == AppStrings.Text("ViperMappingReadProfileSaveFailed"))
            {
                deviceName = viperName;
                capability = string.IsNullOrEmpty(rawCapability)
                    ? AppStrings.Text("Text_23A7CCBC")
                    : rawCapability;
            }
            var detail = LocalizeDiagnosticDetail(separator > 0 ? error[(separator + 1)..] : error);
            if (!groups.TryGetValue(deviceName, out var group))
            {
                group = new DiagnosticRowViewModel(deviceName, AppStrings.Text("Text_DA035EAB"),
                    AppStrings.Text("Text_6027BEB0"), string.Empty, errorBrush);
                Rows.Add(group);
                groups.Add(deviceName, group);
            }
            group.AddIssue(new DiagnosticRowViewModel(deviceName, capability,
                AppStrings.Text("Text_6027BEB0"), detail, errorBrush));
        }

        DiagnosticRowViewModel? startupGroup = null;
        foreach (var error in _startupDiagnostics)
        {
            var separator = FindDiagnosticSeparator(error);
            var source = separator > 0 ? error[..separator] : AppStrings.Text("Text_23A7CCBC");
            var detail = separator > 0 ? error[(separator + 1)..] : error;
            if (startupGroup is null)
            {
                startupGroup = new DiagnosticRowViewModel(AppStrings.Text("Text_6634ED40"),
                    AppStrings.Text("Text_23A7CCBC"), AppStrings.Text("Text_4B4189B0"),
                    string.Empty, errorBrush);
                Rows.Add(startupGroup);
            }
            startupGroup.AddIssue(new DiagnosticRowViewModel(startupGroup.Device, source,
                AppStrings.Text("Text_4B4189B0"), detail, errorBrush));
        }

        RefreshAccessoryRows();
        if (Rows.Count == 0) AddFallbackRow();
    }

    private static DiagnosticRowViewModel CreateDeviceRow(DeviceRowViewModel row)
    {
        var group = new DiagnosticRowViewModel(
            row.Name,
            AppStrings.Text("Text_5EAB9F51"),
            row.Capability,
            $"{row.Access} · {row.ReportInfo}",
            row.StatusBrush);
        foreach (var detail in row.ProtocolDetails)
        {
            group.AddProtocolDetail(detail);
        }
        return group;
    }

    private static void AppendDiagnostic(
        StringBuilder report,
        DiagnosticRowViewModel row,
        int depth)
    {
        report.Append(' ', depth * 2)
            .Append(row.Device)
            .Append(" | ")
            .Append(row.Capability)
            .Append(" | ")
            .Append(row.Status)
            .Append(" | ")
            .AppendLine(row.Detail);
        foreach (var protocol in row.ProtocolDetails)
        {
            report.Append(' ', (depth + 1) * 2)
                .Append(protocol.Capability)
                .Append(" | ")
                .Append(protocol.Status)
                .Append(" | ")
                .AppendLine(protocol.Detail);
        }
        foreach (var issue in row.Issues)
        {
            AppendDiagnostic(report, issue, depth + 1);
        }
    }

    /// <summary>
    /// Names the capabilities that actually failed. The generic "writes are paused" text
    /// alone leaves a device owner with nothing to act on.
    /// </summary>
    private static string DescribeFailedReads(RazerHidDeviceViewModel device)
    {
        var failures = device.BasicErrors;
        return failures.Count == 0
            ? device.ErrorText
            : AppStrings.FormatText("RazerHidFailedReadsDetail",
                string.Join("; ", failures.Select(read =>
                    $"{AppStrings.Text("RazerHidCapability" + read.Key)}: {read.Value}")));
    }

    private static (string Capability, string Device) LocalizeTelemetryError(
        string rawCapability,
        string bladeName,
        string viperName)
    {
        var mapping = rawCapability switch
        {
            "Keyboard brightness" => ("Text_5F0C27DB", bladeName),
            "Performance and fan state" => ("DiagnosticPerformanceFan", bladeName),
            "Performance mode" => ("DiagnosticPerformanceFan", bladeName),
            "Fan mode" => ("DiagnosticPerformanceFan", bladeName),
            "Fan target" => ("DiagnosticPerformanceFan", bladeName),
            "Current CPU fan speed" => ("Text_5FCEEE13", bladeName),
            "Current GPU fan speed" => ("Text_737DBF87", bladeName),
            "CPU advanced fan mode" => ("Text_BE0A43ED", bladeName),
            "GPU advanced fan mode" => ("Text_37B37761", bladeName),
            "CPU/GPU Boost" => ("Text_1DE2FEA3", bladeName),
            "Charge limit" => ("Text_DEE979FD", bladeName),
            "Power Mode Control" => ("DiagnosticPowerModeControl", bladeName),
            "Startup animation" => ("Text_AA91A1FF", bladeName),
            "Native display mode" => ("Text_24A8C247", bladeName),
            "SKU hardware configuration" => ("Text_9D1351CB", bladeName),
            "Blade Logo" => ("DiagnosticBladeLogo", bladeName),
            "Mouse battery" => ("Text_8B2E15F8", viperName),
            "Mouse polling rate" => ("DiagnosticMousePollingRate", viperName),
            "Mouse DPI" => ("Text_25083B1F", viperName),
            "Mouse idle timeout" => ("DiagnosticMouseIdleTimeout", viperName),
            "Mouse DPI stages" => ("Text_6D7EF7B5", viperName),
            "Mouse low-battery threshold" => ("Text_B98036FA", viperName),
            _ => ("Text_DA035EAB", "Windows HID"),
        };
        return (AppStrings.Text(mapping.Item1), mapping.Item2);
    }

    private static string LocalizeDiagnosticDetail(string detail) =>
        detail.StartsWith("The device timed out", StringComparison.OrdinalIgnoreCase)
            ? AppStrings.Text("DiagnosticDeviceTimedOut")
            : detail;

    private static int FindDiagnosticSeparator(string value)
    {
        var ascii = value.IndexOf(':');
        var fullWidth = value.IndexOf('\uFF1A');
        return ascii < 0 ? fullWidth : fullWidth < 0 ? ascii : Math.Min(ascii, fullWidth);
    }
}
