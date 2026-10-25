using System.Buffers.Binary;

namespace OpenSynapse.Windows.Protocols;

/// <summary>One parsed protocol 30 reply. Status 2 is the middleware's "Command Successful".</summary>
public sealed record RazerBleResponse(
    byte TransactionId,
    byte CommandClass,
    byte CommandId,
    byte Status,
    IReadOnlyList<byte> Payload,
    int HeaderLength)
{
    public bool IsSuccess => Status == 0x02;

    /// <summary>Not Supported / Not Supported in current device mode: the caller should hide the feature.</summary>
    public bool IsUnsupported => Status is 0x05 or 0x09;
}

public sealed record RazerBleFirmware(byte Major, byte Minor, byte Internal, byte Reserved)
{
    public string ToVersionString() => $"{Major}.{Minor}.{Internal}";
}

/// <summary>
/// Razer protocol 30 over BLE, reconstructed from the middleware class <c>rzDevice30</c>.
/// Requests carry an 8-byte header and no CRC; replies carry a 20-byte header with the
/// status byte at offset 7, falling back to an 8-byte header when the declared length disagrees.
/// </summary>
public static class RazerBleProtocol30
{
    public static readonly Guid ServiceUuid = new("52401523-f97c-7f90-0e7f-6c6f4e36db1c");
    public static readonly Guid WriteCharacteristicUuid = new("52401524-f97c-7f90-0e7f-6c6f4e36db1c");
    public static readonly Guid NotifyCharacteristicUuid = new("52401525-f97c-7f90-0e7f-6c6f4e36db1c");

    public const int RequestHeaderLength = 8;
    public const int ResponseHeaderLength = 20;
    public const byte MaximumTransactionId = 31;
    public const byte StatusSuccessful = 0x02;
    public const byte StatusBusy = 0x01;
    public const byte StatusFailure = 0x03;

    // The middleware sends the payload length in 24 bits; >=255 needs an explicit packet size.
    public const int MaximumPayloadLength = 254;

    /// <summary>Polling codes seen in <c>DO.RATE_*</c>; unknown codes fall back to 100 Hz.</summary>
    public static readonly IReadOnlyList<(byte Code, int Hertz)> PollingRates =
    [
        (0x01, 1_000),
        (0x02, 500),
        (0x03, 333),
        (0x04, 250),
        (0x05, 200),
        (0x08, 125),
        (0x0A, 100),
    ];

    public static byte[] CreateFrame(
        byte transactionId,
        byte commandClass,
        byte commandId,
        byte param6 = 0,
        byte param7 = 0,
        ReadOnlySpan<byte> payload = default)
    {
        if (transactionId > MaximumTransactionId)
        {
            throw new ArgumentOutOfRangeException(nameof(transactionId), "Protocol 30 transaction ids are 0..31.");
        }
        if (payload.Length > MaximumPayloadLength)
        {
            throw new ArgumentOutOfRangeException(nameof(payload), "Protocol 30 payloads above 254 bytes need an explicit packet size.");
        }

        var frame = new byte[RequestHeaderLength + payload.Length];
        frame[0] = transactionId;
        frame[1] = (byte)(payload.Length & 0xFF);
        frame[2] = (byte)((payload.Length >> 8) & 0xFF);
        frame[3] = (byte)((payload.Length >> 16) & 0xFF);
        frame[4] = commandClass;
        frame[5] = commandId;
        frame[6] = param6;
        frame[7] = param7;
        payload.CopyTo(frame.AsSpan(RequestHeaderLength));
        return frame;
    }

    public static RazerBleResponse ParseResponse(ReadOnlySpan<byte> response)
    {
        if (response.Length < RequestHeaderLength)
        {
            throw new InvalidOperationException(
                $"Protocol 30 reply is {response.Length} bytes, shorter than the 8-byte header.");
        }

        var declared = response[1] | (response[2] << 8) | (response[3] << 16);
        var headerLength = -1;
        if (response.Length >= ResponseHeaderLength &&
            declared == response.Length - ResponseHeaderLength)
        {
            headerLength = ResponseHeaderLength;
        }
        else if (declared == response.Length - RequestHeaderLength)
        {
            headerLength = RequestHeaderLength;
        }
        else if (response.Length >= ResponseHeaderLength && declared == 0)
        {
            headerLength = ResponseHeaderLength;
        }

        if (headerLength < 0)
        {
            throw new InvalidOperationException(
                $"Protocol 30 reply declares {declared} payload bytes but carries {response.Length - RequestHeaderLength}.");
        }

        return new RazerBleResponse(
            response[0],
            response[4],
            response[5],
            response[7],
            response[headerLength..].ToArray(),
            headerLength);
    }

    public static byte[] CreateGetDeviceMode(byte transactionId) =>
        CreateFrame(transactionId, 0x01, 0x82);

    public static byte[] CreateSetDeviceMode(byte transactionId, byte mode, byte parameter) =>
        CreateFrame(transactionId, 0x01, 0x02, payload: [mode, parameter]);

    public static byte[] CreateGetSerialNumber(byte transactionId) =>
        CreateFrame(transactionId, 0x01, 0x83);

    public static byte[] CreateGetPollingRate(byte transactionId) =>
        CreateFrame(transactionId, 0x01, 0x84);

    public static byte[] CreateSetPollingRate(byte transactionId, int hertz) =>
        CreateFrame(transactionId, 0x01, 0x04, payload: [PollingCode(hertz)]);

    /// <summary>The base class reports firmware through class 0x00 command 0x81.</summary>
    public static byte[] CreateGetFirmware(byte transactionId) =>
        CreateFrame(transactionId, 0x00, 0x81);

    public static byte[] CreateGetBatteryLevel(byte transactionId, byte batteryId = 0) =>
        CreateFrame(transactionId, 0x05, 0x81, param7: batteryId);

    public static byte[] CreateGetChargingStatus(byte transactionId, byte batteryId = 0) =>
        CreateFrame(transactionId, 0x05, 0x85, param7: batteryId);

    public static byte[] CreateGetDpi(byte transactionId, byte profileId = 0) =>
        CreateFrame(transactionId, 0x0B, 0x81, param6: profileId);

    public static byte[] CreateSetDpi(byte transactionId, int x, int y, byte profileId = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(x, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(x, ushort.MaxValue);
        ArgumentOutOfRangeException.ThrowIfLessThan(y, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(y, ushort.MaxValue);

        var payload = new byte[6];
        BinaryPrimitives.WriteUInt16LittleEndian(payload, (ushort)x);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2), (ushort)y);
        return CreateFrame(transactionId, 0x0B, 0x01, param6: profileId, payload: payload);
    }

    public static byte PollingCode(int hertz) =>
        PollingCodeOrNull(hertz) ?? throw new ArgumentOutOfRangeException(
            nameof(hertz),
            $"Protocol 30 polling rates are {string.Join(", ", PollingRates.Select(item => item.Hertz))} Hz.");

    public static int ReadPollingRate(RazerBleResponse response) =>
        PollingRates.FirstOrDefault(item => item.Code == First(response)).Hertz is var hertz && hertz != 0
            ? hertz
            : 100;

    public static int ReadBatteryPercent(RazerBleResponse response) =>
        (int)Math.Floor(First(response) / 255.0 * 100);

    public static byte ReadChargingStatus(RazerBleResponse response) => First(response);

    public static (byte Mode, byte Parameter) ReadDeviceMode(RazerBleResponse response) =>
        (response.Payload[0], response.Payload[1]);

    public static RazerBleFirmware ReadFirmware(RazerBleResponse response) =>
        new(response.Payload[0], response.Payload[1], response.Payload[2], response.Payload[3]);

    public static (int X, int Y, int Z) ReadDpi(RazerBleResponse response)
    {
        if (response.Payload.Count < 6)
        {
            throw new InvalidOperationException($"Protocol 30 DPI reply carried {response.Payload.Count} bytes, expected 6.");
        }

        var payload = response.Payload.ToArray();
        return (
            BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(0)),
            BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(2)),
            BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(4)));
    }

    public static string ReadSerialNumber(RazerBleResponse response) =>
        new(response.Payload.Where(value => value is >= 32 and < 127).Select(value => (char)value).ToArray());

    private static byte? PollingCodeOrNull(int hertz)
    {
        foreach (var (code, value) in PollingRates)
        {
            if (value == hertz) return code;
        }

        return null;
    }

    private static byte First(RazerBleResponse response)
    {
        if (response.Payload.Count == 0)
        {
            throw new InvalidOperationException("Protocol 30 reply carried no payload.");
        }

        return response.Payload[0];
    }
}
