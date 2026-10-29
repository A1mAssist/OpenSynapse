using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Displays;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

internal sealed class BladeFanTelemetryReader
{
    private readonly RazerDeviceResolver _resolver;
    private readonly RazerCapabilityClient _client;

    public BladeFanTelemetryReader(RazerDeviceResolver resolver, RazerCapabilityClient client)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async ValueTask<BladeFanControlState> SetBladeFanAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeFanMode mode,
        int? cpuTargetRpm,
        int? gpuTargetRpm,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }
        if (mode == BladeFanMode.Manual)
        {
            if (cpuTargetRpm is null)
            {
                throw new ArgumentNullException(nameof(cpuTargetRpm));
            }
            if (gpuTargetRpm is null)
            {
                throw new ArgumentNullException(nameof(gpuTargetRpm));
            }
        }
        else if (cpuTargetRpm is not null || gpuTargetRpm is not null)
        {
            throw new ArgumentException("Automatic fan mode cannot specify a fixed speed.");
        }

        var blade = _resolver.FindReadyBlade(devices)
            ?? throw new InvalidOperationException("The Blade fan control channel is unavailable.");
        if (mode == BladeFanMode.Manual &&
            !blade.SupportsFixedFan)
        {
            throw new InvalidOperationException("Fixed fan control is not admitted for this Blade model.");
        }
        var original = await ReadFanTransactionStateAsync(blade, cancellationToken, readTachometers: true);
        if (mode == BladeFanMode.Manual)
        {
            // Synapse publishes a different fixed-speed range per performance mode, so the
            // check needs the mode the device is actually in, not a single model range.
            ValidateModelFanTargetRpm(blade, original.Thermal.PerformanceMode, cpuTargetRpm!.Value);
            ValidateModelFanTargetRpm(blade, original.Thermal.PerformanceMode, gpuTargetRpm!.Value);
        }

        var actual = await SetFanTargetsCoreAsync(
            blade,
            mode,
            cpuTargetRpm ?? original.CpuTargetRpm,
            gpuTargetRpm ?? original.GpuTargetRpm,
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
        var blade = _resolver.FindReadyBlade(devices)
            ?? throw new InvalidOperationException("The Blade fan control channel is unavailable.");
        var state = await ReadFanTransactionStateAsync(
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
        var blade = _resolver.FindReadyBlade(devices)
            ?? throw new InvalidOperationException("The Blade fan control channel is unavailable.");
        if (!blade.SupportsFanCurve)
        {
            throw new InvalidOperationException("Fan-curve control is not admitted for this Blade model.");
        }
        var maximumRpm = blade.MaximumWritableFanTargetRpm;
        BladeFanProtocol.ValidateCurveTargetRpm(cpuTargetRpm, maximumRpm);
        BladeFanProtocol.ValidateCurveTargetRpm(gpuTargetRpm, maximumRpm);
        var original = await ReadFanTransactionStateAsync(
            blade, cancellationToken, readTachometers: true);
        var actual = await SetFanTargetsCoreAsync(
            blade,
            mode,
            cpuTargetRpm,
            gpuTargetRpm,
            writeTargets: true,
            original,
            cancellationToken);
        return actual.ToPublic();
    }

    private async Task<BladeFanTransactionState> SetFanTargetsCoreAsync(
        ResolvedRazerDevice blade,
        BladeFanMode mode,
        int cpuTargetRpm,
        int gpuTargetRpm,
        bool writeTargets,
        BladeFanTransactionState original,
        CancellationToken cancellationToken)
    {
        try
        {
            var requiresCustomMode = blade.FixedFanRequiresCustomMode;
            // The performance byte is written verbatim so a fan change does not also
            // change the performance mode: the named mode can be a widened reading of
            // the byte, and writing the name back would move the machine.
            var performanceModeByte = requiresCustomMode && mode == BladeFanMode.Manual
                ? (byte)BladePerformanceMode.Custom
                : original.Thermal.PerformanceModeByte;

            // The 028C EC only acts on a fixed target after the matching Custom/manual
            // mode write, so the mode has to go out first. Every other audited model
            // keeps the original order.
            if (requiresCustomMode)
            {
                await WriteThermalZoneAsync(
                    blade, BladeFanProtocol.ZoneCpu, performanceModeByte, mode, cancellationToken);
                await WriteThermalZoneAsync(
                    blade, BladeFanProtocol.ZoneGpu, performanceModeByte, mode, cancellationToken);
                if (writeTargets)
                {
                    await WriteFanTargetAsync(
                        blade, BladeFanProtocol.ZoneCpu, cpuTargetRpm, cancellationToken, curveTarget: true);
                    await WriteFanTargetAsync(
                        blade, BladeFanProtocol.ZoneGpu, gpuTargetRpm, cancellationToken, curveTarget: true);
                }
            }
            else
            {
                if (writeTargets)
                {
                    await WriteFanTargetAsync(
                        blade, BladeFanProtocol.ZoneCpu, cpuTargetRpm, cancellationToken, curveTarget: true);
                    await WriteFanTargetAsync(
                        blade, BladeFanProtocol.ZoneGpu, gpuTargetRpm, cancellationToken, curveTarget: true);
                }

                await WriteThermalZoneAsync(
                    blade, BladeFanProtocol.ZoneCpu, performanceModeByte, mode, cancellationToken);
                await WriteThermalZoneAsync(
                    blade, BladeFanProtocol.ZoneGpu, performanceModeByte, mode, cancellationToken);
            }

            var actual = await ReadFanTransactionStateAsync(
                blade, cancellationToken, readTachometers: false);
            if (actual.Thermal.PerformanceModeByte != performanceModeByte ||
                actual.Thermal.FanMode != mode ||
                actual.CpuTargetRpm != cpuTargetRpm ||
                actual.GpuTargetRpm != gpuTargetRpm)
            {
                throw new InvalidOperationException(
                    $"Fan readback mismatch: wrote 0x{performanceModeByte:X2} / {mode} / CPU {cpuTargetRpm} / GPU {gpuTargetRpm} RPM, " +
                    $"read {actual.Thermal.PerformanceMode} / {actual.Thermal.FanMode} / " +
                    $"CPU {actual.CpuTargetRpm} / GPU {actual.GpuTargetRpm} RPM.");
            }

            return actual;
        }
        catch (Exception operationException)
        {
            var restorationException = await RestoreFanTransactionStateAsync(blade, original);
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

    internal async Task<BladeThermalState> ReadThermalStateAsync(
        ResolvedRazerDevice device,
        CancellationToken cancellationToken)
    {
        BladeThermalState? state = null;
        foreach (var report in await ReadThermalReportsAsync(device, cancellationToken))
        {
            var current = new BladeThermalState(
                ParsePerformanceMode(report[RazerFeatureReport.ArgumentsOffset + 2]),
                ParseFanMode(report[RazerFeatureReport.ArgumentsOffset + 3]),
                report[RazerFeatureReport.ArgumentsOffset + 2]);
            if (state is not null && state != current)
            {
                throw new InvalidOperationException($"Blade fan-zone states do not match: {state} / {current}.");
            }
            state = current;
        }

        return state ?? throw new InvalidOperationException("Blade did not return a performance mode.");
    }

    /// <summary>
    /// Reads the thermal report of both fan zones. The 597 and 2026 middleware select
    /// the default profile (1); the 710/671 reports accept profile 0. The remaining two
    /// bytes are the performance mode and fan-mode response slots.
    /// </summary>
    internal async Task<IReadOnlyList<byte[]>> ReadThermalReportsAsync(
        ResolvedRazerDevice device,
        CancellationToken cancellationToken)
    {
        var profileId = GetThermalProfileId(device);
        var reports = new List<byte[]>(2);
        foreach (var zone in new byte[] { 0x01, 0x02 })
        {
            var response = await _client.QueryAsync(
                device,
                "thermal-state.get",
                new byte[] { profileId, zone, 0x00, 0x00 },
                cancellationToken);
            RazerCapabilityClient.EnsureDataSize(response, 4, "Blade performance mode");
            if (response[RazerFeatureReport.ArgumentsOffset + 1] != zone)
            {
                throw new InvalidOperationException(
                    $"Blade returned the wrong fan zone: {response[RazerFeatureReport.ArgumentsOffset + 1]}.");
            }

            reports.Add(response);
        }

        return reports;
    }

    /// <summary>
    /// The profile byte the thermal report selects. The 597 and 2026 middleware use the
    /// default profile (1); the 710/671 reports accept profile 0.
    /// </summary>
    private static byte GetThermalProfileId(ResolvedRazerDevice device) =>
        device.Descriptor.ProtocolFamily == "blade-597" ||
        device.UsesBlade2026ThermalSelector
            ? (byte)0x01
            : (byte)0x00;

    internal async Task<int> ReadFanTargetRpmAsync(
        ResolvedRazerDevice device,
        CancellationToken cancellationToken)
    {
        // Query both zones before parsing either one. A model-specific target above
        // the write ceiling must not prevent the second diagnostic read.
        var zones = new[] { BladeFanProtocol.ZoneCpu, BladeFanProtocol.ZoneGpu };
        var classId = GetFanClassId(device);
        var requests = new byte[zones.Length][];
        var responses = new byte[zones.Length][];
        for (var index = 0; index < zones.Length; index++)
        {
            requests[index] = BladeFanProtocol.CreateGetTargetRequest(zones[index], classId);
            responses[index] = await _client.QueryBuiltRequestAsync(
                device, "fan-target.get", requests[index], cancellationToken);
        }

        int? rpm = null;
        var maximumRpm = device.MaximumFanTargetRpm;
        for (var index = 0; index < zones.Length; index++)
        {
            var current = BladeFanProtocol.ParseTarget(
                responses[index],
                zones[index],
                RazerCapabilityClient.CreateConfiguredRequest(device, "fan-target.get", requests[index]),
                maximumRpm,
                classId);
            if (rpm is not null && rpm != current)
            {
                throw new InvalidOperationException($"Blade fan-zone targets do not match: {rpm} / {current} RPM.");
            }
            rpm = current;
        }

        return rpm ?? throw new InvalidOperationException("Blade did not return a fan speed.");
    }

    private async Task<BladeFanTransactionState> ReadFanTransactionStateAsync(
        ResolvedRazerDevice device,
        CancellationToken cancellationToken,
        bool readTachometers)
    {
        var thermal = await ReadThermalStateAsync(device, cancellationToken);
        var cpuTargetRpm = await ReadFanTargetRpmAsync(
            device, BladeFanProtocol.ZoneCpu, cancellationToken, curveTarget: true);
        var gpuTargetRpm = await ReadFanTargetRpmAsync(
            device, BladeFanProtocol.ZoneGpu, cancellationToken, curveTarget: true);
        if (readTachometers)
        {
            _ = await ReadCurrentFanRpmAsync(
                device, BladeThermalProtocol.CpuFanId, cancellationToken);
            _ = await ReadCurrentFanRpmAsync(
                device, BladeThermalProtocol.GpuFanId, cancellationToken);
        }
        return new BladeFanTransactionState(thermal, cpuTargetRpm, gpuTargetRpm);
    }

    private async Task<int> ReadFanTargetRpmAsync(
        ResolvedRazerDevice device,
        byte zone,
        CancellationToken cancellationToken,
        bool curveTarget)
    {
        var classId = GetFanClassId(device);
        var request = BladeFanProtocol.CreateGetTargetRequest(zone, classId);
        var response = await _client.QueryBuiltRequestAsync(
            device, "fan-target.get", request, cancellationToken);
        var configured = RazerCapabilityClient.CreateConfiguredRequest(device, "fan-target.get", request);
        var maximumRpm = device.MaximumFanTargetRpm;
        return curveTarget
            ? BladeFanProtocol.ParseCurveTarget(response, zone, configured, maximumRpm, classId)
            : BladeFanProtocol.ParseTarget(response, zone, configured, maximumRpm, classId);
    }

    private Task WriteFanTargetAsync(
        ResolvedRazerDevice device,
        byte zone,
        int rpm,
        CancellationToken cancellationToken,
        bool curveTarget = false)
    {
        var classId = GetFanClassId(device);
        var request = curveTarget
            ? BladeFanProtocol.CreateSetCurveTargetRequest(
                zone,
                rpm,
                device.MaximumWritableFanTargetRpm,
                classId)
            : BladeFanProtocol.CreateSetTargetRequest(
                zone,
                rpm,
                device.MaximumWritableFanTargetRpm,
                classId);
        return _client.QueryCommandAsync(
            device,
            "fan-target.get",
            0x01,
            request.AsMemory(RazerFeatureReport.ArgumentsOffset, request[6]),
            cancellationToken,
            request[6]);
    }

    private async Task<Exception?> RestoreFanTransactionStateAsync(
        ResolvedRazerDevice device,
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

        await AttemptAsync("Restore CPU fan target", () => WriteFanTargetAsync(
            device,
            BladeFanProtocol.ZoneCpu,
            original.CpuTargetRpm,
            CancellationToken.None,
            curveTarget: true));
        await AttemptAsync("Restore GPU fan target", () => WriteFanTargetAsync(
            device,
            BladeFanProtocol.ZoneGpu,
            original.GpuTargetRpm,
            CancellationToken.None,
            curveTarget: true));
        await AttemptAsync("Restore CPU fan mode", () => WriteThermalZoneAsync(
            device,
            BladeFanProtocol.ZoneCpu,
            original.Thermal.PerformanceModeByte,
            original.Thermal.FanMode,
            CancellationToken.None));
        await AttemptAsync("Restore GPU fan mode", () => WriteThermalZoneAsync(
            device,
            BladeFanProtocol.ZoneGpu,
            original.Thermal.PerformanceModeByte,
            original.Thermal.FanMode,
            CancellationToken.None));
        await AttemptAsync("Restore readback", async () =>
        {
            var restored = await ReadFanTransactionStateAsync(
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

    internal async Task<int> ReadCurrentFanRpmAsync(
        ResolvedRazerDevice device,
        byte fanId,
        CancellationToken cancellationToken)
    {
        var request = BladeThermalProtocol.CreateGetCurrentSpeedRequest(fanId);
        var response = await _client.QueryBuiltRequestAsync(
            device, "current-fan-rpm.get", request, cancellationToken);
        return BladeThermalProtocol.ParseCurrentSpeedRpm(
            response,
            fanId,
            RazerCapabilityClient.CreateConfiguredRequest(device, "current-fan-rpm.get", request));
    }

    internal async Task<byte> ReadAdvancedFanModeAsync(
        ResolvedRazerDevice device,
        byte fanId,
        CancellationToken cancellationToken)
    {
        var request = BladeThermalProtocol.CreateGetAdvancedFanModeRequest(fanId);
        var response = await _client.QueryBuiltRequestAsync(
            device, "advanced-fan-mode.get", request, cancellationToken);
        return BladeThermalProtocol.ParseAdvancedFanMode(
            response,
            fanId,
            RazerCapabilityClient.CreateConfiguredRequest(device, "advanced-fan-mode.get", request));
    }

    /// <summary>
    /// Rejects a fixed target the exact model does not accept. The 028C EC refuses
    /// anything below its own floor, and both models step the target in 100 RPM units.
    /// </summary>
    private static void ValidateModelFanTargetRpm(
        ResolvedRazerDevice blade,
        BladePerformanceMode performanceMode,
        int rpm)
    {
        var range = blade.GetBladeFanTargetRange(performanceMode);
        var minimum = range.MinimumRpm;
        var maximum = range.MaximumRpm;
        if (rpm < minimum || rpm > maximum || rpm % BladeFanLimits.StepRpm != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rpm),
                $"Blade fixed fan target must be {minimum}..{maximum} RPM in {BladeFanLimits.StepRpm} RPM steps.");
        }
    }

    internal Task WriteThermalZoneAsync(
        ResolvedRazerDevice device,
        byte zone,
        BladePerformanceMode performanceMode,
        BladeFanMode fanMode,
        CancellationToken cancellationToken) =>
        WriteThermalZoneAsync(device, zone, (byte)performanceMode, fanMode, cancellationToken);

    /// <summary>
    /// Writes the performance byte verbatim. Restoring a captured state must not turn a
    /// widened reading back into a different mode.
    /// </summary>
    internal Task WriteThermalZoneAsync(
        ResolvedRazerDevice device,
        byte zone,
        byte rawPerformanceMode,
        BladeFanMode fanMode,
        CancellationToken cancellationToken) =>
        _client.QueryAsync(
            device,
            "thermal-state.set",
            new byte[] { 0x01, zone, rawPerformanceMode, (byte)fanMode },
            cancellationToken);

    internal async Task<bool> TryRestoreThermalStateAsync(
        ResolvedRazerDevice device,
        BladeThermalState state)
    {
        try
        {
            await WriteThermalZoneAsync(
                device, 0x01, state.PerformanceModeByte, state.FanMode, CancellationToken.None);
            await WriteThermalZoneAsync(
                device, 0x02, state.PerformanceModeByte, state.FanMode, CancellationToken.None);
            var restored = await ReadThermalStateAsync(device, CancellationToken.None);
            return restored == state;
        }
        catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
        {
            return false;
        }
    }

    private static byte GetFanClassId(ResolvedRazerDevice device) =>
        device.UsesBlade2026ThermalSelector
            ? (byte)0x01
            : BladeFanProtocol.DefaultClassId;

    internal static BladePerformanceMode ParsePerformanceMode(byte value) => value switch
    {
        0x01 => BladePerformanceMode.Gaming,
        0x00 => BladePerformanceMode.Balanced,
        0x02 => BladePerformanceMode.Performance,
        0x03 => BladePerformanceMode.BatterySaver,
        0x04 => BladePerformanceMode.Custom,
        0x05 => BladePerformanceMode.Silent,
        0x06 => BladePerformanceMode.BalancedDc,
        0x07 => BladePerformanceMode.Hyperboost,
        _ => throw new InvalidOperationException($"Blade returned an unknown performance mode: 0x{value:X2}."),
    };

    internal static BladeFanMode ParseFanMode(byte value) => value switch
    {
        0x00 => BladeFanMode.Automatic,
        0x01 => BladeFanMode.Manual,
        _ => throw new InvalidOperationException($"Blade returned an unknown fan mode: 0x{value:X2}."),
    };
}

/// <summary>
/// The thermal report's two facts. The raw performance byte is kept because the
/// named mode can be a widened reading of it: a model whose bundle publishes
/// neither Gaming nor Performance still reports 0x01, and writing that name back
/// would change the machine's mode.
/// </summary>
internal sealed record BladeThermalState(
    BladePerformanceMode PerformanceMode,
    BladeFanMode FanMode,
    byte RawPerformanceMode)
{
    internal byte PerformanceModeByte => RawPerformanceMode;
}

internal sealed record BladeFanTransactionState(
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
