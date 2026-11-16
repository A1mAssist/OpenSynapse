namespace OpenSynapse.Windows.Protocols;

internal enum RazerThermalChannelKind
{
    Fan,
    Pump,
}

internal sealed record RazerThermalChannel(
    int Index,
    string Name,
    RazerThermalChannelKind Kind,
    int CurrentRpm,
    int TargetDuty,
    int ActualDuty,
    int Mode,
    bool Connected);

internal sealed record RazerThermalCurvePoint(double TemperatureC, int DutyPercent);

internal sealed record RazerThermalState(
    string Name,
    int Edition,
    IReadOnlyList<RazerThermalChannel> Channels,
    double? CpuTemperature,
    double? GpuTemperature,
    string? Error);

/// <summary>
/// Hanbo AIO (0F35) uses a 65-byte Elsa frame; the PWM fan controller (0F3C) uses the
/// generic thermal class on the same 65-byte channel. Command bytes are from the official
/// bundles; the curve layout is kept behind a probe because its 9-point packing has not
/// been confirmed on hardware yet.
/// </summary>
internal static class RazerThermalAccessoryProtocol
{
    internal const ushort HanboProductId = 0x0F35;
    internal const ushort PwmProductId = 0x0F3C;
    internal const int MaximumHanboDuty = 100;
    internal const int PwmRpmPerUnit = 50;
    internal const int PwmMaximumRpm = 3000;
    internal const int CurvePointCount = 9;

    internal static byte[] CreateHanboGetDeviceInfo() =>
        RazerAccessoryProtocol.CreateRequest(
            RazerAccessoryProtocol.ReportLength65, 0x07, 0x1F, 0, 0x02, 0x02, ReadOnlySpan<byte>.Empty);

    internal static byte[] CreateHanboGetFanInfo() =>
        RazerAccessoryProtocol.CreateRequest(
            RazerAccessoryProtocol.ReportLength65, 0x07, 0x1F, 0, 0x20, 0x01, ReadOnlySpan<byte>.Empty);

    internal static byte[] CreateHanboSetFan(int mode, int speed) =>
        CreateHanboSet(0x22, 0x01, mode, speed);

    internal static byte[] CreateHanboGetPumpInfo() =>
        RazerAccessoryProtocol.CreateRequest(
            RazerAccessoryProtocol.ReportLength65, 0x07, 0x1F, 0, 0x12, 0x01, ReadOnlySpan<byte>.Empty);

    internal static byte[] CreateHanboSetPump(int mode, int speed) =>
        CreateHanboSet(0x14, 0x01, mode, speed);

    internal static byte[] CreateHanboGetTemperatures() =>
        RazerAccessoryProtocol.CreateRequest(
            RazerAccessoryProtocol.ReportLength65, 0x07, 0x1F, 0, 0xC2, 0x01, ReadOnlySpan<byte>.Empty);

    internal static byte[] CreateHanboGetFanCurve() =>
        RazerAccessoryProtocol.CreateRequest(
            RazerAccessoryProtocol.ReportLength65, 0x07, 0x1F, 0, 0x24, 0x01, ReadOnlySpan<byte>.Empty);

    internal static byte[] CreateHanboSetFanCurve(IReadOnlyList<RazerThermalCurvePoint> points) =>
        CreateCurveRequest(0x26, 0x01, points);

    internal static byte[] CreateHanboGetPumpCurve() =>
        RazerAccessoryProtocol.CreateRequest(
            RazerAccessoryProtocol.ReportLength65, 0x07, 0x1F, 0, 0x16, 0x01, ReadOnlySpan<byte>.Empty);

    internal static byte[] CreateHanboSetPumpCurve(IReadOnlyList<RazerThermalCurvePoint> points) =>
        CreateCurveRequest(0x18, 0x01, points);

    internal static bool MatchesHanbo(byte[] response, byte requestClass, byte requestId) =>
        response.Length == RazerAccessoryProtocol.ReportLength65 &&
        response[7] == (byte)(requestClass + 1) &&
        response[8] == (byte)(requestId + 1);

    internal static int ReadHanboEdition(byte[] response)
    {
        var data = RazerAccessoryProtocol.Arguments(response);
        return data.Length == 0 ? 0 : data[0];
    }

    internal static RazerThermalChannel ReadHanboFan(byte[] response)
    {
        var data = RazerAccessoryProtocol.Arguments(response);
        if (data.Length < 8)
        {
            throw new InvalidDataException("Hanbo fan information is shorter than 8 bytes.");
        }

        return new RazerThermalChannel(
            data[0],
            "Fans",
            RazerThermalChannelKind.Fan,
            RazerAccessoryProtocol.ReadUInt16BigEndian(data, 4),
            data[6],
            data[7],
            data[3],
            data[1] != 0);
    }

    internal static RazerThermalChannel ReadHanboPump(byte[] response)
    {
        var data = RazerAccessoryProtocol.Arguments(response);
        if (data.Length < 9)
        {
            throw new InvalidDataException("Hanbo pump information is shorter than 9 bytes.");
        }

        return new RazerThermalChannel(
            2,
            "Pump",
            RazerThermalChannelKind.Pump,
            RazerAccessoryProtocol.ReadUInt16BigEndian(data, 5),
            data[7],
            data[8],
            data[2],
            data[0] != 0);
    }

    internal static (double? Cpu, double? Gpu) ReadHanboTemperatures(byte[] response)
    {
        var data = RazerAccessoryProtocol.Arguments(response);
        return data.Length >= 2
            ? (data[0], data[1])
            : (null, null);
    }

    internal static IReadOnlyList<RazerThermalCurvePoint> ReadCurve(byte[] response)
    {
        var data = RazerAccessoryProtocol.Arguments(response);
        if (data.Length < CurvePointCount * 2)
        {
            throw new InvalidDataException("Thermal curve response is shorter than 9 points.");
        }

        var points = new RazerThermalCurvePoint[CurvePointCount];
        for (var index = 0; index < points.Length; index++)
        {
            points[index] = new RazerThermalCurvePoint(data[index * 2], data[(index * 2) + 1]);
        }

        return points;
    }

    internal static byte[] CreatePwmGetFanSpeed(byte classId, byte thermalId) =>
        RazerAccessoryProtocol.CreateRequest(
            RazerAccessoryProtocol.ReportLength65,
            0x07,
            0x1F,
            2,
            0x0D,
            0x81,
            new[] { classId, thermalId });

    internal static byte[] CreatePwmSetFanSpeed(byte classId, byte thermalId, int rpm)
    {
        var units = ToPwmUnits(rpm);
        return RazerAccessoryProtocol.CreateRequest(
            RazerAccessoryProtocol.ReportLength65,
            0x07,
            0x1F,
            3,
            0x0D,
            0x01,
            new[] { classId, thermalId, checked((byte)units) });
    }

    internal static byte[] CreatePwmGetFanInformation(byte classId) =>
        RazerAccessoryProtocol.CreateRequest(
            RazerAccessoryProtocol.ReportLength65,
            0x07,
            0x1F,
            8,
            0x0D,
            0x83,
            new[] { classId });

    internal static (int CurrentRpm, int TargetRpm) ReadPwmSpeed(byte[] response, byte expectedClass, byte expectedThermal)
    {
        var data = RazerAccessoryProtocol.Arguments(response);
        if (data.Length < 6 || data[0] != expectedClass || data[1] != expectedThermal)
        {
            throw new InvalidDataException("PWM fan response has an unexpected thermal object.");
        }

        return (
            RazerAccessoryProtocol.ReadUInt16BigEndian(data, 4),
            RazerAccessoryProtocol.ReadUInt16BigEndian(data, 2));
    }

    internal static int ToPwmUnits(int rpm)
    {
        if (rpm is < 0 or > PwmMaximumRpm || rpm % PwmRpmPerUnit != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rpm),
                rpm,
                $"PWM speed must be 0..{PwmMaximumRpm} RPM in {PwmRpmPerUnit} RPM steps.");
        }

        return rpm / PwmRpmPerUnit;
    }

    private static byte[] CreateHanboSet(byte commandClass, byte commandId, int mode, int speed)
    {
        if (mode is < 0 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(mode), "Hanbo mode must be 0..3.");
        }
        if (speed is < 0 or > MaximumHanboDuty)
        {
            throw new ArgumentOutOfRangeException(nameof(speed), "Hanbo duty must be 0..100.");
        }

        return RazerAccessoryProtocol.CreateRequest(
            RazerAccessoryProtocol.ReportLength65,
            0x07,
            0x1F,
            3,
            commandClass,
            commandId,
            new[] { checked((byte)mode), checked((byte)speed), (byte)0 });
    }

    private static byte[] CreateCurveRequest(
        byte commandClass,
        byte commandId,
        IReadOnlyList<RazerThermalCurvePoint> points)
    {
        if (points.Count != CurvePointCount)
        {
            throw new ArgumentException($"Thermal curves need exactly {CurvePointCount} points.", nameof(points));
        }

        var payload = new byte[CurvePointCount * 2];
        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];
            if (point.TemperatureC is < 0 or > 100 || point.DutyPercent is < 0 or > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(points), "Curve points must be 0..100.");
            }

            payload[index * 2] = checked((byte)Math.Round(point.TemperatureC));
            payload[(index * 2) + 1] = checked((byte)point.DutyPercent);
        }

        return RazerAccessoryProtocol.CreateRequest(
            RazerAccessoryProtocol.ReportLength65,
            0x07,
            0x1F,
            checked((byte)payload.Length),
            commandClass,
            commandId,
            payload);
    }
}
