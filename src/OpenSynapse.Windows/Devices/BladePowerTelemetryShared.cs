using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

internal static class BladePowerTelemetryShared
{
    internal static BladeMaxFanMode ToMaxFanMode(byte powerModeMask) =>
        (powerModeMask & BladePowerModeProtocol.MaxFanBit) != 0
            ? BladeMaxFanMode.Enabled
            : BladeMaxFanMode.Disabled;

    internal static async Task<int> ReadChargeLimitAsync(
        RazerCapabilityClient client,
        ResolvedRazerDevice device,
        CancellationToken cancellationToken)
    {
        var response = await client.QueryAsync(
            device, "charge-limit.get", cancellationToken);
        RazerCapabilityClient.EnsureDataSize(response, 1, "Blade charge limit");
        return DecodeChargeLimit(response[RazerFeatureReport.ArgumentsOffset]);
    }

    internal static Task WriteChargeLimitAsync(
        RazerCapabilityClient client,
        ResolvedRazerDevice device,
        byte raw,
        CancellationToken cancellationToken) =>
        client.QueryAsync(device, "charge-limit.set", new byte[] { raw }, cancellationToken);

    /// <summary>
    /// Decodes the charge-limiter report the way the official middleware does: the low
    /// seven bits hold the threshold and the top bit holds the enabled flag. A disabled
    /// limiter charges to 100 percent whatever threshold the firmware reports.
    /// </summary>
    internal static int DecodeChargeLimit(byte value)
    {
        var enabled = (value & 0x80) != 0;
        var threshold = value & 0x7F;
        if (!enabled)
        {
            return 100;
        }

        return threshold switch
        {
            50 => 50,
            55 => 55,
            60 => 60,
            65 => 65,
            70 => 70,
            75 => 75,
            80 => 80,
            _ => throw new InvalidOperationException(
                $"Blade returned an unknown charge-limit code: 0x{value:X2}."),
        };
    }

    internal static byte EncodeChargeLimit(int percent) => percent switch
    {
        50 => 0xB2,
        55 => 0xB7,
        60 => 0xBC,
        65 => 0xC1,
        70 => 0xC6,
        75 => 0xCB,
        80 => 0xD0,
        100 => 0x50,
        _ => throw new ArgumentOutOfRangeException(
            nameof(percent), "Charge limit must be 50, 55, 60, 65, 70, 75, 80, or 100 percent."),
    };

    /// <summary>
    /// Rejects the Max Fan write on models whose firmware does not implement the Custom
    /// maximum-fan bit, so a stale profile cannot reach an unaudited model.
    /// </summary>
    internal static void EnsureBoostAdmitted(ResolvedRazerDevice blade, byte cluster, byte value)
    {
        // Only the CPU cluster carries per-model level restrictions. The GPU cluster
        // admits Low/Medium/High on every audited model.
        if (cluster != BladeBoostProtocol.CpuCluster ||
            value <= (byte)BladeCpuBoostMode.High)
        {
            return;
        }

        if (!blade.AllowsCpuBoost((BladeCpuBoostMode)value))
        {
            throw new InvalidOperationException(
                $"The CPU level 0x{value:X2} is not available on this Blade model.");
        }
    }

    internal static void EnsureMaxFanSupported(ResolvedRazerDevice blade)
    {
        if (!blade.SupportsMaxFan)
        {
            throw new InvalidOperationException(
                "Max Fan is not available on this Blade model; use the fixed fan speed control instead.");
        }
    }
}

internal sealed record BladeBoostState(
    BladeCpuBoostMode Cpu,
    BladeGpuBoostMode Gpu);
