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

public enum RazerBleLightingEffect : byte
{
    Off = 0,
    Static = 1,
    Breathing = 2,
    Spectrum = 3,
    Wave = 4,
    Reactive = 5,
    Wheel = 12,
}

public sealed record RazerBleLightingState(
    byte RegionId,
    int BrightnessPercent,
    byte EffectId,
    byte Direction,
    byte Rate,
    IReadOnlyList<byte> Colors);

public sealed record RazerBleButtonAssignment(
    byte ButtonId,
    byte Mode,
    byte FunctionId,
    IReadOnlyList<byte> Data);

public sealed record RazerBleDpiStage(byte Index, int X, int Y, int Z);

public sealed record RazerBleDpiStages(byte ActiveStage, IReadOnlyList<RazerBleDpiStage> Stages);

public sealed record RazerBleScrollState(
    byte? Mode,
    bool? SmartReel,
    byte? SmartReelThreshold,
    bool? Acceleration,
    IReadOnlyList<string> Errors);

public sealed record RazerBlePowerState(
    int? LowBatteryPercent,
    bool? AutoSleep,
    int? SleepMinutes,
    int? TimeToDimSeconds,
    IReadOnlyList<string> Errors);

public sealed record RazerBleProfileState(IReadOnlyList<byte> Ids, byte? ActiveId);

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

    public static byte[] CreateGetDpiStages(byte transactionId, byte profileId = 0) =>
        CreateFrame(transactionId, 0x0B, 0x84, param6: profileId);

    public static RazerBleDpiStages ReadDpiStages(RazerBleResponse response)
    {
        var payload = response.Payload.ToArray();
        if (payload.Length < 2)
        {
            throw new InvalidOperationException(
                $"Protocol 30 DPI stage reply carried {payload.Length} bytes, expected at least 2.");
        }

        var count = Math.Min((int)payload[1], 5);
        var stages = new List<RazerBleDpiStage>(count);
        for (var offset = 2; offset + 7 <= payload.Length && stages.Count < count; offset += 7)
        {
            stages.Add(new RazerBleDpiStage(
                payload[offset],
                BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(offset + 1)),
                BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(offset + 3)),
                BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(offset + 5))));
        }

        if (stages.Count == 0)
        {
            throw new InvalidOperationException("Protocol 30 DPI stage reply contained no stages.");
        }

        return new RazerBleDpiStages(payload[0], stages);
    }

    public static byte[] CreateSetDpiStages(
        byte transactionId,
        byte profileId,
        RazerBleDpiStages state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Stages.Count is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(state), "DPI stages must contain 1-5 entries.");
        }
        if (state.ActiveStage < 1 || state.ActiveStage > state.Stages.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(state), "The active DPI stage is outside the stage list.");
        }

        // The middleware writes a 2-byte header, 7 bytes per stage and one reserved byte.
        var payload = new byte[state.Stages.Count * 7 + 3];
        payload[0] = state.ActiveStage;
        payload[1] = (byte)state.Stages.Count;
        for (var index = 0; index < state.Stages.Count; index++)
        {
            var stage = state.Stages[index];
            if (stage.X is < 0 or > ushort.MaxValue || stage.Y is < 0 or > ushort.MaxValue ||
                stage.Z is < 0 or > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(state), "DPI stage values must fit in 16 bits.");
            }

            var offset = 2 + index * 7;
            payload[offset] = stage.Index;
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(offset + 1), (ushort)stage.X);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(offset + 3), (ushort)stage.Y);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(offset + 5), (ushort)stage.Z);
        }

        return CreateFrame(transactionId, 0x0B, 0x04, param6: profileId, payload: payload);
    }

    public static byte[] CreateGetScrollFreeNotchedMode(byte transactionId, byte classId = 0) =>
        CreateFrame(transactionId, 0x08, 0x85, param6: classId);

    public static byte[] CreateSetScrollFreeNotchedMode(byte transactionId, byte mode, byte classId = 0) =>
        CreateFrame(transactionId, 0x08, 0x05, param6: classId, payload: [mode]);

    // The middleware task runner calls 0x87/0x07 "Smart Reel"; its command description is Auto Switch.
    public static byte[] CreateGetScrollSmartReel(byte transactionId, byte classId = 0) =>
        CreateFrame(transactionId, 0x08, 0x87, param6: classId);

    public static byte[] CreateSetScrollSmartReel(byte transactionId, bool enabled, byte classId = 0) =>
        CreateFrame(transactionId, 0x08, 0x07, param6: classId, payload: [enabled ? (byte)1 : (byte)0]);

    public static byte[] CreateGetScrollSmartReelThreshold(byte transactionId, byte classId = 0) =>
        CreateFrame(transactionId, 0x08, 0x8A, param6: classId);

    public static byte[] CreateSetScrollSmartReelThreshold(byte transactionId, byte threshold, byte classId = 0) =>
        CreateFrame(transactionId, 0x08, 0x0A, param6: classId, payload: [threshold]);

    public static byte[] CreateGetScrollAcceleration(byte transactionId, byte classId = 0) =>
        CreateFrame(transactionId, 0x08, 0x86, param6: classId);

    public static byte[] CreateSetScrollAcceleration(byte transactionId, bool enabled, byte classId = 0) =>
        CreateFrame(transactionId, 0x08, 0x06, param6: classId, payload: [enabled ? (byte)1 : (byte)0]);

    public static byte[] CreateGetBatteryCriticalLevel(byte transactionId, byte batteryId = 0) =>
        CreateFrame(transactionId, 0x05, 0x82, param7: batteryId);

    public static int ReadBatteryCriticalPercent(RazerBleResponse response) =>
        (int)Math.Floor(First(response) / 255.0 * 100);

    public static byte[] CreateSetBatteryCriticalLevel(
        byte transactionId,
        int percent,
        byte batteryId = 0)
    {
        var raw = (byte)Math.Ceiling(Math.Clamp(percent, 0, 100) / 100d * 255);
        return CreateFrame(transactionId, 0x05, 0x02, param7: batteryId, payload: [raw]);
    }

    public static byte[] CreateGetAutoSleep(byte transactionId, byte profileId = 0, byte batteryId = 0) =>
        CreateFrame(transactionId, 0x05, 0x87, param6: profileId, param7: batteryId);

    public static byte[] CreateSetAutoSleep(
        byte transactionId,
        bool enabled,
        byte profileId = 0,
        byte batteryId = 0) =>
        CreateFrame(transactionId, 0x05, 0x07, param6: profileId, param7: batteryId,
            payload: [enabled ? (byte)1 : (byte)0]);

    public static byte[] CreateGetSleepTime(byte transactionId, byte profileId = 0, byte batteryId = 0) =>
        CreateFrame(transactionId, 0x05, 0x84, param6: profileId, param7: batteryId);

    public static int ReadSleepMinutes(RazerBleResponse response) =>
        ReadUInt16(response, 0);

    public static byte[] CreateSetSleepTime(byte transactionId, int minutes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(minutes, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minutes, ushort.MaxValue);
        var payload = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(payload, (ushort)minutes);
        return CreateFrame(transactionId, 0x05, 0x04, payload: payload);
    }

    public static byte[] CreateGetTimeToDim(byte transactionId, byte profileId = 0, byte batteryId = 0) =>
        CreateFrame(transactionId, 0x05, 0x8A, param6: profileId, param7: batteryId);

    public static int ReadTimeToDimSeconds(RazerBleResponse response) =>
        ReadUInt16(response, 1);

    public static bool ReadBoolean(RazerBleResponse response) => First(response) != 0;

    public static byte[] CreateGetProfileIds(byte transactionId) =>
        CreateFrame(transactionId, 0x03, 0x80);

    public static IReadOnlyList<byte> ReadProfileIds(RazerBleResponse response) =>
        response.Payload.Where(id => id != 0).Distinct().ToArray();

    public static byte[] CreateGetActiveProfileId(byte transactionId) =>
        CreateFrame(transactionId, 0x03, 0x82);

    public static byte ReadActiveProfileId(RazerBleResponse response) => First(response);

    public static byte[] CreateSetActiveProfileId(byte transactionId, byte profileId) =>
        CreateFrame(transactionId, 0x03, 0x02, payload: [profileId]);

    public static byte[] CreateGetRegionIds(byte transactionId) =>
        CreateFrame(transactionId, 0x10, 0x80, param7: 0x01);

    public static IReadOnlyList<byte> ReadRegionIds(RazerBleResponse response) =>
        response.Payload.Select(value => (byte)(value & 0x3F)).ToArray();

    public static byte[] CreateGetBrightness(byte transactionId, byte profileId, byte regionId) =>
        CreateFrame(transactionId, 0x10, 0x85, param6: profileId, param7: regionId);

    public static int ReadBrightnessPercent(RazerBleResponse response) =>
        (int)Math.Ceiling(First(response) / 255d * 100);

    public static byte[] CreateSetBrightness(
        byte transactionId,
        byte profileId,
        byte regionId,
        int percent)
    {
        var raw = (byte)Math.Floor(Math.Clamp(percent, 0, 100) / 100d * 255);
        return CreateFrame(transactionId, 0x10, 0x05, param6: profileId, param7: regionId, payload: [raw]);
    }

    public static byte[] CreateGetEffect(byte transactionId, byte profileId, byte regionId) =>
        CreateFrame(transactionId, 0x10, 0x83, param6: profileId, param7: regionId);

    public static RazerBleLightingState ReadEffect(RazerBleResponse response, byte regionId)
    {
        if (response.Payload.Count < 4)
        {
            throw new InvalidOperationException(
                $"Protocol 30 Chroma reply carried {response.Payload.Count} bytes, expected at least 4.");
        }

        var payload = response.Payload;
        var colorCount = Math.Min(payload[3], (payload.Count - 4) / 3);
        var colors = new byte[colorCount * 3];
        for (var index = 0; index < colors.Length; index++)
        {
            colors[index] = payload[4 + index];
        }

        return new RazerBleLightingState(regionId, 0, payload[0], payload[1], payload[2], colors);
    }

    public static byte[] CreateSetEffect(
        byte transactionId,
        byte profileId,
        byte regionId,
        RazerBleLightingEffect effect,
        byte flags,
        byte rate,
        ReadOnlySpan<byte> colors)
    {
        if (colors.Length % 3 != 0 || colors.Length > 6)
        {
            throw new ArgumentOutOfRangeException(nameof(colors), "Chroma colors must be 0-2 RGB triples.");
        }

        var payload = new byte[4 + colors.Length];
        payload[0] = (byte)effect;
        payload[1] = flags;
        payload[2] = rate;
        payload[3] = (byte)(colors.Length / 3);
        colors.CopyTo(payload.AsSpan(4));
        return CreateFrame(transactionId, 0x10, 0x03, param6: profileId, param7: regionId, payload: payload);
    }

    public static byte[] CreateGetButtonList(byte transactionId, byte classId = 0) =>
        CreateFrame(transactionId, 0x08, 0x80, param7: classId);

    public static IReadOnlyList<byte> ReadButtonList(RazerBleResponse response) =>
        response.Payload.ToArray();

    public static byte[] CreateGetButtonAssignment(byte transactionId, byte classId, byte buttonId) =>
        CreateFrame(transactionId, 0x08, 0x84, param6: classId, param7: buttonId);

    public static RazerBleButtonAssignment ReadButtonAssignment(RazerBleResponse response, byte mode)
    {
        var data = response.Payload;
        if (data.Count < 3)
        {
            throw new InvalidOperationException(
                $"Protocol 30 mapping reply carried {data.Count} bytes, expected at least 3.");
        }

        var functionId = mode == 1 && data.Count > 3 ? data[3] : data[2];
        var size = mode switch
        {
            0 when data.Count > 4 => data[4],
            1 when data.Count > 5 => data[5],
            _ => (byte)0,
        };
        var start = mode == 1 ? 7 : 6;
        var values = new List<byte>(Math.Min((int)size, 5));
        for (var index = start; index < data.Count && values.Count < Math.Min((int)size, 5); index += 2)
        {
            values.Add(data[index]);
        }

        return new RazerBleButtonAssignment(data[0], mode, functionId, values);
    }

    public static byte[] CreateSetButtonAssignment(
        byte transactionId,
        byte classId,
        byte buttonId,
        byte mode,
        byte functionId,
        ReadOnlySpan<byte> data)
    {
        if (data.Length > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(data), "Protocol 30 button assignments carry at most 5 data bytes.");
        }

        var payload = new byte[5 + data.Length];
        payload[0] = classId;
        payload[1] = buttonId;
        payload[2] = mode;
        payload[3] = functionId;
        payload[4] = (byte)data.Length;
        data.CopyTo(payload.AsSpan(5));
        return CreateFrame(transactionId, 0x08, 0x04, param6: classId, param7: buttonId, payload: payload);
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

    private static int ReadUInt16(RazerBleResponse response, int offset)
    {
        if (response.Payload.Count < offset + 2)
        {
            throw new InvalidOperationException(
                $"Protocol 30 reply carried {response.Payload.Count} bytes, expected {offset + 2}.");
        }

        var pair = response.Payload.Skip(offset).Take(2).ToArray();
        return BinaryPrimitives.ReadUInt16LittleEndian(pair);
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
