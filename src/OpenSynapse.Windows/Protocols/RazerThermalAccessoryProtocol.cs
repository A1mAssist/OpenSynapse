namespace OpenSynapse.Windows.Protocols;

internal enum RazerThermalChannelKind { Fan, Pump }

internal sealed record RazerThermalChannel(
    int Index, string Name, RazerThermalChannelKind Kind, int CurrentRpm,
    int TargetDuty, int ActualDuty, int Mode, bool Connected)
{
    internal int? TargetRpm { get; init; }
    internal double? TemperatureC { get; init; }
}

internal sealed record RazerThermalState(
    string Name, int Edition, IReadOnlyList<RazerThermalChannel> Channels,
    double? CpuTemperature, double? GpuTemperature, string? Error);

/// <summary>Elsa output/input commands and the separate PWM Protocol25 thermal class.</summary>
internal static class RazerThermalAccessoryProtocol
{
    internal const ushort HanboProductId = 0x0F35;
    internal const ushort PwmProductId = 0x0F3C;
    internal const int MaximumHanboDuty = 100;
    internal const int PwmRpmPerUnit = 50;
    internal const int PwmMaximumRpm = 3000;
    internal const int CurvePointCount = 9;

    internal static byte[] CreateHanboGetDeviceInfo() => CreateElsa(0x01);
    internal static byte[] CreateHanboGetFanInfo() => CreateElsa(0x20);
    internal static byte[] CreateHanboGetPumpInfo() => CreateElsa(0x12);
    internal static byte[] CreateHanboGetFanCurve() => CreateElsa(0x24);
    internal static byte[] CreateHanboGetPumpCurve() => CreateElsa(0x16);
    internal static byte[] CreateHanboSetFan(int mode, int speed) => CreateHanboSet(0x22, mode, speed);
    internal static byte[] CreateHanboSetPump(int mode, int speed) => CreateHanboSet(0x14, mode, speed);

    // Elsa curves contain nine temperatures, not temperature/duty pairs.
    internal static byte[] CreateHanboSetFanCurve(IReadOnlyList<byte> temperatures, byte mode = 0) =>
        CreateCurveRequest(0x26, temperatures, mode);
    internal static byte[] CreateHanboSetPumpCurve(IReadOnlyList<byte> temperatures, byte mode = 0) =>
        CreateCurveRequest(0x18, temperatures, mode);

    internal static byte[] CreateElsa(byte commandClass, ReadOnlySpan<byte> arguments = default)
    {
        if (arguments.Length > 62) throw new ArgumentOutOfRangeException(nameof(arguments));
        var report = new byte[65];
        report[0] = 0; // claimInterface/report ID from Hanbo's product configuration.
        report[1] = commandClass;
        report[2] = 1;
        arguments.CopyTo(report.AsSpan(3));
        return report;
    }

    internal static bool MatchesHanbo(byte[] response, byte requestClass, byte requestId) =>
        response.Length == 64 && response[0] == requestClass + 1 && response[1] == requestId + 1;

    internal static void ValidateHanboAck(byte[] response)
    {
        if (response.Length != 64 || response[2] != 1)
            throw new InvalidOperationException("Hanbo rejected the command.");
    }

    internal static int ReadHanboEdition(byte[] response)
    {
        RequireElsa(response, 0x01);
        return response[26] | (response[27] << 8);
    }

    internal static RazerThermalChannel ReadHanboFan(byte[] response)
    {
        RequireElsa(response, 0x20);
        return new(response[2], "Fans", RazerThermalChannelKind.Fan,
            RazerAccessoryProtocol.ReadUInt16BigEndian(response, 6),
            response[8], response[9], response[4], response[3] == 1);
    }

    internal static RazerThermalChannel ReadHanboPump(byte[] response)
    {
        RequireElsa(response, 0x12);
        return new(2, "Pump", RazerThermalChannelKind.Pump,
            RazerAccessoryProtocol.ReadUInt16BigEndian(response, 7),
            response[9], response[10], response[3], response[2] == 1)
        {
            TemperatureC = response[5] == 255 ? null : response[5] + response[6] / 10d,
        };
    }

    internal static IReadOnlyList<byte> ReadCurve(byte[] response)
    {
        if (response.Length != 64 || response[0] is not (0x17 or 0x25) || response[1] != 2)
            throw new InvalidDataException("Invalid Elsa curve response.");
        return response[2..11];
    }

    internal static byte[] CreatePwmGetFanSpeed(byte classId, byte thermalId) =>
        RazerAccessoryProtocol.CreateRequest(65, 7, 0x1F, 6, 0x0D, 0x81, [classId, thermalId]);

    internal static byte[] CreatePwmSetFanSpeed(byte classId, byte thermalId, int rpm) =>
        RazerAccessoryProtocol.CreateRequest(65, 7, 0x1F, 6, 0x0D, 0x01,
            [classId, thermalId, checked((byte)ToPwmUnits(rpm))]);

    internal static byte[] CreatePwmGetFanInformation(byte classId) =>
        RazerAccessoryProtocol.CreateRequest(65, 7, 0x1F, 8, 0x0D, 0x83, [classId]);

    internal static (int CurrentRpm, int TargetRpm) ReadPwmSpeed(byte[] response, byte expectedClass, byte expectedThermal)
    {
        var data = RazerAccessoryProtocol.Arguments(response);
        if (data.Length < 6 || data[0] != expectedClass || data[1] != expectedThermal)
            throw new InvalidDataException("PWM fan response has an unexpected thermal object.");
        return (RazerAccessoryProtocol.ReadUInt16BigEndian(data, 4),
            RazerAccessoryProtocol.ReadUInt16BigEndian(data, 2));
    }

    internal static int ToPwmUnits(int rpm)
    {
        if (rpm is < 0 or > PwmMaximumRpm || rpm % PwmRpmPerUnit != 0)
            throw new ArgumentOutOfRangeException(nameof(rpm), rpm,
                $"PWM speed must be 0..{PwmMaximumRpm} RPM in {PwmRpmPerUnit} RPM steps.");
        return rpm / PwmRpmPerUnit;
    }

    private static byte[] CreateHanboSet(byte commandClass, int mode, int speed)
    {
        if (mode is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(mode));
        if (speed is < 0 or > MaximumHanboDuty) throw new ArgumentOutOfRangeException(nameof(speed));
        return CreateElsa(commandClass, [(byte)mode, (byte)speed, 0]);
    }

    private static byte[] CreateCurveRequest(byte commandClass, IReadOnlyList<byte> temperatures, byte mode)
    {
        ArgumentNullException.ThrowIfNull(temperatures);
        if (temperatures.Count != CurvePointCount || temperatures.Any(value => value > 100))
            throw new ArgumentException("Elsa curves need nine temperatures in 0..100 C.", nameof(temperatures));
        if (mode > 3) throw new ArgumentOutOfRangeException(nameof(mode));
        var payload = new byte[12];
        payload[0] = mode;
        for (var index = 0; index < temperatures.Count; index++) payload[index + 2] = temperatures[index];
        return CreateElsa(commandClass, payload);
    }

    private static void RequireElsa(byte[] response, byte requestClass)
    {
        if (!MatchesHanbo(response, requestClass, 1))
            throw new InvalidDataException("Invalid Elsa response header or length.");
    }
}
