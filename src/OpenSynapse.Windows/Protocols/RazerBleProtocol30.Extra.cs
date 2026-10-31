namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Protocol 30 settings outside the core mouse surface: Snap Tap, debounce, battery chemistry and
/// smart dimming. Frames follow the middleware rzDevice30 descriptors.
/// </summary>
/// <summary>One Snap Tap pair: two key ids and the tap mode the middleware stores with them.</summary>
public readonly record struct RazerBleSnapTapPair(byte KeyId1, byte KeyId2, byte Mode);

public sealed record RazerBleSnapTapState(
    byte ProfileId,
    bool Enabled,
    IReadOnlyList<RazerBleSnapTapPair> Pairs);

public readonly record struct RazerBleDebounceState(
    byte Make,
    byte MakeMask,
    byte Break,
    byte BreakMask);

public readonly record struct RazerBleSmartIlluminationState(byte Status, byte Mode);

public enum RazerBleBatteryChemistry : byte
{
    Alkaline = 0,
    NiMH = 1,
    Lithium = 2,
}

public enum RazerBleScrollTactileMode : byte
{
    Tactile = 0,
    FreeSpin = 1,
    MicroTactile = 2,
}

public enum RazerBlePowerPlan : byte
{
    PowerSave = 0,
    Balance = 1,
    HighPerformance = 2,
}

public static partial class RazerBleProtocol30
{
    public static byte[] CreateGetSnapTap(byte transactionId, byte profileId = 0) =>
        CreateFrame(transactionId, 0x07, 0x8A, param6: profileId);

    public static RazerBleSnapTapState ReadSnapTap(RazerBleResponse response)
    {
        var data = response.Payload;
        if (data.Count < 3)
        {
            throw new InvalidOperationException(
                $"Protocol 30 Snap Tap reply carried {data.Count} bytes, expected at least 3.");
        }

        var count = data[1];
        if (data.Count < 3 + count * 3)
        {
            throw new InvalidOperationException(
                $"Protocol 30 Snap Tap reply declared {count} pairs but carried {data.Count} bytes.");
        }

        var pairs = new List<RazerBleSnapTapPair>(count);
        for (var index = 0; index < count; index++)
        {
            var offset = 3 + index * 3;
            pairs.Add(new RazerBleSnapTapPair(data[offset], data[offset + 1], data[offset + 2]));
        }

        return new RazerBleSnapTapState(data[0], data[2] != 0, pairs);
    }

    public static byte[] CreateSetSnapTap(
        byte transactionId,
        byte profileId,
        bool enabled,
        IReadOnlyList<RazerBleSnapTapPair> pairs)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        if (pairs.Count > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(pairs), "Protocol 30 Snap Tap accepts at most four pairs.");
        }

        var payload = new byte[16];
        payload[0] = checked((byte)pairs.Count);
        payload[1] = enabled ? (byte)1 : (byte)0;
        for (var index = 0; index < pairs.Count; index++)
        {
            payload[2 + index * 3] = pairs[index].KeyId1;
            payload[3 + index * 3] = pairs[index].KeyId2;
            payload[4 + index * 3] = pairs[index].Mode;
        }

        return CreateFrame(transactionId, 0x07, 0x0A, param6: profileId, payload: payload);
    }

    public static byte[] CreateGetDebounce(byte transactionId) =>
        CreateFrame(transactionId, 0x07, 0x82);

    public static RazerBleDebounceState ReadDebounce(RazerBleResponse response)
    {
        var data = response.Payload;
        if (data.Count < 4)
        {
            throw new InvalidOperationException(
                $"Protocol 30 debounce reply carried {data.Count} bytes, expected 4.");
        }

        return new RazerBleDebounceState(data[0], data[1], data[2], data[3]);
    }

    public static byte[] CreateSetDebounce(byte transactionId, RazerBleDebounceState state) =>
        CreateFrame(transactionId, 0x07, 0x02,
            payload: [state.Make, state.MakeMask, state.Break, state.BreakMask]);

    public static byte[] CreateGetBatteryDischargeCurve(byte transactionId) =>
        CreateFrame(transactionId, 0x05, 0x8F);

    public static byte ReadBatteryDischargeCurve(RazerBleResponse response) => First(response);

    public static byte[] CreateSetBatteryDischargeCurve(byte transactionId, byte chemistry) =>
        CreateFrame(transactionId, 0x05, 0x0F, payload: [chemistry]);

    public static byte[] CreateGetSmartIllumination(byte transactionId, byte profileId = 0) =>
        CreateFrame(transactionId, 0x05, 0x8D, param6: profileId);

    public static RazerBleSmartIlluminationState ReadSmartIllumination(RazerBleResponse response)
    {
        var data = response.Payload;
        if (data.Count < 2)
        {
            throw new InvalidOperationException(
                $"Protocol 30 smart illumination reply carried {data.Count} bytes, expected 2.");
        }

        return new RazerBleSmartIlluminationState(data[0], data[1]);
    }

    public static byte[] CreateSetSmartIllumination(
        byte transactionId,
        byte profileId,
        byte status,
        byte mode) =>
        CreateFrame(transactionId, 0x05, 0x0D, param6: profileId, payload: [status, mode]);

    public static byte[] CreateGetScrollMaskingTime(byte transactionId, byte classId = 0) =>
        CreateFrame(transactionId, 0x08, 0x88, param6: classId);

    public static byte[] CreateSetScrollMaskingTime(byte transactionId, byte milliseconds, byte classId = 0) =>
        CreateFrame(transactionId, 0x08, 0x08, param6: classId, payload: [milliseconds]);

    public static byte[] CreateGetScrollAccelerationLevel(byte transactionId, byte classId = 0) =>
        CreateFrame(transactionId, 0x08, 0x89, param6: classId);

    public static byte[] CreateSetScrollAccelerationLevel(byte transactionId, byte level, byte classId = 0) =>
        CreateFrame(transactionId, 0x08, 0x09, param6: classId, payload: [level]);

    public static byte[] CreateGetScrollTactileMode(byte transactionId, byte classId = 0) =>
        CreateFrame(transactionId, 0x08, 0xB0, param6: classId);

    public static byte[] CreateSetScrollTactileMode(byte transactionId, RazerBleScrollTactileMode mode, byte classId = 0) =>
        CreateFrame(transactionId, 0x08, 0x30, param6: classId, payload: [(byte)mode]);

    public static byte[] CreateGetPowerPlan(byte transactionId, byte profileId = 0) =>
        CreateFrame(transactionId, 0x05, 0x8B, param6: profileId);

    public static byte[] CreateSetPowerPlan(byte transactionId, RazerBlePowerPlan plan, byte profileId = 0) =>
        CreateFrame(transactionId, 0x05, 0x0B, param6: profileId, payload: [(byte)plan]);

    public static byte ReadSingleByte(RazerBleResponse response) => First(response);
}
