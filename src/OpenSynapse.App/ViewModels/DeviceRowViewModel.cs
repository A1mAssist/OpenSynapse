using System.ComponentModel;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.Core.Devices;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed class DeviceRowViewModel : INotifyPropertyChanged
{
    private readonly string _accessSource;
    private readonly string _iconAutomationSource;
    private readonly int _capabilityState;
    private readonly int _successful;
    private readonly int _total;

    public DeviceRowViewModel(DeviceDescriptor descriptor, RazerDeviceTelemetry telemetry)
    {
        Name = descriptor.Name;
        Identity = $"VID_{descriptor.VendorId:X4} / PID_{descriptor.ProductId:X4}";
        _accessSource = descriptor.Access == DeviceAccessState.Available
            ? AppStrings.Text("Text_5965520A")
            : AppStrings.Text("Text_6555BB41");
        ReportInfo = descriptor.FeatureReportByteLength > 0
            ? $"HID {descriptor.UsagePage:X4}:{descriptor.Usage:X4} · Feature {descriptor.FeatureReportByteLength} B"
            : "Feature report --";
        (IconGlyph, _iconAutomationSource) = descriptor.Category switch
        {
            DeviceCategory.Laptop => ("\uE7F8", AppStrings.Text("Text_6F38F6BC")),
            DeviceCategory.Mouse => ("\uE962", AppStrings.Text("Text_1EE18BFF")),
            DeviceCategory.Keyboard => ("\uE9D3", AppStrings.Text("Text_046A57B8")),
            DeviceCategory.Headset => ("\uE7F6", AppStrings.Text("Text_781EA0FF")),
            _ => ("\uE772", AppStrings.Text("Text_CAF15352")),
        };

        var summary = telemetry.CapabilitySummaries?.GetValueOrDefault(descriptor.Id)
            ?? DeviceCapabilitySummaryCalculator.Calculate(descriptor, telemetry);
        (_successful, _total) = (summary.Available, summary.Supported);

        if (descriptor.Access != DeviceAccessState.Available ||
            descriptor.Capability != DeviceCapabilityState.PendingValidation)
        {
            _capabilityState = 0;
            StatusBrush = new SolidColorBrush(Color.FromArgb(255, 255, 181, 71));
        }
        else if (_successful == _total && _total > 0)
        {
            _capabilityState = 1;
            StatusBrush = new SolidColorBrush(Color.FromArgb(255, 93, 219, 66));
        }
        else if (_successful > 0)
        {
            _capabilityState = 2;
            StatusBrush = new SolidColorBrush(Color.FromArgb(255, 240, 185, 90));
        }
        else
        {
            _capabilityState = 3;
            StatusBrush = new SolidColorBrush(Color.FromArgb(255, 255, 107, 107));
        }

        IsAvailable = _successful > 0;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void RefreshLocalization() => PropertyChanged?.Invoke(this, new(string.Empty));

    public string Name { get; }
    public string Identity { get; }
    public string Access => _accessSource;
    public string Capability => _capabilityState switch
    {
        0 => AppStrings.Text("Text_40985721"),
        1 => AppStrings.FormatText("ProtocolAvailableCount", _successful, _total),
        2 => AppStrings.FormatText("ProtocolPartiallyAvailableCount", _successful, _total),
        _ => AppStrings.Text("Text_FB278D95"),
    };
    public string ReportInfo { get; }
    public string IconGlyph { get; }
    public string IconAutomationName => _iconAutomationSource;
    public bool IsAvailable { get; }
    public Brush StatusBrush { get; }

}
