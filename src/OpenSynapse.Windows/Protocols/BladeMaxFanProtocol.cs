using OpenSynapse.Core.Devices;

namespace OpenSynapse.Windows.Protocols;

public static class BladeMaxFanProtocol
{
    public const byte MaxFanBit = 0x02;
    public const byte OneTimeFullChargeBit = 0x04;
    public const byte LocalDimmingBit = 0x08;

    public static BladeMaxFanMode Parse(ReadOnlySpan<byte> response)
    {
        var mask = ParsePowerModeMask(response);
        return (mask & MaxFanBit) != 0 ? BladeMaxFanMode.Enabled : BladeMaxFanMode.Disabled;
    }

    public static byte ParsePowerModeMask(ReadOnlySpan<byte> response) =>
        ParsePowerModeMask(
            response,
            RazerFeatureReport.CreateRequest(0x1F, 0x01, 0x07, 0x8F, new byte[] { 0x00 }));

    internal static byte ParsePowerModeMask(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request)
    {
        if (!RazerFeatureReport.IsSuccessfulResponse(
                request, response, 1, allowRemainingPacketsMismatch: true))
        {
            throw new InvalidOperationException("Blade Max Fan 返回了无效或错序的 feature report。");
        }

        return response[RazerFeatureReport.ArgumentsOffset];
    }

    public static byte[] CreateSetRequest(BladeMaxFanMode mode, byte existingPowerModeMask = 0)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        var mask = mode == BladeMaxFanMode.Enabled
            ? (byte)(existingPowerModeMask | MaxFanBit)
            : (byte)(existingPowerModeMask & ~MaxFanBit);
        return RazerFeatureReport.CreateRequest(
            transactionId: 0x1F,
            dataSize: 0x01,
            commandClass: 0x07,
            commandId: 0x0F,
            new byte[] { mask });
    }

    public static byte[] CreateSetOneTimeFullChargeRequest(bool enabled, byte existingPowerModeMask) =>
        CreateSetPowerModeBitRequest(OneTimeFullChargeBit, enabled, existingPowerModeMask);

    public static byte[] CreateSetLocalDimmingRequest(bool enabled, byte existingPowerModeMask) =>
        CreateSetPowerModeBitRequest(LocalDimmingBit, enabled, existingPowerModeMask);

    private static byte[] CreateSetPowerModeBitRequest(
        byte bit,
        bool enabled,
        byte existingPowerModeMask)
    {
        var mask = enabled
            ? (byte)(existingPowerModeMask | bit)
            : (byte)(existingPowerModeMask & ~bit);
        return CreateSetPowerModeMaskRequest(mask);
    }

    public static byte[] CreateSetPowerModeMaskRequest(byte mask) =>
        RazerFeatureReport.CreateRequest(
            transactionId: 0x1F,
            dataSize: 0x01,
            commandClass: 0x07,
            commandId: 0x0F,
            new byte[] { mask });
}
