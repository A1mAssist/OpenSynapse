using OpenSynapse.Windows.Devices;

namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Keeps the Product 710 speaker and microphone indicators synchronized with
/// the default Core Audio endpoint mute states.
/// </summary>
public sealed class BladeAudioMuteRuntime : IAsyncDisposable
{
    private static readonly TimeSpan DeviceWait = TimeSpan.FromMilliseconds(5);
    private readonly IRazerFeatureTransport _transport;
    private readonly string _devicePath;
    private readonly BladeSoftwareModeCoordinator _modeCoordinator;
    private readonly WindowsCoreAudioMuteEventSource _source;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private IRazerFeatureSession? _session;
    private BladeSoftwareModeCoordinator.BladeSoftwareModeLease? _modeLease;
    private BladeAudioMuteSynchronizer? _synchronizer;
    private int _started;
    private int _disposed;

    public BladeAudioMuteRuntime(IRazerFeatureTransport transport, string devicePath)
        : this(transport, devicePath, new BladeSoftwareModeCoordinator())
    {
    }

    internal BladeAudioMuteRuntime(
        IRazerFeatureTransport transport,
        string devicePath,
        BladeSoftwareModeCoordinator modeCoordinator)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);
        _devicePath = devicePath;
        _modeCoordinator = modeCoordinator ?? throw new ArgumentNullException(nameof(modeCoordinator));
        _source = new WindowsCoreAudioMuteEventSource(Publish);
        _source.ReadFailed += NotifySynchronizationFailed;
    }

    public string? LastError => _source.LastError ?? _synchronizer?.LastError;

    public event Action<BladeAudioMuteState>? Synchronized;
    public event Action<Exception>? SynchronizationFailed;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (Interlocked.Exchange(ref _started, 1) != 0)
            {
                throw new InvalidOperationException("Blade 音频静音灯同步已经启动。");
            }

            IRazerFeatureSession? session = null;
            BladeSoftwareModeCoordinator.BladeSoftwareModeLease? modeLease = null;
            try
            {
                session = await _transport.OpenSessionAsync(_devicePath, cancellationToken)
                    .ConfigureAwait(false);
                await InitializeSessionAsync(session, cancellationToken).ConfigureAwait(false);
                modeLease = await _modeCoordinator.AcquireAsync(
                    _devicePath,
                    token => SetDeviceModeAsync(session, softwareMode: true, token),
                    () => SetDeviceModeAsync(session, softwareMode: false, CancellationToken.None),
                    cancellationToken)
                    .ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken)
                    .ConfigureAwait(false);

                var synchronizer = new BladeAudioMuteSynchronizer(session);
                synchronizer.Synchronized += NotifySynchronized;
                synchronizer.SynchronizationFailed += NotifySynchronizationFailed;
                _session = session;
                _modeLease = modeLease;
                _synchronizer = synchronizer;
                session = null;
                modeLease = null;
                _source.Start();
            }
            catch
            {
                Volatile.Write(ref _started, 0);
                var synchronizer = Interlocked.Exchange(ref _synchronizer, null);
                if (synchronizer is not null)
                {
                    await synchronizer.DisposeAsync().ConfigureAwait(false);
                }
                session ??= Interlocked.Exchange(ref _session, null);
                modeLease ??= Interlocked.Exchange(ref _modeLease, null);
                if (session is not null)
                {
                    if (modeLease is not null)
                    {
                        try
                        {
                            await modeLease.ReleaseAsync(() => SetDeviceModeAsync(
                                session,
                                softwareMode: false,
                                CancellationToken.None)).ConfigureAwait(false);
                        }
                        catch
                        {
                            // Preserve the original startup error.
                        }
                    }
                    await session.DisposeAsync().ConfigureAwait(false);
                }
                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _source.Dispose();
            var synchronizer = Interlocked.Exchange(ref _synchronizer, null);
            if (synchronizer is not null)
            {
                await synchronizer.DisposeAsync().ConfigureAwait(false);
            }

            var session = Interlocked.Exchange(ref _session, null);
            var modeLease = Interlocked.Exchange(ref _modeLease, null);
            if (session is null)
            {
                return;
            }

            try
            {
                if (modeLease is not null)
                {
                    await modeLease.ReleaseAsync(() => SetDeviceModeAsync(
                        session,
                        softwareMode: false,
                        CancellationToken.None)).ConfigureAwait(false);
                }
            }
            catch
            {
                // Keep shutdown best-effort; reconnecting Synapse restores Normal mode too.
            }
            finally
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private void Publish(BladeAudioMuteState state) => _synchronizer?.Publish(state);

    private void NotifySynchronized(BladeAudioMuteState state) => Synchronized?.Invoke(state);

    private void NotifySynchronizationFailed(Exception exception) =>
        SynchronizationFailed?.Invoke(exception);

    internal static Task InitializeSessionAsync(
        IRazerFeatureSession session,
        CancellationToken cancellationToken)
    {
        var request = RazerFeatureReport.CreateRequest(
            session.NextTransactionId(),
            dataSize: 0x02,
            commandClass: 0x00,
            commandId: 0x81,
            arguments: []);
        request[0] = 0x02;
        return session.SendAsync(request, cancellationToken);
    }

    internal static async Task SetDeviceModeAsync(
        IRazerFeatureSession session,
        bool softwareMode,
        CancellationToken cancellationToken)
    {
        var transactionId = session.NextTransactionId();
        var request = softwareMode
            ? BladeDeviceModeProtocol.CreateSetSoftwareRequest(transactionId)
            : BladeDeviceModeProtocol.CreateSetNormalRequest(transactionId);
        var response = await session.QueryAsync(
            request[2],
            request[6],
            request[7],
            request[8],
            request.AsMemory(RazerFeatureReport.ArgumentsOffset, request[6]),
            DeviceWait,
            responseReportId: 0x02,
            cancellationToken).ConfigureAwait(false);
        if (!RazerFeatureReport.IsSuccessfulResponse(request, response, minimumArguments: 2))
        {
            throw new InvalidOperationException("Blade 设备模式切换未收到成功响应。");
        }
    }
}
