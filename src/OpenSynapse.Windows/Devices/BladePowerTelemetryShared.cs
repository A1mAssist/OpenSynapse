using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

internal static class BladePowerTelemetryShared
{
    internal static async Task SetPerformanceMaxFanAsync(
        RazerCapabilityClient client, ResolvedRazerDevice device, bool enabled, bool skip,
        CancellationToken cancellationToken)
    {
        if (skip) return;
        var mask = await ReadPowerMaskAsync(client, device, cancellationToken);
        if (((mask & BladePowerModeProtocol.MaxFanBit) != 0) == enabled) return;
        // Preserve the existing HyperBoost restriction on pending/unknown bits.
        if (enabled && (mask & ~(BladePowerModeProtocol.MaxFanBit |
                BladePowerModeProtocol.CpuPowerBoostBit | BladePowerModeProtocol.LocalDimmingBit)) != 0)
            throw new InvalidOperationException("HyperBoost cannot pin Max Fan while an unverified power-mode bit is active.");
        var expected = enabled ? (byte)(mask | BladePowerModeProtocol.MaxFanBit)
            : (byte)(mask & ~BladePowerModeProtocol.MaxFanBit);
        await WritePowerMaskAsync(client, device, expected, cancellationToken);
        if (await ReadPowerMaskAsync(client, device, cancellationToken) != expected)
            throw new InvalidOperationException("Performance Max Fan readback did not match the requested state.");
    }

    internal static async Task<bool> TryRestoreMaxFanBitAsync(
        RazerCapabilityClient client, ResolvedRazerDevice device, byte originalMask)
    {
        try
        {
            var current = await ReadPowerMaskAsync(client, device, CancellationToken.None);
            var expected = (byte)((current & ~BladePowerModeProtocol.MaxFanBit) |
                (originalMask & BladePowerModeProtocol.MaxFanBit));
            if (current != expected)
                await WritePowerMaskAsync(client, device, expected, CancellationToken.None);
            return await ReadPowerMaskAsync(client, device, CancellationToken.None) == expected;
        }
        catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
        {
            return false;
        }
    }

    private static async Task<byte> ReadPowerMaskAsync(
        RazerCapabilityClient client, ResolvedRazerDevice device, CancellationToken cancellationToken) =>
        BladePowerModeProtocol.ParsePowerModeMask(
            await client.QueryAsync(device, CapabilityIds.PowerModeGet, cancellationToken),
            RazerCapabilityClient.CreateRequest(device, CapabilityIds.PowerModeGet));

    private static Task WritePowerMaskAsync(
        RazerCapabilityClient client, ResolvedRazerDevice device, byte mask, CancellationToken cancellationToken) =>
        client.QueryBuiltRequestAsync(device, CapabilityIds.PowerModeSet,
            BladePowerModeProtocol.CreateSetPowerModeMaskRequest(mask), cancellationToken);

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
            device, CapabilityIds.ChargeLimitGet, cancellationToken);
        RazerCapabilityClient.EnsureDataSize(response, 1, "Blade charge limit");
        return DecodeChargeLimit(response[RazerFeatureReport.ArgumentsOffset]);
    }

    internal static Task WriteChargeLimitAsync(
        RazerCapabilityClient client,
        ResolvedRazerDevice device,
        byte raw,
        CancellationToken cancellationToken) =>
        client.QueryAsync(device, CapabilityIds.ChargeLimitSet, new byte[] { raw }, cancellationToken);

    internal static async Task<int> WriteAndReadChargeLimitAsync(
        RazerCapabilityClient client,
        ResolvedRazerDevice device,
        byte raw,
        int expected,
        CancellationToken cancellationToken)
    {
        Exception? unacknowledgedWrite = null;
        try
        {
            await WriteChargeLimitAsync(client, device, raw, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            // Some Blade ECs persist the limiter but do not return a report the
            // transport can match. The charge-limit read is the authoritative result.
            unacknowledgedWrite = exception;
        }

        var actual = await ReadChargeLimitAfterWriteAsync(
            client, device, expected, cancellationToken);
        if (actual != expected)
        {
            var detail = unacknowledgedWrite is null
                ? string.Empty
                : $" The write was not acknowledged: {unacknowledgedWrite.Message}";
            throw new InvalidOperationException(
                $"Charge limit readback mismatch: wrote {expected}%, read {actual}%.{detail}");
        }

        return actual;
    }

    /// <summary>
    /// Some ECs acknowledge the limiter write before the new threshold is visible on
    /// the read path. Poll briefly so a stale first report does not roll the choice back.
    /// </summary>
    internal static async Task<int> ReadChargeLimitAfterWriteAsync(
        RazerCapabilityClient client,
        ResolvedRazerDevice device,
        int expected,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2.5);
        while (true)
        {
            var actual = await ReadChargeLimitAsync(client, device, cancellationToken);
            if (actual == expected || DateTimeOffset.UtcNow >= deadline)
            {
                return actual;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }
    }

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
