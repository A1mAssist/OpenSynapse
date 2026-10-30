namespace OpenSynapse.Windows.Protocols;

/// <summary>The middleware's shared Chroma effect ids (chunk 79151).</summary>
public enum RazerGamepadChromaEffect : byte
{
    Off = 0,
    Static = 1,
    Breathing = 2,
    Spectrum = 3,
    Wave = 4,
    Reactive = 5,
    BasicRipple = 6,
    Starlight = 7,
    CustomFrame = 8,
    Fire = 9,
    AudioMeter = 10,
    Immersive = 11,
    Wheel = 12,
    StaticExtended = 13,
}

public readonly record struct RazerGamepadChromaRegion(
    byte RegionId,
    byte UpdateRate,
    byte DataType,
    byte ArrayX,
    byte ArrayY);

public sealed record RazerGamepadChromaEffectState(
    byte RegionId,
    byte EffectId,
    byte Flags,
    byte Rate,
    IReadOnlyList<RazerRgb> Colors);

public static partial class RazerGamepadProtocol
{
    public const byte ChromaCommandClass = 0x0F;

    private const byte ChromaRegionListGet = 0x80;
    private const byte ChromaEffectIdListGet = 0x81;
    private const byte ChromaEffectGet = 0x82;
    private const byte ChromaEffectSet = 0x02;
    private const byte ChromaFrameSet = 0x03;
    private const byte ChromaBrightnessGet = 0x84;
    private const byte ChromaBrightnessSet = 0x04;

    public static byte[] CreateGetChromaRegionIdListRequest(byte transactionId) =>
        CreateForClass(transactionId, 80, ChromaCommandClass, ChromaRegionListGet, new byte[80]);

    public static byte[] CreateGetChromaEffectIdListRequest(byte transactionId, byte regionId)
    {
        var arguments = new byte[80];
        arguments[0] = regionId;
        return CreateForClass(transactionId, 80, ChromaCommandClass, ChromaEffectIdListGet, arguments);
    }

    public static byte[] CreateGetChromaEffectRequest(
        byte transactionId,
        byte profileId,
        byte regionId)
    {
        var arguments = new byte[80];
        arguments[0] = profileId;
        arguments[1] = regionId;
        return CreateForClass(transactionId, 80, ChromaCommandClass, ChromaEffectGet, arguments);
    }

    public static byte[] CreateSetChromaEffectRequest(
        byte transactionId,
        byte profileId,
        byte regionId,
        byte effectId,
        byte flags,
        byte rate,
        IReadOnlyList<RazerRgb> colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        var colorBytes = colors.Count * 3;
        if (colorBytes > 74)
        {
            throw new ArgumentOutOfRangeException(nameof(colors));
        }

        var dataSize = checked((byte)(6 + colorBytes));
        var arguments = new byte[dataSize];
        arguments[0] = profileId;
        arguments[1] = regionId;
        arguments[2] = effectId;
        arguments[3] = flags;
        arguments[4] = rate;
        arguments[5] = (byte)colors.Count;
        for (var index = 0; index < colors.Count; index++)
        {
            arguments[6 + index * 3] = colors[index].Red;
            arguments[7 + index * 3] = colors[index].Green;
            arguments[8 + index * 3] = colors[index].Blue;
        }

        return CreateForClass(transactionId, dataSize, ChromaCommandClass, ChromaEffectSet, arguments);
    }

    public static byte[] CreateSetChromaFrameRequest(
        byte transactionId,
        byte profileId,
        byte regionId,
        byte row,
        byte start,
        byte end,
        IReadOnlyList<RazerRgb> colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        if (start > end || colors.Count != end - start + 1)
        {
            throw new ArgumentException("A Chroma frame row must cover every column from start through end.", nameof(colors));
        }

        var colorBytes = colors.Count * 3;
        if (colorBytes > 75)
        {
            throw new ArgumentOutOfRangeException(nameof(colors));
        }

        var dataSize = checked((byte)(5 + colorBytes));
        var arguments = new byte[dataSize];
        arguments[0] = profileId;
        arguments[1] = regionId;
        arguments[2] = row;
        arguments[3] = start;
        arguments[4] = end;
        for (var index = 0; index < colors.Count; index++)
        {
            arguments[5 + index * 3] = colors[index].Red;
            arguments[6 + index * 3] = colors[index].Green;
            arguments[7 + index * 3] = colors[index].Blue;
        }

        return CreateForClass(transactionId, dataSize, ChromaCommandClass, ChromaFrameSet, arguments);
    }

    public static byte[] CreateGetChromaBrightnessRequest(
        byte transactionId,
        byte profileId,
        byte regionId) =>
        CreateForClass(transactionId, 3, ChromaCommandClass, ChromaBrightnessGet, [profileId, regionId, 0x00]);

    public static byte[] CreateSetChromaBrightnessRequest(
        byte transactionId,
        byte profileId,
        byte regionId,
        byte wireBrightness) =>
        CreateForClass(transactionId, 3, ChromaCommandClass, ChromaBrightnessSet, [profileId, regionId, wireBrightness]);

    public static IReadOnlyList<RazerGamepadChromaRegion> ReadChromaRegionList(ReadOnlySpan<byte> response)
    {
        var data = ReadData(response);
        var size = response[RazerFeatureReport.DataSizeOffset];
        if (size > 80 || size % 5 != 0)
        {
            throw new InvalidDataException("The gamepad Chroma region list is malformed.");
        }

        var regions = new RazerGamepadChromaRegion[size / 5];
        for (var index = 0; index < regions.Length; index++)
        {
            var offset = index * 5;
            regions[index] = new(
                data[offset],
                data[offset + 1],
                data[offset + 2],
                data[offset + 3],
                data[offset + 4]);
        }

        return regions;
    }

    public static IReadOnlyList<byte> ReadChromaEffectIdList(ReadOnlySpan<byte> response)
    {
        var data = ReadData(response);
        var size = response[RazerFeatureReport.DataSizeOffset];
        if (size > 80)
        {
            throw new InvalidDataException("The gamepad Chroma effect list is malformed.");
        }

        return size <= 1 ? [] : data[1..size].ToArray();
    }

    public static RazerGamepadChromaEffectState ReadChromaEffect(ReadOnlySpan<byte> response)
    {
        var data = ReadData(response);
        var size = response[RazerFeatureReport.DataSizeOffset];
        if (size < 6 || size > 80)
        {
            throw new InvalidDataException("The gamepad Chroma effect reply is truncated.");
        }

        var colorCount = data[5];
        if (6 + colorCount * 3 > size)
        {
            throw new InvalidDataException("The gamepad Chroma effect colour array is truncated.");
        }

        var colors = new RazerRgb[colorCount];
        for (var index = 0; index < colors.Length; index++)
        {
            var offset = 6 + index * 3;
            colors[index] = new RazerRgb(data[offset], data[offset + 1], data[offset + 2]);
        }

        return new(data[1], data[2], data[3], data[4], colors);
    }

    public static byte ReadChromaBrightness(ReadOnlySpan<byte> response)
    {
        var data = ReadData(response);
        if (response[RazerFeatureReport.DataSizeOffset] < 3)
        {
            throw new InvalidDataException("The gamepad Chroma brightness reply is truncated.");
        }

        return data[2];
    }
}
