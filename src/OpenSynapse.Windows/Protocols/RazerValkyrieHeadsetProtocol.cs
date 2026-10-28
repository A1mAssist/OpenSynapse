namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Razer AudioValkyrie / AudioBillie 64-byte HID audio channel (report id 2).
/// The bundle's general sender writes a three-byte command at 10..12 and its payload
/// at 13, uses the fixed 0x60 audio transaction id, XORs bytes 0..61 into byte 62,
/// and leaves byte 63 zero. The wired Billie and Valkyrie chunks keep byte 9 at zero;
/// only dongle-forwarded host commands set 0x80 there.
/// </summary>
public static class RazerValkyrieHeadsetProtocol
{
    public const byte ReportId = 2;
    public const int ReportLength = 64;
    public const byte TransactionId = 0x60;
    public const byte StatusOk = 2;
    public const byte StatusResend = 1;
    public const byte StatusResendAlternate = 3;
    public const int TenBandCount = 10;
    public const int EqBandsWithProfileLength = TenBandCount + 1;

    public static readonly byte[] GetBattery = [0x21, 0x00, 0x00];
    public static readonly byte[] BatteryHeader = [0x21, 0x01, 0x01];

    public static readonly byte[] GetCharging = [0x2A, 0x00, 0x00];
    public static readonly byte[] ChargingHeader = [0x2A, 0x01, 0x01];

    public static readonly byte[] GetEqPreset = [0x13, 0x00, 0x00];
    public static readonly byte[] EqPresetHeader = [0x13, 0x01, 0x01];
    public static readonly byte[] SetEqPresetCommand = [0x93, 0x00, 0x01];
    public static readonly byte[] SetEqPresetHeader = [0x93, 0x01, 0x01];

    // The shared equalizer helpers send the profile-prefixed tuple: a one-byte
    // selector on read and the selector plus ten bands on write. The bare tuple is
    // kept for the alternate shape documented during the first CDN pass.
    public static readonly byte[] GetEqBandsBare = [0x15, 0x00, 0x00];
    public static readonly byte[] EqBandsBareHeader = [0x15, 0x01, 0x0A];
    public static readonly byte[] SetEqBandsBareCommand = [0x95, 0x00, 0x0A];
    public static readonly byte[] GetEqBandsWithProfile = [0x15, 0x00, 0x01];
    public static readonly byte[] EqBandsWithProfileHeader = [0x15, 0x01, 0x0B];
    public static readonly byte[] SetEqBandsWithProfileCommand = [0x95, 0x00, 0x0B];
    public static readonly byte[] SetEqBandsHeader = [0x95, 0x01, 0x01];

    public static readonly byte[] GetMicPreset = [0x16, 0x00, 0x00];
    public static readonly byte[] MicPresetHeader = [0x16, 0x01, 0x01];
    public static readonly byte[] SetMicPresetCommand = [0x96, 0x00, 0x01];
    public static readonly byte[] SetMicPresetHeader = [0x96, 0x01, 0x01];

    public static readonly byte[] GetMicBands = [0x17, 0x00, 0x00];
    public static readonly byte[] MicBandsHeader = [0x17, 0x01, 0x0A];
    public static readonly byte[] SetMicBandsCommand = [0x97, 0x00, 0x0A];
    public static readonly byte[] SetMicBandsHeader = [0x97, 0x01, 0x01];

    public static readonly byte[] GetMicMute = [0x55, 0x00, 0x00];
    public static readonly byte[] MicMuteHeader = [0x55, 0x01, 0x01];
    public static readonly byte[] SetMicMuteCommand = [0xE9, 0x00, 0x01];
    public static readonly byte[] SetMicMuteHeader = [0xE9, 0x01, 0x01];

    public static byte[] CreateGetBattery() => CreateRequest(GetBattery);

    public static byte[] CreateGetCharging() => CreateRequest(GetCharging);

    public static byte[] CreateGetEqPreset() => CreateRequest(GetEqPreset);

    public static byte[] CreateSetEqPreset(byte preset) =>
        CreateRequest(SetEqPresetCommand, [preset]);

    public static byte[] CreateGetEqBands(bool withProfile) =>
        withProfile
            ? CreateRequest(GetEqBandsWithProfile, [0x00])
            : CreateRequest(GetEqBandsBare);

    public static byte[] CreateSetEqBands(ReadOnlySpan<byte> bands, bool withProfile)
    {
        if (bands.Length != TenBandCount)
        {
            throw new ArgumentException("Equalizer data must contain 10 bands.", nameof(bands));
        }

        if (!withProfile)
        {
            return CreateRequest(SetEqBandsBareCommand, bands);
        }

        Span<byte> payload = stackalloc byte[EqBandsWithProfileLength];
        payload[0] = 0x00;
        bands.CopyTo(payload[1..]);
        return CreateRequest(SetEqBandsWithProfileCommand, payload);
    }

    public static byte[] CreateGetMicPreset() => CreateRequest(GetMicPreset);

    public static byte[] CreateSetMicPreset(byte preset) =>
        CreateRequest(SetMicPresetCommand, [preset]);

    public static byte[] CreateGetMicBands() => CreateRequest(GetMicBands);

    public static byte[] CreateSetMicBands(ReadOnlySpan<byte> bands)
    {
        if (bands.Length != TenBandCount)
        {
            throw new ArgumentException("Equalizer data must contain 10 bands.", nameof(bands));
        }

        return CreateRequest(SetMicBandsCommand, bands);
    }

    public static byte[] CreateGetMicMute() => CreateRequest(GetMicMute);

    public static byte[] CreateSetMicMute(bool muted) =>
        CreateRequest(SetMicMuteCommand, [muted ? (byte)1 : (byte)0]);

    public static byte[] CreateRequest(
        ReadOnlySpan<byte> command,
        ReadOnlySpan<byte> data = default,
        byte transactionId = TransactionId,
        byte direction = 0x00)
    {
        if (command.Length != 3)
        {
            throw new ArgumentException(
                "An AudioValkyrie command must be a three-byte triplet.", nameof(command));
        }

        if (command.Length + data.Length + 10 > ReportLength - 2)
        {
            throw new ArgumentException("AudioValkyrie command and data exceed the 64-byte report.");
        }

        var report = new byte[ReportLength];
        report[0] = ReportId;
        report[2] = transactionId;
        report[6] = (byte)(command.Length + data.Length + 1);
        report[9] = direction;
        command.CopyTo(report.AsSpan(10));
        data.CopyTo(report.AsSpan(13));
        report[62] = Checksum(report);
        return report;
    }

    public static bool IsSuccess(byte status) => status == StatusOk;

    public static bool IsResend(byte status) =>
        status is StatusResend or StatusResendAlternate;

    public static bool TryReadResponse(
        ReadOnlySpan<byte> report,
        ReadOnlySpan<byte> expectedHeader,
        out byte status,
        out byte[] data) =>
        TryReadResponse(report, expectedHeader, TransactionId, out status, out data);

    public static bool TryReadResponse(
        ReadOnlySpan<byte> report,
        ReadOnlySpan<byte> expectedHeader,
        byte transactionId,
        out byte status,
        out byte[] data)
    {
        status = 0;
        data = [];
        if (report.Length < ReportLength ||
            report[0] != ReportId ||
            report[2] != transactionId ||
            expectedHeader.Length < 3 ||
            report[10] != expectedHeader[0] ||
            report[11] != expectedHeader[1])
        {
            return false;
        }

        var length = expectedHeader[2];
        if (13 + length > report.Length)
        {
            return false;
        }

        status = report[1];
        data = report.Slice(13, length).ToArray();
        return true;
    }

    public static byte ReadBatteryPercent(ReadOnlySpan<byte> value)
    {
        Require(value, 1);
        return Math.Min(value[0], (byte)100);
    }

    public static bool ReadChargingStatus(ReadOnlySpan<byte> value)
    {
        Require(value, 1);
        return value[0] == 1;
    }

    public static byte ReadEqPreset(ReadOnlySpan<byte> value)
    {
        Require(value, 1);
        return value[0];
    }

    public static byte[] ReadEqBands(ReadOnlySpan<byte> value, bool withProfile)
    {
        if (!withProfile)
        {
            Require(value, TenBandCount);
            return value[..TenBandCount].ToArray();
        }

        Require(value, EqBandsWithProfileLength);
        return value[1..EqBandsWithProfileLength].ToArray();
    }

    public static byte ReadMicPreset(ReadOnlySpan<byte> value)
    {
        Require(value, 1);
        return value[0];
    }

    public static byte[] ReadMicBands(ReadOnlySpan<byte> value)
    {
        Require(value, TenBandCount);
        return value[..TenBandCount].ToArray();
    }

    public static bool ReadMicMute(ReadOnlySpan<byte> value)
    {
        Require(value, 1);
        return value[0] == 1;
    }

    /// <summary>
    /// The bundles store an EQ level as sign-magnitude: -12 becomes 140 (128 + 12),
    /// while +12 stays 12. The service passes these bytes through unchanged.
    /// </summary>
    public static byte EncodeEqBandLevel(sbyte level) =>
        level < 0 ? (byte)(128 + Math.Abs((int)level)) : (byte)level;

    public static sbyte DecodeEqBandLevel(byte raw) =>
        raw > 128 ? (sbyte)(128 - raw) : (sbyte)raw;

    private static byte Checksum(ReadOnlySpan<byte> report)
    {
        byte checksum = 0;
        for (var index = 0; index < ReportLength - 2; index++)
        {
            checksum ^= report[index];
        }

        return checksum;
    }

    private static void Require(ReadOnlySpan<byte> value, int length)
    {
        if (value.Length < length)
        {
            throw new InvalidDataException(
                $"AudioValkyrie response has {value.Length} data bytes; {length} required.");
        }
    }
}
