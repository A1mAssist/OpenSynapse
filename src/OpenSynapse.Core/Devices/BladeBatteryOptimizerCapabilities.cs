namespace OpenSynapse.Core.Devices;

/// <summary>
/// The 2022+ Blade bundles expose a "battery optimizer" panel through
/// <c>deviceUI.power.hasBatteryOptimizer</c>. It is a UI capability, not a
/// separate report: the panel writes the same charge-limiter byte as the older
/// charge-limit card and, on the models that declare it, the same one-time
/// override bit as the full-charge control.
/// </summary>
public static class BladeBatteryOptimizerCapabilities
{
    // Bundle evidence: deviceUI.power.hasBatteryOptimizer = true.
    private static readonly HashSet<ushort> OptimizerProductIds =
    [
        0x028A, 0x028B, 0x028C, 0x029D, 0x029E, 0x029F, 0x02A0,
        0x02B6, 0x02B7, 0x02B8, 0x02C5, 0x02C6, 0x02C7, 0x02E0, 0x02E1,
    ];

    // Bundle evidence: deviceUI.power.hasBatteryChargingOverrideEnabled = true.
    private static readonly HashSet<ushort> ChargingOverrideProductIds =
    [
        0x02B6, 0x02B7, 0x02B8, 0x02C5, 0x02C6, 0x02C7, 0x02E0, 0x02E1,
    ];

    // Bundle evidence: useFeature("getBatteryOptimizerFromFW", "yes").
    private static readonly HashSet<ushort> FirmwareOwnedProductIds = [0x02E0, 0x02E1];

    /// <summary>
    /// True when the model's bundle declares the battery-optimizer panel. Models
    /// without the flag keep the existing charge-limit card but must not show the
    /// optimizer card.
    /// </summary>
    public static bool IsSupported(ushort productId) => OptimizerProductIds.Contains(productId);

    /// <summary>
    /// True when the model's bundle declares <c>hasBatteryChargingOverrideEnabled</c>.
    /// </summary>
    public static bool SupportsChargingOverride(ushort productId) =>
        ChargingOverrideProductIds.Contains(productId);

    /// <summary>
    /// The 2026 bundles read the optimizer state from firmware instead of restoring
    /// a cached value at startup. The report itself is still the charge-limiter report.
    /// </summary>
    public static bool UsesFirmwareState(ushort productId) =>
        FirmwareOwnedProductIds.Contains(productId);
}
