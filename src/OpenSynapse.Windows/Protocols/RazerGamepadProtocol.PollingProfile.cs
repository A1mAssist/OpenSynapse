namespace OpenSynapse.Windows.Protocols;

public static partial class RazerGamepadProtocol
{
    private const byte ProfileIdListGet = 0x81;
    private const byte ActiveProfileSet = 0x04;
    private const byte PollingCommandClass = 0x00;
    private const byte ProfilePollingGet = 0x8E;
    private const byte ProfilePollingSet = 0x0E;
    private const byte HighSpeedPollingGet = 0xC0;
    private const byte HighSpeedPollingSet = 0x40;

    public static byte[] CreateGetProfileIdListRequest(byte transactionId) =>
        CreateForClass(transactionId, 0x50, ProfileCommandClass, ProfileIdListGet, new byte[0x50]);

    public static IReadOnlyList<byte> ReadProfileIdList(ReadOnlySpan<byte> response)
    {
        var data = ReadData(response);
        var total = data[0];
        if (total > 0x50 || data.Length < total + 1)
        {
            throw new InvalidDataException("The gamepad profile-id reply is truncated.");
        }

        return data.Slice(1, total).ToArray();
    }

    public static byte[] CreateSetActiveProfileIdRequest(byte transactionId, byte profileId) =>
        CreateForClass(transactionId, 1, ProfileCommandClass, ActiveProfileSet, [profileId]);

    public static byte[] CreateGetProfilePollingRateRequest(byte transactionId, byte profileId) =>
        CreateForClass(transactionId, 2, PollingCommandClass, ProfilePollingGet, [profileId, 0x00]);

    public static byte[] CreateSetProfilePollingRateRequest(byte transactionId, byte profileId, byte code) =>
        CreateForClass(transactionId, 2, PollingCommandClass, ProfilePollingSet, [profileId, code]);

    public static byte[] CreateGetHighSpeedPollingRateRequest(byte transactionId, byte profileId) =>
        CreateForClass(transactionId, 2, PollingCommandClass, HighSpeedPollingGet, [profileId, 0x00]);

    public static byte[] CreateSetHighSpeedPollingRateRequest(byte transactionId, byte profileId, byte code) =>
        CreateForClass(transactionId, 2, PollingCommandClass, HighSpeedPollingSet, [profileId, code]);

    public static byte ReadPollingCode(ReadOnlySpan<byte> response, byte profileId)
    {
        var data = ReadData(response);
        if (data.Length < 2 || data[0] != profileId)
        {
            throw new InvalidDataException("The gamepad polling reply did not echo the requested profile.");
        }

        return data[1];
    }

    public static byte EncodeStandardPollingRate(int hertz) => hertz switch
    {
        1_000 => 0x01,
        500 => 0x02,
        333 => 0x03,
        250 => 0x04,
        200 => 0x05,
        125 => 0x08,
        100 => 0x0A,
        _ => throw new ArgumentOutOfRangeException(nameof(hertz)),
    };

    public static int DecodeStandardPollingRate(byte code) => code switch
    {
        0x01 => 1_000,
        0x02 => 500,
        0x03 => 333,
        0x04 => 250,
        0x05 => 200,
        0x08 => 125,
        0x0A => 100,
        _ => throw new InvalidDataException($"Unknown standard polling code 0x{code:X2}."),
    };

    public static byte EncodeHighSpeedPollingRate(int hertz) => hertz switch
    {
        8_000 => 0x01,
        4_000 => 0x02,
        2_000 => 0x04,
        1_000 => 0x08,
        500 => 0x10,
        250 => 0x20,
        125 => 0x40,
        _ => throw new ArgumentOutOfRangeException(nameof(hertz)),
    };

    public static int DecodeHighSpeedPollingRate(byte code) => code switch
    {
        0x01 => 8_000,
        0x02 => 4_000,
        0x04 => 2_000,
        0x08 => 1_000,
        0x10 => 500,
        0x20 => 250,
        0x40 => 125,
        _ => throw new InvalidDataException($"Unknown high-speed polling code 0x{code:X2}."),
    };
}
