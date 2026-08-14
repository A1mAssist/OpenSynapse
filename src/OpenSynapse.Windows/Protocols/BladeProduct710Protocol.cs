namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Product 710 (Blade 16 2025) read-only power reports used by the local Synapse product code.
/// </summary>
public static class BladeProduct710Protocol
{
    public const byte TransactionId = 0x1F;
    public const byte WiredBatteryId = 0x00;

    public static byte[] CreateGetBatteryLevelRequest() =>
        RazerFeatureReport.CreateRequest(TransactionId, 0x02, 0x07, 0x80, new[] { WiredBatteryId });

    public static byte[] CreateGetChargingStatusRequest() =>
        RazerFeatureReport.CreateRequest(TransactionId, 0x02, 0x07, 0x84, new[] { WiredBatteryId });

    public static byte[] CreateGetAutoSleepRequest() =>
        RazerFeatureReport.CreateRequest(TransactionId, 0x02, 0x07, 0x88, new[] { WiredBatteryId });

    public static byte[] CreateGetTimeToSleepRequest() =>
        RazerFeatureReport.CreateRequest(TransactionId, 0x02, 0x07, 0x83, ReadOnlySpan<byte>.Empty);

    public static int ParseBatteryPercent(ReadOnlySpan<byte> response) =>
        ParseBatteryPercent(response, CreateGetBatteryLevelRequest());

    internal static int ParseBatteryPercent(ReadOnlySpan<byte> response, ReadOnlySpan<byte> request)
    {
        ValidateBatteryObject(response, request);

        var raw = response[RazerFeatureReport.ArgumentsOffset + 1];
        return (int)Math.Floor(raw * 100d / 255d);
    }

    public static byte ParseChargingStatusRaw(ReadOnlySpan<byte> response) =>
        ParseChargingStatusRaw(response, CreateGetChargingStatusRequest());

    internal static byte ParseChargingStatusRaw(ReadOnlySpan<byte> response, ReadOnlySpan<byte> request)
    {
        ValidateBatteryObject(response, request);
        return response[RazerFeatureReport.ArgumentsOffset + 1];
    }

    public static byte ParseAutoSleepRaw(ReadOnlySpan<byte> response) =>
        ParseAutoSleepRaw(response, CreateGetAutoSleepRequest());

    internal static byte ParseAutoSleepRaw(ReadOnlySpan<byte> response, ReadOnlySpan<byte> request)
    {
        ValidateBatteryObject(response, request);
        return response[RazerFeatureReport.ArgumentsOffset + 1];
    }

    public static int ParseTimeToSleepSeconds(ReadOnlySpan<byte> response) =>
        ParseTimeToSleepSeconds(response, CreateGetTimeToSleepRequest());

    internal static int ParseTimeToSleepSeconds(ReadOnlySpan<byte> response, ReadOnlySpan<byte> request)
    {
        Validate(response, request);
        return (response[RazerFeatureReport.ArgumentsOffset] << 8) |
            response[RazerFeatureReport.ArgumentsOffset + 1];
    }

    private static void ValidateBatteryObject(ReadOnlySpan<byte> response, ReadOnlySpan<byte> request)
    {
        Validate(response, request);
        if (response[RazerFeatureReport.ArgumentsOffset] != WiredBatteryId)
        {
            throw new InvalidOperationException("Blade 返回了错误的电池对象。");
        }
    }

    private static void Validate(ReadOnlySpan<byte> response, ReadOnlySpan<byte> request)
    {
        if (!RazerFeatureReport.IsSuccessfulResponse(request, response, 2))
        {
            throw new InvalidOperationException("Blade 电源返回了无效或错序的 feature report。");
        }
    }
}
