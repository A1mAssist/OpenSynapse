namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// protocol40 headset channel selected by rzDevice40Headset / rzDevice40HeadsetAiroha
/// (1532:0593 Barracuda V2 Pro). The bundle builds 64-byte report id 2 with the
/// 0x60 transaction at byte 2, class/id/size at 5..8, payload at 9 and XOR of
/// bytes 3..61 at 62. The Airoha class only exposes battery and charging; EQ and
/// microphone mute live in rzDevice40, which this PID does not select.
/// </summary>
public static class RazerProtocol40HeadsetProtocol
{
    public const byte ReportId = 2;
    public const int ReportLength = 64;
    public const byte TransactionId = 0x60;
    public const byte StatusOk = 2;

    public static readonly byte[] GetBattery = [0x00, 0x07, 0x80];
    public static readonly byte[] GetCharging = [0x02, 0x07, 0x84];
    public static readonly byte[] GetConnect = [0x00, 0x00, 0x87];

    public static bool IsSupported(ushort productId) => productId == 0x0593;

    // rzDevice40Headset.connectDevice writes [0,0,135] once to wake the HID channel.
    public static byte[] CreateConnect() => CreateRequest(GetConnect);

    public static byte[] CreateGetBattery() => CreateRequest(GetBattery);

    public static byte[] CreateGetCharging() => CreateRequest(GetCharging, [0x00, 0x00]);

    public static byte[] CreateRequest(
        ReadOnlySpan<byte> command,
        ReadOnlySpan<byte> data = default)
    {
        if (command.Length != 3)
        {
            throw new ArgumentException(
                "A protocol40 headset command must be a three-byte triplet.", nameof(command));
        }

        if (9 + data.Length > ReportLength - 2)
        {
            throw new ArgumentException("protocol40 command and data exceed the 64-byte report.");
        }

        var report = new byte[ReportLength];
        report[0] = ReportId;
        report[2] = TransactionId;
        report[6] = command[0];
        report[7] = command[1];
        report[8] = command[2];
        data.CopyTo(report.AsSpan(9));
        report[62] = Checksum(report);
        return report;
    }

    public static bool TryReadValue(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request,
        out byte[] value)
    {
        value = [];
        if (response.Length < ReportLength ||
            request.Length < 9 ||
            response[0] != request[0] ||
            response[1] != StatusOk ||
            response[2] != request[2] ||
            response[7] != request[7] ||
            response[8] != request[8])
        {
            return false;
        }

        var length = response[6];
        if (length > ReportLength - 9)
        {
            return false;
        }

        value = response.Slice(9, length).ToArray();
        return true;
    }

    // The bundle reports both earbud levels and takes the lower one; a headset
    // reply carries the same level in both bytes.
    public static byte ReadBatteryPercent(ReadOnlySpan<byte> value)
    {
        Require(value, 2);
        var first = Math.Min(value[0], (byte)100);
        var second = Math.Min(value[1], (byte)100);
        return Math.Min(first, second);
    }

    public static bool ReadChargingStatus(ReadOnlySpan<byte> value)
    {
        Require(value, 2);
        return value[1] == 1;
    }

    private static byte Checksum(ReadOnlySpan<byte> report)
    {
        byte checksum = 0;
        for (var index = 3; index < ReportLength - 2; index++)
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
                $"protocol40 response has {value.Length} data bytes; {length} required.");
        }
    }
}
