using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

internal sealed class RazerThermalAccessoryService : IAsyncDisposable
{
    private readonly RazerAccessoryCommandClient _client;
    private readonly Action<string>? _diagnostic;

    internal RazerThermalAccessoryService(
        RazerAccessoryCommandClient client,
        Action<string>? diagnostic = null)
    {
        _client = client;
        _diagnostic = diagnostic;
    }

    internal ushort ProductId => _client.ProductId;

    internal static async Task<RazerThermalAccessoryService?> ConnectAsync(
        ushort productId,
        Action<string>? diagnostic = null,
        CancellationToken cancellationToken = default)
    {
        var connection = await RazerAccessoryDiscovery.FindAsync(
            productId,
            65,
            diagnostic,
            cancellationToken).ConfigureAwait(false);
        if (connection is null)
        {
            connection = await RazerAccessoryDiscovery.FindAsync(
                productId,
                91,
                diagnostic,
                cancellationToken).ConfigureAwait(false);
        }

        return connection is null
            ? null
            : new RazerThermalAccessoryService(
                new RazerAccessoryCommandClient(connection, diagnostic),
                diagnostic);
    }

    internal async Task<RazerThermalState> ReadStateAsync(CancellationToken cancellationToken = default)
    {
        return ProductId switch
        {
            RazerThermalAccessoryProtocol.HanboProductId => await ReadHanboAsync(cancellationToken)
                .ConfigureAwait(false),
            RazerThermalAccessoryProtocol.PwmProductId => await ReadPwmAsync(cancellationToken)
                .ConfigureAwait(false),
            _ => throw new NotSupportedException($"Thermal accessory {ProductId:X4} is not supported."),
        };
    }

    internal async Task SetHanboModeAsync(
        RazerThermalChannelKind kind,
        int mode,
        int speed,
        CancellationToken cancellationToken = default)
    {
        var isPump = kind == RazerThermalChannelKind.Pump;
        var request = isPump
            ? RazerThermalAccessoryProtocol.CreateHanboSetPump(mode, speed)
            : RazerThermalAccessoryProtocol.CreateHanboSetFan(mode, speed);
        var commandClass = request[7];
        var commandId = request[8];
        await QueryHanboAsync(
            request,
            commandClass,
            commandId,
            cancellationToken).ConfigureAwait(false);
    }

    internal async Task<int> SetPwmSpeedAsync(
        byte thermalId,
        int rpm,
        CancellationToken cancellationToken = default)
    {
        var request = RazerThermalAccessoryProtocol.CreatePwmSetFanSpeed(0x01, thermalId, rpm);
        await _client.QueryAsync(
            3,
            0x0D,
            0x01,
            request.AsMemory(RazerAccessoryProtocol.ArgumentsOffset, 3),
            response => RazerAccessoryProtocol.MatchesCommand(response, 0x0D, 0x01),
            cancellationToken).ConfigureAwait(false);
        var readback = await ReadPwmChannelAsync(thermalId, cancellationToken).ConfigureAwait(false);
        if (readback.CurrentRpm != rpm)
        {
            throw new InvalidOperationException(
                $"PWM fan {thermalId} readback was {readback.CurrentRpm} RPM, expected {rpm}.");
        }

        return readback.CurrentRpm;
    }

    internal async Task<IReadOnlyList<RazerThermalCurvePoint>> ReadHanboCurveAsync(
        RazerThermalChannelKind kind,
        CancellationToken cancellationToken = default)
    {
        var request = kind == RazerThermalChannelKind.Pump
            ? RazerThermalAccessoryProtocol.CreateHanboGetPumpCurve()
            : RazerThermalAccessoryProtocol.CreateHanboGetFanCurve();
        var response = await QueryHanboAsync(
            request,
            request[7],
            request[8],
            cancellationToken).ConfigureAwait(false);
        return RazerThermalAccessoryProtocol.ReadCurve(response);
    }

    internal async Task SetHanboCurveAsync(
        RazerThermalChannelKind kind,
        IReadOnlyList<RazerThermalCurvePoint> points,
        CancellationToken cancellationToken = default)
    {
        var request = kind == RazerThermalChannelKind.Pump
            ? RazerThermalAccessoryProtocol.CreateHanboSetPumpCurve(points)
            : RazerThermalAccessoryProtocol.CreateHanboSetFanCurve(points);
        await QueryHanboAsync(
            request,
            request[7],
            request[8],
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<RazerThermalState> ReadHanboAsync(CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var edition = 0;
        try
        {
            var info = await QueryHanboAsync(
                RazerThermalAccessoryProtocol.CreateHanboGetDeviceInfo(),
                0x02,
                0x02,
                cancellationToken).ConfigureAwait(false);
            edition = RazerThermalAccessoryProtocol.ReadHanboEdition(info);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            errors.Add($"device info: {exception.Message}");
        }

        var channels = new List<RazerThermalChannel>();
        try
        {
            var fan = await QueryHanboAsync(
                RazerThermalAccessoryProtocol.CreateHanboGetFanInfo(),
                0x20,
                0x01,
                cancellationToken).ConfigureAwait(false);
            channels.Add(RazerThermalAccessoryProtocol.ReadHanboFan(fan));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            errors.Add($"fan: {exception.Message}");
        }

        try
        {
            var pump = await QueryHanboAsync(
                RazerThermalAccessoryProtocol.CreateHanboGetPumpInfo(),
                0x12,
                0x01,
                cancellationToken).ConfigureAwait(false);
            channels.Add(RazerThermalAccessoryProtocol.ReadHanboPump(pump));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            errors.Add($"pump: {exception.Message}");
        }

        double? cpu = null;
        double? gpu = null;
        try
        {
            var temperatures = await QueryHanboAsync(
                RazerThermalAccessoryProtocol.CreateHanboGetTemperatures(),
                0xC2,
                0x01,
                cancellationToken).ConfigureAwait(false);
            (cpu, gpu) = RazerThermalAccessoryProtocol.ReadHanboTemperatures(temperatures);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            errors.Add($"temperature: {exception.Message}");
        }

        var name = edition == 128
            ? "Razer Hanbo AIO 360MM"
            : edition == 0
                ? "Razer Hanbo AIO 240MM"
                : $"Razer Hanbo AIO ({edition})";
        return new RazerThermalState(
            name,
            edition,
            channels,
            cpu,
            gpu,
            errors.Count == 0 ? null : string.Join("; ", errors));
    }

    private async Task<RazerThermalState> ReadPwmAsync(CancellationToken cancellationToken)
    {
        var channels = new List<RazerThermalChannel>();
        var errors = new List<string>();
        foreach (var thermalId in Enumerable.Range(1, 7).Select(value => (byte)value))
        {
            try
            {
                channels.Add(await ReadPwmChannelAsync(thermalId, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                errors.Add($"fan {thermalId}: {exception.Message}");
            }
        }

        return new RazerThermalState(
            "Razer PWM PC Fan Controller",
            0,
            channels,
            null,
            null,
            channels.Count == 0 ? string.Join("; ", errors) : null);
    }

    private async Task<RazerThermalChannel> ReadPwmChannelAsync(
        byte thermalId,
        CancellationToken cancellationToken)
    {
        var request = RazerThermalAccessoryProtocol.CreatePwmGetFanSpeed(0x01, thermalId);
        var response = await _client.QueryAsync(
            2,
            0x0D,
            0x81,
            request.AsMemory(RazerAccessoryProtocol.ArgumentsOffset, 2),
            candidate => RazerAccessoryProtocol.MatchesCommand(candidate, 0x0D, 0x81),
            cancellationToken).ConfigureAwait(false);
        var (current, target) = RazerThermalAccessoryProtocol.ReadPwmSpeed(response, 0x01, thermalId);
        return new RazerThermalChannel(
            thermalId,
            $"Fan {thermalId}",
            RazerThermalChannelKind.Fan,
            current,
            target / RazerThermalAccessoryProtocol.PwmRpmPerUnit,
            0,
            0,
            true);
    }

    private async Task<byte[]> QueryHanboAsync(
        byte[] request,
        byte commandClass,
        byte commandId,
        CancellationToken cancellationToken)
    {
        var dataSize = request[6];
        return await _client.QueryAsync(
            dataSize,
            commandClass,
            commandId,
            request.AsMemory(RazerAccessoryProtocol.ArgumentsOffset, dataSize),
            response => RazerThermalAccessoryProtocol.MatchesHanbo(response, commandClass, commandId),
            validateStatus: false,
            cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => _client.DisposeAsync();

    private static bool IsExpected(Exception exception) =>
        exception is IOException or InvalidDataException or InvalidOperationException or
            NotSupportedException or TimeoutException or UnauthorizedAccessException or
            System.ComponentModel.Win32Exception or ArgumentException;
}
