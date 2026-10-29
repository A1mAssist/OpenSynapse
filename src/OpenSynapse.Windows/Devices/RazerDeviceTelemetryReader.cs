using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Displays;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Runtime facade over the per-family readers. It resolves the admitted devices,
/// runs the readers and merges their results; protocol details live in the readers.
/// </summary>
public sealed class RazerDeviceTelemetryReader : IRazerDeviceTelemetryReader
{
    private readonly RazerDeviceResolver _resolver;
    private readonly BladeTelemetryReader _bladeReader;
    private readonly OpenRazerTelemetryReader _openRazerReader;
    private readonly ViperTelemetryReader _viperReader;

    public RazerDeviceTelemetryReader()
        : this(new RazerFeatureTransport(), RazerDeviceRegistry.BuiltIn)
    {
    }

    public RazerDeviceTelemetryReader(IRazerFeatureTransport transport)
        : this(transport, RazerDeviceRegistry.BuiltIn)
    {
    }

    internal RazerDeviceTelemetryReader(
        IRazerFeatureTransport transport,
        RazerDeviceRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(registry);
        _resolver = new RazerDeviceResolver(registry);
        var client = new RazerCapabilityClient(transport);
        _bladeReader = new BladeTelemetryReader(_resolver, client);
        _openRazerReader = new OpenRazerTelemetryReader(_resolver, client);
        _viperReader = new ViperTelemetryReader(_resolver, client);
    }

    public async ValueTask<RazerDeviceTelemetry> ReadAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(devices);
        _bladeReader.ResetValidation();
        _openRazerReader.ResetValidation();
        _viperReader.ResetValidation();
        var blade = _resolver.FindReadyBlade(devices);
        var mouse = _resolver.FindReadyDevice(devices, "viper-184");
        var bladeResult = blade is null
            ? null
            : await _bladeReader.ReadAsync(blade, cancellationToken);
        var mouseResult = mouse is null
            ? null
            : await _openRazerReader.ReadAsync(mouse, cancellationToken);

        var errors = new List<string>();
        if (bladeResult is not null)
        {
            errors.AddRange(bladeResult.Errors);
        }
        if (mouseResult is not null)
        {
            errors.AddRange(mouseResult.Errors);
        }

        var telemetry = new RazerDeviceTelemetry(
            bladeResult?.BladeKeyboardBrightness,
            bladeResult?.BladePerformanceMode,
            bladeResult?.BladeFanMode,
            bladeResult?.BladeFanTargetRpm,
            bladeResult?.BladeChargeLimitPercent,
            mouseResult?.BatteryPercent,
            mouseResult?.PollingRateHertz,
            mouseResult?.DpiX,
            mouseResult?.DpiY,
            mouseResult?.IdleSeconds,
            errors,
            DateTimeOffset.UtcNow,
            BladeCpuBoostMode: bladeResult?.BladeCpuBoostMode,
            BladeGpuBoostMode: bladeResult?.BladeGpuBoostMode,
            BladeMaxFanMode: bladeResult?.BladeMaxFanMode,
            BladeCurrentFanCpuRpm: bladeResult?.BladeCurrentFanCpuRpm,
            BladeCurrentFanGpuRpm: bladeResult?.BladeCurrentFanGpuRpm,
            BladeAdvancedFanCpuModeRaw: bladeResult?.BladeAdvancedFanCpuModeRaw,
            BladeAdvancedFanGpuModeRaw: bladeResult?.BladeAdvancedFanGpuModeRaw,
            ViperDpiStages: mouseResult?.DpiStages,
            ViperLowBatteryThresholdRaw: mouseResult?.LowBatteryThresholdRaw,
            BladeLogoMode: bladeResult?.BladeLogoMode,
            BladeGameMode: bladeResult?.BladeGameMode,
            BladeStartupAnimationEnabled: bladeResult?.BladeStartupAnimationEnabled,
            BladeNativeDisplayMode: bladeResult?.BladeNativeDisplayMode,
            BladeSkuHardwareConfiguration: bladeResult?.BladeSkuHardwareConfiguration,
            BladeOneTimeFullChargeEnabled: bladeResult?.BladeOneTimeFullChargeEnabled,
            BladeLocalDimmingEnabled: bladeResult?.BladeLocalDimmingEnabled,
            BladeCpuPowerBoostEnabled: bladeResult?.BladeCpuPowerBoostEnabled,
            BladeKeyboardEffect: bladeResult?.BladeKeyboardEffect,
            BladeFnKeyMultimediaPrimary: bladeResult?.BladeFnKeyMultimediaPrimary,
            BladeMcuVersion: bladeResult?.BladeMcuVersion,
            BladeAdapterPower: bladeResult?.BladeAdapterPower);

        var capabilitySummaries = new Dictionary<string, DeviceCapabilitySummary>(
            StringComparer.OrdinalIgnoreCase);
        if (blade is not null)
        {
            capabilitySummaries[blade.Descriptor.Id] =
                DeviceCapabilitySummaryCalculator.Calculate(blade.Descriptor, telemetry);
        }
        if (mouse is not null)
        {
            capabilitySummaries[mouse.Descriptor.Id] =
                DeviceCapabilitySummaryCalculator.Calculate(mouse.Descriptor, telemetry);
        }
        return telemetry with { CapabilitySummaries = capabilitySummaries };
    }

    public ValueTask<byte> SetBladeKeyboardBrightnessAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        byte brightness,
        CancellationToken cancellationToken = default,
        bool verifyReadback = true) =>
        _bladeReader.SetBladeKeyboardBrightnessAsync(
            devices, brightness, cancellationToken, verifyReadback);

    public ValueTask<byte> ReadBladeKeyboardBrightnessAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        CancellationToken cancellationToken = default) =>
        _bladeReader.ReadBladeKeyboardBrightnessAsync(devices, cancellationToken);

    public ValueTask<BladeLighting2023Effect> SetBlade2023KeyboardEffectAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeLighting2023Effect effect,
        CancellationToken cancellationToken = default) =>
        _bladeReader.SetBlade2023KeyboardEffectAsync(devices, effect, cancellationToken);

    public ValueTask<BladePerformanceMode> SetBladePerformanceModeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladePerformanceMode mode,
        CancellationToken cancellationToken = default,
        bool coolingPadConnected = false) =>
        _bladeReader.SetBladePerformanceModeAsync(devices, mode, cancellationToken, coolingPadConnected);

    public ValueTask<BladeGameModeTelemetry> SetBladeGameModeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        bool enabled,
        CancellationToken cancellationToken = default) =>
        _bladeReader.SetBladeGameModeAsync(devices, enabled, cancellationToken);

    public ValueTask<bool> SetBladeFnKeyStateAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        bool multiFunctionPrimary,
        CancellationToken cancellationToken = default) =>
        _bladeReader.SetBladeFnKeyStateAsync(devices, multiFunctionPrimary, cancellationToken);

    public ValueTask<BladeFanControlState> SetBladeFanAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeFanMode mode,
        int? cpuTargetRpm,
        int? gpuTargetRpm,
        CancellationToken cancellationToken = default) =>
        _bladeReader.SetBladeFanAsync(devices, mode, cpuTargetRpm, gpuTargetRpm, cancellationToken);

    public ValueTask<BladeFanControlSnapshot> ReadBladeFanControlStateAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        CancellationToken cancellationToken = default) =>
        _bladeReader.ReadBladeFanControlStateAsync(devices, cancellationToken);

    public ValueTask<BladeFanControlSnapshot> SetBladeFanTargetsAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeFanMode mode,
        int cpuTargetRpm,
        int gpuTargetRpm,
        CancellationToken cancellationToken = default) =>
        _bladeReader.SetBladeFanTargetsAsync(
            devices, mode, cpuTargetRpm, gpuTargetRpm, cancellationToken);

    public ValueTask<BladeCpuBoostMode> SetBladeCpuBoostModeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeCpuBoostMode mode,
        CancellationToken cancellationToken = default) =>
        _bladeReader.SetBladeCpuBoostModeAsync(devices, mode, cancellationToken);

    public ValueTask<BladeGpuBoostMode> SetBladeGpuBoostModeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeGpuBoostMode mode,
        CancellationToken cancellationToken = default) =>
        _bladeReader.SetBladeGpuBoostModeAsync(devices, mode, cancellationToken);

    public ValueTask<BladeMaxFanMode> SetBladeMaxFanModeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeMaxFanMode mode,
        CancellationToken cancellationToken = default) =>
        _bladeReader.SetBladeMaxFanModeAsync(devices, mode, cancellationToken);

    public ValueTask<bool> SetBladeOneTimeFullChargeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        bool enabled,
        CancellationToken cancellationToken = default) =>
        _bladeReader.SetBladeOneTimeFullChargeAsync(devices, enabled, cancellationToken);

    public ValueTask<bool> SetBladeLocalDimmingAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        bool enabled,
        CancellationToken cancellationToken = default) =>
        _bladeReader.SetBladeLocalDimmingAsync(devices, enabled, cancellationToken);

    public ValueTask<bool> SetBladeCpuPowerBoostAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        bool enabled,
        CancellationToken cancellationToken = default) =>
        _bladeReader.SetBladeCpuPowerBoostAsync(devices, enabled, cancellationToken);

    public ValueTask<bool> SetBladeStartupAnimationAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        bool enabled,
        CancellationToken cancellationToken = default) =>
        _bladeReader.SetBladeStartupAnimationAsync(devices, enabled, cancellationToken);

    public ValueTask<BladeNativeDisplayMode> SetBladeNativeDisplayModeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeNativeDisplayMode mode,
        CancellationToken cancellationToken = default) =>
        _bladeReader.SetBladeNativeDisplayModeAsync(devices, mode, cancellationToken);

    public ValueTask<BladeDisplayMuxStatus> ReadBladeDisplayMuxStatusAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        CancellationToken cancellationToken = default) =>
        _bladeReader.ReadBladeDisplayMuxStatusAsync(devices, cancellationToken);

    public ValueTask<BladeDisplayMuxSwitchResult> SetBladeDisplayMuxModeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeDisplayMuxMode mode,
        CancellationToken cancellationToken = default) =>
        _bladeReader.SetBladeDisplayMuxModeAsync(devices, mode, cancellationToken);

    public ValueTask<int> SetBladeChargeLimitAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        int percent,
        CancellationToken cancellationToken = default) =>
        _bladeReader.SetBladeChargeLimitAsync(devices, percent, cancellationToken);

    public ValueTask<BladeLogoMode> SetBladeLogoModeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeLogoMode mode,
        CancellationToken cancellationToken = default) =>
        _bladeReader.SetBladeLogoModeAsync(devices, mode, cancellationToken);

    public ValueTask<int> SetViperPollingRateAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        int hertz,
        CancellationToken cancellationToken = default) =>
        _openRazerReader.SetViperPollingRateAsync(devices, hertz, cancellationToken);

    public ValueTask<(int X, int Y)> SetViperDpiAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        int x,
        int y,
        CancellationToken cancellationToken = default) =>
        _openRazerReader.SetViperDpiAsync(devices, x, y, cancellationToken);

    public ValueTask<ViperDpiStagesTelemetry> SetViperDpiStagesAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        ViperDpiStagesTelemetry stages,
        CancellationToken cancellationToken = default) =>
        _openRazerReader.SetViperDpiStagesAsync(devices, stages, cancellationToken);

    public ValueTask<int> SetViperIdleSecondsAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        int seconds,
        CancellationToken cancellationToken = default) =>
        _openRazerReader.SetViperIdleSecondsAsync(devices, seconds, cancellationToken);

    public ValueTask<int> SetViperLowBatteryThresholdAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        int percent,
        CancellationToken cancellationToken = default) =>
        _openRazerReader.SetViperLowBatteryThresholdAsync(devices, percent, cancellationToken);

    public ValueTask<byte> SetViperBatteryChemistryAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        byte chemistry,
        CancellationToken cancellationToken = default) =>
        _viperReader.SetViperBatteryChemistryAsync(devices, chemistry, cancellationToken);

    public ValueTask<IReadOnlyList<ViperButtonAssignment>> ReadViperButtonAssignmentsAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        CancellationToken cancellationToken = default) =>
        _viperReader.ReadViperButtonAssignmentsAsync(devices, cancellationToken);

    public ValueTask<ViperButtonAssignment> SetViperButtonAssignmentAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        ViperButtonAssignment assignment,
        CancellationToken cancellationToken = default) =>
        _viperReader.SetViperButtonAssignmentAsync(devices, assignment, cancellationToken);

    public ValueTask<IReadOnlyList<ViperButtonAssignment>> SetViperButtonAssignmentsAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        IReadOnlyList<ViperButtonAssignment> assignments,
        CancellationToken cancellationToken = default) =>
        _viperReader.SetViperButtonAssignmentsAsync(devices, assignments, cancellationToken);
}
