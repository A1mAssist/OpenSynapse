namespace OpenSynapse.Windows.Protocols;

public enum RazerChromaEffect : byte
{
    Off = 0,
    Static = 1,
    Breathing = 2,
    Spectrum = 3,
    AudioMeter = 4,
}

/// <summary>
/// The class 0x10 Chroma commands the middleware uses for mice, keyboards, lamps,
/// ARGB controllers and docks. The effect payload is
/// [effectId, flags, rate, colorCount, r, g, b, ...]; brightness is a single 0..255 byte
/// that the middleware reports as a percentage.
/// </summary>
public static class RazerChromaStandardProtocol
{
    public const byte CommandClass = 0x10;
    public const byte BatteryCommandClass = 0x07;
    public const int MaxColors = 2;

    public static byte[] GetEffect(byte transactionId) =>
        Create(transactionId, 0, 0x83);

    public static byte[] SetEffect(
        byte transactionId,
        RazerChromaEffect effect,
        ReadOnlySpan<byte> colors,
        byte flags = 0,
        byte rate = 0)
    {
        if (!Enum.IsDefined(effect))
        {
            throw new ArgumentOutOfRangeException(nameof(effect));
        }
        if (colors.Length % 3 != 0 || colors.Length / 3 > MaxColors)
        {
            throw new ArgumentException("Chroma effects take one or two RGB colors.", nameof(colors));
        }

        var arguments = new byte[4 + colors.Length];
        arguments[0] = (byte)effect;
        arguments[1] = flags;
        arguments[2] = rate;
        arguments[3] = (byte)(colors.Length / 3);
        colors.CopyTo(arguments.AsSpan(4));
        return Create(transactionId, (byte)arguments.Length, 0x03, arguments);
    }

    public static byte[] GetBrightness(byte transactionId) =>
        Create(transactionId, 0, 0x85);

    public static byte[] SetBrightness(byte transactionId, byte level) =>
        Create(transactionId, 1, 0x05, level);

    public static (RazerChromaEffect Effect, byte Flags, byte Rate, byte[] Colors) ReadEffect(
        ReadOnlySpan<byte> response)
    {
        var data = Arguments(response);
        Require(data, 4);
        var count = data[3];
        if (count > MaxColors || data.Length < 4 + (count * 3))
        {
            throw new InvalidDataException($"Chroma effect declares {count} colors.");
        }

        return ((RazerChromaEffect)data[0], data[1], data[2], data.Slice(4, count * 3).ToArray());
    }

    // The firmware stores brightness as 0..255; the middleware shows it as a percentage.
    public static byte ReadBrightnessPercent(ReadOnlySpan<byte> response)
    {
        var data = Arguments(response);
        Require(data, 1);
        return (byte)Math.Ceiling(data[0] * 100d / 255d);
    }

    /// <summary>Battery and charging live on class 0x07, the same commands the keyboard page uses.</summary>
    public static byte[] GetBatteryLevel(byte transactionId) =>
        RazerFeatureReport.CreateRequest(transactionId, 2, BatteryCommandClass, 0x80, ReadOnlySpan<byte>.Empty);

    public static byte[] GetChargingStatus(byte transactionId) =>
        RazerFeatureReport.CreateRequest(transactionId, 2, BatteryCommandClass, 0x84, ReadOnlySpan<byte>.Empty);

    public static byte ReadBatteryPercent(ReadOnlySpan<byte> response)
    {
        var data = Arguments(response);
        Require(data, 1);
        return (byte)(data[0] * 100 / 255);
    }

    public static bool ReadCharging(ReadOnlySpan<byte> response)
    {
        var data = Arguments(response);
        Require(data, 1);
        return data[0] != 0;
    }
    public static byte ToBrightnessLevel(byte percent) =>
        (byte)Math.Round(Math.Clamp(percent, (byte)0, (byte)100) * 255d / 100d);

    private static byte[] Create(
        byte transactionId,
        byte dataSize,
        byte commandId,
        params byte[] arguments) =>
        RazerFeatureReport.CreateRequest(transactionId, dataSize, CommandClass, commandId, arguments);

    private static ReadOnlySpan<byte> Arguments(ReadOnlySpan<byte> response)
    {
        if (response.Length != RazerFeatureReport.Length)
        {
            throw new InvalidDataException(
                $"Razer feature reports are {RazerFeatureReport.Length} bytes; got {response.Length}.");
        }

        return response[RazerFeatureReport.ArgumentsOffset..];
    }

    private static void Require(ReadOnlySpan<byte> data, int length)
    {
        if (data.Length < length)
        {
            throw new InvalidDataException($"Razer response has {data.Length} data bytes; {length} required.");
        }
    }
}
