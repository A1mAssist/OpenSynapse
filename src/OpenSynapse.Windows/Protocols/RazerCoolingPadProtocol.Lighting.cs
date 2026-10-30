namespace OpenSynapse.Windows.Protocols;

/// <summary>One 0F:80 Chroma region record: [id, updateRate, dataType, x, y].</summary>
public readonly record struct RazerCoolingPadLightingRegion(
    byte RegionId,
    byte UpdateRate,
    byte DataType,
    byte ArrayX,
    byte ArrayY);

public sealed record RazerCoolingPadLightingState(
    byte RegionId,
    int BrightnessPercent,
    byte EffectId,
    byte Flags,
    byte Rate,
    IReadOnlyList<byte> Colors,
    IReadOnlyList<byte> EffectIds);

public static partial class RazerCoolingPadProtocol
{
    private const byte ChromaClass = 0x0F;
    private const byte ChromaProfile = 0x00;
    private const byte GetRegionIdsCommand = 0x80;
    private const byte GetEffectIdsCommand = 0x81;
    private const byte GetEffectCommand = 0x82;
    private const byte SetEffectCommand = 0x02;
    private const byte GetBrightnessCommand = 0x84;
    private const byte SetBrightnessCommand = 0x04;

    public static byte[] CreateGetLightingRegionsRequest() =>
        RazerFeatureReport.CreateRequest(TransactionId, 0x50, ChromaClass, GetRegionIdsCommand, []);

    public static byte[] CreateGetLightingEffectsRequest(byte regionId) =>
        RazerFeatureReport.CreateRequest(TransactionId, 0x50, ChromaClass, GetEffectIdsCommand, [regionId]);

    public static byte[] CreateGetLightingEffectRequest(byte regionId) =>
        RazerFeatureReport.CreateRequest(
            TransactionId, 0x50, ChromaClass, GetEffectCommand, [ChromaProfile, regionId]);

    public static byte[] CreateGetLightingBrightnessRequest(byte regionId) =>
        RazerFeatureReport.CreateRequest(
            TransactionId, 0x03, ChromaClass, GetBrightnessCommand, [ChromaProfile, regionId]);

    public static byte[] CreateSetLightingBrightnessRequest(byte regionId, int percent)
    {
        if (percent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(percent));
        return RazerFeatureReport.CreateRequest(
            TransactionId,
            0x03,
            ChromaClass,
            SetBrightnessCommand,
            [ChromaProfile, regionId, (byte)Math.Floor(percent / 100d * 255d)]);
    }

    public static byte[] CreateSetLightingEffectRequest(
        byte regionId,
        byte effectId,
        byte flags,
        byte rate,
        ReadOnlySpan<byte> colors)
    {
        if (colors.Length % 3 != 0) throw new ArgumentException("RGB colors must be triples.", nameof(colors));
        if (colors.Length / 3 > 24) throw new ArgumentException("At most 24 colors fit the feature report.", nameof(colors));
        var payload = new byte[6 + colors.Length];
        payload[0] = ChromaProfile;
        payload[1] = regionId;
        payload[2] = effectId;
        payload[3] = flags;
        payload[4] = rate;
        payload[5] = (byte)(colors.Length / 3);
        colors.CopyTo(payload.AsSpan(6));
        return RazerFeatureReport.CreateRequest(
            TransactionId, (byte)payload.Length, ChromaClass, SetEffectCommand, payload);
    }

    /// <summary>Flags used by the bundle for each parameter-free quick effect.</summary>
    public static byte LightingFlagsFor(byte effectId) => effectId switch
    {
        2 => 0x01,
        8 => 0x0A,
        _ => 0x00,
    };

    /// <summary>
    /// Wave flags are 1..4 (left/right/up/down); Wheel flags are 1..2
    /// (clockwise/counter-clockwise). Other effects ignore the direction index.
    /// </summary>
    public static byte LightingFlagsFor(byte effectId, int directionIndex) => effectId switch
    {
        4 => (byte)(1 + Math.Clamp(directionIndex, 0, 3)),
        13 => (byte)(1 + Math.Clamp(directionIndex, 0, 1)),
        _ => LightingFlagsFor(effectId),
    };

    /// <summary>Wave uses the bundle's fixed rate 40; the other quick effects use 0.</summary>
    public static byte LightingRateFor(byte effectId) => effectId == 4 ? (byte)40 : (byte)0;

    /// <summary>Starlight flags are a tri-state: random, one color, or two colors.</summary>
    public static byte LightingFlagsForStarlight(bool random, bool dualColor) =>
        (byte)(random ? 0 : dualColor ? 2 : 1);

    /// <summary>Starlight rate is the bundle's 1..3 duration level.</summary>
    public static byte LightingRateForStarlight(int duration)
    {
        if (duration is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(duration));
        return (byte)duration;
    }

    public static IReadOnlyList<RazerCoolingPadLightingRegion> ParseLightingRegions(ReadOnlySpan<byte> response)
    {
        ValidateChromaResponse(response, CreateGetLightingRegionsRequest(), 1);
        var count = Math.Min((int)response[6], 80);
        var arguments = response[RazerFeatureReport.ArgumentsOffset..];
        var regions = new List<RazerCoolingPadLightingRegion>();
        for (var offset = 0; offset + 5 <= count; offset += 5)
        {
            regions.Add(new RazerCoolingPadLightingRegion(
                arguments[offset],
                arguments[offset + 1],
                arguments[offset + 2],
                arguments[offset + 3],
                arguments[offset + 4]));
        }

        return regions;
    }

    public static IReadOnlyList<byte> ParseLightingEffectIds(ReadOnlySpan<byte> response)
    {
        ValidateChromaResponse(response, CreateGetLightingEffectsRequest(0), 1);
        var count = Math.Min((int)response[6], 80);
        return response.Slice(RazerFeatureReport.ArgumentsOffset, count)
            .ToArray()
            .Where(id => id != 0)
            .ToArray();
    }

    public static int ParseLightingBrightness(ReadOnlySpan<byte> response, byte regionId)
    {
        ValidateChromaResponse(response, CreateGetLightingBrightnessRequest(regionId), 3);
        var arguments = response[RazerFeatureReport.ArgumentsOffset..];
        if (arguments[1] != regionId)
        {
            throw new InvalidDataException("Cooling pad brightness reply did not echo the requested region.");
        }

        return (int)Math.Ceiling(arguments[2] / 255d * 100d);
    }

    public static (byte EffectId, byte Flags, byte Rate, IReadOnlyList<byte> Colors)
        ParseLightingEffect(ReadOnlySpan<byte> response, byte regionId)
    {
        ValidateChromaResponse(response, CreateGetLightingEffectRequest(regionId), 6);
        var arguments = response[RazerFeatureReport.ArgumentsOffset..];
        if (arguments[1] != regionId)
        {
            throw new InvalidDataException("Cooling pad effect reply did not echo the requested region.");
        }

        var colorCount = arguments[5];
        if (6 + colorCount * 3 > Math.Min(response[6], arguments.Length))
        {
            throw new InvalidDataException("Cooling pad effect reply is truncated.");
        }

        return (arguments[2], arguments[3], arguments[4],
            arguments.Slice(6, colorCount * 3).ToArray());
    }

    private static void ValidateChromaResponse(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request,
        byte minimumArguments)
    {
        if (!RazerFeatureReport.IsSuccessfulResponse(
                request, response, minimumArguments, allowRemainingPacketsMismatch: true))
        {
            throw new InvalidDataException(
                "Cooling pad returned an invalid or out-of-order lighting feature report.");
        }
    }
}
