namespace OpenSynapse.Core.Devices;

public sealed record RazerDeviceLiveTelemetry(
    BladePerformanceMode? BladePerformanceMode,
    BladeFanMode? BladeFanMode,
    int? BladeFanTargetRpm,
    int? BladeCurrentFanCpuRpm,
    int? BladeCurrentFanGpuRpm,
    BladeGameModeTelemetry? BladeGameMode,
    int? ViperBatteryPercent,
    bool ViperWirelessLinkDown,
    IReadOnlyList<string> Errors,
    DateTimeOffset CapturedAt);
