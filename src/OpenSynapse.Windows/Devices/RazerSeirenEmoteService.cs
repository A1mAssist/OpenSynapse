using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Owns one Seiren Emote HID session and sends complete 8x8 frames with the
/// protocol's fixed transaction id. Calls are serialized so a latch cannot overtake
/// another frame's rows.
/// </summary>
public sealed class RazerSeirenEmoteService : IAsyncDisposable
{
    private static readonly TimeSpan RowDelay = TimeSpan.FromMilliseconds(5);

    private readonly IRazerFeatureTransport _transport;
    private readonly string _devicePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IRazerFeatureSession? _session;
    private int _disposed;

    public RazerSeirenEmoteService(IRazerFeatureTransport transport, string devicePath)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _devicePath = string.IsNullOrWhiteSpace(devicePath)
            ? throw new ArgumentException("A HID device path is required.", nameof(devicePath))
            : devicePath;
    }

    public async Task SendFrameAsync(
        IReadOnlyList<RazerRgb> pixels,
        CancellationToken cancellationToken = default)
    {
        var requests = RazerSeirenEmoteProtocol.CreateFrameRequests(pixels);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var session = _session ??= await _transport
                .OpenSessionAsync(_devicePath, cancellationToken)
                .ConfigureAwait(false);
            await session.SendBatchAsync(requests, RowDelay, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task ClearAsync(CancellationToken cancellationToken = default) =>
        SendFrameAsync(new RazerRgb[RazerSeirenEmoteProtocol.PixelCount], cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        IRazerFeatureSession? session;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            session = _session;
            _session = null;
        }
        finally
        {
            _gate.Release();
        }

        _gate.Dispose();
        if (session is not null)
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }
}
