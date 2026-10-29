using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Displays;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

internal sealed class BladeTelemetryReader
{
    private readonly RazerCapabilityClient _client;
    private readonly BladeValidationState _validation = new();
    private readonly BladeFanTelemetryReader _fan;
    private readonly BladePowerTelemetryReader _power;
    private readonly BladeDisplayTelemetryReader _display;

    public BladeTelemetryReader(RazerDeviceResolver resolver, RazerCapabilityClient client)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _fan = new BladeFanTelemetryReader(resolver, client);
        _power = new BladePowerTelemetryReader(resolver, client, _validation, _fan);
        _display = new BladeDisplayTelemetryReader(resolver, client, _validation);
    }

    public async ValueTask<BladeTelemetryReadResult> ReadAsync(
        ResolvedRazerDevice blade,
        CancellationToken cancellationToken = default)
    {
        ResetValidation();
        byte? bladeBrightness = null;
        BladePerformanceMode? bladePerformanceMode = null;
        BladeLighting2023Effect? bladeKeyboardEffect = null;
        BladeFanMode? bladeFanMode = null;
        int? bladeFanTargetRpm = null;
        int? bladeChargeLimitPercent = null;
        BladeCpuBoostMode? bladeCpuBoostMode = null;
        BladeGpuBoostMode? bladeGpuBoostMode = null;
        BladeMaxFanMode? bladeMaxFanMode = null;
        bool? bladeCpuPowerBoostEnabled = null;
        int? bladeCurrentFanCpuRpm = null;
        int? bladeCurrentFanGpuRpm = null;
        byte? bladeAdvancedFanCpuModeRaw = null;
        byte? bladeAdvancedFanGpuModeRaw = null;
        BladeLogoMode? bladeLogoMode = null;
        BladeGameModeTelemetry? bladeGameMode = null;
        bool? bladeStartupAnimationEnabled = null;
        BladeNativeDisplayMode? bladeNativeDisplayMode = null;
        BladeSkuHardwareConfiguration? bladeSkuHardwareConfiguration = null;
        bool? bladeOneTimeFullChargeEnabled = null;
        bool? bladeLocalDimmingEnabled = null;
        bool? bladeFnKeyMultimediaPrimary = null;
        string? bladeMcuVersion = null;
        BladeAdapterPowerSnapshot? bladeAdapterPower = null;
        var errors = new List<string>();
        // Capabilities absent from this model's manifest are not attempted, so a
        // Blade family that never owned a report never logs a phantom error.
        var owns = blade.Owns;
        var bladeThermalReadSucceeded = false;
        if (owns(CapabilityIds.KeyboardBrightnessGet))
        {
            try
            {
                // The two Blade families report brightness in different argument
                // positions, so each uses its own parser instead of a shared offset.
                bladeBrightness = await _display.ReadBrightnessAsync(blade, cancellationToken);
                _validation.BrightnessPath = blade.Descriptor.Id;
            }
            catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
            {
                errors.Add($"Keyboard brightness: {exception.Message}");
            }
        }

        if (owns(CapabilityIds.KeyboardEffectGet))
        {
            try
            {
                bladeKeyboardEffect = (await _client.QueryAsync(
                    blade, CapabilityIds.KeyboardEffectGet, cancellationToken)) is { } effectResponse
                    ? BladeLighting2023Protocol.ParseEffect(effectResponse)
                    : null;
                _validation.KeyboardEffectPath = blade.Descriptor.Id;
            }
            catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
            {
                errors.Add($"Keyboard effect: {exception.Message}");
            }
        }

        // The performance mode and the fan mode travel in one report, but they are
        // independent facts. A model whose firmware reports a performance mode the
        // contract does not know must not lose its fan controls, so each is read on
        // its own and a failure of one does not hide the other.
        IReadOnlyList<byte[]>? thermalReports = null;
        try
        {
            thermalReports = await _fan.ReadThermalReportsAsync(blade, cancellationToken);
        }
        catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
        {
            errors.Add($"Performance and fan state: {exception.Message}");
        }

        if (thermalReports is not null)
        {
            BladePerformanceMode? performanceMode = null;
            BladeFanMode? fanMode = null;
            var performanceBytes = new HashSet<byte>();
            var fanBytes = new HashSet<byte>();
            foreach (var report in thermalReports)
            {
                var performanceByte = report[RazerFeatureReport.ArgumentsOffset + 2];
                var fanByte = report[RazerFeatureReport.ArgumentsOffset + 3];
                performanceBytes.Add(performanceByte);
                fanBytes.Add(fanByte);
                try
                {
                    performanceMode = ParseBladePerformanceMode(performanceByte);
                }
                catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
                {
                    errors.Add($"Performance mode: {exception.Message}");
                }

                try
                {
                    fanMode = ParseBladeFanMode(fanByte);
                }
                catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
                {
                    errors.Add($"Fan mode: {exception.Message}");
                }
            }

            // The two zones describe one machine, so a split between them is a
            // fault worth reporting even when each byte decodes on its own.
            if (performanceBytes.Count > 1)
            {
                errors.Add("Blade fan zones report different performance modes.");
            }

            if (fanBytes.Count > 1)
            {
                errors.Add("Blade fan zones report different fan modes.");
            }

            bladePerformanceMode = performanceMode;
            bladeFanMode = fanMode;
            if (performanceMode is not null)
            {
                _validation.PerformancePath = blade.Descriptor.Id;
                bladeThermalReadSucceeded = true;
            }
        }

        if (owns(CapabilityIds.FanTargetGet))
        {
            try
            {
                // Always attempt the target read: some models keep a manual fan target
                // without ever reporting the fan mode as manual. The value is only used
                // in manual mode, so a failure elsewhere is expected, not an error.
                var targetRpm = await _fan.ReadFanTargetRpmAsync(blade, cancellationToken);
                if (bladeFanMode == BladeFanMode.Manual)
                {
                    bladeFanTargetRpm = targetRpm;
                }
            }
            catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
            {
                if (bladeFanMode == BladeFanMode.Manual)
                {
                    errors.Add($"Fan target: {exception.Message}");
                }
            }
        }

        if (owns(CapabilityIds.CurrentFanRpmGet))
        {
            try
            {
                bladeCurrentFanCpuRpm = await _fan.ReadCurrentFanRpmAsync(
                    blade, BladeThermalProtocol.CpuFanId, cancellationToken);
            }
            catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
            {
                errors.Add($"Current CPU fan speed: {exception.Message}");
            }

            try
            {
                bladeCurrentFanGpuRpm = await _fan.ReadCurrentFanRpmAsync(
                    blade, BladeThermalProtocol.GpuFanId, cancellationToken);
            }
            catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
            {
                errors.Add($"Current GPU fan speed: {exception.Message}");
            }
        }

        if (owns(CapabilityIds.AdvancedFanModeGet))
        {
            try
            {
                bladeAdvancedFanCpuModeRaw = await _fan.ReadAdvancedFanModeAsync(
                    blade, BladeThermalProtocol.CpuFanId, cancellationToken);
            }
            catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
            {
                errors.Add($"CPU advanced fan mode: {exception.Message}");
            }

            try
            {
                bladeAdvancedFanGpuModeRaw = await _fan.ReadAdvancedFanModeAsync(
                    blade, BladeThermalProtocol.GpuFanId, cancellationToken);
            }
            catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
            {
                errors.Add($"GPU advanced fan mode: {exception.Message}");
            }
        }

        if (bladeThermalReadSucceeded && owns(CapabilityIds.BoostGet))
        {
            try
            {
                var boostState = await _power.ReadBoostStateAsync(blade, cancellationToken);
                bladeCpuBoostMode = boostState.Cpu;
                bladeGpuBoostMode = boostState.Gpu;
                _validation.BoostPath = blade.Descriptor.Id;
            }
            catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
            {
                errors.Add($"CPU/GPU Boost: {exception.Message}");
            }
        }

        if (owns(CapabilityIds.ChargeLimitGet))
        {
            try
            {
                bladeChargeLimitPercent = await BladePowerTelemetryShared.ReadChargeLimitAsync(
                    _client, blade, cancellationToken);
                _validation.ChargeLimitPath = blade.Descriptor.Id;
            }
            catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
            {
                errors.Add($"Charge limit: {exception.Message}");
            }
        }

        if (owns(CapabilityIds.PowerModeGet))
        {
            try
            {
                var powerModeMask = await _power.ReadPowerModeMaskAsync(blade, cancellationToken);
                // Synapse shares this bitmask across models, so a readable Max Fan bit
                // is not evidence of support. Only an audited model may expose it.
                if (blade.SupportsMaxFan)
                {
                    bladeMaxFanMode = BladePowerTelemetryShared.ToMaxFanMode(powerModeMask);
                }
                if (blade.SupportsCpuPowerBoost)
                {
                    bladeCpuPowerBoostEnabled =
                        (powerModeMask & BladePowerModeProtocol.CpuPowerBoostBit) != 0;
                }
                bladeOneTimeFullChargeEnabled =
                    (powerModeMask & BladePowerModeProtocol.OneTimeFullChargeBit) != 0;
                bladeLocalDimmingEnabled =
                    (powerModeMask & BladePowerModeProtocol.LocalDimmingBit) != 0;
                _validation.PowerModePath = blade.Descriptor.Id;
            }
            catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
            {
                errors.Add($"Power Mode Control: {exception.Message}");
            }
        }

        if (owns(CapabilityIds.GamingModeGet))
        {
            try
            {
                var response = await _client.QueryAsync(
                    blade, CapabilityIds.GamingModeGet, cancellationToken);
                bladeGameMode = BladeSynapsePolicyProtocol.ParseGameMode(
                    response, BladeSynapsePolicyProtocol.CreateGetGameModeRequest());
            }
            catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
            {
                errors.Add($"Gaming mode: {exception.Message}");
            }
        }

        if (owns(CapabilityIds.StartupAnimationGet))
        {
            try
            {
                bladeStartupAnimationEnabled =
                    (await _display.ReadStartupAnimationAsync(blade, cancellationToken)).Enabled;
                _validation.StartupAnimationPath = blade.Descriptor.Id;
            }
            catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
            {
                errors.Add($"Startup animation: {exception.Message}");
            }
        }

        if (owns(CapabilityIds.FnKeyGet))
        {
            try
            {
                var response = await _client.QueryAsync(blade, CapabilityIds.FnKeyGet, cancellationToken);
                bladeFnKeyMultimediaPrimary = BladeSynapsePolicyProtocol.ParseFnKeyState(
                    response, RazerCapabilityClient.CreateRequest(blade, CapabilityIds.FnKeyGet)).MultiFunctionPrimary;
            }
            catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
            {
                errors.Add($"Fn row behavior: {exception.Message}");
            }
        }

        if (owns(CapabilityIds.McuVersionGet))
        {
            try
            {
                var response = await _client.QueryAsync(blade, CapabilityIds.McuVersionGet, cancellationToken);
                bladeMcuVersion = BladeMcuVersionProtocol.Parse(
                    response, RazerCapabilityClient.CreateRequest(blade, CapabilityIds.McuVersionGet));
            }
            catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
            {
                errors.Add($"MCU firmware version: {exception.Message}");
            }
        }

        if (owns(CapabilityIds.AdapterPowerGet))
        {
            try
            {
                var response = await _client.QueryAsync(blade, CapabilityIds.AdapterPowerGet, cancellationToken);
                bladeAdapterPower = BladeAdapterPowerProtocol.Parse(
                    response, RazerCapabilityClient.CreateRequest(blade, CapabilityIds.AdapterPowerGet));
            }
            catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
            {
                errors.Add($"Adapter power: {exception.Message}");
            }
        }

        if (owns(CapabilityIds.NativeDisplayModeGet))
        {
            try
            {
                bladeNativeDisplayMode = await _display.ReadNativeDisplayModeAsync(blade, cancellationToken);
                _validation.NativeDisplayModePath = blade.Descriptor.Id;
            }
            catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
            {
                errors.Add($"Native display mode: {exception.Message}");
            }
        }

        if (owns(CapabilityIds.SkuHardwareConfigurationGet))
        {
            try
            {
                var response = await _client.QueryAsync(
                    blade, CapabilityIds.SkuHardwareConfigurationGet, cancellationToken);
                bladeSkuHardwareConfiguration = BladeProduct710Protocol.ParseSkuHardwareConfiguration(
                    response, RazerCapabilityClient.CreateRequest(blade, CapabilityIds.SkuHardwareConfigurationGet));
                if (bladeSkuHardwareConfiguration.Value.MiniLedResolution)
                {
                    _validation.LocalDimmingPath = _validation.PowerModePath;
                }
                else
                {
                    bladeLocalDimmingEnabled = null;
                }
            }
            catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
            {
                errors.Add($"SKU hardware configuration: {exception.Message}");
            }
        }

        // 07:8F is shared and can answer even on a non-MiniLED panel; only a
        // successful SKU report may promote local dimming to a supported control.
        if (bladeSkuHardwareConfiguration?.MiniLedResolution != true)
        {
            bladeLocalDimmingEnabled = null;
        }

        try
        {
            if (owns(CapabilityIds.LogoPowerGet))
            {
                bladeLogoMode = (await _display.ReadLogoStateAsync(blade, cancellationToken)).CombinedMode;
                _validation.LogoPath = blade.Descriptor.Id;
            }
        }
        catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
        {
            errors.Add($"Blade Logo: {exception.Message}");
        }

        return new BladeTelemetryReadResult(
            bladeBrightness,
            bladePerformanceMode,
            bladeKeyboardEffect,
            bladeFanMode,
            bladeFanTargetRpm,
            bladeChargeLimitPercent,
            bladeCpuBoostMode,
            bladeGpuBoostMode,
            bladeMaxFanMode,
            bladeCpuPowerBoostEnabled,
            bladeCurrentFanCpuRpm,
            bladeCurrentFanGpuRpm,
            bladeAdvancedFanCpuModeRaw,
            bladeAdvancedFanGpuModeRaw,
            bladeLogoMode,
            bladeGameMode,
            bladeStartupAnimationEnabled,
            bladeNativeDisplayMode,
            bladeSkuHardwareConfiguration,
            bladeOneTimeFullChargeEnabled,
            bladeLocalDimmingEnabled,
            bladeFnKeyMultimediaPrimary,
            bladeMcuVersion,
            bladeAdapterPower,
            errors);
    }

    public void ResetValidation() => _validation.Reset();

    public ValueTask<byte> SetBladeKeyboardBrightnessAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        byte brightness,
        CancellationToken cancellationToken = default,
        bool verifyReadback = true) =>
        _display.SetBladeKeyboardBrightnessAsync(
            devices, brightness, cancellationToken, verifyReadback);

    public ValueTask<byte> ReadBladeKeyboardBrightnessAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        CancellationToken cancellationToken = default) =>
        _display.ReadBladeKeyboardBrightnessAsync(devices, cancellationToken);

    public ValueTask<BladeLighting2023Effect> SetBlade2023KeyboardEffectAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeLighting2023Effect effect,
        CancellationToken cancellationToken = default) =>
        _display.SetBlade2023KeyboardEffectAsync(devices, effect, cancellationToken);

    public ValueTask<BladePerformanceMode> SetBladePerformanceModeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladePerformanceMode mode,
        CancellationToken cancellationToken = default,
        bool coolingPadConnected = false) =>
        _power.SetBladePerformanceModeAsync(devices, mode, cancellationToken, coolingPadConnected);

    public ValueTask<BladeGameModeTelemetry> SetBladeGameModeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        bool enabled,
        CancellationToken cancellationToken = default) =>
        _power.SetBladeGameModeAsync(devices, enabled, cancellationToken);

    public ValueTask<bool> SetBladeFnKeyStateAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        bool multiFunctionPrimary,
        CancellationToken cancellationToken = default) =>
        _power.SetBladeFnKeyStateAsync(devices, multiFunctionPrimary, cancellationToken);

    public ValueTask<BladeFanControlState> SetBladeFanAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeFanMode mode,
        int? cpuTargetRpm,
        int? gpuTargetRpm,
        CancellationToken cancellationToken = default) =>
        _fan.SetBladeFanAsync(devices, mode, cpuTargetRpm, gpuTargetRpm, cancellationToken);

    public ValueTask<BladeFanControlSnapshot> ReadBladeFanControlStateAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        CancellationToken cancellationToken = default) =>
        _fan.ReadBladeFanControlStateAsync(devices, cancellationToken);

    public ValueTask<BladeFanControlSnapshot> SetBladeFanTargetsAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeFanMode mode,
        int cpuTargetRpm,
        int gpuTargetRpm,
        CancellationToken cancellationToken = default) =>
        _fan.SetBladeFanTargetsAsync(devices, mode, cpuTargetRpm, gpuTargetRpm, cancellationToken);

    public ValueTask<BladeCpuBoostMode> SetBladeCpuBoostModeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeCpuBoostMode mode,
        CancellationToken cancellationToken = default) =>
        _power.SetBladeCpuBoostModeAsync(devices, mode, cancellationToken);

    public ValueTask<BladeGpuBoostMode> SetBladeGpuBoostModeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeGpuBoostMode mode,
        CancellationToken cancellationToken = default) =>
        _power.SetBladeGpuBoostModeAsync(devices, mode, cancellationToken);

    public ValueTask<BladeMaxFanMode> SetBladeMaxFanModeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeMaxFanMode mode,
        CancellationToken cancellationToken = default) =>
        _power.SetBladeMaxFanModeAsync(devices, mode, cancellationToken);

    public ValueTask<bool> SetBladeOneTimeFullChargeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        bool enabled,
        CancellationToken cancellationToken = default) =>
        _power.SetBladeOneTimeFullChargeAsync(devices, enabled, cancellationToken);

    public ValueTask<bool> SetBladeLocalDimmingAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        bool enabled,
        CancellationToken cancellationToken = default) =>
        _power.SetBladeLocalDimmingAsync(devices, enabled, cancellationToken);

    public ValueTask<bool> SetBladeCpuPowerBoostAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        bool enabled,
        CancellationToken cancellationToken = default) =>
        _power.SetBladeCpuPowerBoostAsync(devices, enabled, cancellationToken);

    public ValueTask<bool> SetBladeStartupAnimationAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        bool enabled,
        CancellationToken cancellationToken = default) =>
        _display.SetBladeStartupAnimationAsync(devices, enabled, cancellationToken);

    public ValueTask<BladeNativeDisplayMode> SetBladeNativeDisplayModeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeNativeDisplayMode mode,
        CancellationToken cancellationToken = default) =>
        _display.SetBladeNativeDisplayModeAsync(devices, mode, cancellationToken);

    public ValueTask<BladeDisplayMuxStatus> ReadBladeDisplayMuxStatusAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        CancellationToken cancellationToken = default) =>
        _display.ReadBladeDisplayMuxStatusAsync(devices, cancellationToken);

    public ValueTask<BladeDisplayMuxSwitchResult> SetBladeDisplayMuxModeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeDisplayMuxMode mode,
        CancellationToken cancellationToken = default) =>
        _display.SetBladeDisplayMuxModeAsync(devices, mode, cancellationToken);

    public ValueTask<int> SetBladeChargeLimitAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        int percent,
        CancellationToken cancellationToken = default) =>
        _power.SetBladeChargeLimitAsync(devices, percent, cancellationToken);

    public ValueTask<BladeLogoMode> SetBladeLogoModeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeLogoMode mode,
        CancellationToken cancellationToken = default) =>
        _display.SetBladeLogoModeAsync(devices, mode, cancellationToken);

    internal static BladePerformanceMode ParseBladePerformanceMode(byte value) =>
        BladeFanTelemetryReader.ParsePerformanceMode(value);

    private static BladeFanMode ParseBladeFanMode(byte value) =>
        BladeFanTelemetryReader.ParseFanMode(value);

    internal static int DecodeBladeChargeLimit(byte value) =>
        BladePowerTelemetryShared.DecodeChargeLimit(value);

    internal static void EnsureControlWritesAdmitted(ResolvedRazerDevice blade)
    {
        if (!blade.AllowsControlWrites)
        {
            throw new InvalidOperationException(
                "Control writes are not admitted for this Blade model; only its read-only status reports are enabled.");
        }
    }
}

internal sealed record BladeTelemetryReadResult(
    byte? BladeKeyboardBrightness,
    BladePerformanceMode? BladePerformanceMode,
    BladeLighting2023Effect? BladeKeyboardEffect,
    BladeFanMode? BladeFanMode,
    int? BladeFanTargetRpm,
    int? BladeChargeLimitPercent,
    BladeCpuBoostMode? BladeCpuBoostMode,
    BladeGpuBoostMode? BladeGpuBoostMode,
    BladeMaxFanMode? BladeMaxFanMode,
    bool? BladeCpuPowerBoostEnabled,
    int? BladeCurrentFanCpuRpm,
    int? BladeCurrentFanGpuRpm,
    byte? BladeAdvancedFanCpuModeRaw,
    byte? BladeAdvancedFanGpuModeRaw,
    BladeLogoMode? BladeLogoMode,
    BladeGameModeTelemetry? BladeGameMode,
    bool? BladeStartupAnimationEnabled,
    BladeNativeDisplayMode? BladeNativeDisplayMode,
    BladeSkuHardwareConfiguration? BladeSkuHardwareConfiguration,
    bool? BladeOneTimeFullChargeEnabled,
    bool? BladeLocalDimmingEnabled,
    bool? BladeFnKeyMultimediaPrimary,
    string? BladeMcuVersion,
    BladeAdapterPowerSnapshot? BladeAdapterPower,
    IReadOnlyList<string> Errors);
