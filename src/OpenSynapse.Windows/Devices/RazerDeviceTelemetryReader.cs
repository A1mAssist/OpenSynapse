using System.ComponentModel;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerDeviceTelemetryReader : IRazerDeviceTelemetryReader
{
    private readonly IRazerFeatureTransport _transport;
    private readonly RazerDeviceRegistry _registry;
    private string? _validatedBladeBrightnessPath;
    private string? _validatedBladePerformancePath;
    private string? _validatedBladeBoostPath;
    private string? _validatedBladeChargeLimitPath;
    private string? _validatedBladeMaxFanPath;
    private string? _validatedBladeLocalDimmingPath;
    private string? _validatedBladeLogoPath;
    private string? _validatedBladeStartupAnimationPath;
    private string? _validatedBladeNativeDisplayModePath;
    private string? _validatedViperPollingPath;
    private string? _validatedViperDpiPath;
    private string? _validatedViperDpiStagesPath;
    private string? _validatedViperIdlePath;
    private readonly SemaphoreSlim _bladePowerModeTransactionGate = new(1, 1);

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
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public async ValueTask<RazerDeviceTelemetry> ReadAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        CancellationToken cancellationToken = default)
    {
        _validatedBladeBrightnessPath = null;
        _validatedBladePerformancePath = null;
        _validatedBladeBoostPath = null;
        _validatedBladeChargeLimitPath = null;
        _validatedBladeMaxFanPath = null;
        _validatedBladeLocalDimmingPath = null;
        _validatedBladeLogoPath = null;
        _validatedBladeStartupAnimationPath = null;
        _validatedBladeNativeDisplayModePath = null;
        _validatedViperPollingPath = null;
        _validatedViperDpiPath = null;
        _validatedViperDpiStagesPath = null;
        _validatedViperIdlePath = null;
        _validatedViperButtonMappingsPath = null;
        byte? bladeBrightness = null;
        BladePerformanceMode? bladePerformanceMode = null;
        BladeFanMode? bladeFanMode = null;
        int? bladeFanTargetRpm = null;
        int? bladeChargeLimitPercent = null;
        BladeCpuBoostMode? bladeCpuBoostMode = null;
        BladeGpuBoostMode? bladeGpuBoostMode = null;
        BladeMaxFanMode? bladeMaxFanMode = null;
        int? bladeCurrentFanCpuRpm = null;
        int? bladeCurrentFanGpuRpm = null;
        byte? bladeAdvancedFanCpuModeRaw = null;
        byte? bladeAdvancedFanGpuModeRaw = null;
        BladeLogoMode? bladeLogoMode = null;
        bool? bladeStartupAnimationEnabled = null;
        BladeNativeDisplayMode? bladeNativeDisplayMode = null;
        BladeSkuHardwareConfiguration? bladeSkuHardwareConfiguration = null;
        bool? bladeOneTimeFullChargeEnabled = null;
        bool? bladeLocalDimmingEnabled = null;
        int? batteryPercent = null;
        int? pollingRate = null;
        int? dpiX = null;
        int? dpiY = null;
        int? idleSeconds = null;
        ViperDpiStagesTelemetry? dpiStages = null;
        byte? lowBatteryThresholdRaw = null;
        var errors = new List<string>();

        var blade = FindReadyDevice(devices, "blade-710");
        if (blade is not null)
        {
            var bladeThermalReadSucceeded = false;
            try
            {
                var response = await QueryCapabilityAsync(
                    blade, "keyboard-brightness.get", cancellationToken);
                EnsureDataSize(response, 2, "keyboard brightness");
                bladeBrightness = response[RazerFeatureReport.ArgumentsOffset + 1];
                _validatedBladeBrightnessPath = blade.Descriptor.Id;
            }
        catch (Exception exception) when (IsExpectedHardwareException(exception))
            {
                errors.Add($"Keyboard brightness: {exception.Message}");
            }

            try
            {
                var thermalState = await ReadBladeThermalStateAsync(blade, cancellationToken);
                bladePerformanceMode = thermalState.PerformanceMode;
                bladeFanMode = thermalState.FanMode;
                if (thermalState.FanMode == BladeFanMode.Manual)
                {
                    bladeFanTargetRpm = await ReadBladeFanTargetRpmAsync(blade, cancellationToken);
                }
                _validatedBladePerformancePath = blade.Descriptor.Id;
                bladeThermalReadSucceeded = true;
            }
            catch (Exception exception) when (IsExpectedHardwareException(exception))
            {
                errors.Add($"Performance and fan state: {exception.Message}");
            }

            try
            {
                bladeCurrentFanCpuRpm = await ReadBladeCurrentFanRpmAsync(
                    blade, BladeThermalProtocol.CpuFanId, cancellationToken);
            }
            catch (Exception exception) when (IsExpectedHardwareException(exception))
            {
                errors.Add($"Current CPU fan speed: {exception.Message}");
            }

            try
            {
                bladeCurrentFanGpuRpm = await ReadBladeCurrentFanRpmAsync(
                    blade, BladeThermalProtocol.GpuFanId, cancellationToken);
            }
            catch (Exception exception) when (IsExpectedHardwareException(exception))
            {
                errors.Add($"Current GPU fan speed: {exception.Message}");
            }

            try
            {
                bladeAdvancedFanCpuModeRaw = await ReadBladeAdvancedFanModeAsync(
                    blade, BladeThermalProtocol.CpuFanId, cancellationToken);
            }
            catch (Exception exception) when (IsExpectedHardwareException(exception))
            {
                errors.Add($"CPU advanced fan mode: {exception.Message}");
            }

            try
            {
                bladeAdvancedFanGpuModeRaw = await ReadBladeAdvancedFanModeAsync(
                    blade, BladeThermalProtocol.GpuFanId, cancellationToken);
            }
            catch (Exception exception) when (IsExpectedHardwareException(exception))
            {
                errors.Add($"GPU advanced fan mode: {exception.Message}");
            }

            if (bladeThermalReadSucceeded)
            {
                try
                {
                    var boostState = await ReadBladeBoostStateAsync(blade, cancellationToken);
                    bladeCpuBoostMode = boostState.Cpu;
                    bladeGpuBoostMode = boostState.Gpu;
                    _validatedBladeBoostPath = blade.Descriptor.Id;
                }
                catch (Exception exception) when (IsExpectedHardwareException(exception))
                {
                    errors.Add($"CPU/GPU Boost: {exception.Message}");
                }
            }

            try
            {
                bladeChargeLimitPercent = await ReadBladeChargeLimitAsync(blade, cancellationToken);
                _validatedBladeChargeLimitPath = blade.Descriptor.Id;
            }
            catch (Exception exception) when (IsExpectedHardwareException(exception))
            {
                errors.Add($"Charge limit: {exception.Message}");
            }

            try
            {
                var powerModeMask = await ReadBladePowerModeMaskAsync(blade, cancellationToken);
                bladeMaxFanMode = ToMaxFanMode(powerModeMask);
                bladeOneTimeFullChargeEnabled =
                    (powerModeMask & BladeMaxFanProtocol.OneTimeFullChargeBit) != 0;
                bladeLocalDimmingEnabled =
                    (powerModeMask & BladeMaxFanProtocol.LocalDimmingBit) != 0;
                _validatedBladeMaxFanPath = blade.Descriptor.Id;
            }
            catch (Exception exception) when (IsExpectedHardwareException(exception))
            {
                errors.Add($"Power Mode Control: {exception.Message}");
            }

            try
            {
                var response = await QueryCapabilityAsync(
                    blade, "startup-animation.get", cancellationToken);
                bladeStartupAnimationEnabled = BladeSynapsePolicyProtocol.ParseStartupAnimation(
                    response, CreateCapabilityRequest(blade, "startup-animation.get")).Enabled;
                _validatedBladeStartupAnimationPath = blade.Descriptor.Id;
            }
            catch (Exception exception) when (IsExpectedHardwareException(exception))
            {
                errors.Add($"Startup animation: {exception.Message}");
            }

            try
            {
                var response = await QueryCapabilityAsync(
                    blade, "native-display-mode.get", cancellationToken);
                bladeNativeDisplayMode = BladeProduct710Protocol.ParseNativeDisplayMode(
                    response, CreateCapabilityRequest(blade, "native-display-mode.get"));
                _validatedBladeNativeDisplayModePath = blade.Descriptor.Id;
            }
            catch (Exception exception) when (IsExpectedHardwareException(exception))
            {
                errors.Add($"Native display mode: {exception.Message}");
            }

            try
            {
                var response = await QueryCapabilityAsync(
                    blade, "sku-hardware-configuration.get", cancellationToken);
                bladeSkuHardwareConfiguration = BladeProduct710Protocol.ParseSkuHardwareConfiguration(
                    response, CreateCapabilityRequest(blade, "sku-hardware-configuration.get"));
                if (bladeSkuHardwareConfiguration.Value.MiniLedResolution)
                {
                    _validatedBladeLocalDimmingPath = _validatedBladeMaxFanPath;
                }
                else
                {
                    bladeLocalDimmingEnabled = null;
                }
            }
            catch (Exception exception) when (IsExpectedHardwareException(exception))
            {
                errors.Add($"SKU hardware configuration: {exception.Message}");
            }

            try
            {
                bladeLogoMode = (await ReadBladeLogoStateAsync(blade, cancellationToken)).CombinedMode;
                _validatedBladeLogoPath = blade.Descriptor.Id;
            }
            catch (Exception exception) when (IsExpectedHardwareException(exception))
            {
                errors.Add($"Blade Logo: {exception.Message}");
            }

        }

        var viper = FindReadyDevice(devices, "viper-184");
        if (viper is not null)
        {
            try
            {
                var response = await QueryCapabilityAsync(viper, "battery.get", cancellationToken);
                batteryPercent = ViperProduct184Protocol.ParseBatteryPercent(
                    response, CreateCapabilityRequest(viper, "battery.get"));
            }
            catch (Exception exception) when (IsExpectedHardwareException(exception))
            {
                errors.Add($"Mouse battery: {exception.Message}");
            }

            try
            {
                var response = await QueryCapabilityAsync(viper, "polling-rate.get", cancellationToken);
                pollingRate = ViperProduct184Protocol.ParsePollingRateHertz(
                    response, CreateCapabilityRequest(viper, "polling-rate.get"));
                _validatedViperPollingPath = viper.Descriptor.Id;
            }
            catch (Exception exception) when (IsExpectedHardwareException(exception))
            {
                errors.Add($"Mouse polling rate: {exception.Message}");
            }

            try
            {
                var response = await QueryCapabilityAsync(viper, "current-dpi.get", cancellationToken);
                (dpiX, dpiY) = ViperProduct184Protocol.ParseDpi(
                    response, CreateCapabilityRequest(viper, "current-dpi.get"));
                _validatedViperDpiPath = viper.Descriptor.Id;
            }
            catch (Exception exception) when (IsExpectedHardwareException(exception))
            {
                errors.Add($"Mouse DPI: {exception.Message}");
            }

            try
            {
                var response = await QueryCapabilityAsync(viper, "idle-timeout.get", cancellationToken);
                idleSeconds = ViperProduct184Protocol.ParseIdleSeconds(
                    response, CreateCapabilityRequest(viper, "idle-timeout.get"));
                _validatedViperIdlePath = viper.Descriptor.Id;
            }
            catch (Exception exception) when (IsExpectedHardwareException(exception))
            {
                errors.Add($"Mouse idle timeout: {exception.Message}");
            }

            try
            {
                var response = await QueryCapabilityAsync(viper, "dpi-stages.get", cancellationToken);
                var parsed = ViperDpiStagesProtocol.Parse(
                    response, CreateCapabilityRequest(viper, "dpi-stages.get"));
                dpiStages = new ViperDpiStagesTelemetry(
                    parsed.ActiveStage,
                    parsed.Stages.Select(stage => new ViperDpiStageTelemetry(stage.Number, stage.X, stage.Y)).ToArray());
                _validatedViperDpiStagesPath = viper.Descriptor.Id;
            }
            catch (Exception exception) when (IsExpectedHardwareException(exception))
            {
                errors.Add($"Mouse DPI stages: {exception.Message}");
            }

            try
            {
                var response = await QueryCapabilityAsync(
                    viper, "low-battery-threshold.get", cancellationToken);
                lowBatteryThresholdRaw = ViperLowBatteryThresholdProtocol.ParseRaw(
                    response, CreateCapabilityRequest(viper, "low-battery-threshold.get"));
            }
            catch (Exception exception) when (IsExpectedHardwareException(exception))
            {
                errors.Add($"Mouse low-battery threshold: {exception.Message}");
            }

        }

        var telemetry = new RazerDeviceTelemetry(
            bladeBrightness,
            bladePerformanceMode,
            bladeFanMode,
            bladeFanTargetRpm,
            bladeChargeLimitPercent,
            batteryPercent,
            pollingRate,
            dpiX,
            dpiY,
            idleSeconds,
            errors,
            DateTimeOffset.UtcNow,
            bladeCpuBoostMode,
            bladeGpuBoostMode,
            bladeMaxFanMode,
            bladeCurrentFanCpuRpm,
            bladeCurrentFanGpuRpm,
            bladeAdvancedFanCpuModeRaw,
            bladeAdvancedFanGpuModeRaw,
            dpiStages,
            lowBatteryThresholdRaw,
            bladeLogoMode,
            null,
            bladeStartupAnimationEnabled,
            bladeNativeDisplayMode,
            bladeSkuHardwareConfiguration,
            bladeOneTimeFullChargeEnabled,
            bladeLocalDimmingEnabled);
        var capabilitySummaries = new Dictionary<string, DeviceCapabilitySummary>(StringComparer.OrdinalIgnoreCase);
        if (blade is not null)
        {
            capabilitySummaries[blade.Descriptor.Id] =
                DeviceCapabilitySummaryCalculator.Calculate(blade.Descriptor, telemetry);
        }
        if (viper is not null)
        {
            capabilitySummaries[viper.Descriptor.Id] =
                DeviceCapabilitySummaryCalculator.Calculate(viper.Descriptor, telemetry);
        }
        return telemetry with { CapabilitySummaries = capabilitySummaries };
    }

    public async ValueTask<BladePerformanceMode> SetBladePerformanceModeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladePerformanceMode mode,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        var blade = FindReadyDevice(devices, "blade-710")
            ?? throw new InvalidOperationException("The Blade platform control channel is unavailable.");
        EnsureValidated(_validatedBladePerformancePath, blade.Descriptor.Id, "Read the Blade performance mode successfully first.");

        var original = await ReadBladeThermalStateAsync(blade, cancellationToken);
        try
        {
            await WriteBladeThermalZoneAsync(blade, 0x01, mode, original.FanMode, cancellationToken);
            await WriteBladeThermalZoneAsync(blade, 0x02, mode, original.FanMode, cancellationToken);

            var actual = await ReadBladeThermalStateAsync(blade, cancellationToken);
            if (actual.PerformanceMode != mode || actual.FanMode != original.FanMode)
            {
                throw new InvalidOperationException(
                    $"Performance mode readback mismatch: wrote {mode} / {original.FanMode}, read {actual.PerformanceMode} / {actual.FanMode}.");
            }

            return actual.PerformanceMode;
        }
        catch (Exception exception) when (
            IsExpectedHardwareException(exception) || exception is OperationCanceledException)
        {
            var restored = await TryRestoreBladeThermalStateAsync(blade, original);
            var message = $"Performance mode update failed: {exception.Message} " +
                (restored
                    ? "The original state was restored."
                    : "Original state restoration failed; check both fan zones immediately.");
            if (exception is OperationCanceledException)
            {
                throw new OperationCanceledException(message, exception, cancellationToken);
            }
            throw new InvalidOperationException(message, exception);
        }
    }

    public async ValueTask<BladeGameModeTelemetry> SetBladeGameModeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var blade = FindReadyDevice(devices, "blade-710")
            ?? throw new InvalidOperationException("The Blade gaming-mode control channel is unavailable.");
        await QueryBuiltRequestAsync(
            blade,
            "gaming-mode.set",
            BladeSynapsePolicyProtocol.CreateSetGameModeRequest(enabled),
            cancellationToken);
        return new(enabled ? (byte)1 : (byte)0, 0, 0);
    }

    public async ValueTask<bool> SetBladeFnKeyStateAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        bool multiFunctionPrimary,
        CancellationToken cancellationToken = default)
    {
        var blade = FindReadyDevice(devices, "blade-710")
            ?? throw new InvalidOperationException("The Blade Fn primary-function control channel is unavailable.");
        var builtRequest = BladeSynapsePolicyProtocol.CreateSetFnKeyStateRequest(multiFunctionPrimary);
        var request = CreateConfiguredRequest(blade, "fn-key.set", builtRequest);
        var response = await QueryCapabilityAsync(
            blade,
            "fn-key.set",
            request.AsMemory(RazerFeatureReport.ArgumentsOffset, request[6]),
            cancellationToken,
            request[6]);
        var state = BladeSynapsePolicyProtocol.ParseFnKeyState(response, request);
        return state.MultiFunctionPrimary;
    }

    public async ValueTask<BladeFanControlState> SetBladeFanAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeFanMode mode,
        int? targetRpm,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }
        if (mode == BladeFanMode.Manual)
        {
            if (targetRpm is null)
            {
                throw new ArgumentNullException(nameof(targetRpm));
            }
            BladeFanProtocol.ValidateTargetRpm(targetRpm.Value);
        }
        else if (targetRpm is not null)
        {
            throw new ArgumentException("Automatic fan mode cannot specify a fixed speed.", nameof(targetRpm));
        }

        var blade = FindReadyDevice(devices, "blade-710")
            ?? throw new InvalidOperationException("The Blade fan control channel is unavailable.");

        var original = await ReadBladeFanTransactionStateAsync(blade, cancellationToken, readTachometers: true);
        var cpuTargetRpm = targetRpm ?? original.CpuTargetRpm;
        var gpuTargetRpm = targetRpm ?? original.GpuTargetRpm;
        var actual = await SetBladeFanTargetsCoreAsync(
            blade,
            mode,
            cpuTargetRpm,
            gpuTargetRpm,
            writeTargets: mode == BladeFanMode.Manual,
            original,
            cancellationToken);
        return new BladeFanControlState(actual.Thermal.FanMode, actual.CpuTargetRpm);
    }

    public async ValueTask<BladeFanControlSnapshot> ReadBladeFanControlStateAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(devices);
        var blade = FindReadyDevice(devices, "blade-710")
            ?? throw new InvalidOperationException("The Blade fan control channel is unavailable.");
        var state = await ReadBladeFanTransactionStateAsync(
            blade, cancellationToken, readTachometers: true);
        return state.ToPublic();
    }

    public async ValueTask<BladeFanControlSnapshot> SetBladeFanTargetsAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeFanMode mode,
        int cpuTargetRpm,
        int gpuTargetRpm,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(devices);
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }
        BladeFanProtocol.ValidateCurveTargetRpm(cpuTargetRpm);
        BladeFanProtocol.ValidateCurveTargetRpm(gpuTargetRpm);
        var blade = FindReadyDevice(devices, "blade-710")
            ?? throw new InvalidOperationException("The Blade fan control channel is unavailable.");
        var original = await ReadBladeFanTransactionStateAsync(
            blade, cancellationToken, readTachometers: true);
        var actual = await SetBladeFanTargetsCoreAsync(
            blade,
            mode,
            cpuTargetRpm,
            gpuTargetRpm,
            writeTargets: true,
            original,
            cancellationToken);
        return actual.ToPublic();
    }

    private async Task<BladeFanTransactionState> SetBladeFanTargetsCoreAsync(
        ReadyDevice blade,
        BladeFanMode mode,
        int cpuTargetRpm,
        int gpuTargetRpm,
        bool writeTargets,
        BladeFanTransactionState original,
        CancellationToken cancellationToken)
    {
        try
        {
            if (writeTargets)
            {
                await WriteBladeFanTargetAsync(
                    blade, BladeFanProtocol.ZoneCpu, cpuTargetRpm, cancellationToken, curveTarget: true);
                await WriteBladeFanTargetAsync(
                    blade, BladeFanProtocol.ZoneGpu, gpuTargetRpm, cancellationToken, curveTarget: true);
            }

            await WriteBladeThermalZoneAsync(
                blade, BladeFanProtocol.ZoneCpu, original.Thermal.PerformanceMode, mode, cancellationToken);
            await WriteBladeThermalZoneAsync(
                blade, BladeFanProtocol.ZoneGpu, original.Thermal.PerformanceMode, mode, cancellationToken);

            var actual = await ReadBladeFanTransactionStateAsync(
                blade, cancellationToken, readTachometers: false);
            if (actual.Thermal.PerformanceMode != original.Thermal.PerformanceMode ||
                actual.Thermal.FanMode != mode ||
                actual.CpuTargetRpm != cpuTargetRpm ||
                actual.GpuTargetRpm != gpuTargetRpm)
            {
                throw new InvalidOperationException(
                    $"Fan readback mismatch: wrote {mode} / CPU {cpuTargetRpm} / GPU {gpuTargetRpm} RPM, " +
                    $"read {actual.Thermal.FanMode} / CPU {actual.CpuTargetRpm} / GPU {actual.GpuTargetRpm} RPM.");
            }

            return actual;
        }
        catch (Exception operationException)
        {
            var restorationException = await RestoreBladeFanTransactionStateAsync(blade, original);
            if (restorationException is not null)
            {
                throw new AggregateException(
                    "Fixed fan update failed, and the original state could not be restored.",
                    operationException,
                    restorationException);
            }

            var message = $"Fan update failed: {operationException.Message} The original state was restored.";
            if (operationException is OperationCanceledException)
            {
                throw new OperationCanceledException(message, operationException, cancellationToken);
            }
            throw new InvalidOperationException(message, operationException);
        }
    }

    public async ValueTask<int> SetBladeChargeLimitAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        int percent,
        CancellationToken cancellationToken = default)
    {
        var raw = EncodeBladeChargeLimit(percent);
        var blade = FindReadyDevice(devices, "blade-710")
            ?? throw new InvalidOperationException("The Blade charge control channel is unavailable.");
        EnsureValidated(_validatedBladeChargeLimitPath, blade.Descriptor.Id, "Read the Blade charge limit successfully first.");

        var original = await ReadBladeChargeLimitAsync(blade, cancellationToken);
        try
        {
            await WriteBladeChargeLimitAsync(blade, raw, cancellationToken);
            var actual = await ReadBladeChargeLimitAsync(blade, cancellationToken);
            if (actual != percent)
            {
                throw new InvalidOperationException($"Charge limit readback mismatch: wrote {percent}%, read {actual}%.");
            }

            return actual;
        }
        catch (Exception exception) when (
            IsExpectedHardwareException(exception) || exception is OperationCanceledException)
        {
            var restored = await TryRestoreBladeChargeLimitAsync(blade, original);
            var message = $"Charge limit update failed: {exception.Message} " +
                (restored
                    ? "The original value was restored."
                    : "Original value restoration failed; check the charge limit immediately.");
            if (exception is OperationCanceledException)
            {
                throw new OperationCanceledException(message, exception, cancellationToken);
            }
            throw new InvalidOperationException(message, exception);
        }
    }

    public async ValueTask<BladeCpuBoostMode> SetBladeCpuBoostModeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeCpuBoostMode mode,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        var state = await SetBladeBoostModeAsync(
            devices, BladeBoostProtocol.CpuCluster, (byte)mode, cancellationToken);
        return state.Cpu;
    }

    public async ValueTask<BladeGpuBoostMode> SetBladeGpuBoostModeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeGpuBoostMode mode,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        var state = await SetBladeBoostModeAsync(
            devices, BladeBoostProtocol.GpuCluster, (byte)mode, cancellationToken);
        return state.Gpu;
    }

    public ValueTask<BladeMaxFanMode> SetBladeMaxFanModeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeMaxFanMode mode,
        CancellationToken cancellationToken = default) =>
        RunBladePowerModeTransactionAsync(
            () => SetBladeMaxFanModeCoreAsync(devices, mode, cancellationToken),
            cancellationToken);

    private async ValueTask<BladeMaxFanMode> SetBladeMaxFanModeCoreAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeMaxFanMode mode,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        var blade = FindReadyDevice(devices, "blade-710")
            ?? throw new InvalidOperationException("The Blade Max Fan control channel is unavailable.");
        EnsureValidated(_validatedBladeMaxFanPath, blade.Descriptor.Id, "Read the Blade Max Fan state successfully first.");

        var thermal = await ReadBladeThermalStateAsync(blade, cancellationToken);
        if (thermal.PerformanceMode != BladePerformanceMode.Custom)
        {
            throw new InvalidOperationException("Max Fan can only be changed in Custom performance mode.");
        }

        var originalMask = await ReadBladePowerModeMaskAsync(blade, cancellationToken);
        var original = ToMaxFanMode(originalMask);
        if (original == mode)
        {
            return original;
        }

        try
        {
            await WriteBladeMaxFanModeAsync(blade, mode, originalMask, cancellationToken);
            var actualMask = await ReadBladePowerModeMaskAsync(blade, cancellationToken);
            var actual = ToMaxFanMode(actualMask);
            var expectedMask = mode == BladeMaxFanMode.Enabled
                ? (byte)(originalMask | BladeMaxFanProtocol.MaxFanBit)
                : (byte)(originalMask & ~BladeMaxFanProtocol.MaxFanBit);
            if (actualMask != expectedMask)
            {
                throw new InvalidOperationException($"Max Fan readback mismatch: wrote {mode}, read {actual}, and other power bits also changed.");
            }

            return actual;
        }
        catch (Exception exception) when (
            IsExpectedHardwareException(exception) || exception is OperationCanceledException)
        {
            var restored = await TryRestoreBladeMaxFanModeAsync(blade, original, originalMask);
            var message = "Max Fan update failed: " + exception.Message + " " +
                (restored ? "The original value was restored." : "Original value restoration failed; check the fan settings immediately.");
            if (exception is OperationCanceledException)
            {
                throw new OperationCanceledException(message, exception, cancellationToken);
            }
            throw new InvalidOperationException(message, exception);
        }
    }

    public ValueTask<bool> SetBladeOneTimeFullChargeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        bool enabled,
        CancellationToken cancellationToken = default) =>
        RunBladePowerModeTransactionAsync(
            () => SetBladeOneTimeFullChargeCoreAsync(devices, enabled, cancellationToken),
            cancellationToken);

    private async ValueTask<bool> SetBladeOneTimeFullChargeCoreAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var blade = FindReadyDevice(devices, "blade-710")
            ?? throw new InvalidOperationException("The Blade one-time-full-charge control channel is unavailable.");
        EnsureValidated(
            _validatedBladeMaxFanPath,
            blade.Descriptor.Id,
            "Read the Blade Power Mode Control state successfully first.");
        if (enabled && await ReadBladeChargeLimitAsync(blade, cancellationToken) == 100)
        {
            throw new InvalidOperationException("One-time full charge is available only when the charge limit is enabled.");
        }

        var originalMask = await ReadBladePowerModeMaskAsync(blade, cancellationToken);
        var original = (originalMask & BladeMaxFanProtocol.OneTimeFullChargeBit) != 0;
        if (original == enabled)
        {
            return original;
        }

        var expectedMask = enabled
            ? (byte)(originalMask | BladeMaxFanProtocol.OneTimeFullChargeBit)
            : (byte)(originalMask & ~BladeMaxFanProtocol.OneTimeFullChargeBit);
        try
        {
            await WriteBladePowerModeMaskAsync(blade, expectedMask, cancellationToken);
            var actualMask = await ReadBladePowerModeMaskAsync(blade, cancellationToken);
            if (actualMask != expectedMask)
            {
                throw new InvalidOperationException(
                    $"One-time-full-charge readback mismatch: wrote 0x{expectedMask:X2}, read 0x{actualMask:X2}.");
            }

            return enabled;
        }
        catch (Exception exception) when (
            IsExpectedHardwareException(exception) || exception is OperationCanceledException)
        {
            var restored = await TryRestoreBladePowerModeMaskAsync(blade, originalMask);
            var message = "One-time-full-charge update failed: " + exception.Message + " " +
                (restored ? "The original value was restored." : "Original value restoration failed; check the charge settings immediately.");
            if (exception is OperationCanceledException)
            {
                throw new OperationCanceledException(message, exception, cancellationToken);
            }
            throw new InvalidOperationException(message, exception);
        }
    }

    public ValueTask<bool> SetBladeLocalDimmingAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        bool enabled,
        CancellationToken cancellationToken = default) =>
        RunBladePowerModeTransactionAsync(
            () => SetBladeLocalDimmingCoreAsync(devices, enabled, cancellationToken),
            cancellationToken);

    private async ValueTask<bool> SetBladeLocalDimmingCoreAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var blade = FindReadyDevice(devices, "blade-710")
            ?? throw new InvalidOperationException("The Blade Local Dimming control channel is unavailable.");
        EnsureValidated(
            _validatedBladeLocalDimmingPath,
            blade.Descriptor.Id,
            "Local Dimming is available only on a confirmed MiniLED panel.");

        var originalMask = await ReadBladePowerModeMaskAsync(blade, cancellationToken);
        var original = (originalMask & BladeMaxFanProtocol.LocalDimmingBit) != 0;
        if (original == enabled)
        {
            return original;
        }

        var expectedMask = enabled
            ? (byte)(originalMask | BladeMaxFanProtocol.LocalDimmingBit)
            : (byte)(originalMask & ~BladeMaxFanProtocol.LocalDimmingBit);
        try
        {
            await WriteBladePowerModeMaskAsync(blade, expectedMask, cancellationToken);
            var actualMask = await ReadBladePowerModeMaskAsync(blade, cancellationToken);
            if (actualMask != expectedMask)
            {
                throw new InvalidOperationException(
                    $"Local Dimming readback mismatch: wrote 0x{expectedMask:X2}, read 0x{actualMask:X2}.");
            }

            return enabled;
        }
        catch (Exception exception) when (
            IsExpectedHardwareException(exception) || exception is OperationCanceledException)
        {
            var restored = await TryRestoreBladePowerModeMaskAsync(blade, originalMask);
            var message = "Local Dimming update failed: " + exception.Message + " " +
                (restored ? "The original value was restored." : "Original value restoration failed; check the display settings immediately.");
            if (exception is OperationCanceledException)
            {
                throw new OperationCanceledException(message, exception, cancellationToken);
            }

            throw new InvalidOperationException(message, exception);
        }
    }

    private async ValueTask<T> RunBladePowerModeTransactionAsync<T>(
        Func<ValueTask<T>> operation,
        CancellationToken cancellationToken)
    {
        await _bladePowerModeTransactionGate.WaitAsync(cancellationToken);
        try
        {
            return await operation();
        }
        finally
        {
            _bladePowerModeTransactionGate.Release();
        }
    }

    public async ValueTask<bool> SetBladeStartupAnimationAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var blade = FindReadyDevice(devices, "blade-710")
            ?? throw new InvalidOperationException("The Blade startup-animation control channel is unavailable.");
        EnsureValidated(
            _validatedBladeStartupAnimationPath,
            blade.Descriptor.Id,
            "Read the Blade startup-animation state successfully first.");

        var original = await ReadBladeStartupAnimationAsync(blade, cancellationToken);
        if (original.Enabled == enabled)
        {
            return original.Enabled;
        }

        try
        {
            await WriteBladeStartupAnimationAsync(blade, enabled, cancellationToken);
            var actual = await ReadBladeStartupAnimationAsync(blade, cancellationToken);
            if (actual.Enabled != enabled)
            {
                throw new InvalidOperationException(
                    $"Startup-animation readback mismatch: wrote {(enabled ? "enabled" : "disabled")}, " +
                    $"read {(actual.Enabled ? "enabled" : "disabled")}.");
            }

            return actual.Enabled;
        }
        catch (Exception exception) when (
            IsExpectedHardwareException(exception) || exception is OperationCanceledException)
        {
            var restored = await TryRestoreBladeStartupAnimationAsync(blade, original.Enabled);
            var message = "Startup-animation update failed: " + exception.Message + " " +
                (restored ? "The original value was restored." : "Original value restoration failed; check the startup animation immediately.");
            if (exception is OperationCanceledException)
            {
                throw new OperationCanceledException(message, exception, cancellationToken);
            }

            throw new InvalidOperationException(message, exception);
        }
    }

    public async ValueTask<BladeNativeDisplayMode> SetBladeNativeDisplayModeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeNativeDisplayMode mode,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        var blade = FindReadyDevice(devices, "blade-710")
            ?? throw new InvalidOperationException("The Blade native-display-mode control channel is unavailable.");
        EnsureValidated(
            _validatedBladeNativeDisplayModePath,
            blade.Descriptor.Id,
            "Read the Blade native display mode successfully first.");

        var original = await ReadBladeNativeDisplayModeAsync(blade, cancellationToken);
        if (original == mode)
        {
            return original;
        }

        try
        {
            await WriteBladeNativeDisplayModeAsync(blade, mode, cancellationToken);
            var actual = await ReadBladeNativeDisplayModeAsync(blade, cancellationToken);
            if (actual != mode)
            {
                throw new InvalidOperationException(
                    $"Native display mode readback mismatch: wrote {mode}, read {actual}.");
            }

            return actual;
        }
        catch (Exception exception) when (
            IsExpectedHardwareException(exception) || exception is OperationCanceledException)
        {
            var restored = await TryRestoreBladeNativeDisplayModeAsync(blade, original);
            var message = "Native display mode update failed: " + exception.Message + " " +
                (restored ? "The original value was restored." : "Original value restoration failed; check the display mode immediately.");
            if (exception is OperationCanceledException)
            {
                throw new OperationCanceledException(message, exception, cancellationToken);
            }

            throw new InvalidOperationException(message, exception);
        }
    }

    public async ValueTask<byte> SetBladeKeyboardBrightnessAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        byte brightness,
        CancellationToken cancellationToken = default,
        bool verifyReadback = true)
    {
        var blade = FindReadyDevice(devices, "blade-710")
            ?? throw new InvalidOperationException("The Blade keyboard control channel is unavailable.");
        EnsureValidated(_validatedBladeBrightnessPath, blade.Descriptor.Id, "Read the Blade keyboard brightness successfully first.");

        if (!verifyReadback)
        {
            await WriteBladeBrightnessAsync(blade, brightness, cancellationToken);
            return brightness;
        }

        var original = await ReadBladeBrightnessAsync(blade, cancellationToken);
        if (original == brightness)
        {
            return original;
        }

        try
        {
            await WriteBladeBrightnessAsync(blade, brightness, cancellationToken);
            var actual = await ReadBladeBrightnessAsync(blade, cancellationToken);
            if (actual != brightness)
            {
                throw new InvalidOperationException($"Brightness readback mismatch: wrote {brightness}, read {actual}.");
            }
            return actual;
        }
        catch (Exception exception) when (
            IsExpectedHardwareException(exception) || exception is OperationCanceledException)
        {
            var restored = await TryRestoreBladeBrightnessAsync(blade, original);
            var message = "Brightness update failed: " + exception.Message + " " +
                (restored ? "The original value was restored." : "Original value restoration failed; check the keyboard brightness immediately.");
            if (exception is OperationCanceledException)
            {
                throw new OperationCanceledException(message, exception, cancellationToken);
            }
            throw new InvalidOperationException(message, exception);
        }
    }

    public async ValueTask<byte> ReadBladeKeyboardBrightnessAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        CancellationToken cancellationToken = default)
    {
        var blade = FindReadyDevice(devices, "blade-710")
            ?? throw new InvalidOperationException("The Blade keyboard control channel is unavailable.");
        EnsureValidated(_validatedBladeBrightnessPath, blade.Descriptor.Id, "Read the Blade keyboard brightness successfully first.");
        return await ReadBladeBrightnessAsync(blade, cancellationToken);
    }

    private async Task<byte> ReadBladeBrightnessAsync(
        ReadyDevice device,
        CancellationToken cancellationToken)
    {
        var response = await QueryCapabilityAsync(device, "keyboard-brightness.get", cancellationToken);
        EnsureDataSize(response, 2, "keyboard brightness");
        return response[RazerFeatureReport.ArgumentsOffset + 1];
    }

    private Task WriteBladeBrightnessAsync(
        ReadyDevice device,
        byte brightness,
        CancellationToken cancellationToken) =>
        QueryCapabilityAsync(
            device, "keyboard-brightness.set", new byte[] { 0x01, brightness }, cancellationToken);

    private async Task<bool> TryRestoreBladeBrightnessAsync(ReadyDevice device, byte original)
    {
        try
        {
            await WriteBladeBrightnessAsync(device, original, CancellationToken.None);
            return await ReadBladeBrightnessAsync(device, CancellationToken.None) == original;
        }
            catch (Exception exception) when (IsExpectedHardwareException(exception))
        {
            return false;
        }
    }

    public async ValueTask<BladeLogoMode> SetBladeLogoModeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeLogoMode mode,
        CancellationToken cancellationToken = default)
    {
        if (mode is not (BladeLogoMode.Off or BladeLogoMode.Static or BladeLogoMode.Breathing))
        {
            throw new ArgumentOutOfRangeException(
                nameof(mode), "Unsupported Logo mode.");
        }

        var blade = FindReadyDevice(devices, "blade-710")
            ?? throw new InvalidOperationException("The Blade Logo control channel is unavailable.");
        EnsureValidated(_validatedBladeLogoPath, blade.Descriptor.Id, "Read the Blade Logo state successfully first.");

        var original = await ReadBladeLogoStateAsync(blade, cancellationToken);
        if (original.CombinedMode == mode)
        {
            return mode;
        }

        try
        {
            await WriteBladeLogoStateAsync(blade, mode, cancellationToken);
            var actual = await ReadBladeLogoStateAsync(blade, cancellationToken);
            if (actual.CombinedMode != mode ||
                (mode == BladeLogoMode.Static && actual.PoweredMode != BladeLogoMode.Static))
            {
                throw new InvalidOperationException(
                    $"Logo readback mismatch: wrote {mode}, read {actual.CombinedMode}/{actual.PoweredMode}.");
            }

            return actual.CombinedMode;
        }
        catch (Exception exception) when (
            IsExpectedHardwareException(exception) || exception is OperationCanceledException)
        {
            var restored = await TryRestoreBladeLogoStateAsync(blade, original);
            var message = "Logo update failed: " + exception.Message + " " +
                (restored ? "The original state was restored." : "Original state restoration failed; check the Logo immediately.");
            if (exception is OperationCanceledException)
            {
                throw new OperationCanceledException(message, exception, cancellationToken);
            }
            throw new InvalidOperationException(message, exception);
        }
    }

    private async Task<BladeLogoState> ReadBladeLogoStateAsync(
        ReadyDevice device,
        CancellationToken cancellationToken)
    {
        var powerResponse = await QueryCapabilityAsync(device, "logo-power.get", cancellationToken);
        var modeResponse = await QueryCapabilityAsync(device, "logo-mode.get", cancellationToken);
        var power = BladeLogoProtocol.ParsePower(
            powerResponse, CreateCapabilityRequest(device, "logo-power.get"));
        var poweredMode = BladeLogoProtocol.ParseMode(
            modeResponse, CreateCapabilityRequest(device, "logo-mode.get"));
        return new BladeLogoState(power, poweredMode);
    }

    private async Task WriteBladeLogoStateAsync(
        ReadyDevice device,
        BladeLogoMode combinedMode,
        CancellationToken cancellationToken)
    {
        if (combinedMode == BladeLogoMode.Breathing)
        {
            await QueryBuiltRequestAsync(
                device,
                "logo-effect.set",
                BladeSynapsePolicyProtocol.CreateSetLogoEffectRequest(BladeLogoMode.Breathing),
                cancellationToken);
            await QueryBuiltRequestAsync(
                device,
                "logo-state.set",
                BladeSynapsePolicyProtocol.CreateSetLogoStateRequest(BladeLogoMode.Breathing),
                cancellationToken);
            return;
        }

        if (combinedMode == BladeLogoMode.Static)
        {
            await QueryBuiltRequestAsync(
                device, "logo-mode.set", BladeLogoProtocol.CreateSetModeRequest(BladeLogoMode.Static), cancellationToken);
        }
        else if (combinedMode != BladeLogoMode.Off)
        {
            throw new ArgumentOutOfRangeException(nameof(combinedMode));
        }

        var powerRequest = BladeLogoProtocol.CreateSetPowerRequest(combinedMode != BladeLogoMode.Off);
        await QueryBuiltRequestAsync(device, "logo-power.set", powerRequest, cancellationToken);
    }

    private async Task<bool> TryRestoreBladeLogoStateAsync(
        ReadyDevice device,
        BladeLogoState original)
    {
        try
        {
            await QueryBuiltRequestAsync(
                device,
                "logo-mode.set",
                BladeLogoProtocol.CreateSetModeRequest(original.PoweredMode),
                CancellationToken.None);
            await QueryBuiltRequestAsync(
                device,
                "logo-power.set",
                BladeLogoProtocol.CreateSetPowerRequest(original.Powered),
                CancellationToken.None);
            return await ReadBladeLogoStateAsync(device, CancellationToken.None) == original;
        }
            catch (Exception exception) when (IsExpectedHardwareException(exception))
        {
            return false;
        }
    }

    private async Task<BladeStartupAnimationState> ReadBladeStartupAnimationAsync(
        ReadyDevice device,
        CancellationToken cancellationToken)
    {
        var request = CreateCapabilityRequest(device, "startup-animation.get");
        var response = await QueryCapabilityAsync(device, "startup-animation.get", cancellationToken);
        return BladeSynapsePolicyProtocol.ParseStartupAnimation(response, request);
    }

    private async Task WriteBladeStartupAnimationAsync(
        ReadyDevice device,
        bool enabled,
        CancellationToken cancellationToken)
    {
        var request = BladeSynapsePolicyProtocol.CreateSetStartupAnimationRequest(enabled);
        _ = await QueryBuiltRequestAsync(
            device, "startup-animation.set", request, cancellationToken);
    }

    private async Task<bool> TryRestoreBladeStartupAnimationAsync(
        ReadyDevice device,
        bool original)
    {
        try
        {
            await WriteBladeStartupAnimationAsync(device, original, CancellationToken.None);
            return (await ReadBladeStartupAnimationAsync(device, CancellationToken.None)).Enabled == original;
        }
        catch (Exception exception) when (IsExpectedHardwareException(exception))
        {
            return false;
        }
    }

    private async Task<BladeNativeDisplayMode> ReadBladeNativeDisplayModeAsync(
        ReadyDevice device,
        CancellationToken cancellationToken)
    {
        var request = CreateCapabilityRequest(device, "native-display-mode.get");
        var response = await QueryCapabilityAsync(
            device, "native-display-mode.get", cancellationToken);
        return BladeProduct710Protocol.ParseNativeDisplayMode(response, request);
    }

    private Task WriteBladeNativeDisplayModeAsync(
        ReadyDevice device,
        BladeNativeDisplayMode mode,
        CancellationToken cancellationToken) =>
        QueryBuiltRequestAsync(
            device,
            "native-display-mode.set",
            BladeProduct710Protocol.CreateSetNativeDisplayModeRequest(mode),
            cancellationToken);

    private async Task<bool> TryRestoreBladeNativeDisplayModeAsync(
        ReadyDevice device,
        BladeNativeDisplayMode original)
    {
        try
        {
            await WriteBladeNativeDisplayModeAsync(device, original, CancellationToken.None);
            return await ReadBladeNativeDisplayModeAsync(device, CancellationToken.None) == original;
        }
        catch (Exception exception) when (IsExpectedHardwareException(exception))
        {
            return false;
        }
    }

    private async Task<BladeThermalState> ReadBladeThermalStateAsync(
        ReadyDevice device,
        CancellationToken cancellationToken)
    {
        BladeThermalState? state = null;
        foreach (var zone in new byte[] { 0x01, 0x02 })
        {
            var response = await QueryCapabilityAsync(
                device,
                "thermal-state.get",
                new byte[] { 0x00, zone, 0x00, 0x00 },
                cancellationToken);
            EnsureDataSize(response, 4, "Blade performance mode");
            if (response[RazerFeatureReport.ArgumentsOffset + 1] != zone)
            {
                throw new InvalidOperationException($"Blade returned the wrong fan zone: {response[RazerFeatureReport.ArgumentsOffset + 1]}.");
            }

            var current = new BladeThermalState(
                ParseBladePerformanceMode(response[RazerFeatureReport.ArgumentsOffset + 2]),
                ParseBladeFanMode(response[RazerFeatureReport.ArgumentsOffset + 3]));
            if (state is not null && state != current)
            {
                throw new InvalidOperationException($"Blade fan-zone states do not match: {state} / {current}.");
            }
            state = current;
        }

        return state ?? throw new InvalidOperationException("Blade did not return a performance mode.");
    }

    private async Task<int> ReadBladeFanTargetRpmAsync(
        ReadyDevice device,
        CancellationToken cancellationToken)
    {
        int? rpm = null;
        foreach (var zone in new[] { BladeFanProtocol.ZoneCpu, BladeFanProtocol.ZoneGpu })
        {
            var request = BladeFanProtocol.CreateGetTargetRequest(zone);
            var response = await QueryBuiltRequestAsync(
                device, "fan-target.get", request, cancellationToken);
            var current = BladeFanProtocol.ParseTarget(
                response, zone, CreateConfiguredRequest(device, "fan-target.get", request));
            if (rpm is not null && rpm != current)
            {
                throw new InvalidOperationException($"Blade fan-zone targets do not match: {rpm} / {current} RPM.");
            }
            rpm = current;
        }

        return rpm ?? throw new InvalidOperationException("Blade did not return a fan speed.");
    }

    private async Task<BladeFanTransactionState> ReadBladeFanTransactionStateAsync(
        ReadyDevice device,
        CancellationToken cancellationToken,
        bool readTachometers)
    {
        var thermal = await ReadBladeThermalStateAsync(device, cancellationToken);
        var cpuTargetRpm = await ReadBladeFanTargetRpmAsync(
            device, BladeFanProtocol.ZoneCpu, cancellationToken, curveTarget: true);
        var gpuTargetRpm = await ReadBladeFanTargetRpmAsync(
            device, BladeFanProtocol.ZoneGpu, cancellationToken, curveTarget: true);
        if (readTachometers)
        {
            _ = await ReadBladeCurrentFanRpmAsync(
                device, BladeThermalProtocol.CpuFanId, cancellationToken);
            _ = await ReadBladeCurrentFanRpmAsync(
                device, BladeThermalProtocol.GpuFanId, cancellationToken);
        }
        return new BladeFanTransactionState(thermal, cpuTargetRpm, gpuTargetRpm);
    }

    private async Task<int> ReadBladeFanTargetRpmAsync(
        ReadyDevice device,
        byte zone,
        CancellationToken cancellationToken,
        bool curveTarget)
    {
        var request = BladeFanProtocol.CreateGetTargetRequest(zone);
        var response = await QueryBuiltRequestAsync(
            device, "fan-target.get", request, cancellationToken);
        var configured = CreateConfiguredRequest(device, "fan-target.get", request);
        return curveTarget
            ? BladeFanProtocol.ParseCurveTarget(response, zone, configured)
            : BladeFanProtocol.ParseTarget(response, zone, configured);
    }

    private Task WriteBladeFanTargetAsync(
        ReadyDevice device,
        byte zone,
        int rpm,
        CancellationToken cancellationToken,
        bool curveTarget = false)
    {
        var request = curveTarget
            ? BladeFanProtocol.CreateSetCurveTargetRequest(zone, rpm)
            : BladeFanProtocol.CreateSetTargetRequest(zone, rpm);
        var transport = device.Manifest.GetRequiredCapability("fan-target.get");
        return _transport.QueryAsync(
            device.Descriptor.Id,
            transport.TransactionId,
            request[6],
            transport.CommandClass,
            0x01,
            request.AsMemory(RazerFeatureReport.ArgumentsOffset, request[6]),
            transport.Wait,
            cancellationToken,
            transport.AllowRemainingPacketsMismatch);
    }

    private async Task<Exception?> RestoreBladeFanTransactionStateAsync(
        ReadyDevice device,
        BladeFanTransactionState original)
    {
        var errors = new List<Exception>();

        async Task AttemptAsync(string operation, Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception exception)
            {
                errors.Add(new InvalidOperationException($"{operation} failed: {exception.Message}", exception));
            }
        }

        await AttemptAsync("Restore CPU fan target", () => WriteBladeFanTargetAsync(
            device,
            BladeFanProtocol.ZoneCpu,
            original.CpuTargetRpm,
            CancellationToken.None,
            curveTarget: true));
        await AttemptAsync("Restore GPU fan target", () => WriteBladeFanTargetAsync(
            device,
            BladeFanProtocol.ZoneGpu,
            original.GpuTargetRpm,
            CancellationToken.None,
            curveTarget: true));
        await AttemptAsync("Restore CPU fan mode", () => WriteBladeThermalZoneAsync(
            device,
            BladeFanProtocol.ZoneCpu,
            original.Thermal.PerformanceMode,
            original.Thermal.FanMode,
            CancellationToken.None));
        await AttemptAsync("Restore GPU fan mode", () => WriteBladeThermalZoneAsync(
            device,
            BladeFanProtocol.ZoneGpu,
            original.Thermal.PerformanceMode,
            original.Thermal.FanMode,
            CancellationToken.None));
        await AttemptAsync("Restore readback", async () =>
        {
            var restored = await ReadBladeFanTransactionStateAsync(
                device, CancellationToken.None, readTachometers: false);
            if (restored != original)
            {
                throw new InvalidOperationException(
                    $"Expected {original.Thermal.FanMode} / CPU {original.CpuTargetRpm} / GPU {original.GpuTargetRpm} RPM, " +
                    $"read {restored.Thermal.FanMode} / CPU {restored.CpuTargetRpm} / GPU {restored.GpuTargetRpm} RPM.");
            }
        });

        return errors.Count switch
        {
            0 => null,
            1 => errors[0],
            _ => new AggregateException("Restoring the original fixed-fan state produced multiple failures.", errors),
        };
    }

    private async Task<int> ReadBladeCurrentFanRpmAsync(
        ReadyDevice device,
        byte fanId,
        CancellationToken cancellationToken)
    {
        var request = BladeThermalProtocol.CreateGetCurrentSpeedRequest(fanId);
        var response = await QueryBuiltRequestAsync(
            device, "current-fan-rpm.get", request, cancellationToken);
        return BladeThermalProtocol.ParseCurrentSpeedRpm(
            response,
            fanId,
            CreateConfiguredRequest(device, "current-fan-rpm.get", request));
    }

    private async Task<byte> ReadBladeAdvancedFanModeAsync(
        ReadyDevice device,
        byte fanId,
        CancellationToken cancellationToken)
    {
        var request = BladeThermalProtocol.CreateGetAdvancedFanModeRequest(fanId);
        var response = await QueryBuiltRequestAsync(
            device, "advanced-fan-mode.get", request, cancellationToken);
        return BladeThermalProtocol.ParseAdvancedFanMode(
            response,
            fanId,
            CreateConfiguredRequest(device, "advanced-fan-mode.get", request));
    }

    private async Task<BladeBoostState> ReadBladeBoostStateAsync(
        ReadyDevice device,
        CancellationToken cancellationToken)
    {
        var cpuResponse = await QueryCapabilityAsync(
            device,
            "boost.get",
            new byte[] { 0x00, BladeBoostProtocol.CpuCluster, 0x00 },
            cancellationToken);
        var gpuResponse = await QueryCapabilityAsync(
            device,
            "boost.get",
            new byte[] { 0x00, BladeBoostProtocol.GpuCluster, 0x00 },
            cancellationToken);

        return new BladeBoostState(
            BladeBoostProtocol.ParseCpu(cpuResponse),
            BladeBoostProtocol.ParseGpu(gpuResponse));
    }

    private async Task<BladeBoostState> SetBladeBoostModeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        byte cluster,
        byte value,
        CancellationToken cancellationToken)
    {
        var blade = FindReadyDevice(devices, "blade-710")
            ?? throw new InvalidOperationException("The Blade Boost control channel is unavailable.");
        EnsureValidated(_validatedBladeBoostPath, blade.Descriptor.Id, "Read Blade CPU/GPU Boost successfully first.");

        var thermal = await ReadBladeThermalStateAsync(blade, cancellationToken);
        if (thermal.PerformanceMode != BladePerformanceMode.Custom)
        {
            throw new InvalidOperationException("CPU/GPU Boost can only be changed in Custom performance mode.");
        }

        var original = await ReadBladeBoostStateAsync(blade, cancellationToken);
        if ((cluster == BladeBoostProtocol.CpuCluster && (byte)original.Cpu == value) ||
            (cluster == BladeBoostProtocol.GpuCluster && (byte)original.Gpu == value))
        {
            return original;
        }

        try
        {
            await WriteBladeBoostModeAsync(blade, cluster, value, cancellationToken);
            var actual = await ReadBladeBoostStateAsync(blade, cancellationToken);
            var matches = cluster == BladeBoostProtocol.CpuCluster
                ? (byte)actual.Cpu == value && actual.Gpu == original.Gpu
                : (byte)actual.Gpu == value && actual.Cpu == original.Cpu;
            if (!matches)
            {
                throw new InvalidOperationException(
                    $"Boost readback mismatch: wrote cluster {cluster} / value {value}, " +
                    $"read CPU {actual.Cpu} / GPU {actual.Gpu}.");
            }

            return actual;
        }
        catch (Exception exception) when (
            IsExpectedHardwareException(exception) || exception is OperationCanceledException)
        {
            var restored = await TryRestoreBladeBoostStateAsync(blade, original);
            var message = "Boost update failed: " + exception.Message + " " +
                (restored
                    ? "The original value was restored."
                    : "Original value restoration failed; check CPU/GPU Boost immediately.");
            if (exception is OperationCanceledException)
            {
                throw new OperationCanceledException(message, exception, cancellationToken);
            }
            throw new InvalidOperationException(message, exception);
        }
    }

    private Task WriteBladeBoostModeAsync(
        ReadyDevice device,
        byte cluster,
        byte value,
        CancellationToken cancellationToken) =>
        QueryCapabilityAsync(
            device, "boost.set", new byte[] { 0x00, cluster, value }, cancellationToken);

    private async Task<bool> TryRestoreBladeBoostStateAsync(
        ReadyDevice device,
        BladeBoostState state)
    {
        try
        {
            var thermal = await ReadBladeThermalStateAsync(device, CancellationToken.None);
            if (thermal.PerformanceMode != BladePerformanceMode.Custom)
            {
                return false;
            }

            await WriteBladeBoostModeAsync(
                device, BladeBoostProtocol.CpuCluster, (byte)state.Cpu, CancellationToken.None);
            await WriteBladeBoostModeAsync(
                device, BladeBoostProtocol.GpuCluster, (byte)state.Gpu, CancellationToken.None);
            return await ReadBladeBoostStateAsync(device, CancellationToken.None) == state;
        }
        catch (Exception exception) when (IsExpectedHardwareException(exception))
        {
            return false;
        }
    }

    private async Task<int> ReadBladeChargeLimitAsync(
        ReadyDevice device,
        CancellationToken cancellationToken)
    {
        var response = await QueryCapabilityAsync(
            device, "charge-limit.get", cancellationToken);
        EnsureDataSize(response, 1, "Blade charge limit");
        return DecodeBladeChargeLimit(response[RazerFeatureReport.ArgumentsOffset]);
    }

    private async Task<byte> ReadBladePowerModeMaskAsync(
        ReadyDevice device,
        CancellationToken cancellationToken)
    {
        var response = await QueryCapabilityAsync(
            device, "max-fan.get", cancellationToken);
        return BladeMaxFanProtocol.ParsePowerModeMask(
            response, CreateCapabilityRequest(device, "max-fan.get"));
    }

    private Task WriteBladeMaxFanModeAsync(
        ReadyDevice device,
        BladeMaxFanMode mode,
        byte existingPowerModeMask,
        CancellationToken cancellationToken)
    {
        var request = BladeMaxFanProtocol.CreateSetRequest(mode, existingPowerModeMask);
        return QueryBuiltRequestAsync(device, "max-fan.set", request, cancellationToken);
    }

    private Task WriteBladePowerModeMaskAsync(
        ReadyDevice device,
        byte mask,
        CancellationToken cancellationToken) =>
        QueryBuiltRequestAsync(
            device,
            "max-fan.set",
            BladeMaxFanProtocol.CreateSetPowerModeMaskRequest(mask),
            cancellationToken);

    private async Task<bool> TryRestoreBladePowerModeMaskAsync(
        ReadyDevice device,
        byte originalMask)
    {
        try
        {
            await WriteBladePowerModeMaskAsync(device, originalMask, CancellationToken.None);
            return await ReadBladePowerModeMaskAsync(device, CancellationToken.None) == originalMask;
        }
        catch (Exception exception) when (IsExpectedHardwareException(exception))
        {
            return false;
        }
    }

    private async Task<bool> TryRestoreBladeMaxFanModeAsync(
        ReadyDevice device,
        BladeMaxFanMode mode,
        byte originalMask)
    {
        try
        {
            await WriteBladeMaxFanModeAsync(device, mode, originalMask, CancellationToken.None);
            return await ReadBladePowerModeMaskAsync(device, CancellationToken.None) == originalMask;
        }
        catch (Exception exception) when (IsExpectedHardwareException(exception))
        {
            return false;
        }
    }

    private static BladeMaxFanMode ToMaxFanMode(byte powerModeMask) =>
        (powerModeMask & BladeMaxFanProtocol.MaxFanBit) != 0
            ? BladeMaxFanMode.Enabled
            : BladeMaxFanMode.Disabled;

    private Task WriteBladeThermalZoneAsync(
        ReadyDevice device,
        byte zone,
        BladePerformanceMode performanceMode,
        BladeFanMode fanMode,
        CancellationToken cancellationToken) =>
        QueryCapabilityAsync(
            device,
            "thermal-state.set",
            new byte[] { 0x01, zone, (byte)performanceMode, (byte)fanMode },
            cancellationToken);

    private async Task<bool> TryRestoreBladeThermalStateAsync(
        ReadyDevice device,
        BladeThermalState state)
    {
        try
        {
            await WriteBladeThermalZoneAsync(
                device, 0x01, state.PerformanceMode, state.FanMode, CancellationToken.None);
            await WriteBladeThermalZoneAsync(
                device, 0x02, state.PerformanceMode, state.FanMode, CancellationToken.None);
            var restored = await ReadBladeThermalStateAsync(device, CancellationToken.None);
            return restored == state;
        }
        catch (Exception exception) when (IsExpectedHardwareException(exception))
        {
            return false;
        }
    }

    private Task WriteBladeChargeLimitAsync(
        ReadyDevice device,
        byte raw,
        CancellationToken cancellationToken) =>
        QueryCapabilityAsync(device, "charge-limit.set", new byte[] { raw }, cancellationToken);

    private async Task<bool> TryRestoreBladeChargeLimitAsync(
        ReadyDevice device,
        int percent)
    {
        try
        {
            await WriteBladeChargeLimitAsync(
                device, EncodeBladeChargeLimit(percent), CancellationToken.None);
            var restored = await ReadBladeChargeLimitAsync(device, CancellationToken.None);
            return restored == percent;
        }
        catch (Exception exception) when (IsExpectedHardwareException(exception))
        {
            return false;
        }
    }

    private static BladePerformanceMode ParseBladePerformanceMode(byte value) => value switch
    {
        0x00 => BladePerformanceMode.Balanced,
        0x02 => BladePerformanceMode.Performance,
        0x03 => BladePerformanceMode.BatterySaver,
        0x04 => BladePerformanceMode.Custom,
        0x05 => BladePerformanceMode.Silent,
        0x06 => BladePerformanceMode.BalancedDc,
        0x07 => BladePerformanceMode.Hyperboost,
        _ => throw new InvalidOperationException($"Blade returned an unknown performance mode: 0x{value:X2}."),
    };

    private static BladeFanMode ParseBladeFanMode(byte value) => value switch
    {
        0x00 => BladeFanMode.Automatic,
        0x01 => BladeFanMode.Manual,
        _ => throw new InvalidOperationException($"Blade returned an unknown fan mode: 0x{value:X2}."),
    };

    private static int DecodeBladeChargeLimit(byte value) => value switch
    {
        0xB2 => 50,
        0xB7 => 55,
        0xBC => 60,
        0xC1 => 65,
        0xC6 => 70,
        0xCB => 75,
        0xD0 => 80,
        0x50 => 100,
        _ => throw new InvalidOperationException($"Blade returned an unknown charge-limit code: 0x{value:X2}."),
    };

    private static byte EncodeBladeChargeLimit(int percent) => percent switch
    {
        50 => 0xB2,
        55 => 0xB7,
        60 => 0xBC,
        65 => 0xC1,
        70 => 0xC6,
        75 => 0xCB,
        80 => 0xD0,
        100 => 0x50,
        _ => throw new ArgumentOutOfRangeException(
            nameof(percent), "Charge limit must be 50, 55, 60, 65, 70, 75, 80, or 100 percent."),
    };

    private sealed record BladeThermalState(
        BladePerformanceMode PerformanceMode,
        BladeFanMode FanMode);

    private sealed record BladeFanTransactionState(
        BladeThermalState Thermal,
        int CpuTargetRpm,
        int GpuTargetRpm)
    {
        internal BladeFanControlSnapshot ToPublic() => new(
            Thermal.PerformanceMode,
            Thermal.FanMode,
            CpuTargetRpm,
            GpuTargetRpm);
    }

    private sealed record BladeBoostState(
        BladeCpuBoostMode Cpu,
        BladeGpuBoostMode Gpu);

    public async ValueTask<int> SetViperPollingRateAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        int hertz,
        CancellationToken cancellationToken = default)
    {
        var viper = FindReadyDevice(devices, "viper-184")
            ?? throw new InvalidOperationException("The Viper control channel is unavailable.");
        EnsureValidated(_validatedViperPollingPath, viper.Descriptor.Id, "Read the mouse polling rate successfully first.");

        var original = await ReadViperPollingRateAsync(viper, cancellationToken);
        if (original == hertz)
        {
            return original;
        }

        try
        {
            await WriteViperPollingRateAsync(viper, hertz, cancellationToken);
            var actual = await ReadViperPollingRateAsync(viper, cancellationToken);
            if (actual != hertz)
            {
                throw new InvalidOperationException($"Polling-rate readback mismatch: wrote {hertz} Hz, read {actual} Hz.");
            }
            return actual;
        }
        catch (Exception exception) when (
            IsExpectedHardwareException(exception) || exception is OperationCanceledException)
        {
            var restored = await TryRestoreViperPollingRateAsync(viper, original);
            var message = "Polling-rate update failed: " + exception.Message + " " +
                (restored ? "The original value was restored." : "Original value restoration failed; check the polling rate immediately.");
            if (exception is OperationCanceledException)
            {
                throw new OperationCanceledException(message, exception, cancellationToken);
            }
            throw new InvalidOperationException(message, exception);
        }
    }

    public async ValueTask<(int X, int Y)> SetViperDpiAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        int x,
        int y,
        CancellationToken cancellationToken = default)
    {
        if (x is < 100 or > 30000 || x % 50 != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(x), "DPI must be between 100 and 30000 in increments of 50.");
        }
        if (y is < 100 or > 30000 || y % 50 != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(y), "DPI must be between 100 and 30000 in increments of 50.");
        }

        var viper = FindReadyDevice(devices, "viper-184")
            ?? throw new InvalidOperationException("The Viper control channel is unavailable.");
        EnsureValidated(_validatedViperDpiPath, viper.Descriptor.Id, "Read the mouse DPI successfully first.");
        var original = await ReadViperDpiAsync(viper, cancellationToken);
        if (original == (x, y))
        {
            return original;
        }

        try
        {
            await WriteViperDpiAsync(viper, x, y, cancellationToken);
            var actual = await ReadViperDpiAsync(viper, cancellationToken);
            if (actual != (x, y))
            {
                throw new InvalidOperationException(
                    $"DPI readback mismatch: wrote {x} x {y}, read {actual.X} x {actual.Y}.");
            }
            return actual;
        }
        catch (Exception exception) when (
            IsExpectedHardwareException(exception) || exception is OperationCanceledException)
        {
            var restored = await TryRestoreViperDpiAsync(viper, original);
            var message = "DPI update failed: " + exception.Message + " " +
                (restored ? "The original value was restored." : "Original value restoration failed; check DPI immediately.");
            if (exception is OperationCanceledException)
            {
                throw new OperationCanceledException(message, exception, cancellationToken);
            }
            throw new InvalidOperationException(message, exception);
        }
    }

    public async ValueTask<ViperDpiStagesTelemetry> SetViperDpiStagesAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        ViperDpiStagesTelemetry stages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stages);
        var requested = ToProtocolState(stages);
        var viper = FindReadyDevice(devices, "viper-184")
            ?? throw new InvalidOperationException("The Viper control channel is unavailable.");
        EnsureValidated(_validatedViperDpiStagesPath, viper.Descriptor.Id, "Read the mouse DPI stages successfully first.");

        var original = await ReadViperDpiStagesAsync(viper, cancellationToken);
        if (AreDpiStagesEqual(original, requested))
        {
            return ToTelemetry(original);
        }

        try
        {
            await QueryBuiltRequestAsync(
                viper,
                "dpi-stages.set",
                ViperProduct184Protocol.CreateSetDpiStagesRequest(requested),
                cancellationToken);
            var actual = await ReadViperDpiStagesAsync(viper, cancellationToken);
            EnsureDpiStagesEqual(requested, actual, "mouse DPI stages");
            return ToTelemetry(actual);
        }
        catch (Exception exception) when (
            IsExpectedHardwareException(exception) || exception is OperationCanceledException)
        {
            var restored = await TryRestoreViperDpiStagesAsync(viper, original);
            var message = "DPI-stage update failed: " + exception.Message + " " +
                (restored ? "The original value was restored." : "Original value restoration failed; check the DPI stages immediately.");
            if (exception is OperationCanceledException)
            {
                throw new OperationCanceledException(message, exception, cancellationToken);
            }
            throw new InvalidOperationException(message, exception);
        }
    }

    public async ValueTask<int> SetViperIdleSecondsAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        int seconds,
        CancellationToken cancellationToken = default)
    {
        if (seconds is < 60 or > 900 || seconds % 60 != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds), "Idle timeout must be a whole number of minutes between 1 and 15.");
        }

        var viper = FindReadyDevice(devices, "viper-184")
            ?? throw new InvalidOperationException("The Viper control channel is unavailable.");
        EnsureValidated(_validatedViperIdlePath, viper.Descriptor.Id, "Read the mouse idle timeout successfully first.");
        var original = await ReadViperIdleSecondsAsync(viper, cancellationToken);
        if (original == seconds)
        {
            return original;
        }

        try
        {
            await WriteViperIdleSecondsAsync(viper, seconds, cancellationToken);
            var actual = await ReadViperIdleSecondsAsync(viper, cancellationToken);
            if (actual != seconds)
            {
                throw new InvalidOperationException($"Idle-timeout readback mismatch: wrote {seconds} seconds, read {actual} seconds.");
            }
            return actual;
        }
        catch (Exception exception) when (
            IsExpectedHardwareException(exception) || exception is OperationCanceledException)
        {
            var restored = await TryRestoreViperIdleSecondsAsync(viper, original);
            var message = "Idle-timeout update failed: " + exception.Message + " " +
                (restored ? "The original value was restored." : "Original value restoration failed; check the idle timeout immediately.");
            if (exception is OperationCanceledException)
            {
                throw new OperationCanceledException(message, exception, cancellationToken);
            }
            throw new InvalidOperationException(message, exception);
        }
    }

    public async ValueTask<byte> SetViperBatteryChemistryAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        byte chemistry,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(typeof(ViperBatteryChemistry), chemistry))
        {
            throw new ArgumentOutOfRangeException(nameof(chemistry));
        }

        var viper = FindReadyDevice(devices, "viper-184")
            ?? throw new InvalidOperationException("The Viper control channel is unavailable.");
        await QueryBuiltRequestAsync(
            viper,
            "battery-chemistry.set",
            ViperProduct184Protocol.CreateSetBatteryChemistryRequest(
                (ViperBatteryChemistry)chemistry),
            cancellationToken);
        return chemistry;
    }

    private async Task<int> ReadViperPollingRateAsync(
        ReadyDevice device,
        CancellationToken cancellationToken)
    {
        var response = await QueryCapabilityAsync(device, "polling-rate.get", cancellationToken);
        return ViperProduct184Protocol.ParsePollingRateHertz(
            response, CreateCapabilityRequest(device, "polling-rate.get"));
    }

    private Task WriteViperPollingRateAsync(
        ReadyDevice device,
        int hertz,
        CancellationToken cancellationToken) =>
        QueryBuiltRequestAsync(
            device,
            "polling-rate.set",
            ViperProduct184Protocol.CreateSetPollingRateRequest(hertz),
            cancellationToken);

    private async Task<bool> TryRestoreViperPollingRateAsync(ReadyDevice device, int original)
    {
        try
        {
            await WriteViperPollingRateAsync(device, original, CancellationToken.None);
            return await ReadViperPollingRateAsync(device, CancellationToken.None) == original;
        }
        catch (Exception exception) when (IsExpectedHardwareException(exception))
        {
            return false;
        }
    }

    private async Task<(int X, int Y)> ReadViperDpiAsync(
        ReadyDevice device,
        CancellationToken cancellationToken)
    {
        var response = await QueryCapabilityAsync(device, "current-dpi.get", cancellationToken);
        return ViperProduct184Protocol.ParseDpi(
            response, CreateCapabilityRequest(device, "current-dpi.get"));
    }

    private Task WriteViperDpiAsync(
        ReadyDevice device,
        int x,
        int y,
        CancellationToken cancellationToken) =>
        QueryBuiltRequestAsync(
            device,
            "current-dpi.set",
            ViperProduct184Protocol.CreateSetDpiRequest(x, y),
            cancellationToken);

    private async Task<bool> TryRestoreViperDpiAsync(
        ReadyDevice device,
        (int X, int Y) original)
    {
        try
        {
            await WriteViperDpiAsync(device, original.X, original.Y, CancellationToken.None);
            return await ReadViperDpiAsync(device, CancellationToken.None) == original;
        }
        catch (Exception exception) when (IsExpectedHardwareException(exception))
        {
            return false;
        }
    }

    private async Task<int> ReadViperIdleSecondsAsync(
        ReadyDevice device,
        CancellationToken cancellationToken)
    {
        var response = await QueryCapabilityAsync(device, "idle-timeout.get", cancellationToken);
        return ViperProduct184Protocol.ParseIdleSeconds(
            response, CreateCapabilityRequest(device, "idle-timeout.get"));
    }

    private Task WriteViperIdleSecondsAsync(
        ReadyDevice device,
        int seconds,
        CancellationToken cancellationToken) =>
        QueryBuiltRequestAsync(
            device,
            "idle-timeout.set",
            ViperProduct184Protocol.CreateSetIdleTimeoutRequest(seconds),
            cancellationToken);

    private async Task<bool> TryRestoreViperIdleSecondsAsync(ReadyDevice device, int original)
    {
        try
        {
            await WriteViperIdleSecondsAsync(device, original, CancellationToken.None);
            return await ReadViperIdleSecondsAsync(device, CancellationToken.None) == original;
        }
        catch (Exception exception) when (IsExpectedHardwareException(exception))
        {
            return false;
        }
    }

    private async Task<ViperDpiStagesState> ReadViperDpiStagesAsync(
        ReadyDevice device,
        CancellationToken cancellationToken)
    {
        var response = await QueryCapabilityAsync(device, "dpi-stages.get", cancellationToken);
        return ViperDpiStagesProtocol.Parse(
            response, CreateCapabilityRequest(device, "dpi-stages.get"));
    }

    private async Task<bool> TryRestoreViperDpiStagesAsync(
        ReadyDevice device,
        ViperDpiStagesState original)
    {
        try
        {
            await QueryBuiltRequestAsync(
                device,
                "dpi-stages.set",
                ViperProduct184Protocol.CreateSetDpiStagesRequest(original),
                CancellationToken.None);
            var restored = await ReadViperDpiStagesAsync(device, CancellationToken.None);
            EnsureDpiStagesEqual(original, restored, "DPI-stage restore");
            return true;
        }
        catch (Exception exception) when (IsExpectedHardwareException(exception))
        {
            return false;
        }
    }

    private static ViperDpiStagesState ToProtocolState(ViperDpiStagesTelemetry telemetry) =>
        new(
            telemetry.ActiveStage,
            telemetry.Stages.Select(stage => new ViperDpiStage(stage.Number, stage.X, stage.Y)).ToArray());

    private static ViperDpiStagesTelemetry ToTelemetry(ViperDpiStagesState state) =>
        new(
            state.ActiveStage,
            state.Stages.Select(stage => new ViperDpiStageTelemetry(stage.Number, stage.X, stage.Y)).ToArray());

    private static void EnsureDpiStagesEqual(
        ViperDpiStagesState expected,
        ViperDpiStagesState actual,
        string operation)
    {
        if (expected.ActiveStage != actual.ActiveStage ||
            expected.Stages.Count != actual.Stages.Count ||
            !expected.Stages.SequenceEqual(actual.Stages))
        {
            throw new InvalidOperationException(
                $"{operation} readback mismatch: wrote {ViperDpiStagesProtocol.Format(expected)}, " +
                $"read {ViperDpiStagesProtocol.Format(actual)}.");
        }
    }

    private static bool AreDpiStagesEqual(
        ViperDpiStagesState left,
        ViperDpiStagesState right) =>
        left.ActiveStage == right.ActiveStage &&
        left.Stages.Count == right.Stages.Count &&
        left.Stages.SequenceEqual(right.Stages);

    private Task<byte[]> QueryCapabilityAsync(
        ReadyDevice device,
        string capabilityId,
        CancellationToken cancellationToken) =>
        QueryCapabilityAsync(
            device,
            capabilityId,
            device.Manifest.GetRequiredCapability(capabilityId).Arguments,
            cancellationToken);

    private Task<byte[]> QueryCapabilityAsync(
        ReadyDevice device,
        string capabilityId,
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken,
        byte? dataSize = null)
    {
        var request = device.Manifest.GetRequiredCapability(capabilityId);
        var actualDataSize = dataSize ?? request.MaximumDataSize;
        if (actualDataSize > request.MaximumDataSize || arguments.Length > actualDataSize)
        {
            throw new InvalidOperationException(
                $"Dynamic arguments for capability '{capabilityId}' exceed the manifest limit.");
        }

        return _transport.QueryAsync(
            device.Descriptor.Id,
            request.TransactionId,
            actualDataSize,
            request.CommandClass,
            request.CommandId,
            arguments,
            request.Wait,
            cancellationToken,
            request.AllowRemainingPacketsMismatch);
    }

    private Task<byte[]> QueryBuiltRequestAsync(
        ReadyDevice device,
        string capabilityId,
        byte[] builtRequest,
        CancellationToken cancellationToken)
    {
        var request = CreateConfiguredRequest(device, capabilityId, builtRequest);

        return QueryCapabilityAsync(
            device,
            capabilityId,
            request.AsMemory(RazerFeatureReport.ArgumentsOffset, request[6]),
            cancellationToken,
            request[6]);
    }

    private static byte[] CreateCapabilityRequest(ReadyDevice device, string capabilityId) =>
        device.Manifest.GetRequiredCapability(capabilityId).CreateRequest();

    private static byte[] CreateConfiguredRequest(
        ReadyDevice device,
        string capabilityId,
        byte[] builtRequest)
    {
        if (builtRequest.Length != RazerFeatureReport.Length)
        {
            throw new InvalidOperationException("The strongly typed builder returned an invalid feature-report length.");
        }

        return device.Manifest.GetRequiredCapability(capabilityId).CreateRequest(
            builtRequest.AsSpan(RazerFeatureReport.ArgumentsOffset, builtRequest[6]),
            builtRequest[6]);
    }

    private ReadyDevice? FindReadyDevice(
        IReadOnlyList<DeviceDescriptor> devices,
        string protocolFamily)
    {
        foreach (var device in devices)
        {
            if (device.Access != DeviceAccessState.Available ||
                device.Capability != DeviceCapabilityState.PendingValidation)
            {
                continue;
            }

            var manifest = _registry.Find(device.VendorId, device.ProductId);
            if (manifest?.ProtocolFamily == protocolFamily)
            {
                return new ReadyDevice(device, manifest);
            }
        }

        return null;
    }

    private static void EnsureDataSize(byte[] response, byte minimum, string query)
    {
        if (response[6] < minimum)
        {
            throw new InvalidOperationException($"{query} response is too short: {response[6]} < {minimum}.");
        }
    }

    private static void EnsureValidated(string? validatedPath, string currentPath, string message)
    {
        if (!StringComparer.OrdinalIgnoreCase.Equals(validatedPath, currentPath))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static bool IsExpectedHardwareException(Exception exception) =>
        exception is Win32Exception or IOException or UnauthorizedAccessException or
        InvalidOperationException or NotSupportedException or ArgumentException;

    private sealed record ReadyDevice(
        DeviceDescriptor Descriptor,
        RazerDeviceManifest Manifest);

    private sealed record BladeLogoState(
        bool Powered,
        BladeLogoMode PoweredMode)
    {
        internal BladeLogoMode CombinedMode => BladeLogoProtocol.Combine(Powered, PoweredMode);
    }

}
