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

    public static IReadOnlyList<byte> ParseFanIdList(ReadOnlySpan<byte> response)
    {
        ValidateResponse(response, CreateGetFanIdListRequest(), expectedArguments: 0);
        var count = response[RazerFeatureReport.ArgumentsOffset];
        if (count is 0 or > 16 || RazerFeatureReport.ArgumentsOffset + 1 + count > 89)
        {
            throw new InvalidOperationException("Blade 返回了无效的风扇 ID 数量。");
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
        ValidateResponse(response, request, expectedArguments: 2);
        var arguments = response[RazerFeatureReport.ArgumentsOffset..];
        if (arguments[0] != ProfileId || arguments[1] != expectedFanId)
        {
            throw new InvalidOperationException("Blade 返回了错误的当前风扇对象。");
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
        ValidateResponse(response, request, expectedArguments: 2);
        var arguments = response[RazerFeatureReport.ArgumentsOffset..];
        if (arguments[0] != ProfileId || arguments[1] != expectedFanId)
        {
            throw new InvalidOperationException("Blade 返回了错误的高级风扇对象。");
        }

        return arguments[2];
    }

    private static void ValidateResponse(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request,
        byte expectedArguments)
    {
        if (!RazerFeatureReport.IsSuccessfulResponse(request, response, expectedArguments))
        {
            throw new InvalidOperationException("Blade 热控返回了无效或错序的 feature report。");
        }
    }

    private static byte ValidateFanId(byte fanId) => fanId switch
    {
        CpuFanId or GpuFanId => fanId,
        _ => throw new ArgumentOutOfRangeException(nameof(fanId)),
    };
}
