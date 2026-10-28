namespace OpenSynapse.Windows.Protocols;

public enum LeviathanV2XEqPreset : byte
{
    Default = 0x00,
    Game = 0x01,
    Music = 0x02,
    Movie = 0x03,
    Treble = 0x04,
    Custom = 0x10,
}

public enum LeviathanV2XInputSource : byte
{
    None = 0x00,
    Optical = 0x01,
    Aux = 0x02,
    Bluetooth = 0x03,
    Usb = 0x04,
}

public readonly record struct LeviathanV2XAudioIoStatus(
    bool Headphone,
    bool Usb,
    bool AuxIn,
    bool Optical,
    bool Bluetooth);

/// <summary>
/// Razer Leviathan V2 X / NoaT3 protocol 25. The command channel is report id 7 with a
/// 91-byte feature report; bytes 1..90 carry the same v25 envelope used by other Razer
/// feature-report devices.
/// </summary>
public static class LeviathanV2XProtocol
{
    public const ushort ProductId = 0x054A;
    public const byte ReportId = 0x07;
    public const int EqBandCount = 10;

    // The soundbar panel reads and writes the same profile 0. Profile 1 belongs to the
    // Broadcaster broadcast path, which the soundbar never uses.
    public const byte ReadProfile = 0x00;
    public const byte WriteProfile = 0x00;

    // The transport assigns the per-device 0..31 transaction sequence for 054A.
    private const byte TransportTransactionId = 0x00;

    public static readonly IReadOnlyList<int> EqBandFrequencies =
        [31, 63, 125, 250, 500, 1_000, 2_000, 4_000, 8_000, 16_000];

    public static byte[] CreateGetEqPreset(byte profileId = ReadProfile) =>
        Create(0x02, 0x08, 0x82, [profileId]);

    public static byte[] CreateSetEqPreset(
        LeviathanV2XEqPreset preset,
        byte profileId = ReadProfile)
    {
        if (!Enum.IsDefined(preset))
        {
            throw new ArgumentOutOfRangeException(nameof(preset));
        }

        return Create(0x02, 0x08, 0x02, [profileId, (byte)preset]);
    }

    public static byte[] CreateGetEqBands(byte profileId = ReadProfile) =>
        Create(0x0B, 0x08, 0x84, [profileId]);

    public static byte[] CreateSetEqBands(
        ReadOnlySpan<byte> bands,
        byte profileId = WriteProfile)
    {
        if (bands.Length != EqBandCount)
        {
            throw new ArgumentException($"Equalizer data must contain {EqBandCount} bands.", nameof(bands));
        }
        if (bands.ContainsAnyExceptInRange((byte)0, (byte)100))
        {
            throw new ArgumentOutOfRangeException(nameof(bands), "Equalizer levels must be between 0 and 100.");
        }

        var arguments = new byte[EqBandCount + 1];
        arguments[0] = profileId;
        bands.CopyTo(arguments.AsSpan(1));
        return Create(0x0B, 0x08, 0x04, arguments);
    }

    public static byte[] CreateGetBass(byte profileId = ReadProfile) =>
        Create(0x02, 0x08, 0x87, [profileId]);

    public static byte[] CreateSetBass(byte level, byte profileId = WriteProfile)
    {
        if (level is < 1 or > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(level), "Bass level must be between 1 and 7.");
        }

        return Create(0x02, 0x08, 0x07, [profileId, level]);
    }

    public static byte[] CreateGetInputSource(byte profileId = ReadProfile) =>
        Create(0x02, 0x08, 0x8A, [profileId]);

    public static byte[] CreateGetAudioIoStatus() =>
        Create(0x05, 0x08, 0x8B, []);

    public static byte[] CreateGetRegionIds() =>
        Create(0x50, 0x0F, 0x80, []);

    public static byte[] CreateGetEffect(byte profileId, byte regionId) =>
        Create(0x50, 0x0F, 0x82, [profileId, regionId]);

    public static byte[] CreateSetEffect(
        byte profileId,
        byte regionId,
        byte effectId,
        byte flags,
        byte rate,
        ReadOnlySpan<byte> colors)
    {
        if (colors.Length % 3 != 0)
        {
            throw new ArgumentException("Chroma effect colors must be RGB triples.", nameof(colors));
        }

        var arguments = new byte[6 + colors.Length];
        arguments[0] = profileId;
        arguments[1] = regionId;
        arguments[2] = effectId;
        arguments[3] = flags;
        arguments[4] = rate;
        arguments[5] = checked((byte)(colors.Length / 3));
        colors.CopyTo(arguments.AsSpan(6));
        return Create(checked((byte)arguments.Length), 0x0F, 0x02, arguments);
    }

    /// <summary>
    /// Quick-effect payload the 1354 middleware builds: effect id, flags (breathing color
    /// mode or wave direction), rate and one RGB triple per color. The soundbar reports
    /// every writable effect the same way, so one entry point covers the whole list.
    /// </summary>
    public static byte[] CreateEffect(
        byte regionId,
        OpenRazerLightingEffect effect,
        byte direction,
        OpenRazerColor primary,
        OpenRazerColor secondary) => effect switch
    {
        OpenRazerLightingEffect.Off => CreateSetEffect(WriteProfile, regionId, 0x00, 0, 0, []),
        OpenRazerLightingEffect.Static => CreateSetEffect(WriteProfile, regionId, 0x01, 0, 0,
            [primary.Red, primary.Green, primary.Blue]),
        OpenRazerLightingEffect.BreathingDual => CreateSetEffect(WriteProfile, regionId, 0x02, 2, 0,
            [primary.Red, primary.Green, primary.Blue, secondary.Red, secondary.Green, secondary.Blue]),
        OpenRazerLightingEffect.Spectrum => CreateSetEffect(WriteProfile, regionId, 0x03, 0, 0, []),
        OpenRazerLightingEffect.Wave => CreateSetEffect(WriteProfile, regionId, 0x04,
            direction == 1 ? (byte)2 : (byte)1, 40, []),
        _ => throw new NotSupportedException(
            $"The Leviathan V2 X does not expose the {effect} lighting effect."),
    };

    public static byte[] CreateGetFirmware() =>
        Create(0x02, 0x00, 0x81, []);

    public static byte[] CreateGetEditionInfo() =>
        Create(0x03, 0x00, 0x86, []);

    public static byte ReadEqPreset(ReadOnlySpan<byte> response, out byte profileId)
    {
        var arguments = Arguments(response, 2);
        profileId = arguments[0];
        var preset = (LeviathanV2XEqPreset)arguments[1];
        if (!Enum.IsDefined(preset))
        {
            throw new InvalidDataException($"Unknown Leviathan EQ preset 0x{arguments[1]:X2}.");
        }

        return arguments[1];
    }

    public static byte[] ReadEqBands(ReadOnlySpan<byte> response, out byte profileId)
    {
        var arguments = Arguments(response, EqBandCount + 1);
        profileId = arguments[0];
        return arguments.Slice(1, EqBandCount).ToArray();
    }

    public static byte ReadBass(ReadOnlySpan<byte> response, out byte profileId)
    {
        var arguments = Arguments(response, 2);
        profileId = arguments[0];
        return arguments[1];
    }

    public static LeviathanV2XInputSource ReadInputSource(ReadOnlySpan<byte> response, out byte profileId)
    {
        var arguments = Arguments(response, 2);
        profileId = arguments[0];
        var source = (LeviathanV2XInputSource)arguments[1];
        if (!Enum.IsDefined(source))
        {
            throw new InvalidDataException($"Unknown Leviathan input source 0x{arguments[1]:X2}.");
        }

        return source;
    }

    public static LeviathanV2XAudioIoStatus ReadAudioIoStatus(ReadOnlySpan<byte> response)
    {
        var arguments = Arguments(response, 3);
        return arguments.Length >= 5
            ? new LeviathanV2XAudioIoStatus(
                arguments[0] != 0,
                arguments[1] != 0,
                arguments[2] != 0,
                arguments[3] != 0,
                arguments[4] != 0)
            : new LeviathanV2XAudioIoStatus(
                arguments[0] != 0,
                arguments[1] != 0,
                arguments[2] != 0,
                Optical: false,
                Bluetooth: false);
    }

    public static Version ReadFirmware(ReadOnlySpan<byte> response)
    {
        var arguments = Arguments(response, 2);
        return new Version(arguments[0], arguments[1]);
    }

    public static (byte KeyboardLayout, byte Edition, byte FirmwareId) ReadEditionInfo(
        ReadOnlySpan<byte> response)
    {
        var arguments = Arguments(response, 3);
        return (arguments[0], arguments[1], arguments[2]);
    }

    public static string GetEditionDisplayName(byte edition) => edition switch
    {
        128 => "Razer | Pokémon Pikachu Edition Soundbar",
        129 => "Razer Leviathan V2 X Pokémon AP Edition",
        131 => "Razer | Kuromi Limited Edition Soundbar",
        132 => "Razer Leviathan V2 X Kuromi Edition",
        _ => "Razer Leviathan V2 X",
    };

    public static byte ReadFirstRegion(ReadOnlySpan<byte> response) =>
        Arguments(response, 1)[0];

    public static byte ReadEffect(ReadOnlySpan<byte> response, out byte profileId, out byte regionId)
    {
        var arguments = Arguments(response, 3);
        profileId = arguments[0];
        regionId = arguments[1];
        return arguments[2];
    }

    private static byte[] Create(byte dataSize, byte commandClass, byte commandId, ReadOnlySpan<byte> arguments) =>
        RazerFeatureReport.CreateRequest(TransportTransactionId, dataSize, commandClass, commandId, arguments);

    private static ReadOnlySpan<byte> Arguments(ReadOnlySpan<byte> response, byte minimumLength)
    {
        if (response.Length != RazerFeatureReport.Length ||
            response[1] is not (0x01 or 0x02) ||
            response[6] < minimumLength ||
            response[6] > 80)
        {
            throw new InvalidDataException("Leviathan response is invalid or unsuccessful.");
        }

        return response.Slice(RazerFeatureReport.ArgumentsOffset, response[6]);
    }
}
