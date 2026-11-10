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
    private readonly bool _telemetryPending;

    public DeviceRowViewModel(DeviceDescriptor descriptor, RazerDeviceTelemetry telemetry, bool telemetryPending = false)
    {
        _telemetryPending = telemetryPending;
        ProtocolFamily = descriptor.ProtocolFamily;
        Id = descriptor.Id;
        HardwareCategory = descriptor.Category;
        IsPresent = descriptor.Access == DeviceAccessState.Available;
        Name = descriptor.Name;
        ProductId = descriptor.ProductId;
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
            DeviceCategory.Keyboard => ("\uE765", AppStrings.Text("Text_046A57B8")),
            DeviceCategory.Headset => ("\uE7F6", AppStrings.Text("Text_781EA0FF")),
            _ => ("\uE772", AppStrings.Text("Text_CAF15352")),
        };

        var declaredCapabilities = telemetry.DeclaredCapabilities?.GetValueOrDefault(descriptor.Id);
        var summary = telemetry.CapabilitySummaries?.GetValueOrDefault(descriptor.Id)
            ?? DeviceCapabilitySummaryCalculator.Calculate(descriptor, telemetry, declaredCapabilities);
        (_successful, _total) = (summary.Available, summary.Supported);
        ProtocolDetails = CreateProtocolDetails(descriptor, telemetry, declaredCapabilities);

        if (_telemetryPending)
        {
            _capabilityState = 4;
            StatusBrush = new SolidColorBrush(Color.FromArgb(255, 68, 214, 44));
        }
        else if (descriptor.Access != DeviceAccessState.Available ||
            descriptor.Capability != DeviceCapabilityState.PendingValidation)
        {
            _capabilityState = 0;
            StatusBrush = new SolidColorBrush(Color.FromArgb(255, 255, 181, 71));
        }
        else if (_successful == _total && _total > 0)
        {
            _capabilityState = 1;
            StatusBrush = new SolidColorBrush(Color.FromArgb(255, 68, 214, 44));
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
        // The Viper reports its charge through telemetry; other families do not.
        BatteryPercent = descriptor.ProtocolFamily == "viper-184" ? telemetry.ViperBatteryPercent : null;
    }
    internal string Id { get; }
    internal DeviceCapabilitySummary CapabilitySummary => new(_successful, _total);
    public event PropertyChangedEventHandler? PropertyChanged;

    public void RefreshLocalization() => PropertyChanged?.Invoke(this, new(string.Empty));

    public string Name { get; }
    public DeviceCategory HardwareCategory { get; }
    public string ProtocolFamily { get; }
    public ushort ProductId { get; }
    public string Identity { get; }
    public string Access => _accessSource;
    public string Capability => _capabilityState switch
    {
        0 => AppStrings.Text("Text_40985721"),
        1 => AppStrings.FormatText("ProtocolAvailableCount", _successful, _total),
        2 => AppStrings.FormatText("ProtocolPartiallyAvailableCount", _successful, _total),
        4 => AppStrings.Text("Text_95979F99"),
        _ => AppStrings.Text("Text_FB278D95"),
    };
    public string ReportInfo { get; }
    public string IconGlyph { get; }
    public string IconAutomationName => _iconAutomationSource;
    public bool IsAvailable { get; }
    public bool IsPresent { get; }
    public int SupportedCapabilityCount => _total;
    public int? BatteryPercent { get; }
    public Brush StatusBrush { get; }
    public IReadOnlyList<DeviceProtocolDetailViewModel> ProtocolDetails { get; }

    private static IReadOnlyList<DeviceProtocolDetailViewModel> CreateProtocolDetails(
        DeviceDescriptor descriptor,
        RazerDeviceTelemetry telemetry,
        IReadOnlySet<string>? declaredCapabilities)
    {
        var details = new List<DeviceProtocolDetailViewModel>();

        void Add(
            string resourceKey,
            object? value,
            string? formattedValue = null,
            string? capability = null)
        {
            if (capability is not null && declaredCapabilities is not null &&
                !declaredCapabilities.Contains(capability))
            {
                return;
            }
            details.Add(new DeviceProtocolDetailViewModel(
                resourceKey,
                value is not null,
                formattedValue ?? FormatProtocolValue(value)));
        }

        void AddBladeDetails()
        {
            Add("Text_5F0C27DB", telemetry.BladeKeyboardBrightness,
                telemetry.BladeKeyboardBrightness is byte brightness ? $"{brightness}/255" : null,
                CapabilityIds.KeyboardBrightnessGet);
            Add("DiagnosticKeyboardEffect", telemetry.BladeKeyboardEffect,
                capability: CapabilityIds.KeyboardEffectGet);
            Add("DiagnosticPowerModeControl", telemetry.BladePerformanceMode,
                capability: CapabilityIds.ThermalStateGet);
            Add("Text_DEE979FD", telemetry.BladeChargeLimitPercent,
                telemetry.BladeChargeLimitPercent is int charge ? $"{charge}%" : null,
                CapabilityIds.ChargeLimitGet);
            Add("DiagnosticCpuBoost", telemetry.BladeCpuBoostMode,
                capability: CapabilityIds.BoostGet);
            Add("DiagnosticGpuBoost", telemetry.BladeGpuBoostMode,
                capability: CapabilityIds.BoostGet);
            if (BladeModelCapabilities.SupportsMaxFan(descriptor.ProductId))
            {
                Add("DiagnosticMaxFan", telemetry.BladeMaxFanMode,
                    capability: CapabilityIds.PowerModeGet);
            }
            Add("DiagnosticBladeLogo",
                telemetry.BladeLogoMode ?? (object?)telemetry.BladeLogoUnsupportedModeRaw,
                telemetry.BladeLogoUnsupportedModeRaw is byte rawLogoMode
                    ? AppStrings.FormatText("BladeLogoUnsupportedMode", $"0x{rawLogoMode:X2}")
                    : null,
                CapabilityIds.LogoModeGet);
            Add("DiagnosticPerformanceFan", telemetry.BladeFanMode,
                capability: CapabilityIds.ThermalStateGet);
            Add("BladeFanTargetRpmLabel", telemetry.BladeFanTargetRpm,
                telemetry.BladeFanTargetRpm is int targetRpm ? $"{targetRpm} RPM" : null,
                CapabilityIds.FanTargetGet);
            Add("Text_5FCEEE13", telemetry.BladeCurrentFanCpuRpm,
                telemetry.BladeCurrentFanCpuRpm is int cpuRpm ? $"{cpuRpm} RPM" : null,
                CapabilityIds.CurrentFanRpmGet);
            Add("Text_737DBF87", telemetry.BladeCurrentFanGpuRpm,
                telemetry.BladeCurrentFanGpuRpm is int gpuRpm ? $"{gpuRpm} RPM" : null,
                CapabilityIds.CurrentFanRpmGet);
            Add("Text_BE0A43ED", telemetry.BladeAdvancedFanCpuModeRaw,
                telemetry.BladeAdvancedFanCpuModeRaw is byte cpuMode ? FormatRawByte(cpuMode) : null,
                CapabilityIds.AdvancedFanModeGet);
            Add("Text_37B37761", telemetry.BladeAdvancedFanGpuModeRaw,
                telemetry.BladeAdvancedFanGpuModeRaw is byte gpuMode ? FormatRawByte(gpuMode) : null,
                CapabilityIds.AdvancedFanModeGet);
            Add("Text_AA91A1FF", telemetry.BladeStartupAnimationEnabled,
                capability: CapabilityIds.StartupAnimationGet);
            Add("DiagnosticOneTimeCharge", telemetry.BladeOneTimeFullChargeEnabled,
                capability: CapabilityIds.PowerModeGet);
            Add("Text_9D1351CB", telemetry.BladeSkuHardwareConfiguration,
                telemetry.BladeSkuHardwareConfiguration is { } skuConfig ? $"0x{skuConfig.Raw:X2}" : null,
                CapabilityIds.SkuHardwareConfigurationGet);
            Add("DiagnosticAdapterPower", telemetry.BladeAdapterPower,
                telemetry.BladeAdapterPower is { } adapter
                    ? $"{adapter.ActualWatts?.ToString() ?? "?"} W / {adapter.RecommendedWatts?.ToString() ?? "?"} W"
                    : null,
                CapabilityIds.AdapterPowerGet);
            Add("BladeFnKeyLabel", telemetry.BladeFnKeyMultimediaPrimary,
                telemetry.BladeFnKeyMultimediaPrimary is bool multimediaPrimary
                    ? AppStrings.Text(multimediaPrimary ? "BladeFnKeyMultimediaPrimaryText" : "BladeFnKeyFunctionPrimaryText")
                    : null,
                declaredCapabilities?.Contains(CapabilityIds.FnKeyGet) == true
                    ? CapabilityIds.FnKeyGet
                    : CapabilityIds.FnKeySwapGet);
            Add("FirmwareMcuLabel", telemetry.BladeMcuVersion,
                capability: CapabilityIds.McuVersionGet);
            Add("BladeAutoSleepTitle.Text", telemetry.BladeAutoSleepEnabled,
                capability: CapabilityIds.AutoSleepGet);
            Add("BladeTimeToDimTitle.Text", telemetry.BladeTimeToDim,
                telemetry.BladeTimeToDim is { } dim ? $"{dim.Seconds}s · {dim.Percentage}%" : null,
                CapabilityIds.TimeToDimGet);
            Add("BladeIndicatorLedTitle.Text", telemetry.BladeIndicatorLedEnabled,
                capability: CapabilityIds.IndicatorLedGet);
            Add("BladeGpuCardDetectionTitle.Text", telemetry.BladeGpuCardDetection,
                telemetry.BladeGpuCardDetection is { } gpuCard
                    ? $"{(gpuCard.Enabled ? AppStrings.Text("DiagnosticProtocolEnabled") : AppStrings.Text("DiagnosticProtocolDisabled"))} · 0x{gpuCard.LedClassId:X2}"
                    : null,
                CapabilityIds.GpuCardDetectionGet);
            Add("ProfileSummary_Item_GamingMode",
                telemetry.BladeGameMode is { } gameMode ? gameMode.GameMode != 0 : null,
                capability: CapabilityIds.GamingModeGet);
            if (BladeModelCapabilities.SupportsCpuPowerBoost(descriptor.ProductId))
            {
                Add("ProfileSummary_Item_CpuPowerBoost", telemetry.BladeCpuPowerBoostEnabled,
                    capability: CapabilityIds.PowerModeGet);
            }
            if (telemetry.BladeSkuHardwareConfiguration?.SupportsNativeDisplayMode == true)
            {
                Add("Text_24A8C247", telemetry.BladeNativeDisplayMode,
                    capability: CapabilityIds.NativeDisplayModeGet);
                Add("DiagnosticLocalDimming", telemetry.BladeLocalDimmingEnabled,
                    capability: CapabilityIds.PowerModeGet);
            }
        }

        switch (descriptor.ProtocolFamily)
        {
            case "blade-597":
            case "blade-671":
            case "blade-710":
            case "blade-710-chroma":
                AddBladeDetails();
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

public sealed class DeviceProtocolDetailViewModel : INotifyPropertyChanged
{
    private readonly string _capabilityResourceKey;
    private string _detail;
    private readonly bool _capabilityIsLiteral;
    private bool _failed;

    public DeviceProtocolDetailViewModel(
        string capabilityResourceKey,
        bool isAvailable,
        string detail,
        bool capabilityIsLiteral = false,
        bool failed = false)
    {
        _capabilityResourceKey = capabilityResourceKey;
        _detail = detail;
        _capabilityIsLiteral = capabilityIsLiteral;
        _failed = failed;
        IsAvailable = isAvailable;
        StatusBrush = new SolidColorBrush(failed
            ? Color.FromArgb(255, 255, 107, 107)
            : isAvailable
            ? Color.FromArgb(255, 68, 214, 44)
            : Color.FromArgb(255, 255, 181, 71));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsAvailable { get; private set; }
    public bool IsPresent { get; }
    internal string CapabilityKey => _capabilityResourceKey;
    public string Capability => _capabilityIsLiteral ? _capabilityResourceKey : AppStrings.Text(_capabilityResourceKey);
    public string Status => AppStrings.Text(_failed
        ? "DiagnosticProtocolFailedStatus"
        : IsAvailable ? "DiagnosticProtocolAvailable" : "DiagnosticProtocolUnavailable");
    public string Detail => _detail;
    public Brush StatusBrush { get; private set; }

    internal void UpdateFrom(DeviceProtocolDetailViewModel other)
    {
        if (IsAvailable == other.IsAvailable && _detail == other.Detail && _failed == other._failed) return;
        IsAvailable = other.IsAvailable;
        _detail = other.Detail;
        _failed = other._failed;
        StatusBrush = new SolidColorBrush(_failed
            ? Color.FromArgb(255, 255, 107, 107)
            : IsAvailable
                ? Color.FromArgb(255, 68, 214, 44)
                : Color.FromArgb(255, 255, 181, 71));
        PropertyChanged?.Invoke(this, new(string.Empty));
    }

    public void RefreshLocalization() => PropertyChanged?.Invoke(this, new(string.Empty));
}
