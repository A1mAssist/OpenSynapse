using System.Collections.Frozen;

namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Razer's MXIC audio channel, used by the 2023 BlackShark V2 Pro (1532:0555).
/// Requests are 64-byte output reports with a 50 41 sync word and a little-endian
/// payload length. Responses use 50 49 sync words and may carry several sub-responses.
/// </summary>
public static class RazerMxicHeadsetProtocol
{
    public const ushort ProductId = 0x0555;
    public const byte ReportId = 2;
    public const int ReportLength = 64;
    public const byte StatusOk = 0x4F;
    public const byte RazerCommand = 8;
    public const byte RemoteCommand = 14;
    public const int TenBandCount = 10;

    public static readonly byte[] GetFirmware = [0x02, 0x00, 0x00];
    public static readonly byte[] FirmwareHeader = [0x02, 0x01, 0x18];

    public static readonly byte[] GetBattery = [0x21, 0x00, 0x00];
    public static readonly byte[] BatteryHeader = [0x21, 0x01, 0x01];

    public static readonly byte[] GetCharging = [0x2A, 0x00, 0x00];
    public static readonly byte[] ChargingHeader = [0x2A, 0x01, 0x01];

    public static readonly byte[] GetEqPreset = [0x13, 0x00, 0x00];
    public static readonly byte[] EqPresetHeader = [0x13, 0x01, 0x01];
    public static readonly byte[] SetEqPresetCommand = [0x93, 0x00, 0x01];
    public static readonly byte[] SetEqPresetHeader = [0x93, 0x01, 0x01];

    public static readonly byte[] GetEqBands = [0x15, 0x00, 0x00];
    public static readonly byte[] EqBandsHeader = [0x15, 0x01, 0x0A];
    public static readonly byte[] SetEqBandsCommand = [0x95, 0x00, 0x0A];
    public static readonly byte[] SetEqBandsHeader = [0x95, 0x01, 0x01];

    public static readonly byte[] GetMicMute = [0x55, 0x00, 0x00];
    public static readonly byte[] MicMuteHeader = [0x55, 0x01, 0x01];
    public static readonly byte[] SetMicMuteCommand = [0xE9, 0x00, 0x01];
    public static readonly byte[] SetMicMuteHeader = [0xE9, 0x01, 0x01];

    /// <summary>
    /// Products whose audio channel uses this 64-byte MXIC envelope. The 2026
    /// BlackShark V3 Xbox/PlayStation identities select the AudioValkyrie chunk, whose
    /// command triplets are the same ones defined above: EQ preset 13:00/93:00, EQ
    /// bands 15:00/95:00 (10 bands), mic mute 55:00/E9:00, battery 21:00 and
    /// charging 2A:00. The Hammerhead V3 and BlackShark V3 X families use a different
    /// 0x18 command set and are not listed here.
    /// </summary>
    private static readonly FrozenSet<ushort> ProductIds = new ushort[]
    {
        ProductId,
        0x0A4E,
        0x0A51,
        0x101E,
        0x101F,
    }.ToFrozenSet();

    public static bool IsSupported(ushort productId) => ProductIds.Contains(productId);

    public static byte[] CreateRemoteMode(bool enabled) =>
        Create(RemoteCommand, [0xE1], [enabled ? (byte)1 : (byte)0]);

    public static byte[] CreateRequest(ReadOnlySpan<byte> command, ReadOnlySpan<byte> data = default)
    {
        if (command.IsEmpty)
        {
            throw new ArgumentException("An MXIC command is required.", nameof(command));
        }

        return Create(RazerCommand, command, data);
    }

    public static byte[] CreateSetEqPreset(byte preset) =>
        CreateRequest(SetEqPresetCommand, [preset]);

    public static byte[] CreateSetEqBands(ReadOnlySpan<byte> bands)
    {
        if (bands.Length != TenBandCount)
        {
            throw new ArgumentException("Equalizer data must contain 10 bands.", nameof(bands));
        }

        return CreateRequest(SetEqBandsCommand, bands);
    }

    public static byte[] CreateSetMicMute(bool muted) =>
        CreateRequest(SetMicMuteCommand, [muted ? (byte)1 : (byte)0]);

    public static bool TryReadValue(
        ReadOnlySpan<byte> report,
        ReadOnlySpan<byte> expectedHeader,
        out byte[] value)
    {
        value = [];
        var offset = report.Length > 0 && report[0] == StatusOk ? 1 : 0;
        if (report.Length < offset + 2 || report[offset] != ReportId)
        {
            return false;
        }

        var payload = report[(offset + 2)..];
        for (var index = 0; index + 10 <= payload.Length;)
        {
            if (payload[index] != 0x50 || payload[index + 1] != 0x49)
            {
                index++;
                continue;
            }

            var length = payload[index + 8] | (payload[index + 9] << 8);
            if (index + 10 + length > payload.Length)
            {
                return false;
            }

            var data = payload.Slice(index + 10, length);
            if (data.Length >= expectedHeader.Length &&
                data[..expectedHeader.Length].SequenceEqual(expectedHeader))
            {
                value = data[expectedHeader.Length..].ToArray();
                return true;
            }

            index += 10 + length;
        }

        return false;
    }

    public static Version ReadFirmware(ReadOnlySpan<byte> value)
    {
        Require(value, 4);
        return new Version(value[0], value[1], value[2], value[3]);
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

    public static byte[] ReadEqBands(ReadOnlySpan<byte> value)
    {
        Require(value, TenBandCount);
        return value[..TenBandCount].ToArray();
    }

    public static bool ReadMicMute(ReadOnlySpan<byte> value)
    {
        Require(value, 1);
        return value[0] == 1;
    }

    private static byte[] Create(
        byte commandType,
        ReadOnlySpan<byte> command,
        ReadOnlySpan<byte> data)
    {
        var total = command.Length + data.Length;
        if (total + 10 > ReportLength)
        {
            throw new ArgumentException("MXIC command and data exceed the 64-byte report.");
        }

        var report = new byte[ReportLength];
        report[0] = ReportId;
        report[1] = 0x80;
        report[2] = (byte)(total + 5);
        report[5] = 0x50;
        report[6] = 0x41;
        report[7] = commandType;
        report[9] = (byte)total;
        command.CopyTo(report.AsSpan(10));
        data.CopyTo(report.AsSpan(10 + command.Length));
        return report;
    }

    private static void Require(ReadOnlySpan<byte> value, int length)
    {
        if (value.Length < length)
        {
            throw new InvalidDataException(
                $"MXIC response has {value.Length} data bytes; {length} required.");
        }
    }
}
