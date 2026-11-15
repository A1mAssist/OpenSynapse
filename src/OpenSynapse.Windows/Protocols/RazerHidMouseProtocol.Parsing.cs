using OpenSynapse.Windows.Devices;

namespace OpenSynapse.Windows.Protocols;

internal static partial class RazerHidMouseProtocol
{
    internal static RazerHidDpiStages ParseDpiStages(
        RazerHidDeviceDefinition device,
        ReadOnlySpan<byte> response)
    {
        var arguments = RazerHidStandardProtocol.Arguments(response, 10);
        var activeStage = arguments[1];
        var count = arguments[2];
        if (count is < 1 or > 5 || activeStage is < 1 || activeStage > count ||
            arguments.Length < 3 + (count * 7))
        {
            throw new InvalidDataException("Device returned an invalid DPI stage header.");
        }

        var stages = new RazerHidDpiStage[count];
        for (var index = 0; index < count; index++)
        {
            var offset = 3 + (index * 7);
            var number = arguments[offset];
            var x = (arguments[offset + 1] << 8) | arguments[offset + 2];
            var y = (arguments[offset + 3] << 8) | arguments[offset + 4];
            var z = (arguments[offset + 5] << 8) | arguments[offset + 6];
            ValidateDpi(device, x, nameof(response));
            ValidateDpi(device, y, nameof(response));
            stages[index] = new RazerHidDpiStage(number, x, y, z);
        }
        return new RazerHidDpiStages(activeStage, stages);
    }

    internal static bool ParseAutoSleepEnabled(ReadOnlySpan<byte> response) =>
        RazerHidStandardProtocol.Arguments(response, 2)[1] != 0;

    /// <summary>
    /// The middleware reads the charge limiter level and the enabled flag out of one
    /// byte: `value = data[0] &amp; 0x7F` and `enabled = (data[0] &amp; 0x80) &gt;&gt; 7`.
    /// The flag therefore lives in the high bit, not the low bit.
    /// </summary>
    internal static (byte Level, bool Enabled) ParseBatteryChargeLimiter(ReadOnlySpan<byte> response)
    {
        var value = RazerHidStandardProtocol.Arguments(response, 1)[0];
        return ((byte)(value & 0x7F), (value & 0x80) != 0);
    }

    /// <summary>
    /// The middleware reads the profile debounce times back as five bytes:
    /// profile id, then the make/break debounce and mask times.
    /// </summary>
    internal static RazerHidProfileDebounce ParseProfileDebounceTime(ReadOnlySpan<byte> response)
    {
        var arguments = RazerHidStandardProtocol.Arguments(response, 5);
        return new RazerHidProfileDebounce(
            arguments[0], arguments[1], arguments[2], arguments[3], arguments[4]);
    }

    /// <summary>
    /// The middleware reads the macro storage summary back as fourteen bytes: the
    /// maximum id and the total, available and recyclable memory as four big-endian
    /// 16-bit values in between.
    /// </summary>
    internal static RazerHidMacroStorageInfo ParseMacroStorageInfo(ReadOnlySpan<byte> response)
    {
        var arguments = RazerHidStandardProtocol.Arguments(response, 14);
        return new RazerHidMacroStorageInfo(
            (arguments[0] << 8) | arguments[1],
            (arguments[2] << 8) | arguments[3],
            (arguments[4] << 8) | arguments[5],
            (arguments[6] << 8) | arguments[7],
            (arguments[8] << 8) | arguments[9],
            (arguments[10] << 8) | arguments[11],
            (arguments[12] << 8) | arguments[13]);
    }

    internal static int ParseBatteryPercent(ReadOnlySpan<byte> response)
    {
        var arguments = RazerHidStandardProtocol.Arguments(response, 2);
        return (int)Math.Floor(arguments[1] * 100d / 255d);
    }

    internal static bool ParseCharging(ReadOnlySpan<byte> response) =>
        RazerHidStandardProtocol.Arguments(response, 2)[1] != 0;

    /// <summary>
    /// Reads the polling rate. RazerHid sends get_polling_rate2 with data_size 1 but
    /// documents the rate as argument 1, and the firmware returns it in that second byte
    /// while still declaring a one-byte payload. Upstream reads that byte without checking
    /// the declared length, so the high-rate path must not reject a short response either.
    /// </summary>
    internal static int ParsePollingRate(ReadOnlySpan<byte> response, bool highRate)
    {
        if (!highRate)
        {
            var legacy = RazerHidStandardProtocol.Arguments(response, 1)[0];
            return legacy switch
            {
                0x01 => 1000, 0x02 => 500, 0x03 => 333, 0x04 => 250,
                0x05 => 200, 0x08 => 125, 0x0A => 100,
                _ => throw new InvalidDataException($"Unknown polling-rate code 0x{legacy:X2}."),
            };
        }

        var arguments = RazerHidStandardProtocol.RawArguments(response);
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
        var arguments = RazerHidStandardProtocol.Arguments(response, byteEncoding ? (byte)2 : (byte)5);
        return byteEncoding
            ? (DecodeLegacyDpi(arguments[0]), DecodeLegacyDpi(arguments[1]))
            : ((arguments[1] << 8) | arguments[2], (arguments[3] << 8) | arguments[4]);
    }

    internal static int ParseIdleSeconds(ReadOnlySpan<byte> response)
    {
        var arguments = RazerHidStandardProtocol.Arguments(response, 2);
        return (arguments[0] << 8) | arguments[1];
    }

    internal static int ParseLowBatteryThreshold(ReadOnlySpan<byte> response) =>
        (int)Math.Floor(RazerHidStandardProtocol.Arguments(response, 1)[0] * 100d / 255d);

    internal static RazerHidTimeToDim ParseTimeToDim(ReadOnlySpan<byte> response)
    {
        var arguments = RazerHidStandardProtocol.Arguments(response, 5);
        return new RazerHidTimeToDim(
            arguments[1] != 0,
            (arguments[2] << 8) | arguments[3],
            arguments[4]);
    }

    internal static byte ParseIndicatorLedConfiguration(ReadOnlySpan<byte> response) =>
        RazerHidStandardProtocol.Arguments(response, 1)[0];

    internal static RazerHidGpuCardDetection ParseGpuCardDetection(ReadOnlySpan<byte> response)
    {
        var arguments = RazerHidStandardProtocol.Arguments(response, 3);
        return new RazerHidGpuCardDetection(arguments[0], arguments[1] != 0, arguments[2]);
    }

    /// <summary>
    /// The middleware reads the force-chroma state as two bytes: the profile id
    /// first and the enabled flag second.
    /// </summary>
    internal static bool ParseForceChromaCtrl(ReadOnlySpan<byte> response) =>
        RazerHidStandardProtocol.Arguments(response, 2)[1] != 0;

    internal static RazerHidChromaTap ParseChromaTap(ReadOnlySpan<byte> response)
    {
        var arguments = RazerHidStandardProtocol.Arguments(response, 4);
        return new RazerHidChromaTap(arguments[0], arguments[1], arguments[2], arguments[3]);
    }

    internal static byte ParseBatteryDischargeCurve(ReadOnlySpan<byte> response)
    {
        var chemistry = RazerHidStandardProtocol.Arguments(response, 1)[0];
        return chemistry <= 2
            ? chemistry
            : throw new InvalidDataException($"Unknown battery discharge curve: 0x{chemistry:X2}.");
    }

    internal static RazerHidLkScrollWheelMode ParseLkScrollWheelMode(ReadOnlySpan<byte> response)
    {
        var arguments = RazerHidStandardProtocol.Arguments(response, 2);
        return new RazerHidLkScrollWheelMode(arguments[0], arguments[1]);
    }

    internal static bool ParseBooleanSecondArgument(ReadOnlySpan<byte> response) =>
        RazerHidStandardProtocol.Arguments(response, 2)[1] != 0;

    internal static byte ParseSecondArgument(ReadOnlySpan<byte> response) =>
        RazerHidStandardProtocol.Arguments(response, 2)[1];

    private static int DecodeLegacyDpi(byte value) =>
        (int)Math.Round(value / 255d * 6750d, 2);
}
