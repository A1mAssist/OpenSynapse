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
        ProtocolFamily = descriptor.ProtocolFamily;
        HardwareCategory = descriptor.Category;
        Name = descriptor.Name;
        Identity = $"VID_{descriptor.VendorId:X4} / PID_{descriptor.ProductId:X4}";
        _accessSource = descriptor.Access == DeviceAccessState.Available
            ? AppStrings.Text("Text_5965520A")
            : AppStrings.Text("Text_6555BB41");
        ReportInfo = descriptor.FeatureReportByteLength > 0
            ? $"{Identity} · HID {descriptor.UsagePage:X4}:{descriptor.Usage:X4} · Feature {descriptor.FeatureReportByteLength} B"
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
        ProtocolDetails = CreateProtocolDetails(descriptor, telemetry);

        if (descriptor.Access != DeviceAccessState.Available ||
            descriptor.Capability != DeviceCapabilityState.PendingValidation)
        {
            _capabilityState = 0;
            StatusBrush = new SolidColorBrush(Color.FromArgb(255, 255, 181, 71));
        }
        else if (_successful == _total && _total > 0)
        {
            _capabilityState = 1;
            StatusBrush = new SolidColorBrush(Color.FromArgb(255, 153, 221, 114));
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
    public DeviceCategory HardwareCategory { get; }
    public string ProtocolFamily { get; }
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
    public IReadOnlyList<DeviceProtocolDetailViewModel> ProtocolDetails { get; }

    private static IReadOnlyList<DeviceProtocolDetailViewModel> CreateProtocolDetails(
        DeviceDescriptor descriptor,
        RazerDeviceTelemetry telemetry)
    {
        var details = new List<DeviceProtocolDetailViewModel>();

        void Add(string resourceKey, object? value, string? formattedValue = null)
        {
            details.Add(new DeviceProtocolDetailViewModel(
                resourceKey,
                value is not null,
                formattedValue ?? FormatProtocolValue(value)));
        }

        switch (descriptor.ProtocolFamily)
        {
            case "blade-710":
                Add("Text_5F0C27DB", telemetry.BladeKeyboardBrightness,
                    telemetry.BladeKeyboardBrightness is byte brightness ? $"{brightness}/255" : null);
                Add("DiagnosticPowerModeControl", telemetry.BladePerformanceMode);
                Add("Text_DEE979FD", telemetry.BladeChargeLimitPercent,
                    telemetry.BladeChargeLimitPercent is int charge ? $"{charge}%" : null);
                Add("DiagnosticCpuBoost", telemetry.BladeCpuBoostMode);
                Add("DiagnosticGpuBoost", telemetry.BladeGpuBoostMode);
                Add("DiagnosticMaxFan", telemetry.BladeMaxFanMode);
                Add("DiagnosticBladeLogo", telemetry.BladeLogoMode);
                Add("DiagnosticPerformanceFan", telemetry.BladeFanMode);
                Add("Text_5FCEEE13", telemetry.BladeCurrentFanCpuRpm,
                    telemetry.BladeCurrentFanCpuRpm is int cpuRpm ? $"{cpuRpm} RPM" : null);
                Add("Text_737DBF87", telemetry.BladeCurrentFanGpuRpm,
                    telemetry.BladeCurrentFanGpuRpm is int gpuRpm ? $"{gpuRpm} RPM" : null);
                Add("Text_BE0A43ED", telemetry.BladeAdvancedFanCpuModeRaw,
                    telemetry.BladeAdvancedFanCpuModeRaw is byte cpuMode ? FormatRawByte(cpuMode) : null);
                Add("Text_37B37761", telemetry.BladeAdvancedFanGpuModeRaw,
                    telemetry.BladeAdvancedFanGpuModeRaw is byte gpuMode ? FormatRawByte(gpuMode) : null);
                Add("Text_AA91A1FF", telemetry.BladeStartupAnimationEnabled);
                Add("Text_24A8C247", telemetry.BladeNativeDisplayMode);
                Add("Text_9D1351CB", telemetry.BladeSkuHardwareConfiguration,
                    telemetry.BladeSkuHardwareConfiguration is { } sku
                        ? $"DDS={sku.Dds}, MiniLED={sku.MiniLedResolution}, Raw=0x{sku.Raw:X2}"
                        : null);
                Add("DiagnosticOneTimeCharge", telemetry.BladeOneTimeFullChargeEnabled);
                if (telemetry.BladeSkuHardwareConfiguration?.MiniLedResolution == true)
                {
                    Add("DiagnosticLocalDimming", telemetry.BladeLocalDimmingEnabled);
                }
                break;

            case "viper-184":
                Add("Text_8B2E15F8", telemetry.ViperBatteryPercent,
                    telemetry.ViperBatteryPercent is int battery ? $"{battery}%" : null);
                Add("DiagnosticMousePollingRate", telemetry.ViperPollingRateHertz,
                    telemetry.ViperPollingRateHertz is int polling ? $"{polling} Hz" : null);
                Add("Text_25083B1F", telemetry.ViperDpiX,
                    telemetry.ViperDpiX is int dpiX
                        ? telemetry.ViperDpiY is int dpiY ? $"X {dpiX} · Y {dpiY}" : $"X {dpiX}"
                        : null);
                Add("DiagnosticMouseIdleTimeout", telemetry.ViperIdleSeconds,
                    telemetry.ViperIdleSeconds is int idle ? $"{idle} s" : null);
                Add("Text_6D7EF7B5", telemetry.ViperDpiStages,
                    telemetry.ViperDpiStages is { } stages
                        ? $"{stages.Stages.Count} stages · active {stages.ActiveStage}"
                        : null);
                Add("Text_B98036FA", telemetry.ViperLowBatteryThresholdRaw,
                    telemetry.ViperLowBatteryThresholdRaw is byte threshold ? $"{threshold}%" : null);
                break;
        }

        return details;
    }

    private static string FormatProtocolValue(object? value) => value switch
    {
        null => AppStrings.Text("Text_C409646C"),
        bool enabled => AppStrings.Text(enabled ? "DiagnosticProtocolEnabled" : "DiagnosticProtocolDisabled"),
        _ => value.ToString() ?? AppStrings.Text("Text_C409646C"),
    };

    private static string FormatRawByte(byte value) => $"0x{value:X2} ({value})";

}

public sealed class DeviceProtocolDetailViewModel(
    string capabilityResourceKey,
    bool isAvailable,
    string detail) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Capability => AppStrings.Text(capabilityResourceKey);
    public string Status => AppStrings.Text(isAvailable ? "DiagnosticProtocolAvailable" : "DiagnosticProtocolUnavailable");
    public string Detail => detail;
    public Brush StatusBrush { get; } = new SolidColorBrush(isAvailable
        ? Color.FromArgb(255, 153, 221, 114)
        : Color.FromArgb(255, 255, 181, 71));

    public void RefreshLocalization() => PropertyChanged?.Invoke(this, new(string.Empty));
}
