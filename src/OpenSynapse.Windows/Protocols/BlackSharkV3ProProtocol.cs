namespace OpenSynapse.Windows.Protocols;

public enum BlackSharkAncMode : byte
{
    Off = 0x00,
    Anc = 0x01,
    Ambient = 0x50,
}

public sealed record BlackSharkV3ProResponse(
    byte Status,
    byte CommandClass,
    byte Subtype,
    byte[] Arguments);

/// <summary>
/// Razer BlackShark V3 Pro HID command frames. Report 0x02 is a 64-byte
/// output report; byte 9 carries the transport (0x80 wireless, 0x00 wired),
/// byte 10 the command class, byte 12 the command, and byte 62 the XOR CRC.
/// </summary>
public static class BlackSharkV3ProProtocol
{
    public const ushort WiredProductId = 0x0576;
    public const ushort WirelessProductId = 0x0577;
    public const ushort AudioProductId = 0x101B;
    public const int ReportLength = 64;
    public const byte ReportId = 0x02;
    public const byte TransactionId = 0x60;

    private const byte BatteryClass = 0x21;
    private const byte ChargingClass = 0x2A;
    private const byte SidetoneReadClass = 0x19;
    private const byte SidetoneSetClass = 0x99;
    private const byte ThxClass = 0x9E;
    private const byte AncReadClass = 0x12;
    private const byte AncSetClass = 0x92;
    private const byte GameChatReadClass = 0x5C;
    private const byte GameChatSetClass = 0xDC;

    public static bool IsSupportedProductId(ushort productId) =>
        productId is WiredProductId or WirelessProductId or AudioProductId;

    /// <summary>V3 Pro answers the battery query only after a zero sub-parameter.</summary>
    public static byte[] CreateGetBattery(bool wireless = true) =>
        Create(BatteryClass, 0x00, wireless, 0x00);

    /// <summary>Wireless wake sequence observed in Synapse before the first query.</summary>
    public static IReadOnlyList<byte[]> CreateWakeSequence() =>
    [
        CreateForDirection(0x02, 0x00, 0x00),
        CreateForDirection(0x2A, 0x00, 0x00),
        CreateForDirection(0x2A, 0x00, 0x80),
    ];

    public static byte[] CreateGetCharging(bool wireless = true) =>
        Create(ChargingClass, 0x00, wireless);

    public static byte[] CreateGetSidetone(bool wireless = true) =>
        Create(SidetoneReadClass, 0x00, wireless);

    public static byte[] CreateSetSidetone(int level, bool wireless = true)
    {
        if (level is < 0 or > 15)
        {
            throw new ArgumentOutOfRangeException(nameof(level), "BlackShark sidetone level must be between 0 and 15.");
        }

        return Create(SidetoneSetClass, 0x01, wireless, checked((byte)level), 0x00);
    }

    public static byte[] CreateGetThx(bool wireless = true) =>
        Create(ThxClass, 0x00, wireless);

    public static byte[] CreateSetThx(bool enabled, bool wireless = true) =>
        Create(ThxClass, 0x01, wireless, enabled ? (byte)0x01 : (byte)0x00, 0x00);

    public static byte[] CreateGetAnc(bool wireless = true) =>
        Create(AncReadClass, 0x00, wireless);

    public static byte[] CreateSetAnc(BlackSharkAncMode mode, int level, bool wireless = true)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        var normalizedLevel = mode == BlackSharkAncMode.Anc
            ? Math.Clamp(level, 1, 4)
            : mode == BlackSharkAncMode.Ambient ? 1 : 0;
        return Create(AncSetClass, 0x02, wireless,
            (byte)mode, checked((byte)normalizedLevel));
    }

    public static byte[] CreateGetGameChat(bool wireless = true) =>
        Create(GameChatReadClass, 0x00, wireless);

    public static byte[] CreateSetGameChat(int balance, bool wireless = true)
    {
        if (balance is < 0 or > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(balance), "BlackShark game/chat balance must be between 0 and 20.");
        }

        return Create(GameChatSetClass, 0x01, wireless, checked((byte)balance), 0x00);
    }

    public static bool TryParseResponse(ReadOnlySpan<byte> report, out BlackSharkV3ProResponse response)
    {
        response = default!;
        if (report.Length < ReportLength || report[0] != ReportId)
        {
            return false;
        }

        var count = report[12];
        if (count > ReportLength - 13)
        {
            return false;
        }

        response = new BlackSharkV3ProResponse(
            report[1],
            report[10],
            report[11],
            report.Slice(13, count).ToArray());
        return true;
    }

    public static bool HasValidCrc(ReadOnlySpan<byte> report)
    {
        if (report.Length < ReportLength)
        {
            return false;
        }

        byte crc = 0;
        for (var index = 0; index < 62; index++)
        {
            crc ^= report[index];
        }

        return crc == report[62];
    }

    public static byte[] Create(byte commandClass, byte commandId, bool wireless, params byte[] arguments) =>
        CreateForDirection(commandClass, commandId, wireless ? (byte)0x80 : (byte)0x00, arguments);

    private static byte[] CreateForDirection(
        byte commandClass,
        byte commandId,
        byte direction,
        params byte[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Length > ReportLength - 13)
        {
            throw new ArgumentOutOfRangeException(nameof(arguments), "BlackShark command arguments exceed the report size.");
        }

        var report = new byte[ReportLength];
        report[0] = ReportId;
        report[2] = TransactionId;
        report[6] = checked((byte)(4 + arguments.Length));
        report[9] = direction;
        report[10] = commandClass;
        report[11] = 0x00;
        report[12] = commandId;
        arguments.CopyTo(report, 13);

        byte crc = 0;
        for (var index = 0; index < 62; index++)
        {
            crc ^= report[index];
        }

        report[62] = crc;
        return report;
    }
}
