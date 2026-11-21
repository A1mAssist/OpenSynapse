using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

internal sealed record RazerEgpuFanState(
    RazerEgpuFanMode? Mode,
    int? CurrentSpeedRpm,
    int MinimumRpm,
    int MaximumRpm,
    double? ChassisTemperatureCelsius,
    IReadOnlyList<string> Errors)
{
    internal string? Error => Errors.Count == 0 ? null : string.Join("; ", Errors);
}

/// <summary>
/// Core X V2 chassis fan control. Reads use 0D:81/0D:82/0D:83/0D:85 and writes use
/// 0D:01/0D:02; every write is followed by a read-back before it is reported as applied.
/// </summary>
internal sealed class RazerEgpuFanService : IAsyncDisposable
{
    private readonly RazerAccessoryCommandClient _client;
    private readonly Action<string>? _diagnostic;

    internal RazerEgpuFanService(
        RazerAccessoryCommandClient client,
        Action<string>? diagnostic = null)
    {
        _client = client;
        _diagnostic = diagnostic;
    }

    internal ushort ProductId => _client.ProductId;

    internal static async Task<RazerEgpuFanService?> ConnectAsync(
        Action<string>? diagnostic = null,
        CancellationToken cancellationToken = default)
    {
        var connection = await RazerAccessoryDiscovery.FindAsync(
            RazerEgpuFanProtocol.ProductId,
            91,
            diagnostic,
            cancellationToken).ConfigureAwait(false);
        if (connection is null)
        {
            connection = await RazerAccessoryDiscovery.FindAsync(
                RazerEgpuFanProtocol.ProductId,
                65,
                diagnostic,
                cancellationToken).ConfigureAwait(false);
        }

        return connection is null
            ? null
            : new RazerEgpuFanService(
                new RazerAccessoryCommandClient(connection, diagnostic),
                diagnostic);
    }

    internal async Task<RazerEgpuFanState> ReadStateAsync(
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        RazerEgpuFanMode? mode = null;
        int? speed = null;
        var minimum = RazerEgpuFanProtocol.MinimumCurveRpm;
        var maximum = RazerEgpuFanProtocol.MaximumCurveRpm;
        double? temperature = null;

        try
        {
            mode = await ReadFanModeAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            errors.Add($"fan mode: {exception.Message}");
        }

        try
        {
            speed = await ReadFanSpeedAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            errors.Add($"fan speed: {exception.Message}");
        }

        try
        {
            (minimum, maximum) = await ReadFanInformationAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            errors.Add($"fan range: {exception.Message}");
            _diagnostic?.Invoke(
                $"egpu fan range unavailable; using bundle default {minimum}..{maximum} RPM");
        }

        try
        {
            temperature = await ReadChassisTemperatureAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            errors.Add($"chassis temperature: {exception.Message}");
        }

        return new RazerEgpuFanState(mode, speed, minimum, maximum, temperature, errors);
    }

    internal async Task SetAutomaticAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await SetFanModeAsync(RazerEgpuFanMode.Auto, cancellationToken)
                .ConfigureAwait(false);
            var readback = await ReadFanModeAsync(cancellationToken).ConfigureAwait(false);
            if (readback is not (RazerEgpuFanMode.Auto or RazerEgpuFanMode.ForceAuto))
            {
                throw new InvalidOperationException(
                    $"Core X V2 fan mode readback was {readback}, expected Auto.");
            }

            _diagnostic?.Invoke("egpu fan mode set to Auto");
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            throw new InvalidOperationException(
                $"Core X V2 automatic fan update failed: {exception.Message}",
                exception);
        }
    }

    internal async Task<int> ApplyCurveAsync(
        IReadOnlyList<RazerEgpuFanCurvePoint> curve,
        double temperatureCelsius,
        RazerEgpuTemperatureSource source,
        int minimumRpm,
        int maximumRpm,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(curve);
        var state = await ReadStateAsync(cancellationToken).ConfigureAwait(false);
        var target = RazerEgpuFanProtocol.EvaluateCurve(curve, temperatureCelsius, source);
        target = RazerEgpuFanProtocol.ClampRpm(target, minimumRpm, maximumRpm);
        var previousMode = state.Mode;
        var previousSpeed = state.CurrentSpeedRpm;

        try
        {
            await SetFanModeAsync(RazerEgpuFanMode.Manual, cancellationToken)
                .ConfigureAwait(false);
            await SetFanSpeedAsync(target, cancellationToken).ConfigureAwait(false);
            await VerifyModeAsync(RazerEgpuFanMode.Manual, cancellationToken).ConfigureAwait(false);
            _diagnostic?.Invoke(
                $"egpu fan curve applied: temperature={temperatureCelsius:0.#}C target={target} RPM");
            return target;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            await RestoreAsync(previousMode, previousSpeed, CancellationToken.None)
                .ConfigureAwait(false);
            throw new InvalidOperationException(
                $"Core X V2 fan curve update failed: {exception.Message}",
                exception);
        }
    }

    internal async Task<(double TemperatureCelsius, int Rpm)> RefreshCurveAsync(
        IReadOnlyList<RazerEgpuFanCurvePoint> curve,
        double temperatureCelsius,
        RazerEgpuTemperatureSource source,
        int minimumRpm,
        int maximumRpm,
        CancellationToken cancellationToken = default)
    {
        var target = RazerEgpuFanProtocol.ClampRpm(
            RazerEgpuFanProtocol.EvaluateCurve(curve, temperatureCelsius, source),
            minimumRpm,
            maximumRpm);
        await SetFanSpeedAsync(target, cancellationToken).ConfigureAwait(false);
        return (temperatureCelsius, target);
    }

    private async Task<RazerEgpuFanMode> ReadFanModeAsync(CancellationToken cancellationToken)
    {
        var response = await QueryAsync(
            RazerEgpuFanProtocol.GetFanModeDataSize,
            RazerEgpuFanProtocol.GetFanModeCommand,
            RazerEgpuFanProtocol.CreateGetFanModeArguments(),
            cancellationToken).ConfigureAwait(false);
        return RazerEgpuFanProtocol.ReadFanMode(response);
    }

    private async Task<int> ReadFanSpeedAsync(CancellationToken cancellationToken)
    {
        var response = await QueryAsync(
            RazerEgpuFanProtocol.GetFanSpeedDataSize,
            RazerEgpuFanProtocol.GetFanSpeedCommand,
            RazerEgpuFanProtocol.CreateGetFanSpeedArguments(),
            cancellationToken).ConfigureAwait(false);
        return RazerEgpuFanProtocol.ReadFanSpeed(response);
    }

    private async Task<(int MinimumRpm, int MaximumRpm)> ReadFanInformationAsync(
        CancellationToken cancellationToken)
    {
        var response = await QueryAsync(
            RazerEgpuFanProtocol.GetFanInformationDataSize,
            RazerEgpuFanProtocol.GetFanInformationCommand,
            RazerEgpuFanProtocol.CreateGetFanInformationArguments(),
            cancellationToken).ConfigureAwait(false);
        return RazerEgpuFanProtocol.ReadFanInformation(response);
    }

    internal async Task<double?> ReadChassisTemperatureAsync(CancellationToken cancellationToken)
    {
        var response = await QueryAsync(
            RazerEgpuFanProtocol.GetThermalReadingDataSize,
            RazerEgpuFanProtocol.GetThermalReadingCommand,
            RazerEgpuFanProtocol.CreateThermalReadingArguments(),
            cancellationToken).ConfigureAwait(false);
        return RazerEgpuFanProtocol.ReadChassisTemperature(response);
    }

    private async Task SetFanModeAsync(
        RazerEgpuFanMode mode,
        CancellationToken cancellationToken)
    {
        await QueryAsync(
            RazerEgpuFanProtocol.SetFanModeDataSize,
            RazerEgpuFanProtocol.SetFanModeCommand,
            RazerEgpuFanProtocol.CreateSetFanModeArguments(mode),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task SetFanSpeedAsync(int rpm, CancellationToken cancellationToken)
    {
        await QueryAsync(
            RazerEgpuFanProtocol.SetFanSpeedDataSize,
            RazerEgpuFanProtocol.SetFanSpeedCommand,
            RazerEgpuFanProtocol.CreateSetFanSpeedArguments(rpm),
            cancellationToken).ConfigureAwait(false);

        var actual = await ReadFanSpeedAsync(cancellationToken).ConfigureAwait(false);
        if (actual != rpm)
        {
            throw new InvalidOperationException(
                $"Core X V2 fan speed readback was {actual} RPM, expected {rpm}.");
        }
    }

    private async Task VerifyModeAsync(
        RazerEgpuFanMode expected,
        CancellationToken cancellationToken)
    {
        var actual = await ReadFanModeAsync(cancellationToken).ConfigureAwait(false);
        if (actual != expected)
        {
            throw new InvalidOperationException(
                $"Core X V2 fan mode readback was {actual}, expected {expected}.");
        }
    }

    private async Task RestoreAsync(
        RazerEgpuFanMode? mode,
        int? speed,
        CancellationToken cancellationToken)
    {
        try
        {
            if (speed is { } restoreSpeed)
            {
                await SetFanSpeedAsync(restoreSpeed, cancellationToken).ConfigureAwait(false);
            }

            if (mode is { } restoreMode)
            {
                await SetFanModeAsync(restoreMode, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _diagnostic?.Invoke($"egpu fan restore failed: {exception.Message}");
        }
    }

    private Task<byte[]> QueryAsync(
        byte dataSize,
        byte commandId,
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken) =>
        _client.QueryAsync(
            dataSize,
            RazerEgpuFanProtocol.CommandClass,
            commandId,
            arguments,
            response => RazerAccessoryProtocol.MatchesCommand(
                response,
                RazerEgpuFanProtocol.CommandClass,
                commandId),
            cancellationToken);

    public ValueTask DisposeAsync() => _client.DisposeAsync();

    private static bool IsExpected(Exception exception) =>
        exception is IOException or InvalidDataException or InvalidOperationException or
            NotSupportedException or TimeoutException or UnauthorizedAccessException or
            System.ComponentModel.Win32Exception or ArgumentException;
}
