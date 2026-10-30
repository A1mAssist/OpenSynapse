using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed record RazerGamepadPollingProfileState(
    IReadOnlyList<byte> ProfileIds,
    byte? ActiveProfileId,
    int? PollingRateHertz,
    IReadOnlyList<string> Errors);

public sealed partial class RazerGamepadService
{
    private static readonly int[] StandardPollingRates = [1_000, 500, 333, 250, 200, 125, 100];
    private static readonly int[] HighSpeedPollingRates = [8_000, 4_000, 2_000, 1_000, 500, 250, 125];

    public static IReadOnlyList<int> PollingRatesFor(ushort productId) =>
        UsesHighSpeedPolling(productId) ? HighSpeedPollingRates : StandardPollingRates;

    public static bool SupportsProfileSwitch(ushort productId) => productId != 0x1013;

    public static bool SupportsSingleProfile(ushort productId) => productId is
        0x0A45 or 0x0A57 or 0x0A5A or 0x0A74 or 0x0A7C or 0x1025 or 0x1030 or 0x1013;

    public async Task<RazerGamepadPollingProfileState> ReadPollingProfileAsync(
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        IReadOnlyList<byte> profileIds = [];
        if (SupportsProfileSwitch(_productId))
        {
            try
            {
                profileIds = RazerGamepadProtocol.ReadProfileIdList(
                    await QueryAsync(
                        RazerGamepadProtocol.CreateGetProfileIdListRequest,
                        "read profile id list",
                        cancellationToken).ConfigureAwait(false));
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                errors.Add("profile list");
                _diagnostic?.Invoke($"gamepad profile list unavailable: {exception.Message}");
            }
        }

        var active = await TryReadAsync(
            "active profile",
            RazerGamepadProtocol.CreateGetActiveProfileIdRequest,
            RazerGamepadProtocol.ReadActiveProfileId,
            cancellationToken).ConfigureAwait(false);
        if (active.Ok)
        {
            _profileId = active.Value;
        }

        int? polling = null;
        if (PollingProfileId is not byte profile)
        {
            errors.Add("active profile");
        }
        else
        {
            try
            {
                var highSpeed = UsesHighSpeedPolling(_productId);
                var code = RazerGamepadProtocol.ReadPollingCode(
                    await QueryAsync(
                        transaction => highSpeed
                            ? RazerGamepadProtocol.CreateGetHighSpeedPollingRateRequest(transaction, profile)
                            : RazerGamepadProtocol.CreateGetProfilePollingRateRequest(transaction, profile),
                        "read polling rate",
                        cancellationToken).ConfigureAwait(false),
                    profile);
                polling = highSpeed
                    ? RazerGamepadProtocol.DecodeHighSpeedPollingRate(code)
                    : RazerGamepadProtocol.DecodeStandardPollingRate(code);
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                errors.Add("polling rate");
                _diagnostic?.Invoke($"gamepad polling rate unavailable: {exception.Message}");
            }
        }

        return new RazerGamepadPollingProfileState(
            profileIds,
            active.Ok ? active.Value : null,
            polling,
            errors);
    }

    public async Task<int> SetPollingRateAsync(int hertz, CancellationToken cancellationToken = default)
    {
        var highSpeed = UsesHighSpeedPolling(_productId);
        if (!PollingRatesFor(_productId).Contains(hertz))
        {
            throw new ArgumentOutOfRangeException(nameof(hertz), "Polling rate is not declared for this gamepad.");
        }

        if (PollingProfileId is not byte profile)
        {
            throw new InvalidOperationException(
                "The active gamepad profile is unknown; refusing to write a polling rate.");
        }
        var current = await QueryAsync(
            transaction => highSpeed
                ? RazerGamepadProtocol.CreateGetHighSpeedPollingRateRequest(transaction, profile)
                : RazerGamepadProtocol.CreateGetProfilePollingRateRequest(transaction, profile),
            "read polling rate",
            cancellationToken).ConfigureAwait(false);
        var currentCode = RazerGamepadProtocol.ReadPollingCode(current, profile);
        var currentRate = highSpeed
            ? RazerGamepadProtocol.DecodeHighSpeedPollingRate(currentCode)
            : RazerGamepadProtocol.DecodeStandardPollingRate(currentCode);
        if (currentRate == hertz)
        {
            return currentRate;
        }

        var requestedCode = highSpeed
            ? RazerGamepadProtocol.EncodeHighSpeedPollingRate(hertz)
            : RazerGamepadProtocol.EncodeStandardPollingRate(hertz);
        await QueryAsync(
            transaction => highSpeed
                ? RazerGamepadProtocol.CreateSetHighSpeedPollingRateRequest(transaction, profile, requestedCode)
                : RazerGamepadProtocol.CreateSetProfilePollingRateRequest(transaction, profile, requestedCode),
            "set polling rate",
            cancellationToken).ConfigureAwait(false);

        var applied = await QueryAsync(
            transaction => highSpeed
                ? RazerGamepadProtocol.CreateGetHighSpeedPollingRateRequest(transaction, profile)
                : RazerGamepadProtocol.CreateGetProfilePollingRateRequest(transaction, profile),
            "verify polling rate",
            cancellationToken).ConfigureAwait(false);
        var appliedCode = RazerGamepadProtocol.ReadPollingCode(applied, profile);
        var appliedRate = highSpeed
            ? RazerGamepadProtocol.DecodeHighSpeedPollingRate(appliedCode)
            : RazerGamepadProtocol.DecodeStandardPollingRate(appliedCode);
        if (appliedRate == hertz)
        {
            return appliedRate;
        }

        _diagnostic?.Invoke($"gamepad polling read back {appliedRate} Hz, restoring {currentRate} Hz");
        await QueryAsync(
            transaction => highSpeed
                ? RazerGamepadProtocol.CreateSetHighSpeedPollingRateRequest(transaction, profile, currentCode)
                : RazerGamepadProtocol.CreateSetProfilePollingRateRequest(transaction, profile, currentCode),
            "restore polling rate",
            cancellationToken).ConfigureAwait(false);
        throw new InvalidOperationException(
            $"The gamepad kept {appliedRate} Hz instead of {hertz} Hz; the previous rate was restored.");
    }

    public async Task SetActiveProfileAsync(byte profileId, CancellationToken cancellationToken = default)
    {
        if (!SupportsProfileSwitch(_productId))
        {
            throw new NotSupportedException("This gamepad does not expose profile switching.");
        }

        var current = await TryReadAsync(
            "active profile",
            RazerGamepadProtocol.CreateGetActiveProfileIdRequest,
            RazerGamepadProtocol.ReadActiveProfileId,
            cancellationToken).ConfigureAwait(false);
        if (current.Ok && current.Value == profileId)
        {
            _profileId = profileId;
            return;
        }

        await QueryAsync(
            transaction => RazerGamepadProtocol.CreateSetActiveProfileIdRequest(transaction, profileId),
            "set active profile",
            cancellationToken).ConfigureAwait(false);
        var applied = await TryReadAsync(
            "active profile",
            RazerGamepadProtocol.CreateGetActiveProfileIdRequest,
            RazerGamepadProtocol.ReadActiveProfileId,
            cancellationToken).ConfigureAwait(false);
        if (applied.Ok && applied.Value == profileId)
        {
            _profileId = profileId;
            return;
        }

        if (current.Ok)
        {
            await QueryAsync(
                transaction => RazerGamepadProtocol.CreateSetActiveProfileIdRequest(transaction, current.Value),
                "restore active profile",
                cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException(
            $"The gamepad kept profile {applied.Value} instead of {profileId}.");
    }

    private static bool UsesHighSpeedPolling(ushort productId) => productId is
        0x0A57 or 0x0A5A or 0x0A74 or 0x0A7C or 0x1025 or 0x1030 or 0x1013;

    public async Task<int> ReadPollingRateForProfileAsync(
        byte profileId,
        CancellationToken cancellationToken = default)
    {
        var highSpeed = UsesHighSpeedPolling(_productId);
        var code = RazerGamepadProtocol.ReadPollingCode(
            await QueryAsync(
                transaction => highSpeed
                    ? RazerGamepadProtocol.CreateGetHighSpeedPollingRateRequest(transaction, profileId)
                    : RazerGamepadProtocol.CreateGetProfilePollingRateRequest(transaction, profileId),
                "read polling rate",
                cancellationToken).ConfigureAwait(false),
            profileId);
        return highSpeed
            ? RazerGamepadProtocol.DecodeHighSpeedPollingRate(code)
            : RazerGamepadProtocol.DecodeStandardPollingRate(code);
    }

    private byte? PollingProfileId => _profileId != 0
        ? _profileId
        : SupportsSingleProfile(_productId) ? (byte)1 : null;
}
