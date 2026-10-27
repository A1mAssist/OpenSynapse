namespace OpenSynapse.Windows.Protocols;

/// <summary>Scroll wheel mode values from the middleware enum: NotchedMode 0, FreeSpinMode 1.</summary>
public enum RazerScrollFreeNotchedMode : byte
{
    NotchedMode = 0,
    FreeSpinMode = 1,
}

/// <summary>Free/notched response: class id at argument 0, mode at argument 1.</summary>
public readonly record struct RazerScrollFreeNotchedModeState(
    byte ClassId,
    RazerScrollFreeNotchedMode Mode);

/// <summary>Acceleration response: class id at argument 0, level at argument 1.</summary>
public readonly record struct RazerScrollVirtualAccelerationLevelState(
    byte ClassId,
    byte Level);

/// <summary>
/// Protocol 25 class 0x02 scroll wheel commands from the 00CC Basilisk V3 Pro 35K
/// middleware bundle: get/set free-notched [2,2,148]/[2,2,20] and get/set virtual
/// acceleration level [2,2,162]/[2,2,34]. Smart Reel threshold already lives in
/// <see cref="OpenRazerMouseProtocol"/> and is deliberately not duplicated here.
/// </summary>
public static class RazerScrollWheelProtocol
{
    private const byte CommandClass = 0x02;
    private const byte DataSize = 0x02;
    private const byte GetFreeNotchedModeCommandId = 0x94;
    private const byte SetFreeNotchedModeCommandId = 0x14;
    private const byte GetVirtualAccelerationLevelCommandId = 0xA2;
    private const byte SetVirtualAccelerationLevelCommandId = 0x22;

    public static byte[] CreateGetScrollFreeNotchedModeRequest(
        byte transactionId,
        byte classId = 0) =>
        CreateRequest(transactionId, GetFreeNotchedModeCommandId, classId, 0);

    public static byte[] CreateSetScrollFreeNotchedModeRequest(
        byte transactionId,
        RazerScrollFreeNotchedMode mode,
        byte classId = 0) =>
        CreateRequest(transactionId, SetFreeNotchedModeCommandId, classId, (byte)mode);

    public static byte[] CreateGetScrollVirtualAccelerationLevelRequest(
        byte transactionId,
        byte classId = 0) =>
        CreateRequest(transactionId, GetVirtualAccelerationLevelCommandId, classId, 0);

    public static byte[] CreateSetScrollVirtualAccelerationLevelRequest(
        byte transactionId,
        byte level,
        byte classId = 0) =>
        CreateRequest(transactionId, SetVirtualAccelerationLevelCommandId, classId, level);

    public static RazerScrollFreeNotchedModeState ParseScrollFreeNotchedMode(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request)
    {
        var arguments = Arguments(response, request, "Scroll free/notched mode");
        var mode = arguments[1] switch
        {
            (byte)RazerScrollFreeNotchedMode.NotchedMode => RazerScrollFreeNotchedMode.NotchedMode,
            (byte)RazerScrollFreeNotchedMode.FreeSpinMode => RazerScrollFreeNotchedMode.FreeSpinMode,
            _ => throw new InvalidOperationException(
                $"Scroll wheel returned unknown free/notched mode 0x{arguments[1]:X2}."),
        };

        return new RazerScrollFreeNotchedModeState(arguments[0], mode);
    }

    public static RazerScrollVirtualAccelerationLevelState ParseScrollVirtualAccelerationLevel(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request)
    {
        var arguments = Arguments(response, request, "Scroll virtual acceleration level");
        // The middleware only coerces the level to Number and writes one Uint8Array byte.
        return new RazerScrollVirtualAccelerationLevelState(arguments[0], arguments[1]);
    }

    private static byte[] CreateRequest(
        byte transactionId,
        byte commandId,
        byte classId,
        byte value) =>
        RazerFeatureReport.CreateRequest(
            transactionId,
            DataSize,
            CommandClass,
            commandId,
            [classId, value]);

    private static ReadOnlySpan<byte> Arguments(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request,
        string feature)
    {
        if (!RazerFeatureReport.IsSuccessfulResponse(request, response, DataSize))
        {
            throw new InvalidOperationException(
                $"{feature} returned an invalid or out-of-order feature report.");
        }

        return response[RazerFeatureReport.ArgumentsOffset..];
    }
}
