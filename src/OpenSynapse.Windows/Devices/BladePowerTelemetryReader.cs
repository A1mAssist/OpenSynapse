using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Displays;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

internal sealed class BladePowerTelemetryReader
{
    private readonly RazerDeviceResolver _resolver;
    private readonly RazerCapabilityClient _client;
    private readonly BladeValidationState _validation;
    private readonly BladeFanTelemetryReader _fan;
    private readonly SemaphoreSlim _bladePowerModeTransactionGate = new(1, 1);

    public BladePowerTelemetryReader(
        RazerDeviceResolver resolver,
        RazerCapabilityClient client,
        BladeValidationState validation,
        BladeFanTelemetryReader fan)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _validation = validation ?? throw new ArgumentNullException(nameof(validation));
        _fan = fan ?? throw new ArgumentNullException(nameof(fan));
    }

    public async ValueTask<BladePerformanceMode> SetBladePerformanceModeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladePerformanceMode mode,
        CancellationToken cancellationToken = default,
        bool coolingPadConnected = false)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        var blade = _resolver.FindReadyBlade(devices)
            ?? throw new InvalidOperationException("The Blade platform control channel is unavailable.");
        BladeTelemetryReader.EnsureControlWritesAdmitted(blade);
        RazerCapabilityClient.EnsureValidated(_validation.PerformancePath, blade.Descriptor.Id, "Read the Blade performance mode successfully first.");

        var original = await _fan.ReadThermalStateAsync(blade, cancellationToken);
        // HyperBoost moves the EC to its own preset, so the CPU/GPU level goes with it:
        // the preset would otherwise run on whatever the previous mode left behind.
        // The EC keeps a level per mode, so leaving the preset applies that mode's own
        // values; writing the captured ones back would overwrite them with a stranger's.
        var raiseLevels = mode == BladePerformanceMode.Hyperboost && blade.SupportsCpuBoost;
        BladeBoostState? originalBoost = null;
        if (raiseLevels)
        {
            RazerCapabilityClient.EnsureValidated(_validation.BoostPath, blade.Descriptor.Id, "Read the Blade CPU level successfully first.");
            originalBoost = await ReadBoostStateAsync(blade, cancellationToken);
        }

        try
        {
            await EnsureMaxFanOffBeforeLeavingCustomAsync(blade, mode, cancellationToken);
            // The 028C EC keeps a stale manual flag when a preset leaves Custom mode, so
            // the fans must go back to firmware automatic instead of carrying it over.
            var fanMode = blade.FixedFanRequiresCustomMode &&
                mode != BladePerformanceMode.Custom
                    ? BladeFanMode.Automatic
                    : original.FanMode;
            await _fan.WriteThermalZoneAsync(blade, 0x01, mode, fanMode, cancellationToken);
            await _fan.WriteThermalZoneAsync(blade, 0x02, mode, fanMode, cancellationToken);

            BladeBoostState? expectedBoost = null;
            if (raiseLevels)
            {
                expectedBoost = new BladeBoostState(
                    blade.MaximumCpuBoostMode
                        ?? throw new InvalidOperationException("This Blade model has no admitted CPU level."),
                    BladeGpuBoostMode.High);
            }

            if (expectedBoost is { } levels)
            {
                await WriteBoostModeAsync(blade, BladeBoostProtocol.CpuCluster, (byte)levels.Cpu, cancellationToken);
                await WriteBoostModeAsync(blade, BladeBoostProtocol.GpuCluster, (byte)levels.Gpu, cancellationToken);
            }

            // With the pad attached the pad and the EC own the airflow, so the fans stay
            // on their own curve. Without it the pin is the only thing that keeps the
            // raised budget from turning straight into throttling.
            if (mode == BladePerformanceMode.Hyperboost && !coolingPadConnected)
            {
                await ApplyMaxFanForHyperBoostAsync(blade, cancellationToken);
            }

            var actual = await _fan.ReadThermalStateAsync(blade, cancellationToken);
            var actualBoost = expectedBoost is null
                ? null
                : await ReadBoostStateAsync(blade, cancellationToken);
            if (actual.PerformanceModeByte != (byte)mode || actual.FanMode != fanMode ||
                (expectedBoost is { } expected && actualBoost != expected))
            {
                throw new InvalidOperationException(
                    $"Performance mode readback mismatch: wrote {mode} / {fanMode}" +
                    (expectedBoost is { } target ? $" / CPU {target.Cpu} / GPU {target.Gpu}" : string.Empty) +
                    $", read {actual.PerformanceMode} / {actual.FanMode}" +
                    (actualBoost is { } read ? $" / CPU {read.Cpu} / GPU {read.Gpu}" : string.Empty) + ".");
            }

            return actual.PerformanceMode;
        }
        catch (Exception exception) when (
            RazerCapabilityClient.IsExpectedHardwareException(exception) || exception is OperationCanceledException)
        {
            var restored = await _fan.TryRestoreThermalStateAsync(blade, original);
            if (originalBoost is { } boost)
            {
                restored &= await TryRestoreBoostLevelsAsync(blade, boost);
            }
            var message = $"Performance mode update failed: {exception.Message} " +
                (restored
                    ? "The original state was restored."
                    : "Original state restoration failed; check the fan zones and power levels immediately.");
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
        RunPowerModeTransactionAsync(
            () => SetBladeLocalDimmingCoreAsync(devices, enabled, cancellationToken),
            cancellationToken);

    private async ValueTask<bool> SetBladeLocalDimmingCoreAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var blade = _resolver.FindReadyBlade(devices)
            ?? throw new InvalidOperationException("The Blade Local Dimming control channel is unavailable.");
        RazerCapabilityClient.EnsureValidated(
            _validation.LocalDimmingPath,
            blade.Descriptor.Id,
            "Local Dimming is available only on a confirmed MiniLED panel.");

        var originalMask = await ReadPowerModeMaskAsync(blade, cancellationToken);
        var original = (originalMask & BladePowerModeProtocol.LocalDimmingBit) != 0;
        if (original == enabled)
        {
            return original;
        }

        var expectedMask = enabled
            ? (byte)(originalMask | BladePowerModeProtocol.LocalDimmingBit)
            : (byte)(originalMask & ~BladePowerModeProtocol.LocalDimmingBit);
        try
        {
            await WritePowerModeMaskAsync(blade, expectedMask, cancellationToken);
            var actualMask = await ReadPowerModeMaskAsync(blade, cancellationToken);
            if (actualMask != expectedMask)
            {
                throw new InvalidOperationException(
                    $"Local Dimming readback mismatch: wrote 0x{expectedMask:X2}, read 0x{actualMask:X2}.");
            }

            return enabled;
        }
        catch (Exception exception) when (
            RazerCapabilityClient.IsExpectedHardwareException(exception) || exception is OperationCanceledException)
        {
            var restored = await TryRestorePowerModeMaskAsync(blade, originalMask);
            var message = "Local Dimming update failed: " + exception.Message + " " +
                (restored ? "The original value was restored." : "Original value restoration failed; check the display settings immediately.");
            if (exception is OperationCanceledException)
            {
                throw new OperationCanceledException(message, exception, cancellationToken);
            }

            throw new InvalidOperationException(message, exception);
        }
    }

    public ValueTask<bool> SetBladeCpuPowerBoostAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        bool enabled,
        CancellationToken cancellationToken = default) =>
        RunPowerModeTransactionAsync(
            () => SetBladeCpuPowerBoostCoreAsync(devices, enabled, cancellationToken),
            cancellationToken);

    /// <summary>
    /// Power Mode Control bit 0. Synapse calls it `cpuBoost`: it lets the CPU draw more
    /// power while the GPU is idle. 07:0F carries all four bits, so the write is a
    /// read-modify-write and the readback must match the full byte.
    /// </summary>
    private async ValueTask<bool> SetBladeCpuPowerBoostCoreAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var blade = _resolver.FindReadyBlade(devices)
            ?? throw new InvalidOperationException("The Blade CPU Boost control channel is unavailable.");
        if (!blade.SupportsCpuPowerBoost)
        {
            throw new InvalidOperationException("CPU Boost is not available on this Blade model.");
        }
        RazerCapabilityClient.EnsureValidated(
            _validation.PowerModePath,
            blade.Descriptor.Id,
            "Read the Blade Power Mode Control state successfully first.");

        var originalMask = await ReadPowerModeMaskAsync(blade, cancellationToken);
        var original = (originalMask & BladePowerModeProtocol.CpuPowerBoostBit) != 0;
        if (original == enabled)
        {
            return original;
        }

        var expectedMask = BladePowerModeProtocol.ApplyCpuPowerBoost(originalMask, enabled);
        try
        {
            await WritePowerModeMaskAsync(blade, expectedMask, cancellationToken);
            var actualMask = await ReadPowerModeMaskAsync(blade, cancellationToken);
            if (actualMask != expectedMask)
            {
                throw new InvalidOperationException(
                    $"CPU Boost readback mismatch: wrote 0x{expectedMask:X2}, read 0x{actualMask:X2}.");
            }

            return enabled;
        }
        catch (Exception exception) when (
            RazerCapabilityClient.IsExpectedHardwareException(exception) || exception is OperationCanceledException)
        {
            var restored = await TryRestorePowerModeMaskAsync(blade, originalMask);
            var message = "CPU Boost update failed: " + exception.Message + " " +
                (restored ? "The original value was restored." : "Original value restoration failed; check the power settings immediately.");
            if (exception is OperationCanceledException)
            {
                throw new OperationCanceledException(message, exception, cancellationToken);
            }

            throw new InvalidOperationException(message, exception);
        }
    }

    private async ValueTask<T> RunPowerModeTransactionAsync<T>(
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

    public async ValueTask<BladeGameModeTelemetry> SetBladeGameModeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var blade = _resolver.FindReadyBlade(devices)
            ?? throw new InvalidOperationException("The Blade gaming-mode control channel is unavailable.");
        await _client.QueryBuiltRequestAsync(
            blade,
            CapabilityIds.GamingModeSet,
            BladeSynapsePolicyProtocol.CreateSetGameModeRequest(enabled),
            cancellationToken);
        return new(enabled ? (byte)1 : (byte)0, 0, 0);
    }

    public async ValueTask<bool> SetBladeFnKeyStateAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        bool multiFunctionPrimary,
        CancellationToken cancellationToken = default)
    {
        var blade = _resolver.FindReadyBlade(devices)
            ?? throw new InvalidOperationException("The Blade Fn primary-function control channel is unavailable.");
        if (!blade.Owns(CapabilityIds.FnKeySet))
        {
            throw new InvalidOperationException(
                "The Fn primary-function control is not available on this Blade model.");
        }

        var builtRequest = BladeSynapsePolicyProtocol.CreateSetFnKeyStateRequest(multiFunctionPrimary);
        var request = RazerCapabilityClient.CreateConfiguredRequest(blade, CapabilityIds.FnKeySet, builtRequest);
        var response = await _client.QueryAsync(
            blade,
            CapabilityIds.FnKeySet,
            request.AsMemory(RazerFeatureReport.ArgumentsOffset, request[6]),
            cancellationToken,
            request[6]);
        var state = BladeSynapsePolicyProtocol.ParseFnKeyState(response, request);
        return state.MultiFunctionPrimary;
    }

    public async ValueTask<int> SetBladeChargeLimitAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        int percent,
        CancellationToken cancellationToken = default)
    {
        var raw = BladePowerTelemetryShared.EncodeChargeLimit(percent);
        var blade = _resolver.FindReadyBlade(devices)
            ?? throw new InvalidOperationException("The Blade charge control channel is unavailable.");
        RazerCapabilityClient.EnsureValidated(_validation.ChargeLimitPath, blade.Descriptor.Id, "Read the Blade charge limit successfully first.");

        var original = await BladePowerTelemetryShared.ReadChargeLimitAsync(_client, blade, cancellationToken);
        try
        {
            await BladePowerTelemetryShared.WriteChargeLimitAsync(_client, blade, raw, cancellationToken);
            var actual = await BladePowerTelemetryShared.ReadChargeLimitAsync(_client, blade, cancellationToken);
            if (actual != percent)
            {
                throw new InvalidOperationException($"Charge limit readback mismatch: wrote {percent}%, read {actual}%.");
            }

            return actual;
        }
        catch (Exception exception) when (
            RazerCapabilityClient.IsExpectedHardwareException(exception) || exception is OperationCanceledException)
        {
            var restored = await TryRestoreChargeLimitAsync(blade, original);
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

        var state = await SetBoostModeAsync(
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

        var state = await SetBoostModeAsync(
            devices, BladeBoostProtocol.GpuCluster, (byte)mode, cancellationToken);
        return state.Gpu;
    }

    internal async Task<BladeBoostState> ReadBoostStateAsync(
        ResolvedRazerDevice device,
        CancellationToken cancellationToken)
    {
        var cpuResponse = await _client.QueryAsync(
            device,
            CapabilityIds.BoostGet,
            BladeBoostProtocol.CreateGetArguments(BladeBoostProtocol.CpuCluster),
            cancellationToken);
        var gpuResponse = await _client.QueryAsync(
            device,
            CapabilityIds.BoostGet,
            BladeBoostProtocol.CreateGetArguments(BladeBoostProtocol.GpuCluster),
            cancellationToken);

        return new BladeBoostState(
            BladeBoostProtocol.ParseCpu(cpuResponse, BladeBoostProtocol.ProfileId),
            BladeBoostProtocol.ParseGpu(gpuResponse, BladeBoostProtocol.ProfileId));
    }

    private async Task<BladeBoostState> SetBoostModeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        byte cluster,
        byte value,
        CancellationToken cancellationToken)
    {
        var blade = _resolver.FindReadyBlade(devices)
            ?? throw new InvalidOperationException("The Blade Boost control channel is unavailable.");
        RazerCapabilityClient.EnsureValidated(_validation.BoostPath, blade.Descriptor.Id, "Read Blade CPU/GPU Boost successfully first.");
        BladePowerTelemetryShared.EnsureBoostAdmitted(blade, cluster, value);

        var thermal = await _fan.ReadThermalStateAsync(blade, cancellationToken);
        if (thermal.PerformanceMode != BladePerformanceMode.Custom)
        {
            throw new InvalidOperationException("CPU/GPU Boost can only be changed in Custom performance mode.");
        }

        var original = await ReadBoostStateAsync(blade, cancellationToken);
        if ((cluster == BladeBoostProtocol.CpuCluster && (byte)original.Cpu == value) ||
            (cluster == BladeBoostProtocol.GpuCluster && (byte)original.Gpu == value))
        {
            return original;
        }

        try
        {
            await WriteBoostModeAsync(blade, cluster, value, cancellationToken);
            var actual = await ReadBoostStateAsync(blade, cancellationToken);
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
            RazerCapabilityClient.IsExpectedHardwareException(exception) || exception is OperationCanceledException)
        {
            var restored = await TryRestoreBoostStateAsync(blade, original);
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

    private Task WriteBoostModeAsync(
        ResolvedRazerDevice device,
        byte cluster,
        byte value,
        CancellationToken cancellationToken) =>
        _client.QueryAsync(
            device,
            CapabilityIds.BoostSet,
            BladeBoostProtocol.CreateSetArguments(cluster, value),
            cancellationToken);

    private async Task<bool> TryRestoreBoostStateAsync(
        ResolvedRazerDevice device,
        BladeBoostState state)
    {
        try
        {
            var thermal = await _fan.ReadThermalStateAsync(device, CancellationToken.None);
            if (thermal.PerformanceMode != BladePerformanceMode.Custom)
            {
                return false;
            }

            await WriteBoostModeAsync(
                device, BladeBoostProtocol.CpuCluster, (byte)state.Cpu, CancellationToken.None);
            await WriteBoostModeAsync(
                device, BladeBoostProtocol.GpuCluster, (byte)state.Gpu, CancellationToken.None);
            return await ReadBoostStateAsync(device, CancellationToken.None) == state;
        }
        catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
        {
            return false;
        }
    }

    public ValueTask<BladeMaxFanMode> SetBladeMaxFanModeAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeMaxFanMode mode,
        CancellationToken cancellationToken = default) =>
        RunPowerModeTransactionAsync(
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

        var blade = _resolver.FindReadyBlade(devices)
            ?? throw new InvalidOperationException("The Blade Max Fan control channel is unavailable.");
        BladePowerTelemetryShared.EnsureMaxFanSupported(blade);
        RazerCapabilityClient.EnsureValidated(
            _validation.PowerModePath,
            blade.Descriptor.Id,
            "Read the Blade Max Fan state successfully first.");

        var thermal = await _fan.ReadThermalStateAsync(blade, cancellationToken);
        if (thermal.PerformanceMode != BladePerformanceMode.Custom)
        {
            throw new InvalidOperationException("Max Fan can only be changed in Custom performance mode.");
        }
        if (mode == BladeMaxFanMode.Enabled && thermal.FanMode != BladeFanMode.Automatic)
        {
            throw new InvalidOperationException(
                "Select Automatic fans before enabling Max Fan; a fixed fan target conflicts with it.");
        }

        var originalMask = await ReadPowerModeMaskAsync(blade, cancellationToken);
        var original = BladePowerTelemetryShared.ToMaxFanMode(originalMask);
        if (original == mode)
        {
            return original;
        }

        try
        {
            // 07/0F replaces the whole byte. Level bits (CPU Boost, Local Dimming) ride
            // along untouched; a pending one-time-full-charge request is transient, so
            // Max Fan waits until it clears instead of rewriting a bit whose readback
            // cannot be verified.
            var blockingSettings = (byte)(originalMask &
                ~(BladePowerModeProtocol.MaxFanBit |
                  BladePowerModeProtocol.CpuPowerBoostBit |
                  BladePowerModeProtocol.LocalDimmingBit));
            if (blockingSettings != 0)
            {
                throw new InvalidOperationException(
                    $"Max Fan was not changed because a one-time power setting is pending (0x{blockingSettings:X2}).");
            }

            await WriteMaxFanModeAsync(blade, mode, originalMask, cancellationToken);
            var actualMask = await ReadPowerModeMaskAsync(blade, cancellationToken);
            var actual = BladePowerTelemetryShared.ToMaxFanMode(actualMask);
            var expectedMask = mode == BladeMaxFanMode.Enabled
                ? (byte)(originalMask | BladePowerModeProtocol.MaxFanBit)
                : (byte)(originalMask & ~BladePowerModeProtocol.MaxFanBit);
            if (actualMask != expectedMask)
            {
                throw new InvalidOperationException($"Max Fan readback mismatch: wrote {mode}, read {actual}, and other power bits also changed.");
            }

            return actual;
        }
        catch (Exception exception) when (
            RazerCapabilityClient.IsExpectedHardwareException(exception) || exception is OperationCanceledException)
        {
            var restored = await TryRestoreMaxFanModeAsync(blade, original, originalMask);
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
        RunPowerModeTransactionAsync(
            () => SetBladeOneTimeFullChargeCoreAsync(devices, enabled, cancellationToken),
            cancellationToken);

    private async ValueTask<bool> SetBladeOneTimeFullChargeCoreAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var blade = _resolver.FindReadyBlade(devices)
            ?? throw new InvalidOperationException("The Blade one-time-full-charge control channel is unavailable.");
        RazerCapabilityClient.EnsureValidated(
            _validation.PowerModePath,
            blade.Descriptor.Id,
            "Read the Blade Power Mode Control state successfully first.");
        if (enabled && await BladePowerTelemetryShared.ReadChargeLimitAsync(_client, blade, cancellationToken) == 100)
        {
            throw new InvalidOperationException("One-time full charge is available only when the charge limit is enabled.");
        }

        var originalMask = await ReadPowerModeMaskAsync(blade, cancellationToken);
        var original = (originalMask & BladePowerModeProtocol.OneTimeFullChargeBit) != 0;
        if (original == enabled)
        {
            return original;
        }

        var expectedMask = enabled
            ? (byte)(originalMask | BladePowerModeProtocol.OneTimeFullChargeBit)
            : (byte)(originalMask & ~BladePowerModeProtocol.OneTimeFullChargeBit);
        try
        {
            await WritePowerModeMaskAsync(blade, expectedMask, cancellationToken);
            var actualMask = await ReadPowerModeMaskAsync(blade, cancellationToken);
            if (actualMask != expectedMask)
            {
                throw new InvalidOperationException(
                    $"One-time-full-charge readback mismatch: wrote 0x{expectedMask:X2}, read 0x{actualMask:X2}.");
            }

            return enabled;
        }
        catch (Exception exception) when (
            RazerCapabilityClient.IsExpectedHardwareException(exception) || exception is OperationCanceledException)
        {
            var restored = await TryRestorePowerModeMaskAsync(blade, originalMask);
            var message = "One-time-full-charge update failed: " + exception.Message + " " +
                (restored ? "The original value was restored." : "Original value restoration failed; check the charge settings immediately.");
            if (exception is OperationCanceledException)
            {
                throw new OperationCanceledException(message, exception, cancellationToken);
            }
            throw new InvalidOperationException(message, exception);
        }
    }

    private async Task<bool> TryRestoreBoostLevelsAsync(ResolvedRazerDevice device, BladeBoostState state)
    {
        try
        {
            await WriteBoostModeAsync(
                device, BladeBoostProtocol.CpuCluster, (byte)state.Cpu, CancellationToken.None);
            await WriteBoostModeAsync(
                device, BladeBoostProtocol.GpuCluster, (byte)state.Gpu, CancellationToken.None);
            return await ReadBoostStateAsync(device, CancellationToken.None) == state;
        }
        catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
        {
            return false;
        }
    }

    internal async Task<byte> ReadPowerModeMaskAsync(
        ResolvedRazerDevice device,
        CancellationToken cancellationToken)
    {
        var response = await _client.QueryAsync(
            device, CapabilityIds.PowerModeGet, cancellationToken);
        return BladePowerModeProtocol.ParsePowerModeMask(
            response, RazerCapabilityClient.CreateRequest(device, CapabilityIds.PowerModeGet));
    }

    private Task WriteMaxFanModeAsync(
        ResolvedRazerDevice device,
        BladeMaxFanMode mode,
        byte existingPowerModeMask,
        CancellationToken cancellationToken)
    {
        var request = BladePowerModeProtocol.CreateSetRequest(mode, existingPowerModeMask);
        return _client.QueryBuiltRequestAsync(device, CapabilityIds.PowerModeSet, request, cancellationToken);
    }

    private Task WritePowerModeMaskAsync(
        ResolvedRazerDevice device,
        byte mask,
        CancellationToken cancellationToken) =>
        _client.QueryBuiltRequestAsync(
            device,
            CapabilityIds.PowerModeSet,
            BladePowerModeProtocol.CreateSetPowerModeMaskRequest(mask),
            cancellationToken);

    private async Task<bool> TryRestorePowerModeMaskAsync(
        ResolvedRazerDevice device,
        byte originalMask)
    {
        try
        {
            await WritePowerModeMaskAsync(device, originalMask, CancellationToken.None);
            return await ReadPowerModeMaskAsync(device, CancellationToken.None) == originalMask;
        }
        catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
        {
            return false;
        }
    }

    private async Task<bool> TryRestoreMaxFanModeAsync(
        ResolvedRazerDevice device,
        BladeMaxFanMode mode,
        byte originalMask)
    {
        try
        {
            await WriteMaxFanModeAsync(device, mode, originalMask, CancellationToken.None);
            return await ReadPowerModeMaskAsync(device, CancellationToken.None) == originalMask;
        }
        catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
        {
            return false;
        }
    }

    private async Task<bool> TryRestoreChargeLimitAsync(
        ResolvedRazerDevice device,
        int percent)
    {
        try
        {
            await BladePowerTelemetryShared.WriteChargeLimitAsync(
                _client, device, BladePowerTelemetryShared.EncodeChargeLimit(percent), CancellationToken.None);
            var restored = await BladePowerTelemetryShared.ReadChargeLimitAsync(
                _client, device, CancellationToken.None);
            return restored == percent;
        }
        catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
        {
            return false;
        }
    }

    /// <summary>
    /// HyperBoost raises the sustained power ceiling but leaves the EC on its own fan
    /// curve, which assumes a cooling pad. Without the pin the extra budget only buys
    /// thermal throttling, so the fans go to maximum together with the mode.
    /// </summary>
    private async Task ApplyMaxFanForHyperBoostAsync(
        ResolvedRazerDevice blade,
        CancellationToken cancellationToken)
    {
        if (!blade.SupportsMaxFan ||
            _validation.PowerModePath is null)
        {
            return;
        }

        var mask = await ReadPowerModeMaskAsync(blade, cancellationToken);
        if ((mask & BladePowerModeProtocol.MaxFanBit) != 0)
        {
            return;
        }

        // 07/0F replaces the whole byte. A pending one-time setting owns a bit whose
        // readback cannot be verified, so leave it alone rather than rewriting it.
        var blocking = (byte)(mask &
            ~(BladePowerModeProtocol.MaxFanBit |
              BladePowerModeProtocol.CpuPowerBoostBit |
              BladePowerModeProtocol.LocalDimmingBit));
        if (blocking != 0)
        {
            return;
        }

        await WriteMaxFanModeAsync(blade, BladeMaxFanMode.Enabled, mask, cancellationToken);
        var confirmed = await ReadPowerModeMaskAsync(blade, cancellationToken);
        if ((confirmed & BladePowerModeProtocol.MaxFanBit) == 0)
        {
            throw new InvalidOperationException(
                "HyperBoost could not pin the fans to maximum, so the performance mode was not changed.");
        }
    }

    /// <summary>
    /// Synapse turns Maximum Fans off before leaving Custom on the models that own it.
    /// Without the same cleanup a preset write leaves the fans pinned at maximum, so the
    /// bit is cleared and confirmed before any preset change is submitted.
    /// </summary>
    private async Task EnsureMaxFanOffBeforeLeavingCustomAsync(
        ResolvedRazerDevice blade,
        BladePerformanceMode requestedMode,
        CancellationToken cancellationToken)
    {
        if (requestedMode == BladePerformanceMode.Custom ||
            !blade.SupportsMaxFan ||
            _validation.PowerModePath is null)
        {
            return;
        }

        var mask = await ReadPowerModeMaskAsync(blade, cancellationToken);
        if ((mask & BladePowerModeProtocol.MaxFanBit) == 0)
        {
            return;
        }

        await WritePowerModeMaskAsync(blade, (byte)(mask & ~BladePowerModeProtocol.MaxFanBit), cancellationToken);
        var confirmed = await ReadPowerModeMaskAsync(blade, cancellationToken);
        if ((confirmed & BladePowerModeProtocol.MaxFanBit) != 0)
        {
            throw new InvalidOperationException(
                "Maximum fan speed is still active, so the performance mode was not changed.");
        }
    }

}
