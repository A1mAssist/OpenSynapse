namespace OpenSynapse.Core.Devices;

/// <summary>
/// The battery optimizer uses the charge-limiter report: the official bundle
/// writes one byte to 07:12 as <c>(enabled &lt;&lt; 7) | value</c> and reads it
/// back from 07:92 with the same split. The optional one-time override is bit 2
/// of the 07:0F power-mode mask, which the existing full-charge control owns.
/// </summary>
public static class BladeBatteryOptimizerProtocol
{
    public const int EnabledBit = 0x80;
    public const int ThresholdMask = 0x7F;
    public const int DefaultThresholdPercent = 80;

    /// <summary>
    /// The thresholds the existing charge-limit control exposes. The bundle does
    /// not publish a slider range, so the optimizer reuses this audited set.
    /// </summary>
    public static IReadOnlyList<int> SupportedThresholds { get; } =
        Array.AsReadOnly(new[] { 50, 55, 60, 65, 70, 75, 80 });

    public static bool IsSupportedThreshold(int percent) =>
        SupportedThresholds.Contains(percent);

    public static byte Encode(bool enabled, int thresholdPercent)
    {
        if (!IsSupportedThreshold(thresholdPercent))
        {
            throw new ArgumentOutOfRangeException(
                nameof(thresholdPercent),
                thresholdPercent,
                "Battery optimizer threshold must be 50, 55, 60, 65, 70, 75, or 80 percent.");
        }

        return (byte)((enabled ? EnabledBit : 0) | thresholdPercent);
    }

    public static BladeBatteryOptimizerState Decode(
        byte raw,
        bool oneTimeOverrideEnabled = false) =>
        new(
            IsEnabled: (raw & EnabledBit) != 0,
            ThresholdPercent: raw & ThresholdMask,
            OneTimeOverrideEnabled: oneTimeOverrideEnabled);
}
