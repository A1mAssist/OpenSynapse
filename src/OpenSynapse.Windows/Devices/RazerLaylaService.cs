using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed record RazerLaylaState(string? Firmware, string? Serial, int? BatteryPercent, IReadOnlyList<string> Errors);

/// <summary>One physical Pod: input stays open until driver mode has been restored.</summary>
public sealed class RazerLaylaService : IAsyncDisposable
{
    private readonly RazerBleSession _session;
    private readonly IRazerLaylaInputSource _input;
    private readonly Func<RazerLaylaInput, CancellationToken, Task> _dispatch;
    private readonly Action<string>? _diagnostic;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _events;
    private bool _restoreMode;
    private volatile bool _inputStopped;
    private int _disposed;
    public bool IsRunning { get; private set; }
    public string? Error { get; private set; }
    public event Action<string>? Failed;

    internal RazerLaylaService(IRazerBleTransport transport, IRazerLaylaInputSource input,
        Func<RazerLaylaInput, CancellationToken, Task> dispatch, Action<string>? diagnostic = null)
    {
        _session = new(transport, timeout: TimeSpan.FromMilliseconds(1500), diagnostic: diagnostic, layla: true);
        _input = input;
        _dispatch = dispatch;
        _diagnostic = diagnostic;
    }

    public static async Task<RazerLaylaService> OpenAsync(RazerBleDevice device,
        Func<RazerLaylaInput, CancellationToken, Task> dispatch, Action<string>? diagnostic = null,
        CancellationToken cancellationToken = default)
    {
        if (device.ProductId != RazerLaylaProtocol.ProductId) throw new ArgumentException("Not a Layla device.", nameof(device));
        var input = await RazerLaylaHidInput.OpenAsync(device, cancellationToken).ConfigureAwait(false);
        RazerLaylaService? service = null;
        try
        {
            var transport = await WindowsRazerBleTransport.ConnectAsync(device.Id, cancellationToken, layla: true).ConfigureAwait(false);
            service = new(transport, input, dispatch, diagnostic);
            await service.StartAsync(cancellationToken).ConfigureAwait(false);
            return service;
        }
        catch
        {
            if (service is not null) await service.DisposeAsync().ConfigureAwait(false);
            else input.Dispose();
            throw;
        }
    }

    internal async Task StartAsync(CancellationToken token)
    {
        if (_events is not null) throw new InvalidOperationException("Control Pod session already started.");
        _events = RunInputAsync();
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_inputStopped) throw new IOException(Error ?? "Control Pod input stopped before driver mode was entered.");
            // Even an unacknowledged write may have entered driver mode.
            _restoreMode = true;
            await SendAsync(1, 2, [5, 0], token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (_inputStopped) throw new IOException(Error ?? "Control Pod input stopped while entering driver mode.");
            IsRunning = true;
            _ready.TrySetResult();
        }
        finally { _gate.Release(); }
    }

    private async Task RunInputAsync()
    {
        try
        {
            await _input.RunAsync(async (input, token) =>
            {
                await _ready.Task.WaitAsync(token).ConfigureAwait(false);
                try { await _dispatch(input, token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception exception) { ReportError(exception.Message); }
            }, _stop.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception exception) { IsRunning = false; ReportError(exception.Message); }
        finally
        {
            _inputStopped = true;
            _stop.Cancel();
            await _gate.WaitAsync().ConfigureAwait(false);
            try { await RestoreModeAsync().ConfigureAwait(false); }
            finally { IsRunning = false; _gate.Release(); }
        }
    }

    public async Task<RazerLaylaState> ReadStateAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            string? firmware = null, serial = null;
            int? battery = null;
            var errors = new List<string>();
            await Read(0, 0x81, response => firmware = RazerBleProtocol30.ReadFirmware(response).ToVersionString());
            await Read(1, 0x83, response => serial = RazerBleProtocol30.ReadSerialNumber(response));
            await Read(5, 0x81, response => battery = RazerBleProtocol30.ReadBatteryPercent(response));
            if (Error is { } error) errors.Add(error);
            return new(firmware, serial, battery, errors);

            async Task Read(byte cls, byte command, Action<RazerBleResponse> accept)
            {
                try { accept(await SendAsync(cls, command, [], token).ConfigureAwait(false)); }
                catch (Exception exception) when (exception is IOException or InvalidOperationException) { errors.Add(exception.Message); }
            }
        }
        finally { _gate.Release(); }
    }

    private async Task<RazerBleResponse> SendAsync(byte cls, byte command, byte[] data, CancellationToken token)
    {
        var response = await _session.SendAsync(id => RazerLaylaProtocol.CreateFrame(id, cls, command, data), $"Layla {cls:X2}:{command:X2}", token).ConfigureAwait(false);
        if (!response.IsSuccess) throw new InvalidOperationException($"Layla {cls:X2}:{command:X2}: status {response.Status}.");
        return response;
    }

    private async Task RestoreModeAsync()
    {
        if (!_restoreMode) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        try
        {
            await SendAsync(1, 2, [5, 1], timeout.Token).ConfigureAwait(false);
            _restoreMode = false;
        }
        catch (Exception exception) { ReportError($"Control Pod mode restore failed: {exception.Message}"); }
    }

    private void ReportError(string message) { Error = message; _diagnostic?.Invoke(message); Failed?.Invoke(message); }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        if (_events is not null) await _events.ConfigureAwait(false);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await RestoreModeAsync().ConfigureAwait(false);
            _input.Dispose();
            _session.Dispose();
        }
        finally { _gate.Release(); }
        _stop.Dispose();
        _gate.Dispose();
    }
}
