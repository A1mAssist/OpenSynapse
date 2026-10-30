using OpenSynapse.Core.Devices;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// The 2022+ Blade battery optimizer is the charge-limiter control with a
/// different name and an optional one-time full-charge override. The wire
/// reports are the existing 07:92 read, 07:12 write, and 07:0F bit 2, so this
/// service reuses the telemetry reader instead of adding another protocol path.
/// </summary>
public sealed class BladeBatteryOptimizerService
{
    private readonly IRazerDeviceTelemetryReader _telemetry;

    public BladeBatteryOptimizerService(IRazerDeviceTelemetryReader telemetry) =>
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));

    public bool IsSupported(IReadOnlyList<DeviceDescriptor> devices) =>
        TryFindSupportedProductId(devices, out _);

    public bool SupportsChargingOverride(IReadOnlyList<DeviceDescriptor> devices) =>
        TryFindSupportedProductId(devices, out var productId) &&
        BladeBatteryOptimizerCapabilities.SupportsChargingOverride(productId);

    /// <summary>
    /// Returns the optimizer state, or null when the model does not declare the
    /// panel or the charge-limiter report cannot be read.
    /// </summary>
    public async ValueTask<BladeBatteryOptimizerState?> ReadAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        CancellationToken cancellationToken = default)
    {
        if (!TryFindSupportedProductId(devices, out var productId))
        {
            return null;
        }

        var telemetry = await _telemetry.ReadAsync(devices, cancellationToken);
        if (telemetry.BladeChargeLimitPercent is not int percent)
        {
            return null;
        }

        // shortcut: the shared telemetry facade collapses a disabled limiter to
        // 100 percent, so the exact off-threshold is not available here. The UI
        // only needs the switch plus the bundle default; upgrade if the card must
        // restore the value the firmware held while off.
        var enabled = percent != 100;
        var threshold = enabled
            ? percent
            : BladeBatteryOptimizerProtocol.DefaultThresholdPercent;
        return new BladeBatteryOptimizerState(
            enabled,
            threshold,
            BladeBatteryOptimizerCapabilities.SupportsChargingOverride(productId) &&
            telemetry.BladeOneTimeFullChargeEnabled == true);
    }

    /// <summary>
    /// Applies the optimizer through the existing charge-limiter and power-mode
    /// setters. The one-time override is rejected on models whose bundle does not
    /// declare it.
    /// </summary>
    public async ValueTask<BladeBatteryOptimizerState> ApplyAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        bool enabled,
        int thresholdPercent,
        bool oneTimeOverride,
        CancellationToken cancellationToken = default)
    {
        if (!TryFindSupportedProductId(devices, out var productId))
        {
            throw new NotSupportedException(
                "The battery optimizer is not published by this Blade model.");
        }

        _ = BladeBatteryOptimizerProtocol.Encode(enabled, thresholdPercent);
        var supportsOverride = BladeBatteryOptimizerCapabilities.SupportsChargingOverride(productId);
        if (oneTimeOverride && !supportsOverride)
        {
            throw new NotSupportedException(
                "This Blade model does not publish the one-time charging override.");
        }

        // The existing setter encodes the same enabled bit and threshold, and
        // verifies the readback. Disabling writes the bundle's 0x50 form.
        var actualLimit = await _telemetry.SetBladeChargeLimitAsync(
            devices, enabled ? thresholdPercent : 100, cancellationToken);
        var overrideApplied = false;
        if (supportsOverride)
        {
            overrideApplied = await _telemetry.SetBladeOneTimeFullChargeAsync(
                devices, enabled && oneTimeOverride, cancellationToken);
        }

        return new BladeBatteryOptimizerState(
            IsEnabled: actualLimit != 100,
            ThresholdPercent: actualLimit == 100
                ? BladeBatteryOptimizerProtocol.DefaultThresholdPercent
                : actualLimit,
            OneTimeOverrideEnabled: overrideApplied);
    }

    private static bool TryFindSupportedProductId(
        IReadOnlyList<DeviceDescriptor> devices,
        out ushort productId)
    {
        ArgumentNullException.ThrowIfNull(devices);
        foreach (var device in devices)
        {
            if (BladeBatteryOptimizerCapabilities.IsSupported(device.ProductId))
            {
                productId = device.ProductId;
                return true;
            }
        }

        productId = 0;
        return false;
    }
}
