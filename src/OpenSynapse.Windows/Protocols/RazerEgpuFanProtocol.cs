namespace OpenSynapse.Windows.Protocols;

internal enum RazerEgpuFanMode : byte
{
    Auto = 0x00,
    Manual = 0x01,
    ForceAuto = 0x02,
}

internal enum RazerEgpuTemperatureSource
{
    Gpu,
    Chassis,
}

internal readonly record struct RazerEgpuFanCurvePoint(int TemperatureCelsius, int Rpm);

/// <summary>
/// Core X V2 thermal commands from the 0F51 rzDevice25FiverV2 bundle. The device
/// uses thermal class 0 and thermal id 6 (Fan2), with a two-byte big-endian RPM.
/// </summary>
internal static class RazerEgpuFanProtocol
{
    internal const ushort ProductId = 0x0F51;
    internal const byte CommandClass = 0x0D;
    internal const byte ClassId = 0x00;
    internal const byte ThermalId = 0x06;

    internal const byte SetFanSpeedCommand = 0x01;
    internal const byte SetFanModeCommand = 0x02;
    internal const byte GetFanSpeedCommand = 0x81;
    internal const byte GetFanModeCommand = 0x82;
    internal const byte GetFanInformationCommand = 0x83;
    internal const byte GetThermalReadingCommand = 0x85;

    internal const byte SetFanSpeedDataSize = 6;
    internal const byte GetFanSpeedDataSize = 6;
    internal const byte SetFanModeDataSize = 6;
    internal const byte GetFanModeDataSize = 4;
    internal const byte GetFanInformationDataSize = 12;
    internal const byte GetThermalReadingDataSize = 80;

    internal const int MinimumCurveRpm = 1000;
    internal const int MaximumCurveRpm = 2800;

    internal static readonly IReadOnlyList<RazerEgpuFanCurvePoint> QuietGpuCurve =
    [
        new(50, 1000),
        new(60, 1200),
        new(65, 1400),
        new(70, 1650),
        new(75, 1900),
        new(80, 2200),
        new(85, 2500),
    ];

    internal static readonly IReadOnlyList<RazerEgpuFanCurvePoint> BalancedGpuCurve =
    [
        new(45, 1050),
        new(55, 1300),
        new(60, 1600),
        new(65, 1900),
        new(70, 2200),
        new(75, 2450),
        new(80, 2650),
        new(85, 2800),
    ];

    internal static readonly IReadOnlyList<RazerEgpuFanCurvePoint> PerformanceGpuCurve =
    [
        new(40, 1200),
        new(50, 1600),
        new(60, 2000),
        new(65, 2300),
        new(70, 2500),
        new(75, 2650),
        new(80, 2750),
        new(85, 2800),
    ];

    internal static readonly IReadOnlyList<RazerEgpuFanCurvePoint> QuietChassisCurve =
    [
        new(30, 1000),
        new(35, 1150),
        new(40, 1300),
        new(45, 1500),
        new(50, 1750),
        new(55, 2000),
        new(60, 2250),
    ];

    internal static readonly IReadOnlyList<RazerEgpuFanCurvePoint> BalancedChassisCurve =
    [
        new(25, 1000),
        new(30, 1150),
        new(35, 1300),
        new(40, 1550),
        new(45, 1800),
        new(50, 2050),
        new(55, 2300),
        new(60, 2550),
    ];

    internal static readonly IReadOnlyList<RazerEgpuFanCurvePoint> PerformanceChassisCurve =
    [
        new(20, 1000),
        new(25, 1150),
        new(30, 1300),
        new(35, 1550),
        new(40, 1800),
        new(45, 2050),
        new(50, 2300),
        new(55, 2550),
        new(60, 2800),
    ];

    /// <summary>UI preset 0 is automatic; presets 1..3 are the bundle's selected-source curves.</summary>
    internal static IReadOnlyList<RazerEgpuFanCurvePoint>? CurveForPreset(
        int preset,
        RazerEgpuTemperatureSource source) =>
        source switch
        {
            RazerEgpuTemperatureSource.Gpu => preset switch
            {
                1 => QuietGpuCurve,
                2 => BalancedGpuCurve,
                3 => PerformanceGpuCurve,
                _ => null,
            },
            RazerEgpuTemperatureSource.Chassis => preset switch
            {
                1 => QuietChassisCurve,
                2 => BalancedChassisCurve,
                3 => PerformanceChassisCurve,
                _ => null,
            },
            _ => null,
        };

    internal static int ImplicitLowerTemperature(RazerEgpuTemperatureSource source) =>
        source == RazerEgpuTemperatureSource.Gpu ? 30 : 20;

    internal static byte[] CreateGetFanSpeedArguments() => [ClassId, ThermalId];

    internal static byte[] CreateSetFanSpeedArguments(int rpm)
    {
        if (rpm is < 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(rpm));
        }

        var arguments = new byte[4];
        arguments[0] = ClassId;
        arguments[1] = ThermalId;
        RazerAccessoryProtocol.WriteUInt16BigEndian(arguments, 2, rpm);
        return arguments;
    }

    internal static byte[] CreateGetFanModeArguments() => [ClassId, ThermalId];

    internal static byte[] CreateSetFanModeArguments(RazerEgpuFanMode mode) =>
        [ClassId, ThermalId, (byte)mode];

    internal static byte[] CreateGetFanInformationArguments() => [ThermalId];

    internal static byte[] CreateThermalReadingArguments() => [];

    internal static int ReadFanSpeed(ReadOnlySpan<byte> response)
    {
        var arguments = ReadArguments(response, minimumLength: 4);
        if (arguments[0] != ClassId || arguments[1] != ThermalId)
        {
            throw new InvalidDataException("Core X V2 fan speed response has the wrong fan identity.");
        }

        return RazerAccessoryProtocol.ReadUInt16BigEndian(arguments, 2);
    }

    internal static RazerEgpuFanMode ReadFanMode(ReadOnlySpan<byte> response)
    {
        var arguments = ReadArguments(response, minimumLength: 3);
        if (arguments[0] != ClassId || arguments[1] != ThermalId)
        {
            throw new InvalidDataException("Core X V2 fan mode response has the wrong fan identity.");
        }

        return arguments[2] switch
        {
            (byte)RazerEgpuFanMode.Auto => RazerEgpuFanMode.Auto,
            (byte)RazerEgpuFanMode.Manual => RazerEgpuFanMode.Manual,
            (byte)RazerEgpuFanMode.ForceAuto => RazerEgpuFanMode.ForceAuto,
            _ => throw new InvalidDataException($"Core X V2 returned unsupported fan mode {arguments[2]}."),
        };
    }

    internal static (int MinimumRpm, int MaximumRpm) ReadFanInformation(ReadOnlySpan<byte> response)
    {
        var arguments = ReadArguments(response, minimumLength: 12);
        var minimum = arguments[6] * 100;
        var maximum = arguments[10] * 100;
        if (minimum <= 0 || maximum <= 0 || minimum > maximum)
        {
            throw new InvalidDataException(
                $"Core X V2 returned an invalid fan range {minimum}..{maximum} RPM.");
        }

        return (minimum, maximum);
    }

    internal static double? ReadChassisTemperature(ReadOnlySpan<byte> response)
    {
        var arguments = ReadArguments(response, minimumLength: 1);
        var count = arguments[0];
        if (count == 0 || arguments.Length < count + 1)
        {
            return null;
        }

        return arguments[1];
    }

    internal static int EvaluateCurve(
        IReadOnlyList<RazerEgpuFanCurvePoint> curve,
        double temperatureCelsius,
        int implicitTemperature = 20)
    {
        ArgumentNullException.ThrowIfNull(curve);
        if (curve.Count == 0)
        {
            throw new ArgumentException("A fan curve needs at least one point.", nameof(curve));
        }

        for (var index = 1; index < curve.Count; index++)
        {
            if (curve[index].TemperatureCelsius <= curve[index - 1].TemperatureCelsius)
            {
                throw new ArgumentException(
                    "Fan curve temperatures must be strictly increasing.",
                    nameof(curve));
            }
        }

        // The bundle seeds GPU curves at 30 C and chassis curves at 20 C below their
        // first point, always at 1000 RPM.
        var first = curve[0];
        if (temperatureCelsius < first.TemperatureCelsius)
        {
            if (first.TemperatureCelsius <= implicitTemperature)
            {
                return MinimumCurveRpm;
            }

            var fraction = Math.Max(
                0,
                (temperatureCelsius - implicitTemperature) /
                (first.TemperatureCelsius - implicitTemperature));
            var interpolated = MinimumCurveRpm +
                ((first.Rpm - MinimumCurveRpm) * fraction);
            return Math.Max(MinimumCurveRpm, RoundRpm(interpolated));
        }

        if (temperatureCelsius == first.TemperatureCelsius)
        {
            return first.Rpm;
        }

        var last = curve[^1];
        if (temperatureCelsius >= last.TemperatureCelsius)
        {
            return last.Rpm;
        }

        for (var index = 0; index < curve.Count - 1; index++)
        {
            var left = curve[index];
            var right = curve[index + 1];
            if (temperatureCelsius <= left.TemperatureCelsius ||
                temperatureCelsius > right.TemperatureCelsius)
            {
                continue;
            }

            var span = right.TemperatureCelsius - left.TemperatureCelsius;
            var fraction = (temperatureCelsius - left.TemperatureCelsius) / span;
            return RoundRpm(left.Rpm + ((right.Rpm - left.Rpm) * fraction));
        }

        return last.Rpm;
    }

    internal static int EvaluateCurve(
        IReadOnlyList<RazerEgpuFanCurvePoint> curve,
        double temperatureCelsius,
        RazerEgpuTemperatureSource source) =>
        EvaluateCurve(curve, temperatureCelsius, ImplicitLowerTemperature(source));

    internal static int ClampRpm(int rpm, int minimumRpm, int maximumRpm)
    {
        if (minimumRpm <= 0 || maximumRpm < minimumRpm)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumRpm));
        }

        return Math.Clamp(rpm, minimumRpm, maximumRpm);
    }

    private static ReadOnlySpan<byte> ReadArguments(ReadOnlySpan<byte> response, int minimumLength)
    {
        var arguments = RazerAccessoryProtocol.Arguments(response);
        if (arguments.Length < minimumLength)
        {
            throw new InvalidDataException(
                $"Core X V2 response carried {arguments.Length} bytes; expected at least {minimumLength}.");
        }

        return arguments;
    }

    private static int RoundRpm(double value) =>
        (int)Math.Round(value, MidpointRounding.AwayFromZero);
}
