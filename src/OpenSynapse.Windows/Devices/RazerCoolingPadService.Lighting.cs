using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerCoolingPadService
{
    private RazerCoolingPadLightingState? _lightingState;

    public async Task<RazerCoolingPadLightingState?> ReadLightingAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var regions = RazerCoolingPadProtocol.ParseLightingRegions(
                await QueryAsync(
                    RazerCoolingPadProtocol.CreateGetLightingRegionsRequest(),
                    "read lighting regions",
                    cancellationToken).ConfigureAwait(false));
            var region = regions.FirstOrDefault(item => item.RegionId != 0);
            if (region.RegionId == 0)
            {
                _lightingState = null;
                return null;
            }

            var effectIds = RazerCoolingPadProtocol.ParseLightingEffectIds(
                await QueryAsync(
                    RazerCoolingPadProtocol.CreateGetLightingEffectsRequest(region.RegionId),
                    "read lighting effects",
                    cancellationToken).ConfigureAwait(false));
            var brightness = RazerCoolingPadProtocol.ParseLightingBrightness(
                await QueryAsync(
                    RazerCoolingPadProtocol.CreateGetLightingBrightnessRequest(region.RegionId),
                    "read lighting brightness",
                    cancellationToken).ConfigureAwait(false),
                region.RegionId);
            var effect = RazerCoolingPadProtocol.ParseLightingEffect(
                await QueryAsync(
                    RazerCoolingPadProtocol.CreateGetLightingEffectRequest(region.RegionId),
                    "read lighting effect",
                    cancellationToken).ConfigureAwait(false),
                region.RegionId);

            _lightingState = new RazerCoolingPadLightingState(
                region.RegionId,
                brightness,
                effect.EffectId,
                effect.Flags,
                effect.Rate,
                effect.Colors,
                effectIds);
            return _lightingState;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _lightingState = null;
            _diagnostic?.Invoke($"cooling pad lighting unavailable: {exception.Message}");
            return null;
        }
    }

    public async Task<RazerCoolingPadLightingState> SetLightingAsync(
        int brightnessPercent,
        byte effectId,
        byte flags,
        byte rate,
        IReadOnlyList<byte> colors,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(colors);
        if (colors.Count % 3 != 0)
        {
            throw new ArgumentException("RGB colors must be triples.", nameof(colors));
        }

        var previous = _lightingState ?? await ReadLightingAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Cooling pad lighting is unavailable.");
        var region = previous.RegionId;
        try
        {
            await QueryAsync(
                RazerCoolingPadProtocol.CreateSetLightingBrightnessRequest(region, brightnessPercent),
                "set lighting brightness",
                cancellationToken).ConfigureAwait(false);
            await QueryAsync(
                RazerCoolingPadProtocol.CreateSetLightingEffectRequest(
                    region, effectId, flags, rate, colors.ToArray()),
                "set lighting effect",
                cancellationToken).ConfigureAwait(false);

            var brightness = RazerCoolingPadProtocol.ParseLightingBrightness(
                await QueryAsync(
                    RazerCoolingPadProtocol.CreateGetLightingBrightnessRequest(region),
                    "verify lighting brightness",
                    cancellationToken).ConfigureAwait(false),
                region);
            var effect = RazerCoolingPadProtocol.ParseLightingEffect(
                await QueryAsync(
                    RazerCoolingPadProtocol.CreateGetLightingEffectRequest(region),
                    "verify lighting effect",
                    cancellationToken).ConfigureAwait(false),
                region);
            if (brightness == brightnessPercent &&
                effect.EffectId == effectId &&
                effect.Flags == flags &&
                effect.Rate == rate &&
                effect.Colors.SequenceEqual(colors))
            {
                _lightingState = previous with
                {
                    BrightnessPercent = brightness,
                    EffectId = effect.EffectId,
                    Flags = effect.Flags,
                    Rate = effect.Rate,
                    Colors = effect.Colors.ToArray(),
                };
                return _lightingState;
            }

            throw new InvalidOperationException(
                "Cooling pad lighting readback did not match the requested state.");
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            try
            {
                await QueryAsync(
                    RazerCoolingPadProtocol.CreateSetLightingBrightnessRequest(
                        region, previous.BrightnessPercent),
                    "restore lighting brightness",
                    cancellationToken).ConfigureAwait(false);
                await QueryAsync(
                    RazerCoolingPadProtocol.CreateSetLightingEffectRequest(
                        region, previous.EffectId, previous.Flags, previous.Rate, previous.Colors.ToArray()),
                    "restore lighting effect",
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception restoreException) when (IsExpected(restoreException))
            {
                _diagnostic?.Invoke($"cooling pad lighting restore failed: {restoreException.Message}");
            }

            throw new InvalidOperationException($"Cooling pad lighting update failed: {exception.Message}", exception);
        }
    }
}
