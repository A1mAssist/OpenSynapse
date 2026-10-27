namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// rzDevice25 0D write frames. The table payload is the middleware sequence
/// [class, fan, curve, (temperature, targetHi, targetLo)*, 0xFF].
/// </summary>
public static class BladeThermalWriteProtocol
{
    public const byte CommandClass = 0x0D;
    public const byte SetFanModeCommandId = 0x02;
    public const byte SetFanTableCommandId = 0x04;
    public const byte SetAdvanceFanModeCommandId = 0x07;
    public const byte SetFanDutyCycleCommandId = 0x0D;
    public const byte GetFanSpeedCommandId = 0x81;
    public const byte GetFanInformationCommandId = 0x83;
    public const byte GetFanDutyCycleCommandId = 0x8D;
    public const int FanTableDataSize = 0x50;

    // No count byte: the table ends with 0xFF. The JS allocates 80 + 3*N
    // bytes, but rzDevice25 sends at most command[0] = 80 payload bytes.
    // 3 header + 3*N + 0xFF => N <= 25.
    public const int MaximumFanTableStages = 25;

    public static byte[] CreateSetFanTableRequest(
        byte classId,
        byte fanId,
        byte thermalCurve,
        IReadOnlyList<BladeThermalFanTableStage> stages)
    {
        ArgumentNullException.ThrowIfNull(stages);
        if (stages.Count is < 1 or > MaximumFanTableStages)
        {
            throw new ArgumentException(
                $"Fan table must contain 1..{MaximumFanTableStages} stages.",
                nameof(stages));
        }

        var arguments = new byte[3 + (stages.Count * 3) + 1];
        arguments[0] = classId;
        arguments[1] = ValidateFanId(fanId);
        arguments[2] = thermalCurve;

        var offset = 3;
        foreach (var stage in stages)
        {
            arguments[offset++] = stage.Temperature;
            arguments[offset++] = (byte)(stage.TargetRaw >> 8);
            arguments[offset++] = (byte)stage.TargetRaw;
        }
        arguments[offset] = 0xFF;

        return RazerFeatureReport.CreateRequest(
            BladeThermalProtocol.TransactionId,
            FanTableDataSize,
            CommandClass,
            SetFanTableCommandId,
            arguments);
    }

    public static byte[] CreateSetFanDutyCycleRequest(byte classId, byte fanId, byte speedPercent)
    {
        if (speedPercent > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(speedPercent), "Fan duty cycle must be 0..100 percent.");
        }

        return RazerFeatureReport.CreateRequest(
            BladeThermalProtocol.TransactionId,
            0x03,
            CommandClass,
            SetFanDutyCycleCommandId,
            new[] { classId, ValidateFanId(fanId), speedPercent });
    }

    public static byte[] CreateSetFanModeRequest(
        byte classId,
        byte fanId,
        byte fanMode,
        byte fanModeValue = 0) =>
        RazerFeatureReport.CreateRequest(
            BladeThermalProtocol.TransactionId,
            0x04,
            CommandClass,
            SetFanModeCommandId,
            new[] { classId, ValidateFanId(fanId), ValidateFanMode(fanMode), fanModeValue });

    public static byte[] CreateSetAdvanceFanModeRequest(byte classId, byte fanId, byte fanMode) =>
        RazerFeatureReport.CreateRequest(
            BladeThermalProtocol.TransactionId,
            0x03,
            CommandClass,
            SetAdvanceFanModeCommandId,
            new[] { classId, ValidateFanId(fanId), ValidateFanMode(fanMode) });

    /// <summary>
    /// Compares one 0D:81, 0D:83, or 0D:8D read-back payload with the expected
    /// arguments. The expected arguments are the exact response bytes, not a
    /// converted rpm or temperature value.
    /// </summary>
    public static bool MatchesReadBack(
        ReadOnlySpan<byte> response,
        byte commandId,
        ReadOnlySpan<byte> expectedArguments)
    {
        ReadOnlySpan<byte> request;
        switch (commandId)
        {
            case GetFanSpeedCommandId when expectedArguments.Length == 3:
                request = RazerFeatureReport.CreateRequest(
                    BladeThermalProtocol.TransactionId,
                    0x03,
                    CommandClass,
                    GetFanSpeedCommandId,
                    new[] { expectedArguments[0], expectedArguments[1], (byte)0x00 });
                break;
            case GetFanInformationCommandId when expectedArguments.Length == 0x0C:
                request = BladeThermalProtocol.CreateGetFanInformationRequest();
                break;
            case GetFanDutyCycleCommandId when expectedArguments.Length == 3:
                request = RazerFeatureReport.CreateRequest(
                    BladeThermalProtocol.TransactionId,
                    0x03,
                    CommandClass,
                    GetFanDutyCycleCommandId,
                    new[] { expectedArguments[0], expectedArguments[1] });
                break;
            default:
                return false;
        }

        if (!RazerFeatureReport.IsSuccessfulResponse(
                request,
                response,
                checked((byte)expectedArguments.Length)))
        {
            return false;
        }

        return response
            .Slice(RazerFeatureReport.ArgumentsOffset, expectedArguments.Length)
            .SequenceEqual(expectedArguments);
    }

    private static byte ValidateFanId(byte fanId) => fanId switch
    {
        BladeThermalProtocol.CpuFanId or BladeThermalProtocol.GpuFanId => fanId,
        _ => throw new ArgumentOutOfRangeException(nameof(fanId), "Blade thermal fan id must be CPU (0x01) or GPU (0x02)."),
    };

    private static byte ValidateFanMode(byte fanMode) => fanMode <= 0x07
        ? fanMode
        : throw new ArgumentOutOfRangeException(nameof(fanMode), "Fan mode must be one of the rzDevice25 enum values 0..7.");
}

/// <summary>
/// One rzDevice25 table row. Temperature is the middleware's one-byte value;
/// target is the un-scaled 16-bit big-endian value (the JS applies no scale).
/// </summary>
public readonly record struct BladeThermalFanTableStage(
    byte Temperature,
    ushort TargetRaw);
