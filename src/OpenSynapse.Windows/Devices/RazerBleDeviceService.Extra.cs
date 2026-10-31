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
}