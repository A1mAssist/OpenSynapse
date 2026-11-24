namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Xbox GIP headset channels exposed by Razer.Audio.Simone.Control.dll.
/// Every command is a fixed 60-byte payload sent as HID report id 0x0F.
/// The six dialects are distinct frame/response layouts; do not share a codec.
/// </summary>
public enum RazerGipHeadsetDialect
{
    Simone,
    SimoneX,
    ValkyrieT1T2,
    ValkyrieT3,
    OdessaT1R,
    Protocol40,
}

public readonly record struct RazerGipHeadsetCommands(
    ushort Firmware,
    ushort Battery,
    ushort Charging,
    ushort Link,
    ushort EqPresetRead,
    ushort EqPresetWrite,
    ushort EqBandsRead,
    ushort EqBandsWrite,
    ushort SidetoneLevelRead,
    ushort SidetoneLevelWrite,
    ushort SidetoneSwitchRead,
    ushort SidetoneSwitchWrite,
    ushort AncRead,
    ushort AncWrite,
    ushort DndRead,
    ushort DndWrite);

/// <summary>
/// Wire codec for the native GIP headset families. Command numbers are the exact
/// 16-bit values on the wire; Protocol40 packs its class in the high byte and id in
/// the low byte.
/// </summary>
public static class RazerGipHeadsetProtocol
{
    public const byte ReportId = 0x0F;
    public const int FrameLength = 60;
    public const int TenBandCount = 10;
    public const int FiveBandCount = 5;

    private static readonly RazerGipHeadsetCommands SimoneCommands = new(
        Firmware: 0x0087,
        Battery: 0x0780,
        Charging: 0x0784,
        Link: 0,
        EqPresetRead: 0x0882, EqPresetWrite: 0x0802,
        EqBandsRead: 0x0884, EqBandsWrite: 0x0804,
        SidetoneLevelRead: 0x0890, SidetoneLevelWrite: 0x0810,
        SidetoneSwitchRead: 0, SidetoneSwitchWrite: 0,
        AncRead: 0, AncWrite: 0,
        DndRead: 0, DndWrite: 0);

    private static readonly RazerGipHeadsetCommands SimoneXCommands = new(
        Firmware: 0x0200,
        Battery: 0x2100,
        Charging: 0x2A00,
        Link: 0x2000,
        EqPresetRead: 0x1300, EqPresetWrite: 0x9300,
        EqBandsRead: 0x1500, EqBandsWrite: 0x9500,
        SidetoneLevelRead: 0x1900, SidetoneLevelWrite: 0x9900,
        SidetoneSwitchRead: 0, SidetoneSwitchWrite: 0,
        AncRead: 0x1200, AncWrite: 0x9200,
        DndRead: 0, DndWrite: 0);

    private static readonly RazerGipHeadsetCommands ValkyrieT1T2Commands = new(
        Firmware: 0x0200,
        Battery: 0x2100,
        Charging: 0x2A00,
        Link: 0x2000,
        EqPresetRead: 0, EqPresetWrite: 0,
        EqBandsRead: 0x1500, EqBandsWrite: 0x9500,
        SidetoneLevelRead: 0x1900, SidetoneLevelWrite: 0x9900,
        SidetoneSwitchRead: 0, SidetoneSwitchWrite: 0,
        AncRead: 0x1200, AncWrite: 0x9200,
        DndRead: 0xA700, DndWrite: 0xA700);

    private static readonly RazerGipHeadsetCommands ValkyrieT3Commands = new(
        Firmware: 0x0087,
        Battery: 0x0780,
        Charging: 0x0784,
        Link: 0x00B8,
        EqPresetRead: 0x1880, EqPresetWrite: 0x1800,
        EqBandsRead: 0x1881, EqBandsWrite: 0x1801,
        SidetoneLevelRead: 0x1883, SidetoneLevelWrite: 0x1803,
        SidetoneSwitchRead: 0x1884, SidetoneSwitchWrite: 0x1804,
        AncRead: 0, AncWrite: 0,
        DndRead: 0x1885, DndWrite: 0x1805);

    private static readonly RazerGipHeadsetCommands OdessaCommands = new(
        Firmware: 0x0200,
        Battery: 0x2100,
        Charging: 0x2A00,
        Link: 0x2000,
        EqPresetRead: 0x1300, EqPresetWrite: 0x9300,
        EqBandsRead: 0x1500, EqBandsWrite: 0x9500,
        SidetoneLevelRead: 0x1900, SidetoneLevelWrite: 0x9900,
        SidetoneSwitchRead: 0x1800, SidetoneSwitchWrite: 0x9800,
        AncRead: 0x1200, AncWrite: 0x9200,
        DndRead: 0xA700, DndWrite: 0xA700);

    private static readonly RazerGipHeadsetCommands Protocol40Commands = new(
        Firmware: 0x0087,
        Battery: 0x0780,
        Charging: 0,
        Link: 0x189A,
        EqPresetRead: 0x1880, EqPresetWrite: 0x1800,
        EqBandsRead: 0x1881, EqBandsWrite: 0x1801,
        SidetoneLevelRead: 0, SidetoneLevelWrite: 0,
        SidetoneSwitchRead: 0, SidetoneSwitchWrite: 0,
        AncRead: 0, AncWrite: 0,
        DndRead: 0x1885, DndWrite: 0x1805);

    public static bool IsSupported(ushort productId) => productId is
        0x0A25 or 0x0A27 or 0x0A30 or 0x0A32 or 0x0A41 or
        0x0A4D or 0x0A50 or 0x0A53 or 0x0A69;

    public static RazerGipHeadsetDialect ResolveDialect(ushort productId) => productId switch
    {
        0x0A25 or 0x0A27 => RazerGipHeadsetDialect.Simone,
        0x0A30 or 0x0A32 => RazerGipHeadsetDialect.SimoneX,
        0x0A41 => RazerGipHeadsetDialect.OdessaT1R,
        0x0A4D or 0x0A50 => RazerGipHeadsetDialect.ValkyrieT1T2,
        0x0A53 => RazerGipHeadsetDialect.ValkyrieT3,
        0x0A69 => RazerGipHeadsetDialect.Protocol40,
        _ => throw new NotSupportedException($"Product {productId:X4} is not a GIP headset."),
    };

    public static RazerGipHeadsetCommands Commands(ushort productId) =>
        ResolveDialect(productId) switch
        {
            RazerGipHeadsetDialect.Simone => SimoneCommands,
            RazerGipHeadsetDialect.SimoneX => SimoneXCommands,
            RazerGipHeadsetDialect.ValkyrieT1T2 => ValkyrieT1T2Commands,
            RazerGipHeadsetDialect.ValkyrieT3 => ValkyrieT3Commands,
            RazerGipHeadsetDialect.OdessaT1R => OdessaCommands,
            _ => Protocol40Commands,
        };

    public static bool SupportsEqPreset(ushort productId) =>
        Commands(productId).EqPresetWrite != 0;

    /// <summary>The bundle definition carries no Custom slot on Valkyrie T1/T2 or T3.</summary>
    public static bool SupportsCustomEqPreset(ushort productId) =>
        ResolveDialect(productId) is not (
            RazerGipHeadsetDialect.ValkyrieT1T2 or RazerGipHeadsetDialect.ValkyrieT3);

    // Simone exposes a five-band custom curve; the shared equalizer card is ten-band.
    public static bool SupportsEqBands(ushort productId) =>
        ResolveDialect(productId) != RazerGipHeadsetDialect.Simone;

    public static bool SupportsSidetone(ushort productId) =>
        Commands(productId).SidetoneLevelWrite != 0;

    public static bool SupportsAnc(ushort productId) =>
        Commands(productId).AncWrite != 0;

    public static bool SupportsDnd(ushort productId) =>
        Commands(productId).DndWrite != 0;

    public static int EqBandCount(ushort productId) =>
        ResolveDialect(productId) == RazerGipHeadsetDialect.Simone
            ? FiveBandCount
            : TenBandCount;

    /// <summary>Odessa and SimoneX take raw two's-complement int8; the others use sign-magnitude.</summary>
    public static bool UsesSignMagnitudeEq(ushort productId) =>
        ResolveDialect(productId) is RazerGipHeadsetDialect.ValkyrieT1T2 or
            RazerGipHeadsetDialect.ValkyrieT3 or RazerGipHeadsetDialect.Protocol40;

    public static byte EncodeEqBand(ushort productId, double decibels)
    {
        var rounded = Math.Clamp((int)Math.Round(decibels), -127, 127);
        return UsesSignMagnitudeEq(productId)
            ? (byte)(rounded < 0 ? 0x80 | -rounded : rounded)
            : unchecked((byte)(sbyte)rounded);
    }

    public static double DecodeEqBand(ushort productId, byte value) =>
        UsesSignMagnitudeEq(productId)
            ? (value & 0x80) != 0 ? -(value & 0x7F) : value
            : unchecked((sbyte)value);

    public static byte[] CreateRead(ushort productId, ushort command) =>
        CreateWrite(productId, command, []);

    /// <summary>
    /// Builds the 60-byte payload. Command numbers already carry their read bit;
    /// only Protocol40 splits the packed value into class/id.
    /// </summary>
    public static byte[] CreateWrite(
        ushort productId,
        ushort command,
        ReadOnlySpan<byte> payload)
    {
        var dialect = ResolveDialect(productId);
        var maximum = dialect switch
        {
            RazerGipHeadsetDialect.Simone => 50,
            RazerGipHeadsetDialect.SimoneX or RazerGipHeadsetDialect.OdessaT1R => 57,
            RazerGipHeadsetDialect.ValkyrieT1T2 => 56,
            _ => 49,
        };
        if (payload.Length > maximum)
        {
            throw new ArgumentException(
                $"GIP payload is {payload.Length} bytes; {dialect} allows {maximum}.",
                nameof(payload));
        }

        var frame = new byte[FrameLength];
        switch (dialect)
        {
            case RazerGipHeadsetDialect.Simone:
                frame[0] = 0x00;
                frame[1] = 0xAA;
                WriteUInt16BigEndian(frame, 4, (ushort)payload.Length);
                WriteUInt16BigEndian(frame, 6, command);
                payload.CopyTo(frame.AsSpan(8));
                frame[58] = (byte)Xor(frame, 3, 55);
                break;
            case RazerGipHeadsetDialect.SimoneX:
            case RazerGipHeadsetDialect.OdessaT1R:
                WriteUInt16BigEndian(frame, 0, command);
                frame[2] = (byte)payload.Length;
                payload.CopyTo(frame.AsSpan(3));
                break;
            case RazerGipHeadsetDialect.ValkyrieT1T2:
                frame[0] = 0x80;
                WriteUInt16BigEndian(frame, 1, command);
                frame[3] = (byte)payload.Length;
                payload.CopyTo(frame.AsSpan(4));
                break;
            case RazerGipHeadsetDialect.ValkyrieT3:
                frame[0] = 0x07;
                frame[1] = 0x00;
                frame[2] = 0xAA;
                WriteUInt16BigEndian(frame, 5, (ushort)payload.Length);
                WriteUInt16BigEndian(frame, 7, command);
                payload.CopyTo(frame.AsSpan(9));
                frame[58] = (byte)Xor(frame, 3, 55);
                break;
            default:
                frame[0] = 0x02;
                frame[1] = 0x00;
                frame[2] = 0xF2;
                WriteUInt16BigEndian(frame, 5, (ushort)payload.Length);
                frame[7] = (byte)(command >> 8);
                frame[8] = (byte)command;
                payload.CopyTo(frame.AsSpan(9));
                frame[58] = (byte)Xor(frame, 3, 55);
                break;
        }

        return frame;
    }

    /// <summary>
    /// Returns true when the frame answers <paramref name="command"/>.
    /// <paramref name="retry"/> mirrors the DLL's busy/resend status codes.
    /// </summary>
    public static bool TryReadPayload(
        ushort productId,
        ReadOnlySpan<byte> frame,
        ushort command,
        out ReadOnlySpan<byte> payload,
        out bool retry)
    {
        payload = default;
        retry = false;
        if (frame.Length < FrameLength)
        {
            return false;
        }

        switch (ResolveDialect(productId))
        {
            case RazerGipHeadsetDialect.Simone:
                if (frame[1] != 0xAA || ReadUInt16BigEndian(frame, 2) != command)
                {
                    return false;
                }
                if (frame[0] != 2)
                {
                    retry = true;
                    return false;
                }
                payload = Slice(frame, 8, ReadUInt16BigEndian(frame, 4), 50);
                return true;

            case RazerGipHeadsetDialect.ValkyrieT3:
                if (frame[2] != 0xAA || ReadUInt16BigEndian(frame, 7) != command)
                {
                    return false;
                }
                if (frame[1] == 3)
                {
                    retry = true;
                    return false;
                }
                if (frame[1] != 2)
                {
                    return false;
                }
                payload = Slice(frame, 9, ReadUInt16BigEndian(frame, 5), 49);
                return true;

            case RazerGipHeadsetDialect.ValkyrieT1T2:
                if (frame[0] != 0x80 || frame[1] != (byte)(command >> 8))
                {
                    return false;
                }
                if (frame[2] == 2)
                {
                    retry = true;
                    return false;
                }
                if (frame[2] != 1)
                {
                    return false;
                }
                payload = frame[4..];
                return true;

            case RazerGipHeadsetDialect.SimoneX:
            case RazerGipHeadsetDialect.OdessaT1R:
                if (frame[0] != (byte)(command >> 8) || frame[1] != 1)
                {
                    return false;
                }
                // Write commands answer with a one-byte status at [3]: 0 = ok,
                // 1 = busy, anything else = failure.
                if ((command & 0x8000) != 0 && frame[2] == 1 && frame[3] != 0)
                {
                    retry = frame[3] == 1;
                    return false;
                }
                payload = Slice(frame, 3, frame[2], 57);
                return true;

            default:
                if (frame[1] == 1 || frame[1] == 3)
                {
                    retry = ReadUInt16BigEndian(frame, 7) == command;
                    return false;
                }
                if (frame[1] != 2 || ReadUInt16BigEndian(frame, 7) != command)
                {
                    return false;
                }
                payload = Slice(frame, 9, ReadUInt16BigEndian(frame, 5), 49);
                return true;
        }
    }

    public static Version ReadFirmware(ushort productId, ReadOnlySpan<byte> payload)
    {
        // SimoneX's 24-byte reply keeps the version at 20..23; the 2610 earbuds
        // use the first three bytes instead.
        if (ResolveDialect(productId) == RazerGipHeadsetDialect.SimoneX)
        {
            if (productId == 0x0A32)
            {
                return payload.Length >= 3
                    ? new Version(payload[0], payload[1], payload[2])
                    : throw new InvalidDataException(
                        $"GIP firmware response is {payload.Length} bytes, expected 3.");
            }
            if (payload.Length >= 24)
            {
                return new Version(payload[20], payload[21], payload[22], payload[23]);
            }
        }

        return payload.Length switch
        {
            >= 4 => new Version(payload[0], payload[1], payload[2], payload[3]),
            3 => new Version(payload[0], payload[1], payload[2]),
            _ => throw new InvalidDataException(
                $"GIP firmware response is {payload.Length} bytes, expected at least 3."),
        };
    }

    /// <summary>
    /// Only the Hammerhead HyperSpeed (0A32) reports two earbud percentages.
    /// The other two-byte replies carry the level first, so byte one wins.
    /// </summary>
    public static byte ReadBatteryPercent(ushort productId, ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty || payload[0] > 100)
        {
            throw new InvalidDataException("GIP battery response is outside 0..100.");
        }

        return productId == 0x0A32 && payload.Length >= 2 && payload[1] <= 100
            ? Math.Min(payload[0], payload[1])
            : payload[0];
    }

    public static byte ReadEqPreset(ushort productId, ReadOnlySpan<byte> payload) =>
        ResolveDialect(productId) switch
        {
            RazerGipHeadsetDialect.Simone => payload.Length >= 2
                ? payload[1]
                : throw new InvalidDataException("Simone EQ preset response is shorter than 2 bytes."),
            RazerGipHeadsetDialect.ValkyrieT3 => payload.Length >= 4
                ? payload[3]
                : throw new InvalidDataException("Valkyrie T3 EQ preset response is shorter than 4 bytes."),
            RazerGipHeadsetDialect.Protocol40 => payload.Length >= 4
                ? payload[3]
                : throw new InvalidDataException(
                    "Protocol40 EQ preset response is shorter than 4 bytes."),
            _ => payload.Length >= 1
                ? payload[0]
                : throw new InvalidDataException("GIP EQ preset response is empty."),
        };

    /// <summary>The band array starts after the per-model selector prefix.</summary>
    public static byte[] ReadEqBands(ushort productId, ReadOnlySpan<byte> payload)
    {
        var dialect = ResolveDialect(productId);
        var count = EqBandCount(productId);
        var offset = dialect switch
        {
            RazerGipHeadsetDialect.ValkyrieT1T2 => 1,
            RazerGipHeadsetDialect.ValkyrieT3 or RazerGipHeadsetDialect.Protocol40 => 2,
            _ => 0,
        };
        if (payload.Length < offset + count)
        {
            throw new InvalidDataException(
                $"GIP EQ response is {payload.Length} bytes; {dialect} needs {offset + count}.");
        }

        return payload.Slice(offset, count).ToArray();
    }

    public static byte[] CreateEqBandsPayload(ushort productId, IReadOnlyList<byte> bands)
    {
        var dialect = ResolveDialect(productId);
        var count = EqBandCount(productId);
        if (bands.Count != count)
        {
            throw new ArgumentException(
                $"GIP {dialect} EQ needs {count} bands, got {bands.Count}.", nameof(bands));
        }

        var payload = new byte[dialect switch
        {
            RazerGipHeadsetDialect.ValkyrieT1T2 => 1 + TenBandCount,
            RazerGipHeadsetDialect.ValkyrieT3 or RazerGipHeadsetDialect.Protocol40 => 2 + TenBandCount,
            _ => count,
        }];
        var offset = payload.Length - count;
        for (var index = 0; index < count; index++)
        {
            payload[offset + index] = bands[index];
        }

        // The fixed prefixes select the live profile and the primary device.
        if (offset > 0)
        {
            payload[0] = 1;
        }
        if (offset > 1)
        {
            payload[1] = 1;
        }
        return payload;
    }

    private static ReadOnlySpan<byte> Slice(
        ReadOnlySpan<byte> frame,
        int offset,
        int declaredLength,
        int maximum) =>
        frame.Slice(offset, Math.Clamp(declaredLength, 0, maximum));

    private static int Xor(ReadOnlySpan<byte> frame, int offset, int length)
    {
        var value = 0;
        for (var index = 0; index < length; index++)
        {
            value ^= frame[offset + index];
        }
        return value;
    }

    private static void WriteUInt16BigEndian(Span<byte> frame, int offset, ushort value)
    {
        frame[offset] = (byte)(value >> 8);
        frame[offset + 1] = (byte)value;
    }

    private static ushort ReadUInt16BigEndian(ReadOnlySpan<byte> frame, int offset) =>
        (ushort)((frame[offset] << 8) | frame[offset + 1]);
}
