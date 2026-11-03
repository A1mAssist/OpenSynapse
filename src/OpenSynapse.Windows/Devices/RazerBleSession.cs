using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

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

                if (response.TransactionId != frame[0] || response.CommandClass != frame[4] || response.CommandId != frame[5])
                {
                    lastError = "response identity mismatch";
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
