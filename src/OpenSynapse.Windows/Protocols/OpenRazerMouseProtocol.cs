using OpenSynapse.Windows.Devices;

namespace OpenSynapse.Windows.Protocols;

public sealed record OpenRazerDpiStage(byte Number, int X, int Y);

public sealed record OpenRazerDpiStages(byte ActiveStage, IReadOnlyList<OpenRazerDpiStage> Stages);

internal static class OpenRazerMouseProtocol
{
    internal static OpenRazerRequest GetBattery(OpenRazerDeviceDefinition device) =>
        OpenRazerStandardProtocol.Create(device, "razer_chroma_misc_get_battery_level", 0x02, 0x07, 0x80, []);

    internal static OpenRazerRequest GetCharging(OpenRazerDeviceDefinition device) =>
        OpenRazerStandardProtocol.Create(device, "razer_chroma_misc_get_charging_status", 0x02, 0x07, 0x84, []);

    internal static OpenRazerRequest GetPollingRate(OpenRazerDeviceDefinition device)
    {
        if (device.Transactions.ContainsKey("razer_chroma_misc_get_polling_rate2"))
        {
            return OpenRazerStandardProtocol.Create(device,
                "razer_chroma_misc_get_polling_rate2", 0x01, 0x00, 0xC0, []);
        }
        return OpenRazerStandardProtocol.Create(device,
            "razer_chroma_misc_get_polling_rate", 0x01, 0x00, 0x85, []);
    }

    internal static IReadOnlyList<OpenRazerRequest> SetPollingRate(
        OpenRazerDeviceDefinition device,
        int hertz)
    {
        if (device.PollingRates.Count > 0 && !device.PollingRates.Contains(hertz))
        {
            throw new ArgumentOutOfRangeException(nameof(hertz), "Polling rate is not declared for this device.");
        }
        if (device.CommandSequences.TryGetValue("razer_chroma_misc_set_polling_rate2", out var sequence))
        {
            var rateCode = EncodeHighPollingRate(hertz);
            return sequence.Select(step => new OpenRazerRequest(
                "razer_chroma_misc_set_polling_rate2",
                RazerFeatureReport.CreateRequest(step.TransactionId, 0x02, 0x00, 0x40,
                    [step.FixedArgument0, rateCode]))).ToArray();
        }
        if (device.Transactions.ContainsKey("razer_chroma_misc_set_polling_rate2"))
        {
            return [OpenRazerStandardProtocol.Create(device,
                "razer_chroma_misc_set_polling_rate2", 0x02, 0x00, 0x40,
                [0x00, EncodeHighPollingRate(hertz)])];
        }
        return [OpenRazerStandardProtocol.Create(device,
            "razer_chroma_misc_set_polling_rate", 0x01, 0x00, 0x05,
            [EncodePollingRate(hertz)])];
    }

    internal static OpenRazerRequest GetDpi(OpenRazerDeviceDefinition device)
    {
        if (device.Transactions.ContainsKey("razer_chroma_misc_get_dpi_xy_byte"))
        {
            return OpenRazerStandardProtocol.Create(device,
                "razer_chroma_misc_get_dpi_xy_byte", 0x03, 0x04, 0x81, []);
        }
        var storage = device.GetRequiredFixedArgument("razer_chroma_misc_get_dpi_xy", 0);
        return OpenRazerStandardProtocol.Create(device,
            "razer_chroma_misc_get_dpi_xy", 0x07, 0x04, 0x85, [storage]);
    }

    internal static OpenRazerRequest SetDpi(
        OpenRazerDeviceDefinition device,
        int x,
        int y)
    {
        if (device.Transactions.ContainsKey("razer_chroma_misc_set_dpi_xy_byte"))
        {
            ValidateDpi(device, x, nameof(x));
            ValidateDpi(device, y, nameof(y));
            return OpenRazerStandardProtocol.Create(device,
                "razer_chroma_misc_set_dpi_xy_byte", 0x03, 0x04, 0x01,
                [EncodeLegacyDpi(x), EncodeLegacyDpi(y), 0x00]);
        }

        ValidateDpi(device, x, nameof(x));
        ValidateDpi(device, y, nameof(y));
        return OpenRazerStandardProtocol.Create(device,
            "razer_chroma_misc_set_dpi_xy", 0x07, 0x04, 0x05,
            [OpenRazerStandardProtocol.VariableStore, High(x), Low(x), High(y), Low(y), 0x00, 0x00]);
    }

    internal static OpenRazerRequest GetDpiStages(
        OpenRazerDeviceDefinition device,
        byte storage = OpenRazerStandardProtocol.VariableStore) =>
        OpenRazerStandardProtocol.Create(device,
            "razer_chroma_misc_get_dpi_stages", 0x26, 0x04, 0x86, [storage]);

    internal static OpenRazerRequest SetDpiStages(
        OpenRazerDeviceDefinition device,
        OpenRazerDpiStages state,
        byte storage = OpenRazerStandardProtocol.VariableStore)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Stages is null || state.Stages.Count is < 1 or > 5 ||
            state.ActiveStage is < 1 || state.ActiveStage > state.Stages.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }

        var arguments = new byte[3 + (state.Stages.Count * 7)];
        arguments[0] = storage;
        arguments[1] = state.ActiveStage;
        arguments[2] = checked((byte)state.Stages.Count);
        for (var index = 0; index < state.Stages.Count; index++)
        {
            var stage = state.Stages[index];
            if (stage.Number != index + 1)
            {
                throw new ArgumentException("DPI stage numbers must be contiguous and one-based.", nameof(state));
            }
            ValidateDpi(device, stage.X, nameof(state));
            ValidateDpi(device, stage.Y, nameof(state));
            var offset = 3 + (index * 7);
            arguments[offset] = checked((byte)index);
            arguments[offset + 1] = High(stage.X);
            arguments[offset + 2] = Low(stage.X);
            arguments[offset + 3] = High(stage.Y);
            arguments[offset + 4] = Low(stage.Y);
        }
        return OpenRazerStandardProtocol.Create(device,
            "razer_chroma_misc_set_dpi_stages", 0x26, 0x04, 0x06, arguments);
    }

    internal static OpenRazerDpiStages ParseDpiStages(
        OpenRazerDeviceDefinition device,
        ReadOnlySpan<byte> response)
    {
        var arguments = OpenRazerStandardProtocol.Arguments(response, 10);
        var activeStage = arguments[1];
        var count = arguments[2];
        if (count is < 1 or > 5 || activeStage is < 1 || activeStage > count ||
            arguments.Length < 3 + (count * 7))
        {
            throw new InvalidDataException("Device returned an invalid DPI stage header.");
        }
        var rawBase = arguments[3];
        if (rawBase is not (0 or 1))
        {
            throw new InvalidDataException("Device returned an unknown DPI stage number base.");
        }

        var stages = new OpenRazerDpiStage[count];
        for (var index = 0; index < count; index++)
        {
            var offset = 3 + (index * 7);
            if (arguments[offset] != rawBase + index || arguments[offset + 5] != 0 || arguments[offset + 6] != 0)
            {
                throw new InvalidDataException("Device returned a malformed DPI stage entry.");
            }
            var x = (arguments[offset + 1] << 8) | arguments[offset + 2];
            var y = (arguments[offset + 3] << 8) | arguments[offset + 4];
            ValidateDpi(device, x, nameof(response));
            ValidateDpi(device, y, nameof(response));
            stages[index] = new OpenRazerDpiStage(checked((byte)(index + 1)), x, y);
        }
        return new OpenRazerDpiStages(activeStage, stages);
    }

    internal static OpenRazerRequest GetIdleTime(OpenRazerDeviceDefinition device) =>
        OpenRazerStandardProtocol.Create(device, "razer_chroma_misc_get_idle_time", 0x02, 0x07, 0x83, []);

    internal static OpenRazerRequest SetIdleTime(OpenRazerDeviceDefinition device, int seconds)
    {
        if (seconds is < 60 or > 900)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds));
        }
        return OpenRazerStandardProtocol.Create(device,
            "razer_chroma_misc_set_idle_time", 0x02, 0x07, 0x03, [High(seconds), Low(seconds)]);
    }

    internal static OpenRazerRequest GetLowBatteryThreshold(OpenRazerDeviceDefinition device) =>
        OpenRazerStandardProtocol.Create(device,
            "razer_chroma_misc_get_low_battery_threshold", 0x01, 0x07, 0x81, []);

    internal static OpenRazerRequest SetLowBatteryThreshold(OpenRazerDeviceDefinition device, int percent)
    {
        if (percent is < 5 or > 25 || percent % 5 != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(percent), "Threshold must be 5%..25% in 5% steps.");
        }
        var raw = checked((byte)(percent * 255 / 100));
        return OpenRazerStandardProtocol.Create(device,
            "razer_chroma_misc_set_low_battery_threshold", 0x01, 0x07, 0x01, [raw]);
    }

    internal static OpenRazerRequest GetScrollMode(OpenRazerDeviceDefinition device) =>
        OpenRazerStandardProtocol.Create(device,
            "razer_chroma_misc_get_scroll_mode", 0x02, 0x02, 0x94, [OpenRazerStandardProtocol.VariableStore]);

    internal static OpenRazerRequest SetScrollMode(OpenRazerDeviceDefinition device, byte mode) =>
        OpenRazerStandardProtocol.Create(device,
            "razer_chroma_misc_set_scroll_mode", 0x02, 0x02, 0x14,
            [OpenRazerStandardProtocol.VariableStore, mode]);

    internal static OpenRazerRequest GetScrollAcceleration(OpenRazerDeviceDefinition device) =>
        OpenRazerStandardProtocol.Create(device,
            "razer_chroma_misc_get_scroll_acceleration", 0x02, 0x02, 0x96,
            [OpenRazerStandardProtocol.VariableStore]);

    internal static OpenRazerRequest SetScrollAcceleration(OpenRazerDeviceDefinition device, bool enabled) =>
        OpenRazerStandardProtocol.Create(device,
            "razer_chroma_misc_set_scroll_acceleration", 0x02, 0x02, 0x16,
            [OpenRazerStandardProtocol.VariableStore, enabled ? (byte)1 : (byte)0]);

    internal static OpenRazerRequest GetSmartReel(OpenRazerDeviceDefinition device) =>
        OpenRazerStandardProtocol.Create(device,
            "razer_chroma_misc_get_scroll_smart_reel", 0x02, 0x02, 0x97,
            [OpenRazerStandardProtocol.VariableStore]);

    internal static OpenRazerRequest SetSmartReel(OpenRazerDeviceDefinition device, bool enabled) =>
        OpenRazerStandardProtocol.Create(device,
            "razer_chroma_misc_set_scroll_smart_reel", 0x02, 0x02, 0x17,
            [OpenRazerStandardProtocol.VariableStore, enabled ? (byte)1 : (byte)0]);

    internal static int ParseBatteryPercent(ReadOnlySpan<byte> response)
    {
        var arguments = OpenRazerStandardProtocol.Arguments(response, 2);
        return (int)Math.Round(arguments[1] * 100d / 255d, MidpointRounding.AwayFromZero);
    }

    internal static bool ParseCharging(ReadOnlySpan<byte> response) =>
        OpenRazerStandardProtocol.Arguments(response, 2)[1] != 0;

    internal static int ParsePollingRate(ReadOnlySpan<byte> response, bool highRate)
    {
        var raw = OpenRazerStandardProtocol.Arguments(response, highRate ? (byte)2 : (byte)1)[highRate ? 1 : 0];
        return highRate ? raw switch
        {
            0x01 => 8000, 0x02 => 4000, 0x04 => 2000, 0x08 => 1000,
            0x10 => 500, 0x20 => 250, 0x40 => 125,
            _ => throw new InvalidDataException($"Unknown high polling-rate code 0x{raw:X2}."),
        } : raw switch
        {
            0x01 => 1000, 0x02 => 500, 0x08 => 125,
            _ => throw new InvalidDataException($"Unknown polling-rate code 0x{raw:X2}."),
        };
    }

    internal static (int X, int Y) ParseDpi(ReadOnlySpan<byte> response, bool byteEncoding)
    {
        var arguments = OpenRazerStandardProtocol.Arguments(response, byteEncoding ? (byte)2 : (byte)5);
        return byteEncoding
            ? (DecodeLegacyDpi(arguments[0]), DecodeLegacyDpi(arguments[1]))
            : ((arguments[1] << 8) | arguments[2], (arguments[3] << 8) | arguments[4]);
    }

    internal static int ParseIdleSeconds(ReadOnlySpan<byte> response)
    {
        var arguments = OpenRazerStandardProtocol.Arguments(response, 2);
        return (arguments[0] << 8) | arguments[1];
    }

    internal static int ParseLowBatteryThreshold(ReadOnlySpan<byte> response) =>
        (int)Math.Round(OpenRazerStandardProtocol.Arguments(response, 1)[0] * 100d / 255d,
            MidpointRounding.AwayFromZero);

    internal static bool ParseBooleanSecondArgument(ReadOnlySpan<byte> response) =>
        OpenRazerStandardProtocol.Arguments(response, 2)[1] != 0;

    internal static byte ParseSecondArgument(ReadOnlySpan<byte> response) =>
        OpenRazerStandardProtocol.Arguments(response, 2)[1];

    private static byte EncodePollingRate(int hertz) => hertz switch
    {
        1000 => 0x01,
        500 => 0x02,
        125 => 0x08,
        _ => throw new ArgumentOutOfRangeException(nameof(hertz)),
    };

    private static byte EncodeLegacyDpi(int dpi) =>
        checked((byte)(int)Math.Round(dpi / 6750d * 255d, 2));

    private static int DecodeLegacyDpi(byte value) =>
        (int)Math.Round(value / 255d * 6750d, 2);

    private static byte EncodeHighPollingRate(int hertz) => hertz switch
    {
        8000 => 0x01, 4000 => 0x02, 2000 => 0x04, 1000 => 0x08,
        500 => 0x10, 250 => 0x20, 125 => 0x40,
        _ => throw new ArgumentOutOfRangeException(nameof(hertz)),
    };

    private static void ValidateDpi(OpenRazerDeviceDefinition device, int value, string parameterName)
    {
        var maximum = device.MaximumDpi ?? 45_000;
        if (value is < 100 || value > maximum)
        {
            throw new ArgumentOutOfRangeException(parameterName, $"DPI must be between 100 and {maximum}.");
        }
    }

    private static byte High(int value) => checked((byte)(value >> 8));
    private static byte Low(int value) => checked((byte)(value & 0xFF));
}
