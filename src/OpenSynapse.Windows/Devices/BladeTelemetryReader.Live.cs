using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

internal sealed partial class BladeTelemetryReader
{
    internal sealed record BladeLiveTelemetryReadResult(
        BladePerformanceMode? PerformanceMode,
        BladeFanMode? FanMode,
        int? FanTargetRpm,
        int? CurrentFanCpuRpm,
        int? CurrentFanGpuRpm,
        IReadOnlyList<string> Errors);

    public async ValueTask<BladeLiveTelemetryReadResult> ReadLiveAsync(
        ResolvedRazerDevice blade,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(blade);
        var errors = new List<string>();
        var thermal = await ReadThermalStateAsync(
            blade, errors, updateValidation: false, cancellationToken);

        int? fanTargetRpm = null;
        if (blade.Owns(CapabilityIds.FanTargetGet))
        {
            try
            {
                fanTargetRpm = await _fan.ReadFanTargetRpmAsync(blade, cancellationToken);
            }
            catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
            {
                if (thermal.FanMode == BladeFanMode.Manual || exception is NotSupportedException)
                {
                    errors.Add($"Fan target: {exception.Message}");
                }
            }
        }

        int? currentFanCpuRpm = null;
        int? currentFanGpuRpm = null;
        if (blade.Owns(CapabilityIds.CurrentFanRpmGet))
        {
            try
            {
                currentFanCpuRpm = await _fan.ReadCurrentFanRpmAsync(
                    blade, BladeThermalProtocol.CpuFanId, cancellationToken);
            }
            catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
            {
                errors.Add($"Current CPU fan speed: {exception.Message}");
            }

            try
            {
                currentFanGpuRpm = await _fan.ReadCurrentFanRpmAsync(
                    blade, BladeThermalProtocol.GpuFanId, cancellationToken);
            }
            catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
            {
                errors.Add($"Current GPU fan speed: {exception.Message}");
            }
        }

        return new BladeLiveTelemetryReadResult(
            thermal.PerformanceMode,
            thermal.FanMode,
            fanTargetRpm,
            currentFanCpuRpm,
            currentFanGpuRpm,
            errors);
    }

    private async ValueTask<ThermalState> ReadThermalStateAsync(
        ResolvedRazerDevice blade,
        List<string> errors,
        bool updateValidation,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<byte[]>? thermalReports = null;
        try
        {
            thermalReports = await _fan.ReadThermalReportsAsync(blade, cancellationToken);
        }
        catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
        {
            errors.Add($"Performance and fan state: {exception.Message}");
        }

        if (thermalReports is null)
        {
            return new ThermalState(null, null, false);
        }

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

        if (updateValidation && performanceMode is not null)
        {
            _validation.PerformancePath = blade.Descriptor.Id;
        }

        return new ThermalState(performanceMode, fanMode, performanceMode is not null);
    }

    private readonly record struct ThermalState(
        BladePerformanceMode? PerformanceMode,
        BladeFanMode? FanMode,
        bool Succeeded);
}
