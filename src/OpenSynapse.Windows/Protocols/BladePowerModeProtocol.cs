using OpenSynapse.Core.Devices;

namespace OpenSynapse.Windows.Protocols;

public static class BladePowerModeProtocol
{
    /// <summary>
    /// Power Mode Control bit 0: Synapse's `cpuBoost`, "allows the CPU to draw more power
    /// when the GPU is idle". It is a level bit on the same 07:8F / 07:0F report as the
    /// other three, not a wattage.
    /// </summary>
    public const byte CpuPowerBoostBit = 0x01;

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
            throw new InvalidOperationException("Blade Max Fan returned an invalid or out-of-order feature report.");
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

    /// <summary>
    /// Applies the CPU Boost bit without touching the other three. Synapse's
    /// `setPowerModeControl` writes the OR of the decoded bits, so a read-modify-write
    /// submits the same byte the middleware would.
    /// </summary>
    public static byte ApplyCpuPowerBoost(byte existingPowerModeMask, bool enabled) =>
        enabled
            ? (byte)(existingPowerModeMask | CpuPowerBoostBit)
            : (byte)(existingPowerModeMask & ~CpuPowerBoostBit);

    public static byte[] CreateSetCpuPowerBoostRequest(bool enabled, byte existingPowerModeMask) =>
        CreateSetPowerModeMaskRequest(ApplyCpuPowerBoost(existingPowerModeMask, enabled));

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
