using OpenSynapse.Core.Devices;

namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Product 710 (Blade 16 2025) reports used by the local Synapse product code.
/// </summary>
public static class BladeProduct710Protocol
{
    public const byte TransactionId = 0x1F;

    public static byte[] CreateGetNativeDisplayModeRequest() =>
        RazerFeatureReport.CreateRequest(TransactionId, 0x01, 0x0D, 0x8E, new byte[] { 0x00 });

    public static byte[] CreateSetNativeDisplayModeRequest(BladeNativeDisplayMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        return RazerFeatureReport.CreateRequest(
            TransactionId, 0x01, 0x0D, 0x0E, new[] { (byte)mode });
    }

    public static byte[] CreateGetSkuHardwareConfigurationRequest() =>
        RazerFeatureReport.CreateRequest(TransactionId, 0x01, 0x0D, 0x8F, new byte[] { 0x00 });

    public static BladeNativeDisplayMode ParseNativeDisplayMode(ReadOnlySpan<byte> response)
        => ParseNativeDisplayMode(response, CreateGetNativeDisplayModeRequest());

    internal static BladeNativeDisplayMode ParseNativeDisplayMode(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request)
    {
        Validate(response, request, 1, "Blade 原生显示模式", allowRemainingPacketsMismatch: true);
        var mode = (BladeNativeDisplayMode)response[RazerFeatureReport.ArgumentsOffset];
        return Enum.IsDefined(mode)
            ? mode
            : throw new InvalidOperationException($"Blade 返回了未知原生显示模式 0x{(byte)mode:X2}。");
    }

    public static BladeSkuHardwareConfiguration ParseSkuHardwareConfiguration(
        ReadOnlySpan<byte> response)
        => ParseSkuHardwareConfiguration(response, CreateGetSkuHardwareConfigurationRequest());

    internal static BladeSkuHardwareConfiguration ParseSkuHardwareConfiguration(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request)
    {
        Validate(response, request, 1, "Blade SKU 硬件配置", allowRemainingPacketsMismatch: true);
        var raw = response[RazerFeatureReport.ArgumentsOffset];
        return new(
            (raw & 0x01) != 0,
            (raw & 0x02) != 0,
            (raw & 0x04) != 0,
            raw);
    }

    private static void Validate(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request,
        byte minimumArguments,
        string feature,
        bool allowRemainingPacketsMismatch = false)
    {
        if (!RazerFeatureReport.IsSuccessfulResponse(
                request,
                response,
                minimumArguments,
                allowRemainingPacketsMismatch))
        {
            throw new InvalidOperationException($"{feature}返回了无效或错序的 feature report。");
        }
    }
}
