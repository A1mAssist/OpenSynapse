using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed record RazerBleDeviceState(
    RazerBleFirmware? Firmware,
    string? SerialNumber,
    int? BatteryPercent,
    byte? ChargingStatus,
    int? PollingHertz,
    (int X, int Y, int Z)? Dpi,
    IReadOnlyList<string> Errors);

/// <summary>
/// Serialises protocol 30 commands for one BLE device, mirroring the middleware's
/// per-device lock, 2 attempts and 30 ms retry delay.
/// </summary>
public sealed class RazerBleSession : IDisposable
{
    private readonly IRazerBleTransport _transport;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _retryDelay;
    private readonly Action<string>? _diagnostic;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private byte _transactionId;

    public RazerBleSession(
        IRazerBleTransport transport,
        TimeSpan? timeout = null,
        TimeSpan? retryDelay = null,
        Action<string>? diagnostic = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _timeout = timeout ?? TimeSpan.FromMilliseconds(500);
        _retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(30);
        _diagnostic = diagnostic;
    }

    public async Task<RazerBleResponse> SendAsync(
        Func<byte, byte[]> createFrame,
        string description,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(createFrame);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string? lastError = null;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                if (attempt > 0)
                {
                    await Task.Delay(_retryDelay, cancellationToken).ConfigureAwait(false);
                }

                var frame = createFrame(NextTransactionId());
                var reply = await _transport.ExchangeAsync(frame, _timeout, cancellationToken).ConfigureAwait(false);
                if (reply is null)
                {
                    lastError = "timeout";
                    _diagnostic?.Invoke($"ble {description}: no notification");
                    continue;
                }

                RazerBleResponse response;
                try
                {
                    response = RazerBleProtocol30.ParseResponse(reply);
                }
                catch (InvalidOperationException exception)
                {
                    lastError = exception.Message;
                    _diagnostic?.Invoke($"ble {description}: {exception.Message}");
                    continue;
                }

                if (response.Status is RazerBleProtocol30.StatusBusy or RazerBleProtocol30.StatusFailure)
                {
                    lastError = $"status {response.Status}";
                    _diagnostic?.Invoke($"ble {description}: status {response.Status}, retrying");
                    continue;
                }

                return response;
            }

            throw new InvalidOperationException($"Protocol 30 command '{description}' failed: {lastError}.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _transport.Dispose();
        _gate.Dispose();
    }

    private byte NextTransactionId()
    {
        if (_transactionId > RazerBleProtocol30.MaximumTransactionId)
        {
            _transactionId = 0;
        }

        return _transactionId++;
    }
}

/// <summary>
/// Battery, polling rate, DPI and identity for a protocol 30 BLE device. Writes follow the
/// house rule: read the current value, write, read back, restore the old value on mismatch.
/// </summary>
public sealed class RazerBleDeviceService : IDisposable
{
    private readonly RazerBleSession _session;
    private readonly Action<string>? _diagnostic;

    public RazerBleDeviceService(
        RazerBleSession session,
        string name,
        Action<string>? diagnostic = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        Name = name;
        _diagnostic = diagnostic;
    }

    public string Name { get; }

    public static async Task<RazerBleDeviceService> OpenAsync(
        RazerBleDevice device,
        Action<string>? diagnostic = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        var transport = await WindowsRazerBleTransport.ConnectAsync(device.Id, cancellationToken).ConfigureAwait(false);
        return new RazerBleDeviceService(new RazerBleSession(transport, diagnostic: diagnostic), device.Name, diagnostic);
    }

    public async Task<RazerBleDeviceState> ReadStateAsync(CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();

        var firmware = await TryAsync(
            "firmware",
            async () => RazerBleProtocol30.ReadFirmware(await _session.SendAsync(
                transaction => RazerBleProtocol30.CreateGetFirmware(transaction), "get firmware", cancellationToken).ConfigureAwait(false)),
            errors, cancellationToken).ConfigureAwait(false);

        var serial = await TryAsync(
            "serial",
            async () => RazerBleProtocol30.ReadSerialNumber(await _session.SendAsync(
                transaction => RazerBleProtocol30.CreateGetSerialNumber(transaction), "get serial", cancellationToken).ConfigureAwait(false)),
            errors, cancellationToken).ConfigureAwait(false);

        var battery = await TryStructAsync(
            "battery",
            async () => RazerBleProtocol30.ReadBatteryPercent(await _session.SendAsync(
                transaction => RazerBleProtocol30.CreateGetBatteryLevel(transaction), "get battery", cancellationToken).ConfigureAwait(false)),
            errors, cancellationToken).ConfigureAwait(false);

        var charging = await TryStructAsync(
            "charging",
            async () => RazerBleProtocol30.ReadChargingStatus(await _session.SendAsync(
                transaction => RazerBleProtocol30.CreateGetChargingStatus(transaction), "get charging", cancellationToken).ConfigureAwait(false)),
            errors, cancellationToken).ConfigureAwait(false);

        var polling = await TryStructAsync(
            "polling",
            async () => RazerBleProtocol30.ReadPollingRate(await _session.SendAsync(
                transaction => RazerBleProtocol30.CreateGetPollingRate(transaction), "get polling", cancellationToken).ConfigureAwait(false)),
            errors, cancellationToken).ConfigureAwait(false);

        var dpi = await TryStructAsync(
            "dpi",
            async () => RazerBleProtocol30.ReadDpi(await _session.SendAsync(
                transaction => RazerBleProtocol30.CreateGetDpi(transaction), "get dpi", cancellationToken).ConfigureAwait(false)),
            errors, cancellationToken).ConfigureAwait(false);

        return new RazerBleDeviceState(firmware, serial, battery, charging, polling, dpi, errors);
    }

    public async Task<int> SetPollingRateAsync(int hertz, CancellationToken cancellationToken = default)
    {
        RazerBleProtocol30.PollingCode(hertz);

        var current = RazerBleProtocol30.ReadPollingRate(await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateGetPollingRate(transaction), "get polling", cancellationToken).ConfigureAwait(false));
        if (current == hertz)
        {
            return current;
        }

        await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateSetPollingRate(transaction, hertz),
            "set polling",
            cancellationToken).ConfigureAwait(false);

        var applied = RazerBleProtocol30.ReadPollingRate(await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateGetPollingRate(transaction), "verify polling", cancellationToken).ConfigureAwait(false));
        if (applied == hertz)
        {
            return applied;
        }

        _diagnostic?.Invoke($"ble polling read back {applied} Hz after writing {hertz} Hz, restoring {current} Hz");
        await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateSetPollingRate(transaction, current),
            "restore polling",
            cancellationToken).ConfigureAwait(false);
        throw new InvalidOperationException($"The device kept {applied} Hz instead of {hertz} Hz; the previous rate was restored.");
    }

    public async Task<(int X, int Y)> SetDpiAsync(int x, int y, CancellationToken cancellationToken = default)
    {
        var current = RazerBleProtocol30.ReadDpi(await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateGetDpi(transaction), "get dpi", cancellationToken).ConfigureAwait(false));

        await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateSetDpi(transaction, x, y),
            "set dpi",
            cancellationToken).ConfigureAwait(false);

        var applied = RazerBleProtocol30.ReadDpi(await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateGetDpi(transaction), "verify dpi", cancellationToken).ConfigureAwait(false));
        if (applied.X == x && applied.Y == y)
        {
            return (applied.X, applied.Y);
        }

        _diagnostic?.Invoke(
            $"ble dpi read back {applied.X}x{applied.Y} after writing {x}x{y}, restoring {current.X}x{current.Y}");
        await _session.SendAsync(
            transaction => RazerBleProtocol30.CreateSetDpi(transaction, current.X, current.Y),
            "restore dpi",
            cancellationToken).ConfigureAwait(false);
        throw new InvalidOperationException(
            $"The device kept {applied.X}x{applied.Y} instead of {x}x{y}; the previous DPI was restored.");
    }

    public void Dispose() => _session.Dispose();

    private static async Task<T?> TryStructAsync<T>(
        string label,
        Func<Task<T>> read,
        List<string> errors,
        CancellationToken cancellationToken)
        where T : struct
    {
        try
        {
            return await read().ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // A device that ignores this command class hides the field instead of failing the page.
            errors.Add(label);
            return null;
        }
    }

    private static async Task<T?> TryAsync<T>(
        string label,
        Func<Task<T>> read,
        List<string> errors,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await read().ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            errors.Add(label);
            return null;
        }
    }
}


