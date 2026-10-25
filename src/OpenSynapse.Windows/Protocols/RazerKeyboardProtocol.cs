using System.Text;

namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Protocol 25 keyboard commands taken from the middleware bundles of the Huntsman V3 and
/// Pro Type families. Every request uses the shared 90-byte OpenRazer envelope, so the
/// builders below only declare data size, class, command and arguments.
/// </summary>
public static class RazerKeyboardProtocol
{
    public const int ReportLength = RazerFeatureReport.Length;
    public const int ArgumentsOffset = RazerFeatureReport.ArgumentsOffset;

    // The 8 kHz keyboards encode the polling period as a bit mask, not as hertz.
    public static readonly IReadOnlyList<(byte Code, int Hertz)> PollingRates =
    [
        (0x01, 8_000),
        (0x02, 4_000),
        (0x04, 2_000),
        (0x08, 1_000),
        (0x10, 500),
        (0x20, 250),
        (0x40, 125),
    ];

    public static byte[] GetExtendedFirmware(byte transactionId) =>
        Create(transactionId, 4, 0x00, 0x87);

    public static byte[] GetSerialNumber(byte transactionId) =>
        Create(transactionId, 22, 0x00, 0x82);

    // Pro Type family answers the serial on the class 1 command instead.
    public static byte[] GetSerialNumberAlt(byte transactionId) =>
        Create(transactionId, 0, 0x01, 0x83);

    public static byte[] GetGameModeState(byte transactionId) =>
        Create(transactionId, 4, 0x00, 0x88);

    public static byte[] SetGameModeState(
        byte transactionId,
        byte gameMode,
        byte keyCover,
        byte lifted) =>
        Create(transactionId, 4, 0x00, 0x08, gameMode, keyCover, lifted);

    public static byte[] GetPollingRate(byte transactionId) =>
        Create(transactionId, 2, 0x00, 0x8E);

    public static byte[] SetPollingRate(byte transactionId, byte code) =>
        Create(transactionId, 2, 0x00, 0x0E, code);

    public static byte[] GetBatteryLevel(byte transactionId) =>
        Create(transactionId, 2, 0x07, 0x80);

    public static byte[] GetChargingStatus(byte transactionId) =>
        Create(transactionId, 2, 0x07, 0x84);

    public static Version ReadFirmware(ReadOnlySpan<byte> response)
    {
        var data = Arguments(response);
        Require(data, 2);
        return new Version(data[0], data[1]);
    }

    public static string ReadSerialNumber(ReadOnlySpan<byte> response)
    {
        var data = Arguments(response);
        Require(data, 1);
        var end = 0;
        while (end < data.Length && data[end] != 0 && data[end] is >= 0x20 and < 0x7F)
        {
            end++;
        }

        return Encoding.ASCII.GetString(data[..end]).Trim();
    }

    public static (byte GameMode, byte KeyCover, byte Lifted) ReadGameModeState(ReadOnlySpan<byte> response)
    {
        var data = Arguments(response);
        Require(data, 3);
        return (data[0], data[1], data[2]);
    }

    public static int? ReadPollingRate(ReadOnlySpan<byte> response)
    {
        var data = Arguments(response);
        Require(data, 2);
        var code = data[1];
        foreach (var (candidate, hertz) in PollingRates)
        {
            if (candidate == code)
            {
                return hertz;
            }
        }

        return null;
    }

    public static byte ReadBatteryPercent(ReadOnlySpan<byte> response)
    {
        var data = Arguments(response);
        Require(data, 1);
        return (byte)(data[0] * 100 / 255);
    }

    public static bool ReadChargingStatus(ReadOnlySpan<byte> response)
    {
        var data = Arguments(response);
        Require(data, 1);
        return data[0] != 0;
    }

    public static byte? PollingCode(int hertz)
    {
        foreach (var (code, value) in PollingRates)
        {
            if (value == hertz)
            {
                return code;
            }
        }

        return null;
    }

    private static byte[] Create(
        byte transactionId,
        byte dataSize,
        byte commandClass,
        byte commandId,
        params byte[] arguments) =>
        RazerFeatureReport.CreateRequest(transactionId, dataSize, commandClass, commandId, arguments);

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
