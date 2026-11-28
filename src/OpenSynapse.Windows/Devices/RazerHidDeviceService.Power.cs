using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidDeviceService
{
    // The middleware drives GPU card detection on LED class 0 (All_Region).
    private const byte GpuCardLedClassId = 0x00;

    public async Task<int> GetBatteryPercentAsync(RazerHidDeviceConnection connection, CancellationToken cancellationToken = default) =>
        RazerHidMouseProtocol.ParseBatteryPercent(await ExecuteAsync(connection,
            RazerHidMouseProtocol.GetBattery(connection.Definition), cancellationToken).ConfigureAwait(false));

    public async Task<bool> GetChargingAsync(RazerHidDeviceConnection connection, CancellationToken cancellationToken = default) =>
        RazerHidMouseProtocol.ParseCharging(await ExecuteAsync(connection,
            RazerHidMouseProtocol.GetCharging(connection.Definition), cancellationToken).ConfigureAwait(false));

    public async Task<int> GetIdleTimeoutAsync(RazerHidDeviceConnection connection, CancellationToken cancellationToken = default) =>
        RazerHidMouseProtocol.ParseIdleSeconds(await ExecuteAsync(connection,
            RazerHidMouseProtocol.GetIdleTime(connection.Definition), cancellationToken).ConfigureAwait(false));

    public Task SetIdleTimeoutAsync(RazerHidDeviceConnection connection, int seconds, CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            RazerHidMouseProtocol.SetIdleTime(connection.Definition, seconds), cancellationToken);

    public async Task<int> GetLowBatteryThresholdAsync(RazerHidDeviceConnection connection, CancellationToken cancellationToken = default) =>
        RazerHidMouseProtocol.ParseLowBatteryThreshold(await ExecuteAsync(connection,
            RazerHidMouseProtocol.GetLowBatteryThreshold(connection.Definition), cancellationToken).ConfigureAwait(false));

    public Task SetLowBatteryThresholdAsync(RazerHidDeviceConnection connection, int percent, CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            RazerHidMouseProtocol.SetLowBatteryThreshold(connection.Definition, percent), cancellationToken);

    public async Task<bool> GetAutoSleepAsync(RazerHidDeviceConnection connection, CancellationToken cancellationToken = default) =>
        RazerHidMouseProtocol.ParseAutoSleepEnabled(await ExecuteAsync(connection,
            RazerHidMouseProtocol.GetAutoSleep(connection.Definition), cancellationToken).ConfigureAwait(false));

    public Task SetAutoSleepAsync(RazerHidDeviceConnection connection, bool enabled, CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            RazerHidMouseProtocol.SetAutoSleep(connection.Definition, enabled), cancellationToken);

    public async Task<(byte Level, bool Enabled)> GetBatteryChargeLimiterAsync(
        RazerHidDeviceConnection connection, CancellationToken cancellationToken = default) =>
        RazerHidMouseProtocol.ParseBatteryChargeLimiter(await ExecuteAsync(connection,
            RazerHidMouseProtocol.GetBatteryChargeLimiter(connection.Definition), cancellationToken).ConfigureAwait(false));

    public Task SetBatteryChargeLimiterAsync(
        RazerHidDeviceConnection connection, byte level, bool enabled, CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            RazerHidMouseProtocol.SetBatteryChargeLimiter(connection.Definition, level, enabled), cancellationToken);

    public async Task<RazerHidTimeToDim> GetTimeToDimAsync(
        RazerHidDeviceConnection connection, CancellationToken cancellationToken = default) =>
        RazerHidMouseProtocol.ParseTimeToDim(await ExecuteAsync(connection,
            RazerHidMouseProtocol.GetTimeToDim(connection.Definition), cancellationToken).ConfigureAwait(false));

    public Task SetTimeToDimAsync(
        RazerHidDeviceConnection connection,
        bool enabled,
        int seconds,
        byte percentage,
        CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            RazerHidMouseProtocol.SetTimeToDim(connection.Definition, 0, enabled, seconds, percentage),
            cancellationToken);

    public async Task<byte> GetIndicatorLedConfigurationAsync(
        RazerHidDeviceConnection connection, CancellationToken cancellationToken = default) =>
        RazerHidMouseProtocol.ParseIndicatorLedConfiguration(await ExecuteAsync(connection,
            RazerHidMouseProtocol.GetIndicatorLedConfiguration(connection.Definition), cancellationToken).ConfigureAwait(false));

    public Task SetIndicatorLedConfigurationAsync(
        RazerHidDeviceConnection connection, byte configuration, CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(connection, async token =>
        {
            await ExecuteWithoutResultAsync(connection,
                RazerHidMouseProtocol.SetIndicatorLedConfiguration(connection.Definition, configuration), token).ConfigureAwait(false);
            if (!connection.Capabilities.Contains(RazerHidBackendCapability.IndicatorLedConfigurationRead)) return;
            var actual = await GetIndicatorLedConfigurationAsync(connection, token).ConfigureAwait(false);
            WriteDiagnostic(connection.Definition.ProductId, $"Indicator: requested={configuration}, readback={actual}.");
            if (actual != configuration)
                throw new RazerReportMismatchException($"Indicator readback mismatch: requested={configuration}, actual={actual}.");
        }, cancellationToken);

    public async Task<RazerHidIndicatorLedConfigurationV2> GetIndicatorLedConfigurationV2Async(
        RazerHidDeviceConnection connection, CancellationToken cancellationToken = default) =>
        RazerHidDongleIndicatorProtocol.Parse(await ExecuteAsync(connection,
            RazerHidDongleIndicatorProtocol.Get(connection.Definition), cancellationToken).ConfigureAwait(false));

    public Task SetIndicatorLedConfigurationV2Async(
        RazerHidDeviceConnection connection,
        RazerHidIndicatorLedConfigurationV2 configuration,
        CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(connection, async token =>
        {
            await ExecuteWithoutResultAsync(connection,
                RazerHidDongleIndicatorProtocol.Set(connection.Definition, configuration), token).ConfigureAwait(false);
            if (!connection.Capabilities.Contains(RazerHidBackendCapability.IndicatorLedConfigurationV2Read)) return;
            var actual = await GetIndicatorLedConfigurationV2Async(connection, token).ConfigureAwait(false);
            WriteDiagnostic(connection.Definition.ProductId, $"Indicator V2: requested={configuration}, readback={actual}.");
            if (actual != configuration)
                throw new RazerReportMismatchException($"Indicator V2 readback mismatch: requested={configuration}, actual={actual}.");
        }, cancellationToken);

    public async Task<RazerHidGpuCardDetection> GetGpuCardDetectionAsync(
        RazerHidDeviceConnection connection, CancellationToken cancellationToken = default) =>
        RazerHidMouseProtocol.ParseGpuCardDetection(await ExecuteAsync(connection,
            RazerHidMouseProtocol.GetGpuCardDetection(connection.Definition, GpuCardLedClassId), cancellationToken).ConfigureAwait(false));

    public Task SetGpuCardDetectionAsync(
        RazerHidDeviceConnection connection, bool enabled, CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            RazerHidMouseProtocol.SetGpuCardDetection(connection.Definition, GpuCardLedClassId, enabled ? (byte)1 : (byte)0),
            cancellationToken);

    public async Task<byte> GetBatteryDischargeCurveAsync(
        RazerHidDeviceConnection connection, CancellationToken cancellationToken = default) =>
        RazerHidMouseProtocol.ParseBatteryDischargeCurve(await ExecuteAsync(connection,
            RazerHidMouseProtocol.GetBatteryDischargeCurve(connection.Definition), cancellationToken).ConfigureAwait(false));

    public Task SetBatteryDischargeCurveAsync(
        RazerHidDeviceConnection connection, byte chemistry, CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            RazerHidMouseProtocol.SetBatteryDischargeCurve(connection.Definition, chemistry), cancellationToken);
}
