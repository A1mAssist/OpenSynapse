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
            ? "HID 控制通道可打开"
            : "Synapse 占用或访问被拒绝";
        ReportInfo = descriptor.FeatureReportByteLength > 0
            ? $"HID {descriptor.UsagePage:X4}:{descriptor.Usage:X4} · Feature {descriptor.FeatureReportByteLength} B"
            : "Feature report --";
        (IconGlyph, _iconAutomationSource) = descriptor.Category switch
        {
            DeviceCategory.Laptop => ("\uE7F8", "笔记本设备"),
            DeviceCategory.Mouse => ("\uE962", "鼠标设备"),
            DeviceCategory.Keyboard => ("\uE9D3", "键盘设备"),
            DeviceCategory.Headset => ("\uE7F6", "耳机设备"),
            _ => ("\uE772", "设备"),
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
    public string Access => AppStrings.Get(_accessSource);
    public string Capability => _capabilityState switch
    {
        0 => AppStrings.Get("控制通道不可用"),
        1 => AppStrings.FormatText("ProtocolAvailableCount", _successful, _total),
        2 => AppStrings.FormatText("ProtocolPartiallyAvailableCount", _successful, _total),
        _ => AppStrings.Get("协议查询失败"),
    };
    public string ReportInfo { get; }
    public string IconGlyph { get; }
    public string IconAutomationName => AppStrings.Get(_iconAutomationSource);
    public bool IsAvailable { get; }
    public Brush StatusBrush { get; }

}
