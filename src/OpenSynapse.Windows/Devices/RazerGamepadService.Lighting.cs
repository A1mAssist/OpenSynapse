using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed record RazerGamepadLightingRegionState(
    RazerGamepadChromaRegion Region,
    IReadOnlyList<byte> EffectIds,
    RazerGamepadChromaEffectState? Effect,
    byte? Brightness);

public sealed record RazerGamepadLightingState(
    byte ProfileId,
    IReadOnlyList<RazerGamepadLightingRegionState> Regions,
    IReadOnlyList<string> Errors);

public sealed partial class RazerGamepadService
{
    public static bool SupportsLighting(ushort productId) => productId is 0x0A4C or 0x1013;

    public async Task<RazerGamepadLightingState> ReadLightingAsync(
        CancellationToken cancellationToken = default)
    {
        if (!SupportsLighting(_productId))
        {
            return new(_profileId, [], []);
        }

        var errors = new List<string>();
        if (PollingProfileId is not byte profileId)
        {
            errors.Add("active profile");
            return new(_profileId, [], errors);
        }
        IReadOnlyList<RazerGamepadChromaRegion> regions;
        try
        {
            regions = RazerGamepadProtocol.ReadChromaRegionList(
                await QueryAsync(
                    RazerGamepadProtocol.CreateGetChromaRegionIdListRequest,
                    "read Chroma regions",
                    cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            errors.Add("Chroma regions");
            _diagnostic?.Invoke($"gamepad lighting unavailable: {exception.Message}");
            return new(_profileId, [], errors);
        }

        var states = new List<RazerGamepadLightingRegionState>(regions.Count);
        foreach (var region in regions.Take(16))
        {
            IReadOnlyList<byte> effectIds = [];
            try
            {
                effectIds = RazerGamepadProtocol.ReadChromaEffectIdList(
                    await QueryAsync(
                        transaction => RazerGamepadProtocol.CreateGetChromaEffectIdListRequest(
                            transaction,
                            region.RegionId),
                        $"read Chroma effects {region.RegionId}",
                        cancellationToken).ConfigureAwait(false));
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                errors.Add($"region {region.RegionId} effects");
                _diagnostic?.Invoke($"gamepad Chroma effect list {region.RegionId} unavailable: {exception.Message}");
            }

            RazerGamepadChromaEffectState? effect = null;
            try
            {
                effect = RazerGamepadProtocol.ReadChromaEffect(
                    await QueryAsync(
                        transaction => RazerGamepadProtocol.CreateGetChromaEffectRequest(
                            transaction,
                            profileId,
                            region.RegionId),
                        $"read Chroma effect {region.RegionId}",
                        cancellationToken).ConfigureAwait(false));
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                errors.Add($"region {region.RegionId} effect");
                _diagnostic?.Invoke($"gamepad Chroma effect {region.RegionId} unavailable: {exception.Message}");
            }

            byte? brightness = null;
            try
            {
                brightness = RazerGamepadProtocol.ReadChromaBrightness(
                    await QueryAsync(
                        transaction => RazerGamepadProtocol.CreateGetChromaBrightnessRequest(
                            transaction,
                            profileId,
                            region.RegionId),
                        $"read Chroma brightness {region.RegionId}",
                        cancellationToken).ConfigureAwait(false));
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                errors.Add($"region {region.RegionId} brightness");
                _diagnostic?.Invoke($"gamepad Chroma brightness {region.RegionId} unavailable: {exception.Message}");
            }

            states.Add(new(region, effectIds, effect, brightness));
        }

        return new(_profileId, states, errors);
    }

    public async Task SetLightingEffectAsync(
        byte regionId,
        byte effectId,
        byte flags,
        byte rate,
        IReadOnlyList<RazerRgb> colors,
        CancellationToken cancellationToken = default)
    {
        if (!SupportsLighting(_productId))
        {
            throw new NotSupportedException("This gamepad does not expose Chroma lighting.");
        }

        ArgumentNullException.ThrowIfNull(colors);
        var profileId = PollingProfileId ??
            throw new InvalidOperationException(
                "The active gamepad profile is unknown; refusing to write Chroma lighting.");
        RazerGamepadChromaEffectState? previous = null;
        try
        {
            previous = RazerGamepadProtocol.ReadChromaEffect(
                await QueryAsync(
                    transaction => RazerGamepadProtocol.CreateGetChromaEffectRequest(
                        transaction,
                        profileId,
                        regionId),
                    "read previous Chroma effect",
                    cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _diagnostic?.Invoke($"gamepad previous Chroma effect unavailable: {exception.Message}");
        }

        try
        {
            await QueryAsync(
                transaction => RazerGamepadProtocol.CreateSetChromaEffectRequest(
                    transaction,
                    profileId,
                    regionId,
                    effectId,
                    flags,
                    rate,
                    colors),
                "set Chroma effect",
                cancellationToken).ConfigureAwait(false);
            var actual = RazerGamepadProtocol.ReadChromaEffect(
                await QueryAsync(
                    transaction => RazerGamepadProtocol.CreateGetChromaEffectRequest(
                        transaction,
                        profileId,
                        regionId),
                    "verify Chroma effect",
                    cancellationToken).ConfigureAwait(false));
            if (actual.EffectId != effectId ||
                actual.Flags != flags ||
                actual.Rate != rate ||
                !ColorsMatch(actual.Colors, colors))
            {
                throw new InvalidOperationException("The gamepad Chroma effect readback did not match the request.");
            }
        }
        catch
        {
            if (previous is not null)
            {
                try
                {
                    await QueryAsync(
                        transaction => RazerGamepadProtocol.CreateSetChromaEffectRequest(
                            transaction,
                            profileId,
                            regionId,
                            previous.EffectId,
                            previous.Flags,
                            previous.Rate,
                            previous.Colors),
                        "restore Chroma effect",
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception exception) when (IsExpected(exception))
                {
                    _diagnostic?.Invoke($"gamepad restore Chroma effect failed: {exception.Message}");
                }
            }

            throw;
        }
    }

    public async Task SetLightingBrightnessAsync(
        byte regionId,
        byte percent,
        CancellationToken cancellationToken = default)
    {
        if (!SupportsLighting(_productId))
        {
            throw new NotSupportedException("This gamepad does not expose Chroma lighting.");
        }
        if (percent > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(percent));
        }

        var requested = (byte)Math.Floor(percent / 100d * 255d);
        var profileId = PollingProfileId ??
            throw new InvalidOperationException(
                "The active gamepad profile is unknown; refusing to write Chroma brightness.");
        byte? previous = null;
        try
        {
            previous = RazerGamepadProtocol.ReadChromaBrightness(
                await QueryAsync(
                    transaction => RazerGamepadProtocol.CreateGetChromaBrightnessRequest(
                        transaction,
                        profileId,
                        regionId),
                    "read Chroma brightness",
                    cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _diagnostic?.Invoke($"gamepad previous Chroma brightness unavailable: {exception.Message}");
        }

        try
        {
            await QueryAsync(
                transaction => RazerGamepadProtocol.CreateSetChromaBrightnessRequest(
                    transaction,
                    profileId,
                    regionId,
                    requested),
                "set Chroma brightness",
                cancellationToken).ConfigureAwait(false);
            var actual = RazerGamepadProtocol.ReadChromaBrightness(
                await QueryAsync(
                    transaction => RazerGamepadProtocol.CreateGetChromaBrightnessRequest(
                        transaction,
                        profileId,
                        regionId),
                    "verify Chroma brightness",
                    cancellationToken).ConfigureAwait(false));
            if (actual != requested)
            {
                throw new InvalidOperationException(
                    $"The gamepad Chroma brightness kept {actual} instead of {requested}.");
            }
        }
        catch
        {
            if (previous is byte value)
            {
                try
                {
                    await QueryAsync(
                        transaction => RazerGamepadProtocol.CreateSetChromaBrightnessRequest(
                            transaction,
                            profileId,
                            regionId,
                            value),
                        "restore Chroma brightness",
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception exception) when (IsExpected(exception))
                {
                    _diagnostic?.Invoke($"gamepad restore Chroma brightness failed: {exception.Message}");
                }
            }

            throw;
        }
    }

    private static bool ColorsMatch(IReadOnlyList<RazerRgb> left, IReadOnlyList<RazerRgb> right) =>
        left.Count == right.Count && left.SequenceEqual(right);
}
