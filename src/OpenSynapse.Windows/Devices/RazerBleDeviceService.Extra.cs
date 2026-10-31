using System.Text;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Protocol 30 settings that the middleware exposes next to battery, DPI and lighting:
/// Snap Tap, debounce, battery chemistry and smart illumination.
/// </summary>
public sealed partial class RazerBleDeviceService
{
    // The middleware writes the live smart-dimming setting to profile 1, then replays it per profile.
    private const byte SmartIlluminationProfileId = 1;

    public async Task<RazerBleSnapTapState> ReadSnapTapAsync(
        byte profileId = BleProfileId,
        CancellationToken cancellationToken = default)
    {
        var response = await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateGetSnapTap(transaction, profileId),
            "get snap tap",
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, "get snap tap");
        return RazerBleProtocol30.ReadSnapTap(response);
    }

    public async Task<RazerBleSnapTapState> SetSnapTapAsync(
        bool enabled,
        byte profileId = BleProfileId,
        CancellationToken cancellationToken = default)
    {
        var current = await ReadSnapTapAsync(profileId, cancellationToken).ConfigureAwait(false);
        var response = await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateSetSnapTap(transaction, profileId, enabled, current.Pairs),
            "set snap tap",
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, "set snap tap");

        var applied = await ReadSnapTapAsync(profileId, cancellationToken).ConfigureAwait(false);
        if (applied.Enabled == enabled)
        {
            return applied;
        }

        _diagnostic?.Invoke($"ble Snap Tap read back {applied.Enabled}, restoring {current.Enabled}");
        await TryRestoreAsync("restore snap tap", async () =>
        {
            var restore = await _session.SendAsync(
                transaction => RazerBleProtocol30.CreateSetSnapTap(
                    transaction, profileId, current.Enabled, current.Pairs),
                "restore snap tap",
                cancellationToken).ConfigureAwait(false);
            EnsureSuccess(restore, "restore snap tap");
        }).ConfigureAwait(false);
        throw new InvalidOperationException(
            $"The device kept Snap Tap {(applied.Enabled ? "on" : "off")} instead of {(enabled ? "on" : "off")}.");
    }

    public async Task<RazerBleDebounceState> ReadDebounceAsync(CancellationToken cancellationToken = default)
    {
        var response = await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateGetDebounce(transaction),
            "get debounce",
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, "get debounce");
        return RazerBleProtocol30.ReadDebounce(response);
    }

    public async Task<RazerBleDebounceState> SetDebounceAsync(
        RazerBleDebounceState state,
        CancellationToken cancellationToken = default)
    {
        var current = await ReadDebounceAsync(cancellationToken).ConfigureAwait(false);
        var response = await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateSetDebounce(transaction, state),
            "set debounce",
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, "set debounce");

        var applied = await ReadDebounceAsync(cancellationToken).ConfigureAwait(false);
        if (applied == state)
        {
            return applied;
        }

        _diagnostic?.Invoke($"ble debounce read back {applied}, restoring {current}");
        await TryRestoreAsync("restore debounce", async () =>
        {
            var restore = await _session.SendAsync(
                transaction => RazerBleProtocol30.CreateSetDebounce(transaction, current),
                "restore debounce",
                cancellationToken).ConfigureAwait(false);
            EnsureSuccess(restore, "restore debounce");
        }).ConfigureAwait(false);
        throw new InvalidOperationException(
            "The device kept different debounce values; the previous values were restored.");
    }

    public async Task<RazerBleBatteryChemistry> ReadBatteryChemistryAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateGetBatteryDischargeCurve(transaction),
            "get battery chemistry",
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, "get battery chemistry");
        return (RazerBleBatteryChemistry)RazerBleProtocol30.ReadBatteryDischargeCurve(response);
    }

    public async Task<RazerBleBatteryChemistry> SetBatteryChemistryAsync(
        RazerBleBatteryChemistry chemistry,
        CancellationToken cancellationToken = default)
    {
        var current = await ReadBatteryChemistryAsync(cancellationToken).ConfigureAwait(false);
        var response = await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateSetBatteryDischargeCurve(transaction, (byte)chemistry),
            "set battery chemistry",
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, "set battery chemistry");

        var applied = await ReadBatteryChemistryAsync(cancellationToken).ConfigureAwait(false);
        if (applied == chemistry)
        {
            return applied;
        }

        await TryRestoreAsync("restore battery chemistry", async () =>
        {
            var restore = await _session.SendAsync(
                transaction => RazerBleProtocol30.CreateSetBatteryDischargeCurve(transaction, (byte)current),
                "restore battery chemistry",
                cancellationToken).ConfigureAwait(false);
            EnsureSuccess(restore, "restore battery chemistry");
        }).ConfigureAwait(false);
        throw new InvalidOperationException(
            "The device kept a different battery chemistry; the previous value was restored.");
    }

    public async Task<RazerBleSmartIlluminationState> ReadSmartIlluminationAsync(
        byte profileId = SmartIlluminationProfileId,
        CancellationToken cancellationToken = default)
    {
        var response = await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateGetSmartIllumination(transaction, profileId),
            "get smart illumination",
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, "get smart illumination");
        return RazerBleProtocol30.ReadSmartIllumination(response);
    }

    public async Task<RazerBleSmartIlluminationState> SetSmartIlluminationAsync(
        byte status,
        byte mode,
        byte profileId = SmartIlluminationProfileId,
        CancellationToken cancellationToken = default)
    {
        var current = await ReadSmartIlluminationAsync(profileId, cancellationToken).ConfigureAwait(false);
        var response = await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateSetSmartIllumination(transaction, profileId, status, mode),
            "set smart illumination",
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, "set smart illumination");

        var applied = await ReadSmartIlluminationAsync(profileId, cancellationToken).ConfigureAwait(false);
        if (applied == new RazerBleSmartIlluminationState(status, mode))
        {
            return applied;
        }

        await TryRestoreAsync("restore smart illumination", async () =>
        {
            var restore = await _session.SendAsync(
                transaction => RazerBleProtocol30.CreateSetSmartIllumination(
                    transaction, profileId, current.Status, current.Mode),
                "restore smart illumination",
                cancellationToken).ConfigureAwait(false);
            EnsureSuccess(restore, "restore smart illumination");
        }).ConfigureAwait(false);
        throw new InvalidOperationException(
            "The device kept different smart illumination settings; the previous values were restored.");
    }

    public async Task<byte> ReadScrollMaskingTimeAsync(CancellationToken cancellationToken = default)
    {
        var response = await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateGetScrollMaskingTime(transaction),
            "get scroll masking time",
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, "get scroll masking time");
        return RazerBleProtocol30.ReadSingleByte(response);
    }

    public async Task<byte> SetScrollMaskingTimeAsync(byte milliseconds, CancellationToken cancellationToken = default)
    {
        var current = await ReadScrollMaskingTimeAsync(cancellationToken).ConfigureAwait(false);
        var response = await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateSetScrollMaskingTime(transaction, milliseconds),
            "set scroll masking time",
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, "set scroll masking time");

        var applied = await ReadScrollMaskingTimeAsync(cancellationToken).ConfigureAwait(false);
        if (applied == milliseconds)
        {
            return applied;
        }

        await TryRestoreAsync("restore scroll masking time", async () =>
        {
            var restore = await _session.SendAsync(
                transaction => RazerBleProtocol30.CreateSetScrollMaskingTime(transaction, current),
                "restore scroll masking time",
                cancellationToken).ConfigureAwait(false);
            EnsureSuccess(restore, "restore scroll masking time");
        }).ConfigureAwait(false);
        throw new InvalidOperationException(
            "The device kept a different scroll masking time; the previous value was restored.");
    }

    public async Task<byte> ReadScrollAccelerationLevelAsync(CancellationToken cancellationToken = default)
    {
        var response = await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateGetScrollAccelerationLevel(transaction),
            "get scroll acceleration level",
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, "get scroll acceleration level");
        return RazerBleProtocol30.ReadSingleByte(response);
    }

    public async Task<byte> SetScrollAccelerationLevelAsync(byte level, CancellationToken cancellationToken = default)
    {
        var current = await ReadScrollAccelerationLevelAsync(cancellationToken).ConfigureAwait(false);
        var response = await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateSetScrollAccelerationLevel(transaction, level),
            "set scroll acceleration level",
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, "set scroll acceleration level");

        var applied = await ReadScrollAccelerationLevelAsync(cancellationToken).ConfigureAwait(false);
        if (applied == level)
        {
            return applied;
        }

        await TryRestoreAsync("restore scroll acceleration level", async () =>
        {
            var restore = await _session.SendAsync(
                transaction => RazerBleProtocol30.CreateSetScrollAccelerationLevel(transaction, current),
                "restore scroll acceleration level",
                cancellationToken).ConfigureAwait(false);
            EnsureSuccess(restore, "restore scroll acceleration level");
        }).ConfigureAwait(false);
        throw new InvalidOperationException(
            "The device kept a different scroll acceleration level; the previous value was restored.");
    }

    public async Task<RazerBleScrollTactileMode> ReadScrollTactileModeAsync(CancellationToken cancellationToken = default)
    {
        var response = await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateGetScrollTactileMode(transaction),
            "get scroll tactile mode",
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, "get scroll tactile mode");
        return (RazerBleScrollTactileMode)RazerBleProtocol30.ReadSingleByte(response);
    }

    public async Task<RazerBleScrollTactileMode> SetScrollTactileModeAsync(
        RazerBleScrollTactileMode mode,
        CancellationToken cancellationToken = default)
    {
        var current = await ReadScrollTactileModeAsync(cancellationToken).ConfigureAwait(false);
        var response = await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateSetScrollTactileMode(transaction, mode),
            "set scroll tactile mode",
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, "set scroll tactile mode");

        var applied = await ReadScrollTactileModeAsync(cancellationToken).ConfigureAwait(false);
        if (applied == mode)
        {
            return applied;
        }

        await TryRestoreAsync("restore scroll tactile mode", async () =>
        {
            var restore = await _session.SendAsync(
                transaction => RazerBleProtocol30.CreateSetScrollTactileMode(transaction, current),
                "restore scroll tactile mode",
                cancellationToken).ConfigureAwait(false);
            EnsureSuccess(restore, "restore scroll tactile mode");
        }).ConfigureAwait(false);
        throw new InvalidOperationException(
            "The device kept a different scroll tactile mode; the previous value was restored.");
    }

    public async Task<RazerBlePowerPlan> ReadPowerPlanAsync(
        byte profileId = BleProfileId,
        CancellationToken cancellationToken = default)
    {
        var response = await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateGetPowerPlan(transaction, profileId),
            "get power plan",
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, "get power plan");
        return (RazerBlePowerPlan)RazerBleProtocol30.ReadSingleByte(response);
    }

    public async Task<RazerBlePowerPlan> SetPowerPlanAsync(
        RazerBlePowerPlan plan,
        byte profileId = BleProfileId,
        CancellationToken cancellationToken = default)
    {
        var current = await ReadPowerPlanAsync(profileId, cancellationToken).ConfigureAwait(false);
        var response = await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateSetPowerPlan(transaction, plan, profileId),
            "set power plan",
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, "set power plan");

        var applied = await ReadPowerPlanAsync(profileId, cancellationToken).ConfigureAwait(false);
        if (applied == plan)
        {
            return applied;
        }

        await TryRestoreAsync("restore power plan", async () =>
        {
            var restore = await _session.SendAsync(
                transaction => RazerBleProtocol30.CreateSetPowerPlan(transaction, current, profileId),
                "restore power plan",
                cancellationToken).ConfigureAwait(false);
            EnsureSuccess(restore, "restore power plan");
        }).ConfigureAwait(false);
        throw new InvalidOperationException(
            "The device kept a different power plan; the previous value was restored.");
    }

    public async Task<(byte Active, byte Max)> ReadHapticStageAsync(
        byte profileId = 1,
        CancellationToken cancellationToken = default)
    {
        var active = await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateGetHapticActiveStage(transaction, profileId),
            "get haptic active stage",
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(active, "get haptic active stage");

        var max = await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateGetHapticMaxStage(transaction, profileId),
            "get haptic max stage",
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(max, "get haptic max stage");

        return (RazerBleProtocol30.ReadSingleByte(active), RazerBleProtocol30.ReadSingleByte(max));
    }

    public async Task<byte> SetHapticStageAsync(
        byte stage,
        byte profileId = 1,
        CancellationToken cancellationToken = default)
    {
        var (current, _) = await ReadHapticStageAsync(profileId, cancellationToken).ConfigureAwait(false);
        var response = await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateSetHapticActiveStage(transaction, profileId, stage),
            "set haptic active stage",
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, "set haptic active stage");

        var (applied, _) = await ReadHapticStageAsync(profileId, cancellationToken).ConfigureAwait(false);
        if (applied == stage)
        {
            return applied;
        }

        await TryRestoreAsync("restore haptic active stage", async () =>
        {
            var restore = await _session.SendAsync(
                transaction => RazerBleProtocol30.CreateSetHapticActiveStage(transaction, profileId, current),
                "restore haptic active stage",
                cancellationToken).ConfigureAwait(false);
            EnsureSuccess(restore, "restore haptic active stage");
        }).ConfigureAwait(false);
        throw new InvalidOperationException(
            "The device kept a different haptic stage; the previous stage was restored.");
    }

    /// <summary>
    /// Reads the 250-byte onboard profile block in 78-byte chunks and returns the name at
    /// bytes 16..115. Returns null when the device refuses the read.
    /// </summary>
    public async Task<string?> ReadProfileNameAsync(
        byte profileId,
        CancellationToken cancellationToken = default)
    {
        const int TotalLength = 250;
        const int ChunkLength = 78;
        var buffer = new byte[TotalLength];

        for (var offset = 0; offset < TotalLength; offset += ChunkLength)
        {
            var length = Math.Min(ChunkLength, TotalLength - offset);
            var response = await _session.SendAsync(
                transaction => RazerBleProtocol30.CreateGetProfileName(
                    transaction, profileId, (ushort)offset, (ushort)length),
                "get profile name",
                cancellationToken).ConfigureAwait(false);
            EnsureSuccess(response, "get profile name");

            var chunk = RazerBleProtocol30.ReadProfileNameChunk(response);
            if (chunk.Length == 0)
            {
                return null;
            }

            Array.Copy(chunk, 0, buffer, offset, Math.Min(chunk.Length, length));
        }

        return DecodeProfileName(buffer);
    }

    private static string DecodeProfileName(byte[] buffer)
    {
        var name = Encoding.UTF8.GetString(buffer, 16, 100);
        var end = name.IndexOf('\0');
        return (end >= 0 ? name[..end] : name).Trim();
    }
}
