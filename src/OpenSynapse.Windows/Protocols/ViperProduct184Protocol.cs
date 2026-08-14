namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Product 184 (Viper V3 HyperSpeed) Protocol 2.5 commands used by the local
/// Synapse product code and already admitted by the capability ledger.
/// </summary>
public static class ViperProduct184Protocol
{
    public const ushort ProductId = 0x00B8;
    public const byte TransactionId = 0x1F;

    public static byte[] CreateGetBatteryRequest() =>
        Create(0x02, 0x07, 0x80);

    public static byte[] CreateGetPollingRateRequest() =>
        Create(0x01, 0x00, 0x85);

    public static byte[] CreateSetPollingRateRequest(int hertz) =>
        Create(0x01, 0x00, 0x05, hertz switch
        {
            125 => 0x08,
            500 => 0x02,
            1000 => 0x01,
            _ => throw new ArgumentOutOfRangeException(nameof(hertz), "Viper 轮询率只支持 125、500 或 1000 Hz。"),
        });

    public static byte[] CreateGetDpiRequest() =>
        Create(0x07, 0x04, 0x85, 0x00);

    public static byte[] CreateSetDpiRequest(int x, int y)
    {
        ValidateDpi(x, nameof(x));
        ValidateDpi(y, nameof(y));
        return Create(0x07, 0x04, 0x05, 0x00, High(x), Low(x), High(y), Low(y));
    }

    public static byte[] CreateGetIdleTimeoutRequest() =>
        Create(0x02, 0x07, 0x83);

    public static byte[] CreateSetIdleTimeoutRequest(int seconds)
    {
        if (seconds is < 60 or > 900)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds), "Viper 休眠时间必须在 60 到 900 秒之间。" );
        }

        return Create(0x02, 0x07, 0x03, High(seconds), Low(seconds));
    }

    public static byte[] CreateGetLowBatteryThresholdRequest() =>
        Create(0x01, 0x07, 0x81);

    public static byte[] CreateSetLowBatteryThresholdRequest(int percent) =>
        Create(0x01, 0x07, 0x01, ViperLowBatteryThresholdProtocol.ToRaw(percent));

    public static byte[] CreateGetDpiStagesRequest() =>
        Create(0x26, 0x04, 0x86, 0x01);

    public static byte[] CreateSetDpiStagesRequest(ViperDpiStagesState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Stages is null || state.Stages.Count is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(state), "Viper DPI 档位数量必须在 1 到 5 之间。");
        }
        if (state.ActiveStage is < 1 || state.ActiveStage > state.Stages.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(state), "Viper 当前 DPI 档位必须在档位表范围内。");
        }

        var arguments = new byte[3 + (7 * state.Stages.Count)];
        arguments[0] = 0x01;
        arguments[1] = state.ActiveStage;
        arguments[2] = checked((byte)state.Stages.Count);
        for (var index = 0; index < state.Stages.Count; index++)
        {
            var stage = state.Stages[index];
            if (stage.Number != index + 1)
            {
                throw new ArgumentException("Viper DPI 档位编号必须从 1 开始且连续。", nameof(state));
            }
            ValidateDpi(stage.X, nameof(state));
            ValidateDpi(stage.Y, nameof(state));
            var offset = 3 + (7 * index);
            arguments[offset] = checked((byte)index);
            arguments[offset + 1] = High(stage.X);
            arguments[offset + 2] = Low(stage.X);
            arguments[offset + 3] = High(stage.Y);
            arguments[offset + 4] = Low(stage.Y);
        }

        return Create(checked((byte)arguments.Length), 0x04, 0x06, arguments);
    }

    public static byte[] CreateSetBatteryChemistryRequest(ViperBatteryChemistry chemistry)
    {
        if (!Enum.IsDefined(chemistry))
        {
            throw new ArgumentOutOfRangeException(nameof(chemistry));
        }

        return Create(0x01, 0x07, 0x14, (byte)chemistry);
    }

    public static int ParseBatteryPercent(ReadOnlySpan<byte> response) =>
        ParseBatteryPercent(response, CreateGetBatteryRequest());

    internal static int ParseBatteryPercent(ReadOnlySpan<byte> response, ReadOnlySpan<byte> request)
    {
        ValidateResponse(response, request, 2);
        return (int)Math.Round(response[RazerFeatureReport.ArgumentsOffset + 1] * 100d / 255d,
            MidpointRounding.AwayFromZero);
    }

    public static int ParsePollingRateHertz(ReadOnlySpan<byte> response) =>
        ParsePollingRateHertz(response, CreateGetPollingRateRequest());

    internal static int ParsePollingRateHertz(ReadOnlySpan<byte> response, ReadOnlySpan<byte> request)
    {
        ValidateResponse(response, request, 1);
        return response[RazerFeatureReport.ArgumentsOffset] switch
        {
            0x01 => 1000,
            0x02 => 500,
            0x08 => 125,
            var raw => throw new InvalidOperationException($"Viper 返回了未知轮询率代码 0x{raw:X2}。"),
        };
    }

    public static (int X, int Y) ParseDpi(ReadOnlySpan<byte> response) =>
        ParseDpi(response, CreateGetDpiRequest());

    internal static (int X, int Y) ParseDpi(ReadOnlySpan<byte> response, ReadOnlySpan<byte> request)
    {
        ValidateResponse(response, request, 5);
        if (response[RazerFeatureReport.ArgumentsOffset] != 0x00)
        {
            throw new InvalidOperationException("Viper DPI 返回了错误的 profile。");
        }

        var x = (response[RazerFeatureReport.ArgumentsOffset + 1] << 8) |
            response[RazerFeatureReport.ArgumentsOffset + 2];
        var y = (response[RazerFeatureReport.ArgumentsOffset + 3] << 8) |
            response[RazerFeatureReport.ArgumentsOffset + 4];
        if (!IsValidDpi(x) || !IsValidDpi(y))
        {
            throw new InvalidOperationException($"Viper 返回了无效 DPI：{x} x {y}。");
        }
        return (x, y);
    }

    public static int ParseIdleSeconds(ReadOnlySpan<byte> response) =>
        ParseIdleSeconds(response, CreateGetIdleTimeoutRequest());

    internal static int ParseIdleSeconds(ReadOnlySpan<byte> response, ReadOnlySpan<byte> request)
    {
        ValidateResponse(response, request, 2);
        var seconds = (response[RazerFeatureReport.ArgumentsOffset] << 8) |
            response[RazerFeatureReport.ArgumentsOffset + 1];
        if (seconds is < 60 or > 900 || seconds % 60 != 0)
        {
            throw new InvalidOperationException($"Viper 返回了无效休眠时间 {seconds} 秒。");
        }

        return seconds;
    }

    private static byte[] Create(byte dataSize, byte commandClass, byte commandId, params byte[] arguments) =>
        RazerFeatureReport.CreateRequest(TransactionId, dataSize, commandClass, commandId, arguments);

    private static byte High(int value) => checked((byte)(value >> 8));

    private static byte Low(int value) => (byte)(value & 0xFF);

    private static void ValidateDpi(int value, string parameterName)
    {
        if (value is < 100 or > 30000 || value % 50 != 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Viper DPI 必须在 100 到 30000 之间，并且是 50 的倍数。");
        }
    }

    private static bool IsValidDpi(int value) => value is >= 100 and <= 30000 && value % 50 == 0;

    private static void ValidateResponse(
        ReadOnlySpan<byte> response,
        ReadOnlySpan<byte> request,
        byte minimumArguments)
    {
        if (!RazerFeatureReport.IsSuccessfulResponse(request, response, minimumArguments))
        {
            throw new InvalidOperationException("Viper Product 184 返回了无效或错序的 feature report。");
        }
    }
}
