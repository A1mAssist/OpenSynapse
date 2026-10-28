namespace OpenSynapse.Windows.Protocols;

public static class BladeThermalProtocol
{
    public const byte TransactionId = 0x1F;
    public const byte ProfileId = 0x01;
    public const byte CpuFanId = 0x01;
    public const byte GpuFanId = 0x02;

    public static byte[] CreateGetFanIdListRequest() =>
        RazerFeatureReport.CreateRequest(TransactionId, 0x50, 0x0D, 0x80, ReadOnlySpan<byte>.Empty);

    public static byte[] CreateGetCurrentSpeedRequest(byte fanId) =>
        RazerFeatureReport.CreateRequest(TransactionId, 0x03, 0x0D, 0x88, new[] { ProfileId, ValidateFanId(fanId) });

    public static byte[] CreateGetAdvancedFanModeRequest(byte fanId) =>
        RazerFeatureReport.CreateRequest(TransactionId, 0x03, 0x0D, 0x87, new[] { ProfileId, ValidateFanId(fanId) });

    public static byte[] CreateGetFanInformationRequest() =>
        RazerFeatureReport.CreateRequest(TransactionId, 0x0C, 0x0D, 0x83, new byte[0x0C]);

    public static BladeThermalFanInformation ParseFanInformation(ReadOnlySpan<byte> response)
    {
        ValidateResponse(response, CreateGetFanInformationRequest(), expectedArguments: 0x0C);
        var arguments = response[RazerFeatureReport.ArgumentsOffset..];
        if (arguments[0] != 0x00)
        {
            throw new InvalidOperationException(
                $"Blade returned an unexpected thermal class for fan information: 0x{arguments[0]:X2}.");
        }

        return new BladeThermalFanInformation(
            arguments[0],
            ReadBigEndianUInt16(arguments, 1),
            ReadBigEndianUInt16(arguments, 3),
            arguments[5],
            arguments[6],
            arguments[7],
            arguments[8],
            arguments[9],
            arguments[10],
            arguments[11]);
    }

    public static byte[] CreateGetFanDutyCycleRequest(byte classId, byte fanId) =>
        RazerFeatureReport.CreateRequest(
            TransactionId,
            0x03,
            0x0D,
            0x8D,
            new[] { classId, ValidateFanId(fanId) });

    public static byte ParseFanDutyCycle(
        ReadOnlySpan<byte> response,
        byte expectedClassId,
        byte expectedFanId)
    {
        expectedFanId = ValidateFanId(expectedFanId);
        ValidateResponse(
            response,
            CreateGetFanDutyCycleRequest(expectedClassId, expectedFanId),
            expectedArguments: 3);
        var arguments = response[RazerFeatureReport.ArgumentsOffset..];
        if (arguments[0] != expectedClassId || arguments[1] != expectedFanId)
        {
            throw new InvalidOperationException(
                $"Blade returned an incorrect duty-cycle object: 0x{arguments[0]:X2}/0x{arguments[1]:X2}; " +
                $"expected 0x{expectedClassId:X2}/0x{expectedFanId:X2}.");
        }

        return arguments[2];
    }

    public static byte[] CreateGetThermalReadingRequest() =>
        RazerFeatureReport.CreateRequest(TransactionId, 0x50, 0x0D, 0x85, new byte[0x50]);

    public static BladeThermalReading ParseThermalReading(ReadOnlySpan<byte> response)
    {
        ValidateResponse(response, CreateGetThermalReadingRequest(), expectedArguments: 0x50);
        var arguments = response[RazerFeatureReport.ArgumentsOffset..];
        var total = arguments[0];
        if (total > 0x4F)
        {
            throw new InvalidOperationException(
                $"Blade returned an invalid thermal reading count: {total} > 79.");
        }

        return new BladeThermalReading(total, arguments.Slice(1, total).ToArray());
    }

    public static IReadOnlyList<byte> ParseFanIdList(ReadOnlySpan<byte> response)
    {
        ValidateResponse(response, CreateGetFanIdListRequest(), expectedArguments: 0);
        var count = response[RazerFeatureReport.ArgumentsOffset];
        if (count is 0 or > 16 || RazerFeatureReport.ArgumentsOffset + 1 + count > 89)
        {
            throw new InvalidOperationException("Blade returned an invalid fan ID count.");
        }

        return response.Slice(RazerFeatureReport.ArgumentsOffset + 1, count).ToArray();
    }

    public static int ParseCurrentSpeedRpm(ReadOnlySpan<byte> response, byte expectedFanId)
        => ParseCurrentSpeedRpm(response, expectedFanId, CreateGetCurrentSpeedRequest(expectedFanId));

    internal static int ParseCurrentSpeedRpm(
        ReadOnlySpan<byte> response,
        byte expectedFanId,
        ReadOnlySpan<byte> request)
    {
        expectedFanId = ValidateFanId(expectedFanId);
        ValidateResponse(response, request, expectedArguments: 3, allowFramingMismatch: true);
        var arguments = response[RazerFeatureReport.ArgumentsOffset..];
        if (arguments[0] != ProfileId || arguments[1] != expectedFanId)
        {
            throw new InvalidOperationException("Blade returned an incorrect current-fan object.");
        }

        return checked(arguments[2] * 100);
    }

    public static byte ParseAdvancedFanMode(ReadOnlySpan<byte> response, byte expectedFanId)
        => ParseAdvancedFanMode(response, expectedFanId, CreateGetAdvancedFanModeRequest(expectedFanId));

    internal static byte ParseAdvancedFanMode(
        ReadOnlySpan<byte> response,
        byte expectedFanId,
        ReadOnlySpan<byte> request)
    {
        expectedFanId = ValidateFanId(expectedFanId);
        ValidateResponse(response, request, expectedArguments: 3, allowFramingMismatch: true);
        var arguments = response[RazerFeatureReport.ArgumentsOffset..];
        if (arguments[0] != ProfileId || arguments[1] != expectedFanId)
        {
            throw new InvalidOperationException("Blade returned an incorrect advanced-fan object.");
        }

        return arguments[2];
    }

    private static void ValidateResponse(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request,
        byte expectedArguments,
        bool allowFramingMismatch = false)
    {
        if (!RazerFeatureReport.IsSuccessfulResponse(
                request, response, expectedArguments, allowRemainingPacketsMismatch: allowFramingMismatch))
        {
            throw new InvalidOperationException("Blade thermal control returned an invalid or out-of-order feature report.");
        }
    }

    private static byte ValidateFanId(byte fanId) => fanId switch
    {
        CpuFanId or GpuFanId => fanId,
        _ => throw new ArgumentOutOfRangeException(nameof(fanId)),
    };

    private static ushort ReadBigEndianUInt16(ReadOnlySpan<byte> source, int offset) =>
        (ushort)((source[offset] << 8) | source[offset + 1]);
}

/// <summary>
/// Exact rzDevice25 0D:83 decode. The middleware exposes the GPU/VRAM/A..G view
/// and a second fan-curve view over the same bytes; the aliases below preserve
/// that overlap without inventing units. All values are raw protocol values.
/// </summary>
public readonly record struct BladeThermalFanInformation(
    byte ClassId,
    ushort Gpu,
    ushort Vram,
    byte A,
    byte B,
    byte C,
    byte D,
    byte E,
    byte F,
    byte G)
{
    public ushort ThermalSampling => Gpu;

    public byte Hysteresis => (byte)(Vram >> 8);

    public ushort FanUpdate => (ushort)(((Vram & 0xFF) << 8) | A);

    public ushort FanMaxDelta => (ushort)((B << 8) | C);
}

/// <summary>
/// 0D:85 response: a count followed by that many raw temperature-reading bytes.
/// The middleware does not apply a scale or unit, so Values stays raw.
/// </summary>
public readonly record struct BladeThermalReading(
    byte Total,
    IReadOnlyList<byte> Values);
