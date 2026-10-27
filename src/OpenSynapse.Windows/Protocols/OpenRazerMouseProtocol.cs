using OpenSynapse.Windows.Devices;

namespace OpenSynapse.Windows.Protocols;

public sealed record OpenRazerDpiStage(byte Number, int X, int Y, int Z = 0);

public sealed record OpenRazerDpiStages(byte ActiveStage, IReadOnlyList<OpenRazerDpiStage> Stages);

/// <summary>Profile key debounce timings as the middleware reports them.</summary>
public sealed record OpenRazerProfileDebounce(
    byte ProfileId,
    byte MakeDebounceTimeMs,
    byte MakeMaskTimeMs,
    byte BreakDebounceTimeMs,
    byte BreakMaskTimeMs);

/// <summary>Time-to-dim state as the middleware reports it.</summary>
public sealed record OpenRazerTimeToDim(bool Enabled, int Seconds, byte Percentage);

/// <summary>GPU card detection response: led class id, enabled flag and firmware status.</summary>
public sealed record OpenRazerGpuCardDetection(byte LedClassId, bool Enabled, byte Status);

/// <summary>Chroma tap configuration; the enum fields are derived from these bytes.</summary>
public sealed record OpenRazerChromaTap(byte ProfileId, byte Status, byte TapMode, byte FunctionId);

/// <summary>LK scroll wheel enabled-mode bitmask, bit 1 Tactile, bit 2 FreeSpin, bit 4 MicroTactile.</summary>
public sealed record OpenRazerLkScrollWheelMode(byte ProfileId, byte ModeMask);

/// <summary>Onboard macro storage summary as the middleware reports it.</summary>
public sealed record OpenRazerMacroStorageInfo(
    int MaxId,
    int MaxMemoryLow,
    int MaxMemoryHigh,
    int AvailableMemoryLow,
    int AvailableMemoryHigh,
    int RecyclableMemoryLow,
    int RecyclableMemoryHigh);

internal static class OpenRazerMouseProtocol
{
    // The middleware declares the profile DPI class data frame as 80 bytes and
    // overrides it per call with 7*stageCount+3.
    internal const byte MiddlewareDpiStageFrameSize = 0x50;

    internal static OpenRazerRequest GetBattery(OpenRazerDeviceDefinition device) =>
        OpenRazerStandardProtocol.Create(device, "razer_chroma_misc_get_battery_level", 0x02, 0x07, 0x80, []);

    internal static OpenRazerRequest GetCharging(OpenRazerDeviceDefinition device) =>
        OpenRazerStandardProtocol.Create(device, "razer_chroma_misc_get_charging_status", 0x02, 0x07, 0x84, []);

    internal static OpenRazerRequest GetPollingRate(OpenRazerDeviceDefinition device)
    {
        if (device.Transactions.ContainsKey("razer_chroma_misc_get_polling_rate2"))
        {
            var profile = device.GetFixedArgumentOrNull("razer_chroma_misc_get_polling_rate2", 0);
            var arguments = profile is byte value ? new[] { value } : [];
            return OpenRazerStandardProtocol.Create(device,
                "razer_chroma_misc_get_polling_rate2",
                // The middleware declares the USB high speed polling read as two
                // bytes in every product bundle that ships it.
                device.GetCommandSize("razer_chroma_misc_get_polling_rate2", 0x02),
                0x00, 0xC0, arguments);
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
        if (device.Transactions.ContainsKey("razer_chroma_misc_set_polling_rate2") &&
            device.GetFixedArgumentOrNull("razer_chroma_misc_set_polling_rate2", 0) is byte profile)
        {
            return [OpenRazerStandardProtocol.Create(device,
                "razer_chroma_misc_set_polling_rate2",
                device.GetCommandSize("razer_chroma_misc_set_polling_rate2", 0x02),
                0x00, 0x40, [profile, EncodeHighPollingRate(hertz)])];
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
        byte? storage = null)
    {
        var effectiveStorage = storage
            ?? device.GetFixedArgumentOrNull("razer_chroma_misc_get_dpi_stages", 0)
            ?? OpenRazerStandardProtocol.VariableStore;
        return OpenRazerStandardProtocol.Create(device,
            "razer_chroma_misc_get_dpi_stages",
            device.GetCommandSize("razer_chroma_misc_get_dpi_stages", MiddlewareDpiStageFrameSize),
            0x04, 0x86, [effectiveStorage]);
    }

    internal static OpenRazerRequest SetDpiStages(
        OpenRazerDeviceDefinition device,
        OpenRazerDpiStages state,
        byte? storage = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Stages is null || state.Stages.Count is < 1 or > 5 ||
            state.ActiveStage is < 1 || state.ActiveStage > state.Stages.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }

        var effectiveStorage = storage
            ?? device.GetFixedArgumentOrNull("razer_chroma_misc_set_dpi_stages", 0)
            ?? OpenRazerStandardProtocol.VariableStore;
        var arguments = new byte[3 + (state.Stages.Count * 7)];
        arguments[0] = effectiveStorage;
        arguments[1] = state.ActiveStage;
        arguments[2] = checked((byte)state.Stages.Count);
        for (var index = 0; index < state.Stages.Count; index++)
        {
            var stage = state.Stages[index];
            ValidateDpi(device, stage.X, nameof(state));
            ValidateDpi(device, stage.Y, nameof(state));
            var offset = 3 + (index * 7);
            arguments[offset] = stage.Number;
            arguments[offset + 1] = High(stage.X);
            arguments[offset + 2] = Low(stage.X);
            arguments[offset + 3] = High(stage.Y);
            arguments[offset + 4] = Low(stage.Y);
            arguments[offset + 5] = High(stage.Z);
            arguments[offset + 6] = Low(stage.Z);
        }
        return OpenRazerStandardProtocol.Create(device,
            "razer_chroma_misc_set_dpi_stages",
            device.GetCommandSize("razer_chroma_misc_set_dpi_stages", MiddlewareDpiStageFrameSize),
            0x04, 0x06, arguments);
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

        var stages = new OpenRazerDpiStage[count];
        for (var index = 0; index < count; index++)
        {
            var offset = 3 + (index * 7);
            var number = arguments[offset];
            var x = (arguments[offset + 1] << 8) | arguments[offset + 2];
            var y = (arguments[offset + 3] << 8) | arguments[offset + 4];
            var z = (arguments[offset + 5] << 8) | arguments[offset + 6];
            ValidateDpi(device, x, nameof(response));
            ValidateDpi(device, y, nameof(response));
            stages[index] = new OpenRazerDpiStage(number, x, y, z);
        }
        return new OpenRazerDpiStages(activeStage, stages);
    }

    internal static OpenRazerRequest GetIdleTime(OpenRazerDeviceDefinition device) =>
        OpenRazerStandardProtocol.Create(device, "razer_chroma_misc_get_idle_time", 0x02, 0x07, 0x83, []);

    // The middleware drives the mouse power settings through the class 07 battery
    // namespace. Auto sleep is read through a fallback chain of command ids
    // because the firmware generation differs between the wireless mice; the
    // middleware settles on 07:88 for the mouse family.
    internal static OpenRazerRequest GetAutoSleep(OpenRazerDeviceDefinition device, byte profileId = 0) =>
        OpenRazerStandardProtocol.Create(device,
            "razer_chroma_misc_get_auto_sleep", 0x02, 0x07, 0x88, [profileId]);

    internal static OpenRazerRequest SetAutoSleep(OpenRazerDeviceDefinition device, bool enabled, byte profileId = 0) =>
        OpenRazerStandardProtocol.Create(device,
            "razer_chroma_misc_set_auto_sleep", 0x02, 0x07, 0x08,
            [profileId, enabled ? (byte)1 : (byte)0]);

    internal static OpenRazerRequest GetTimeToDim(OpenRazerDeviceDefinition device, byte profileId = 0) =>
        OpenRazerStandardProtocol.Create(device,
            "razer_chroma_misc_get_time_to_dim", 0x05, 0x07, 0x8B, [profileId]);

    /// <summary>
    /// The middleware writes the dim delay as five bytes: profile id, enabled flag,
    /// the delay as a big-endian 16-bit value, then the percentage.
    /// </summary>
    internal static OpenRazerRequest SetTimeToDim(
        OpenRazerDeviceDefinition device,
        byte profileId,
        bool enabled,
        int seconds,
        byte percentage) =>
        OpenRazerStandardProtocol.Create(device,
            "razer_chroma_misc_set_time_to_dim", 0x05, 0x07, 0x0B,
            [profileId, enabled ? (byte)1 : (byte)0, High(seconds), Low(seconds), percentage]);

    internal static OpenRazerRequest GetBatteryDischargeCurve(OpenRazerDeviceDefinition device) =>
        OpenRazerStandardProtocol.Create(device,
            "razer_chroma_misc_get_battery_discharge_curve", 0x01, 0x07, 0x94, []);

    internal static OpenRazerRequest SetBatteryDischargeCurve(OpenRazerDeviceDefinition device, byte chemistry) =>
        chemistry <= 2
            ? OpenRazerStandardProtocol.Create(device,
                "razer_chroma_misc_set_battery_discharge_curve", 0x01, 0x07, 0x14, [chemistry])
            : throw new ArgumentOutOfRangeException(nameof(chemistry), "Chemistry must be 0..2.");

    internal static OpenRazerRequest GetGpuCardDetection(OpenRazerDeviceDefinition device, byte ledClassId) =>
        OpenRazerStandardProtocol.Create(device,
            "razer_chroma_misc_get_gpu_card_detection", 0x03, 0x03, 0xA0, [ledClassId]);

    internal static OpenRazerRequest SetGpuCardDetection(
        OpenRazerDeviceDefinition device,
        byte ledClassId,
        byte enabled) =>
        OpenRazerStandardProtocol.Create(device,
            "razer_chroma_misc_set_gpu_card_detection", 0x03, 0x03, 0x20, [ledClassId, enabled]);

    internal static OpenRazerRequest GetProfileDebounceTime(OpenRazerDeviceDefinition device, byte profileId) =>
        OpenRazerStandardProtocol.Create(device,
            "razer_chroma_misc_get_profile_debounce_time", 0x05, 0x02, 0x95, [profileId]);

    internal static OpenRazerRequest SetProfileDebounceTime(
        OpenRazerDeviceDefinition device,
        byte profileId,
        byte makeDebounceTimeMs,
        byte makeMaskTimeMs,
        byte breakDebounceTimeMs,
        byte breakMaskTimeMs) =>
        OpenRazerStandardProtocol.Create(device,
            "razer_chroma_misc_set_profile_debounce_time", 0x05, 0x02, 0x15,
            [profileId, makeDebounceTimeMs, makeMaskTimeMs, breakDebounceTimeMs, breakMaskTimeMs]);

    internal static OpenRazerRequest GetChromaTap(OpenRazerDeviceDefinition device, byte profileId) =>
        OpenRazerStandardProtocol.Create(device,
            "razer_chroma_misc_get_chroma_tap", 0x04, 0x0F, 0x93, [profileId, 0x00, 0x00, 0x00]);

    internal static OpenRazerRequest SetChromaTap(
        OpenRazerDeviceDefinition device,
        byte profileId,
        byte status,
        byte tapMode,
        byte functionId)
    {
        if (status is not (1 or 2) || tapMode > 1 || functionId is < 1 or > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(status), "Chroma tap values are out of range.");
        }
        return OpenRazerStandardProtocol.Create(device,
            "razer_chroma_misc_set_chroma_tap", 0x04, 0x0F, 0x13,
            [profileId, status, tapMode, functionId]);
    }

    internal static OpenRazerRequest GetForceChromaCtrl(OpenRazerDeviceDefinition device) =>
        OpenRazerStandardProtocol.Create(device,
            "razer_chroma_misc_get_force_chroma_ctrl", 0x01, 0x0F, 0x90, []);

    internal static OpenRazerRequest SetForceChromaCtrl(OpenRazerDeviceDefinition device, byte configuration) =>
        OpenRazerStandardProtocol.Create(device,
            "razer_chroma_misc_set_force_chroma_ctrl", 0x01, 0x0F, 0x10, [configuration]);

    internal static OpenRazerRequest GetMacroStorageInfo(OpenRazerDeviceDefinition device) =>
        OpenRazerStandardProtocol.Create(device,
            "razer_chroma_misc_get_macro_storage_info", 0x0E, 0x06, 0x8E, []);

    internal static OpenRazerRequest GetBatteryChargeLimiter(OpenRazerDeviceDefinition device) =>
        OpenRazerStandardProtocol.Create(device,
            "razer_chroma_misc_get_battery_charge_limiter", 0x01, 0x07, 0x92, []);

    internal static OpenRazerRequest SetBatteryChargeLimiter(OpenRazerDeviceDefinition device, byte level, bool enabled) =>
        OpenRazerStandardProtocol.Create(device,
            "razer_chroma_misc_set_battery_charge_limiter", 0x01, 0x07, 0x12,
            // The middleware packs the enabled flag into the high bit and the
            // charge level into the low seven bits: `payload[0] = isEnabled << 7 | value`.
            [(byte)((enabled ? 0x80 : 0x00) | (level & 0x7F))]);

    internal static OpenRazerRequest GetIndicatorLedConfiguration(OpenRazerDeviceDefinition device) =>
        OpenRazerStandardProtocol.Create(device,
            "razer_chroma_misc_get_indicator_led_configuration", 0x01, 0x07, 0x90, []);

    internal static OpenRazerRequest SetIndicatorLedConfiguration(OpenRazerDeviceDefinition device, byte configuration) =>
        configuration <= 1
            ? OpenRazerStandardProtocol.Create(device,
                "razer_chroma_misc_set_indicator_led_configuration", 0x01, 0x07, 0x10, [configuration])
            : throw new ArgumentOutOfRangeException(nameof(configuration), "Configuration must be 0 or 1.");

    internal static bool ParseAutoSleepEnabled(ReadOnlySpan<byte> response) =>
        OpenRazerStandardProtocol.Arguments(response, 2)[1] != 0;

    /// <summary>
    /// The middleware reads the charge limiter level and the enabled flag out of one
    /// byte: `value = data[0] &amp; 0x7F` and `enabled = (data[0] &amp; 0x80) &gt;&gt; 7`.
    /// The flag therefore lives in the high bit, not the low bit.
    /// </summary>
    internal static (byte Level, bool Enabled) ParseBatteryChargeLimiter(ReadOnlySpan<byte> response)
    {
        var value = OpenRazerStandardProtocol.Arguments(response, 1)[0];
        return ((byte)(value & 0x7F), (value & 0x80) != 0);
    }

    /// <summary>
    /// The middleware reads the profile debounce times back as five bytes:
    /// profile id, then the make/break debounce and mask times.
    /// </summary>
    internal static OpenRazerProfileDebounce ParseProfileDebounceTime(ReadOnlySpan<byte> response)
    {
        var arguments = OpenRazerStandardProtocol.Arguments(response, 5);
        return new OpenRazerProfileDebounce(
            arguments[0], arguments[1], arguments[2], arguments[3], arguments[4]);
    }

    /// <summary>
    /// The middleware reads the macro storage summary back as fourteen bytes: the
    /// maximum id and the total, available and recyclable memory as four big-endian
    /// 16-bit values in between.
    /// </summary>
    internal static OpenRazerMacroStorageInfo ParseMacroStorageInfo(ReadOnlySpan<byte> response)
    {
        var arguments = OpenRazerStandardProtocol.Arguments(response, 14);
        return new OpenRazerMacroStorageInfo(
            (arguments[0] << 8) | arguments[1],
            (arguments[2] << 8) | arguments[3],
            (arguments[4] << 8) | arguments[5],
            (arguments[6] << 8) | arguments[7],
            (arguments[8] << 8) | arguments[9],
            (arguments[10] << 8) | arguments[11],
            (arguments[12] << 8) | arguments[13]);
    }

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
        // The middleware writes `Math.ceil(percent / 100 * 255)`. Truncating puts
        // the threshold one unit below the official app for most of the range.
        var raw = checked((byte)Math.Ceiling(percent * 255d / 100d));
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
            "razer_chroma_misc_get_scroll_smart_reel", 0x50, 0x02, 0x97,
            [OpenRazerStandardProtocol.VariableStore]);

    internal static OpenRazerRequest SetSmartReel(OpenRazerDeviceDefinition device, bool enabled) =>
        OpenRazerStandardProtocol.Create(device,
            "razer_chroma_misc_set_scroll_smart_reel", 0x50, 0x02, 0x17,
            [OpenRazerStandardProtocol.VariableStore, enabled ? (byte)1 : (byte)0]);

    internal static OpenRazerRequest GetLkScrollWheelMode(OpenRazerDeviceDefinition device, byte profileId = 1) =>
        OpenRazerStandardProtocol.Create(device,
            "razer_chroma_misc_get_lk_scroll_wheel_mode", 0x02, 0x15, 0x8B, [profileId]);

    internal static OpenRazerRequest SetLkScrollWheelMode(OpenRazerDeviceDefinition device, byte modeMask, byte profileId = 1)
    {
        if (modeMask is < 1 or > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(modeMask), "At least one scroll mode must stay enabled.");
        }
        return OpenRazerStandardProtocol.Create(device,
            "razer_chroma_misc_set_lk_scroll_wheel_mode", 0x02, 0x15, 0x0B, [profileId, modeMask]);
    }

    internal static int ParseBatteryPercent(ReadOnlySpan<byte> response)
    {
        var arguments = OpenRazerStandardProtocol.Arguments(response, 2);
        return (int)Math.Round(arguments[1] * 100d / 255d, MidpointRounding.AwayFromZero);
    }

    internal static bool ParseCharging(ReadOnlySpan<byte> response) =>
        OpenRazerStandardProtocol.Arguments(response, 2)[1] != 0;

    /// <summary>
    /// Reads the polling rate. OpenRazer sends get_polling_rate2 with data_size 1 but
    /// documents the rate as argument 1, and the firmware returns it in that second byte
    /// while still declaring a one-byte payload. Upstream reads that byte without checking
    /// the declared length, so the high-rate path must not reject a short response either.
    /// </summary>
    internal static int ParsePollingRate(ReadOnlySpan<byte> response, bool highRate)
    {
        if (!highRate)
        {
            var legacy = OpenRazerStandardProtocol.Arguments(response, 1)[0];
            return legacy switch
            {
                0x01 => 1000, 0x02 => 500, 0x03 => 333, 0x04 => 250,
                0x05 => 200, 0x08 => 125, 0x0A => 100,
                _ => throw new InvalidDataException($"Unknown polling-rate code 0x{legacy:X2}."),
            };
        }

        var arguments = OpenRazerStandardProtocol.RawArguments(response);
        var raw = arguments[1];
        return raw switch
        {
            0x01 => 8000, 0x02 => 4000, 0x04 => 2000, 0x08 => 1000,
            0x10 => 500, 0x20 => 250, 0x40 => 125,
            _ => throw new InvalidDataException($"Unknown high polling-rate code 0x{raw:X2}."),
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

    internal static OpenRazerTimeToDim ParseTimeToDim(ReadOnlySpan<byte> response)
    {
        var arguments = OpenRazerStandardProtocol.Arguments(response, 5);
        return new OpenRazerTimeToDim(
            arguments[1] != 0,
            (arguments[2] << 8) | arguments[3],
            arguments[4]);
    }

    internal static byte ParseIndicatorLedConfiguration(ReadOnlySpan<byte> response) =>
        OpenRazerStandardProtocol.Arguments(response, 1)[0];

    internal static OpenRazerGpuCardDetection ParseGpuCardDetection(ReadOnlySpan<byte> response)
    {
        var arguments = OpenRazerStandardProtocol.Arguments(response, 3);
        return new OpenRazerGpuCardDetection(arguments[0], arguments[1] != 0, arguments[2]);
    }

    /// <summary>
    /// The middleware reads the force-chroma state as two bytes: the profile id
    /// first and the enabled flag second.
    /// </summary>
    internal static bool ParseForceChromaCtrl(ReadOnlySpan<byte> response) =>
        OpenRazerStandardProtocol.Arguments(response, 2)[1] != 0;

    internal static OpenRazerChromaTap ParseChromaTap(ReadOnlySpan<byte> response)
    {
        var arguments = OpenRazerStandardProtocol.Arguments(response, 4);
        return new OpenRazerChromaTap(arguments[0], arguments[1], arguments[2], arguments[3]);
    }

    internal static byte ParseBatteryDischargeCurve(ReadOnlySpan<byte> response) =>
        OpenRazerStandardProtocol.Arguments(response, 1)[0];

    internal static OpenRazerLkScrollWheelMode ParseLkScrollWheelMode(ReadOnlySpan<byte> response)
    {
        var arguments = OpenRazerStandardProtocol.Arguments(response, 2);
        return new OpenRazerLkScrollWheelMode(arguments[0], arguments[1]);
    }

    internal static bool ParseBooleanSecondArgument(ReadOnlySpan<byte> response) =>
        OpenRazerStandardProtocol.Arguments(response, 2)[1] != 0;

    internal static byte ParseSecondArgument(ReadOnlySpan<byte> response) =>
        OpenRazerStandardProtocol.Arguments(response, 2)[1];

    private static byte EncodePollingRate(int hertz) => hertz switch
    {
        1000 => 0x01,
        500 => 0x02,
        333 => 0x03,
        250 => 0x04,
        200 => 0x05,
        125 => 0x08,
        100 => 0x0A,
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
