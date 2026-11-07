namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// The class 0x0F Chroma commands the middleware sends to wired keyboards and headsets.
/// The payload is [profileId, regionId, effectId, flags, rate, colorCount, r, g, b...].
/// Values come from rzDevice25.ec1bc7b7fe16da2a014d.js: the get parser (zt) and the
/// set sender (Gt) both use this layout.
/// </summary>
public static class RazerLegacyChromaProtocol
{
    public const byte CommandClass = 0x0F;
    public const int MaxColors = 2;

    // The led-matrix keyboards (rzDevice25LedMatrix) take the whole-device effect on
    // class 0x03 command 0x0A instead of the class 0x0F effect command. The payload is
    // [effectId, flags, colours...]; ids come from the middleware UE enum.
    public const byte MatrixCommandClass = 0x03;
    public const byte MatrixSetEffectCommandId = 0x0A;
    private const byte MatrixEffectOff = 0x00;
    private const byte MatrixEffectBreathing = 0x03;
    private const byte MatrixEffectSpectrum = 0x04;
    private const byte MatrixEffectStatic = 0x06;

    // The middleware only ever reads profile 1, region 0 (6737.*.js, BLE effect read).
    public const byte ReadProfileId = 1;
    public const byte ReadRegionId = 0;

    /// <summary>
    /// Switches the 320-byte ARGB controller to its custom-frame effect
    /// (class 0x0F, command 0x02, effect 0x08), the mode RazerHid enters before
    /// writing frames. It uses transaction 0x3F on this device.
    /// </summary>
    public static byte[] SetCustomFrameMode(byte transactionId) =>
        RazerFeatureReport.CreateRequest(transactionId, 12, CommandClass, 0x02, [0, 0, 0x08]);

    public static byte[] GetEffect(byte transactionId) =>
        RazerFeatureReport.CreateRequest(
            transactionId,
            80,
            CommandClass,
            0x82,
            [ReadProfileId, ReadRegionId]);

    public static byte[] SetEffect(
        byte transactionId,
        RazerChromaEffect effect,
        ReadOnlySpan<byte> colors,
        byte profileId = ReadProfileId,
        byte regionId = ReadRegionId,
        byte? flags = null,
        byte rate = 0)
    {
        if (effect is not (RazerChromaEffect.Off or RazerChromaEffect.Static or
            RazerChromaEffect.Breathing or RazerChromaEffect.Spectrum))
        {
            throw new ArgumentOutOfRangeException(nameof(effect), "Class 0x0F only defines the four UI effects.");
        }
        if (colors.Length % 3 != 0 || colors.Length / 3 > MaxColors)
        {
            throw new ArgumentException("Chroma effects take one or two RGB colors.", nameof(colors));
        }

        var arguments = new byte[6 + colors.Length];
        var colorCount = (byte)(colors.Length / 3);
        arguments[0] = profileId;
        arguments[1] = regionId;
        arguments[2] = (byte)effect;
        // The flags byte selects random (0), single-colour (1) or dual-colour (2)
        // breathing; leaving it at zero made every breathing request random.
        arguments[3] = flags ?? (effect == RazerChromaEffect.Breathing ? colorCount : (byte)0);
        arguments[4] = rate;
        arguments[5] = colorCount;
        colors.CopyTo(arguments.AsSpan(6));
        return RazerFeatureReport.CreateRequest(
            transactionId,
            (byte)arguments.Length,
            CommandClass,
            0x02,
            arguments);
    }

    /// <summary>
    /// The rzDevice25LedMatrix override of setChromaEffectCommon: Off/Spectrum carry the
    /// effect id alone, Static adds one colour, Breathing adds a flag byte and one or two
    /// colours. profileId and regionId never reach the wire on this dialect.
    /// </summary>
    public static byte[] SetMatrixEffect(
        byte transactionId,
        RazerChromaEffect effect,
        ReadOnlySpan<byte> colors,
        byte flags = 0)
    {
        if (colors.Length % 3 != 0 || colors.Length / 3 > MaxColors)
        {
            throw new ArgumentException("Chroma effects take one or two RGB colors.", nameof(colors));
        }

        var payload = effect switch
        {
            RazerChromaEffect.Off => new byte[] { MatrixEffectOff },
            RazerChromaEffect.Spectrum => new byte[] { MatrixEffectSpectrum },
            RazerChromaEffect.Static when colors.Length == 3 =>
                [MatrixEffectStatic, colors[0], colors[1], colors[2]],
            RazerChromaEffect.Breathing when colors.Length is 3 or 6 =>
                [MatrixEffectBreathing, flags == 0 ? (byte)(colors.Length / 3) : flags, .. colors],
            _ => throw new ArgumentOutOfRangeException(
                nameof(effect), "The matrix dialect only defines Off, Static, Breathing and Spectrum."),
        };

        return RazerFeatureReport.CreateRequest(
            transactionId,
            (byte)payload.Length,
            MatrixCommandClass,
            MatrixSetEffectCommandId,
            payload);
    }

    public static (byte EffectId, byte Flags, byte Rate, byte[] Colors) ReadEffect(
        ReadOnlySpan<byte> response)
    {
        if (response.Length != RazerFeatureReport.Length)
        {
            throw new InvalidDataException(
                $"Razer feature reports are {RazerFeatureReport.Length} bytes; got {response.Length}.");
        }

        var declaredLength = response[6];
        if (declaredLength > 80)
        {
            throw new InvalidDataException($"Razer response declares {declaredLength} data bytes; 80 maximum.");
        }

        var data = response.Slice(RazerFeatureReport.ArgumentsOffset, declaredLength);
        if (data.Length < 6)
        {
            throw new InvalidDataException($"Razer response has {data.Length} data bytes; 6 required.");
        }

        var count = data[5];
        if (count > MaxColors || data.Length < 6 + (count * 3))
        {
            throw new InvalidDataException($"Chroma effect declares {count} colors.");
        }

        return (data[2], data[3], data[4], data.Slice(6, count * 3).ToArray());
    }
}
