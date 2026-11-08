using OpenSynapse.Core.Devices;

namespace OpenSynapse.Windows.Devices;

internal static class BladeCapabilityErrors
{
    private static readonly IReadOnlyDictionary<string, string> CapabilityByLabel =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Keyboard brightness"] = CapabilityIds.KeyboardBrightnessGet,
            ["Keyboard effect"] = CapabilityIds.KeyboardEffectGet,
            ["Performance and fan state"] = CapabilityIds.ThermalStateGet,
            ["Performance mode"] = CapabilityIds.ThermalStateGet,
            ["Fan mode"] = CapabilityIds.ThermalStateGet,
            ["Fan target"] = CapabilityIds.FanTargetGet,
            ["Current CPU fan speed"] = CapabilityIds.CurrentFanRpmGet,
            ["Current GPU fan speed"] = CapabilityIds.CurrentFanRpmGet,
            ["CPU advanced fan mode"] = CapabilityIds.AdvancedFanModeGet,
            ["GPU advanced fan mode"] = CapabilityIds.AdvancedFanModeGet,
            ["CPU/GPU Boost"] = CapabilityIds.BoostGet,
            ["Charge limit"] = CapabilityIds.ChargeLimitGet,
            ["Power Mode Control"] = CapabilityIds.PowerModeGet,
            ["Gaming mode"] = CapabilityIds.GamingModeGet,
            ["Startup animation"] = CapabilityIds.StartupAnimationGet,
            ["Fn row behavior"] = CapabilityIds.FnKeyGet,
            ["MCU firmware version"] = CapabilityIds.McuVersionGet,
            ["Adapter power"] = CapabilityIds.AdapterPowerGet,
            ["Auto sleep"] = CapabilityIds.AutoSleepGet,
            ["Time to dim"] = CapabilityIds.TimeToDimGet,
            ["Indicator LED"] = CapabilityIds.IndicatorLedGet,
            ["GPU card detection"] = CapabilityIds.GpuCardDetectionGet,
            ["Native display mode"] = CapabilityIds.NativeDisplayModeGet,
            ["SKU hardware configuration"] = CapabilityIds.SkuHardwareConfigurationGet,
            ["Blade Logo"] = CapabilityIds.LogoModeGet,
        };

    public static bool TryGetUnsupportedCapability(string error, out string capability)
    {
        capability = string.Empty;
        if (!error.Contains("does not support this command", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var separator = error.IndexOf(':');
        if (separator <= 0 ||
            !CapabilityByLabel.TryGetValue(error[..separator].Trim(), out var found))
        {
            return false;
        }

        capability = found;
        return true;
    }
}
