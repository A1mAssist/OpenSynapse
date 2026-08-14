namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Product 184 low-battery-threshold wire encoding. Production SET remains
/// unavailable until the current device passes write/readback/restore.
/// </summary>
public static class ViperLowBatteryThresholdProtocol
{
    public const int MinimumPercent = 5;
    public const int MaximumPercent = 100;
    public const int PercentStep = 5;

    public static byte ParseRaw(ReadOnlySpan<byte> response) =>
        ParseRaw(response, RazerFeatureReport.CreateRequest(
            0x1F, 0x01, 0x07, 0x81, ReadOnlySpan<byte>.Empty));

    internal static byte ParseRaw(ReadOnlySpan<byte> response, ReadOnlySpan<byte> request)
    {
        if (!RazerFeatureReport.IsSuccessfulResponse(request, response, 1))
        {
            throw new InvalidOperationException("Viper 低电量阈值返回了无效或错序的 feature report。");
        }

        return response[RazerFeatureReport.ArgumentsOffset];
    }

    public static int ToPercent(byte raw) => raw * 100 / 255;

    public static byte ToRaw(int percent)
    {
        if (percent is < MinimumPercent or > MaximumPercent || percent % PercentStep != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(percent),
                $"Viper 低电量阈值必须在 {MinimumPercent}% 到 {MaximumPercent}% 之间，并且是 {PercentStep}% 的倍数。");
        }

        // Official Product 184 SET: ceil(percent / 100 * 255).
        return checked((byte)((percent * 255 + 99) / 100));
    }

    public static string Format(byte raw) => $"{ToPercent(raw)}% (raw 0x{raw:X2})";
}
